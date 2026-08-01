import { Command, CommandContribution, CommandRegistry } from '@theia/core/lib/common';
import { inject, injectable } from '@theia/core/shared/inversify';
import { HarnessEvent, SessionKind, StudyActivityKind } from '../common/harness-protocol';
import { HarnessClient } from './harness-client';

/**
 * Ce qu'un plugin d'exercice transmet quand l'élève demande de l'aide.
 *
 * `contexte` est libre à dessein : chaque type d'exercice sait ce qui décrit son état, et
 * le socle n'a aucune raison de le savoir. Un exercice Python enverra le code et la
 * sortie des tests ; un exercice de SQL enverra la requête et le schéma.
 */
export interface TutorRequestFromPlugin {
    courseId: string;
    stepId?: string;
    message: string;
    contexte?: Record<string, string>;
}

export const AskTutor: Command = {
    id: 'gpoe26.repetiteur.demander',
    label: 'Demander au répétiteur'
};

/**
 * Le pont entre les plugins d'exercice et le répétiteur.
 *
 * Un plugin tourne dans un processus séparé et ne peut pas atteindre les services de
 * l'atelier — c'est précisément l'isolation qu'on a voulue. Le passage se fait donc par
 * une commande, le pont documenté entre l'hôte des plugins et l'interface.
 *
 * Ce qui traverse est l'état de l'exercice, pas un appel d'outil. L'agent voit le code de
 * l'élève et ce que ses tests ont dit sans avoir à les demander : c'est plus court d'un
 * aller-retour, et cela évite pour l'instant d'ouvrir un canal du serveur vers la machine
 * de l'élève — canal qu'il faudra bien construire le jour où l'agent devra modifier un
 * fichier lui-même.
 */
@injectable()
export class TutorBridge implements CommandContribution {

    @inject(HarnessClient) protected readonly harness!: HarnessClient;

    /** Une session par cours, comme sur le Web : c'est ce qui donne sa mémoire au répétiteur. */
    protected readonly sessions = new Map<string, string>();

    registerCommands(commands: CommandRegistry): void {
        commands.registerCommand(AskTutor, {
            execute: (request: TutorRequestFromPlugin) => this.ask(request)
        });
    }

    /**
     * Joue un tour et renvoie la réponse complète.
     *
     * Les évènements intermédiaires sont consommés ici : le plugin n'a que faire du flux,
     * il veut une réponse à afficher. L'interface du répétiteur, elle, s'abonnera au flux
     * quand elle existera.
     */
    async ask(request: TutorRequestFromPlugin): Promise<string | undefined> {
        const sessionId = await this.session(request.courseId);
        if (!sessionId) return undefined;

        // Une question posée compte dans la séance de révision : c'est un des chiffres que
        // le parent voit, et l'oublier ferait passer une soirée de travail pour une lecture.
        await this.harness.recordActivity({
            courseId: request.courseId,
            kind: StudyActivityKind.Question
        });

        let reply: string | undefined;

        for await (const event of this.harness.playTurn(sessionId, {
            message: request.message,
            context: request.contexte
        })) {
            reply = this.absorb(event, reply);
        }

        return reply;
    }

    protected absorb(event: HarnessEvent, reply: string | undefined): string | undefined {
        if (event.type === 'done') return event.reply ?? reply;
        if (event.type === 'error') return event.error ?? reply;

        return reply;
    }

    protected async session(courseId: string): Promise<string | undefined> {
        const known = this.sessions.get(courseId);
        if (known) return known;

        // Nature Code : c'est la seule qui expose les outils de l'espace de travail. Une
        // session de lecture ne les verrait pas, et l'agent ne saurait pas qu'il peut
        // parler de fichiers.
        const session = await this.harness.openSession({ courseId, kind: SessionKind.Code });
        if (!session) return undefined;

        this.sessions.set(courseId, session.id);
        return session.id;
    }
}
