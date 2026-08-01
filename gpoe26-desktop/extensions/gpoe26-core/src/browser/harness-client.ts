import { Emitter, Event } from '@theia/core/lib/common';
import { inject, injectable } from '@theia/core/shared/inversify';
import {
    ActivitySignal, AuthResponse, HarnessEvent, OpenSessionRequest,
    RenderedCourseDto, SessionDto, SyncBatch, SyncResult, TurnRequest, isTerminal
} from '../common/harness-protocol';
import { LocalStore, StoredCredentials } from '../common/store-protocol';

export const HarnessConfig = Symbol('HarnessConfig');

export interface HarnessConfig {
    harnessUrl: string;
    userUrl: string;
}

/**
 * Le client du harness et du service d'identité.
 *
 * Il porte les deux comportements sans lesquels l'application ne servirait à rien sur une
 * connexion scolaire :
 *
 *  · **Le renouvellement silencieux du jeton.** Le jeton d'accès dure une heure. Sans
 *    rafraîchissement automatique, l'élève serait déconnecté au milieu d'une révision.
 *
 *  · **Le repli sur le magasin local.** Une requête qui échoue faute de réseau n'est pas
 *    une erreur à montrer : c'est le cas normal d'un élève qui révise dans le transport.
 */
@injectable()
export class HarnessClient {

    @inject(LocalStore) protected readonly store!: LocalStore;
    @inject(HarnessConfig) protected readonly config!: HarnessConfig;

    protected credentials: StoredCredentials | undefined;

    protected readonly onlineChangedEmitter = new Emitter<boolean>();
    readonly onOnlineChanged: Event<boolean> = this.onlineChangedEmitter.event;

    protected _online = true;

    get online(): boolean {
        return this._online;
    }

    get profile(): AuthResponse['profile'] | undefined {
        return this.credentials?.profile;
    }

    /** Charge l'identité conservée sur la machine, s'il y en a une. */
    async restore(): Promise<boolean> {
        this.credentials = await this.store.readCredentials();
        return this.credentials !== undefined;
    }

    // ── Identité ─────────────────────────────────────────────────────────────

    async login(email: string, password: string): Promise<boolean> {
        const response = await fetch(`${this.config.userUrl}/auth/login`, {
            method: 'POST',
            headers: { 'content-type': 'application/json' },
            body: JSON.stringify({ email, password })
        });

        if (!response.ok) return false;

        await this.remember(await response.json() as AuthResponse);
        return true;
    }

    async logout(): Promise<void> {
        const refreshToken = this.credentials?.refreshToken;
        this.credentials = undefined;
        await this.store.clearCredentials();

        // Au mieux : si le réseau manque, le jeton expirera de lui-même.
        if (refreshToken) {
            await fetch(`${this.config.userUrl}/auth/deconnexion`, {
                method: 'POST',
                headers: { 'content-type': 'application/json' },
                body: JSON.stringify({ refreshToken })
            }).catch(() => undefined);
        }
    }

    /**
     * Un jeton d'accès valable, renouvelé si besoin.
     *
     * Renvoie `undefined` hors ligne : l'appelant se rabat alors sur le magasin local
     * plutôt que de demander à l'élève de se reconnecter, ce qu'il ne pourrait pas faire.
     */
    protected async accessToken(): Promise<string | undefined> {
        if (!this.credentials) return undefined;

        // Une minute de marge : un jeton qui expire pendant le trajet de la requête
        // produirait un 401 que rien ne distingue d'une vraie déconnexion.
        const expiry = new Date(this.credentials.expiresAt).getTime();
        if (Date.now() < expiry - 60_000) return this.credentials.token;

        if (!this.credentials.refreshToken) return undefined;

        try {
            const response = await fetch(`${this.config.userUrl}/auth/refresh`, {
                method: 'POST',
                headers: { 'content-type': 'application/json' },
                body: JSON.stringify({ refreshToken: this.credentials.refreshToken })
            });

            if (response.status === 401) {
                // Jeton révoqué ou rejoué : là, il faut vraiment se reconnecter.
                await this.logout();
                return undefined;
            }

            if (!response.ok) return undefined;

            await this.remember(await response.json() as AuthResponse);
            return this.credentials?.token;
        } catch {
            // Pas de réseau : on ne déconnecte pas. L'élève lit hors ligne.
            this.setOnline(false);
            return undefined;
        }
    }

    protected async remember(auth: AuthResponse): Promise<void> {
        this.credentials = {
            token: auth.token,
            expiresAt: auth.expiresAt,
            refreshToken: auth.refreshToken,
            refreshExpiresAt: auth.refreshExpiresAt,
            profile: auth.profile
        };

        await this.store.writeCredentials(this.credentials);
        this.setOnline(true);
    }

    // ── Cours ────────────────────────────────────────────────────────────────

    /**
     * Le cours, du réseau si possible, du disque sinon.
     *
     * L'ordre compte : on tente le réseau d'abord pour que l'élève voie une version à
     * jour quand il en a une, et on retombe sur le cache sans le prévenir — c'est le
     * fonctionnement attendu, pas une dégradation.
     */
    async getCourse(courseId: string): Promise<RenderedCourseDto | undefined> {
        const token = await this.accessToken();

        if (token) {
            try {
                const response = await fetch(`${this.config.harnessUrl}/cours/${courseId}/rendu`, {
                    headers: { authorization: `Bearer ${token}` }
                });

                if (response.ok) {
                    const course = await response.json() as RenderedCourseDto;
                    await this.store.putCourse(course);
                    this.setOnline(true);
                    return course;
                }
            } catch {
                this.setOnline(false);
            }
        }

        return (await this.store.getCourse(courseId))?.course;
    }

    // ── Répétiteur ───────────────────────────────────────────────────────────

    async openSession(request: OpenSessionRequest): Promise<SessionDto | undefined> {
        const token = await this.accessToken();
        if (!token) return undefined;

        const response = await fetch(`${this.config.harnessUrl}/sessions`, {
            method: 'POST',
            headers: { 'content-type': 'application/json', authorization: `Bearer ${token}` },
            body: JSON.stringify(request)
        });

        return response.ok ? await response.json() as SessionDto : undefined;
    }

    /**
     * Joue un tour et livre les évènements au fil de leur arrivée.
     *
     * Le flux est lu ligne à ligne plutôt que d'un bloc : c'est tout l'intérêt du
     * streaming, et l'attendre entièrement rendrait l'écran figé le temps d'un tour.
     */
    async *playTurn(sessionId: string, request: TurnRequest): AsyncIterable<HarnessEvent> {
        const token = await this.accessToken();
        if (!token) {
            yield { type: 'error', error: "Le répétiteur n'est pas joignable hors ligne." };
            return;
        }

        const response = await fetch(`${this.config.harnessUrl}/sessions/${sessionId}/tour`, {
            method: 'POST',
            headers: { 'content-type': 'application/json', authorization: `Bearer ${token}` },
            body: JSON.stringify(request)
        });

        if (!response.ok || !response.body) {
            yield { type: 'error', error: "Le répétiteur n'a pas pu traiter la question." };
            return;
        }

        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        let buffer = '';
        let sawTerminal = false;

        while (true) {
            const { done, value } = await reader.read();
            if (done) break;

            buffer += decoder.decode(value, { stream: true });

            // Un évènement SSE se termine par une ligne vide ; le dernier morceau du
            // tampon est probablement incomplet et attend la lecture suivante.
            const chunks = buffer.split('\n');
            buffer = chunks.pop() ?? '';

            for (const line of chunks) {
                if (!line.startsWith('data:')) continue;

                const payload = line.slice(5).trim();
                if (payload === '[DONE]') return;

                try {
                    const event = JSON.parse(payload) as HarnessEvent;
                    if (isTerminal(event)) sawTerminal = true;
                    yield event;
                } catch {
                    // Fragment illisible : on l'ignore plutôt que d'interrompre une
                    // réponse déjà partiellement affichée.
                }
            }
        }

        // Le contrat garantit un évènement terminal. S'il manque, le flux a été coupé —
        // mieux vaut le dire que laisser l'interface attendre indéfiniment.
        if (!sawTerminal) {
            yield { type: 'error', error: 'La réponse a été interrompue.' };
        }
    }

    // ── Hors ligne ───────────────────────────────────────────────────────────

    /** Empile un signal d'activité, horodaté maintenant. Il partira au retour du réseau. */
    async recordActivity(signal: Omit<ActivitySignal, 'occurredAt'>): Promise<void> {
        await this.store.queueActivity({ ...signal, occurredAt: new Date().toISOString() });
    }

    /**
     * Remonte ce qui attend.
     *
     * On ne purge que ce que le serveur dit avoir retenu : perdre du travail est pire que
     * le remonter deux fois, et ces signaux sont idempotents côté serveur.
     */
    async sync(): Promise<SyncResult | undefined> {
        const pending = await this.store.peekPending();
        if (pending.activities.length === 0 && pending.results.length === 0) return undefined;

        const token = await this.accessToken();
        if (!token) return undefined;

        try {
            const response = await fetch(`${this.config.harnessUrl}/sync`, {
                method: 'POST',
                headers: { 'content-type': 'application/json', authorization: `Bearer ${token}` },
                body: JSON.stringify({
                    activities: pending.activities,
                    results: pending.results
                } satisfies SyncBatch)
            });

            if (!response.ok) return undefined;

            const result = await response.json() as SyncResult;
            await this.store.clearPending(result.activitiesAccepted, result.resultsAccepted);

            this.setOnline(true);
            return result;
        } catch {
            this.setOnline(false);
            return undefined;
        }
    }

    protected setOnline(online: boolean): void {
        if (this._online === online) return;

        this._online = online;
        this.onlineChangedEmitter.fire(online);
    }
}
