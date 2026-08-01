import { Disposable, DisposableCollection } from '@theia/core/lib/common';
import { FrontendApplicationContribution } from '@theia/core/lib/browser';
import { inject, injectable } from '@theia/core/shared/inversify';
import { HarnessClient } from './harness-client';

/**
 * Remonte le travail hors ligne quand le réseau revient.
 *
 * Trois déclencheurs, parce qu'aucun ne suffit seul :
 *
 *  · l'évènement `online` du navigateur, qui arrive vite mais ment souvent — être
 *    connecté à un point d'accès ne veut pas dire joindre le serveur ;
 *  · une tentative périodique, qui rattrape les cas où l'évènement n'est jamais venu ;
 *  · le démarrage, pour ce qui attendait depuis la veille.
 *
 * Une synchronisation qui échoue ne fait rien perdre : la file n'est purgée que de ce
 * que le serveur dit avoir retenu.
 */
@injectable()
export class SyncService implements FrontendApplicationContribution, Disposable {

    /**
     * Cinq minutes. Assez fréquent pour qu'un élève de retour en cours voie sa soirée
     * remonter avant la fin de l'heure, assez rare pour ne pas réveiller la connexion
     * en permanence.
     */
    static readonly RetryInterval = 5 * 60 * 1000;

    @inject(HarnessClient) protected readonly harness!: HarnessClient;

    protected readonly toDispose = new DisposableCollection();

    onStart(): void {
        void this.attempt();

        const timer = setInterval(() => void this.attempt(), SyncService.RetryInterval);
        this.toDispose.push(Disposable.create(() => clearInterval(timer)));

        const onOnline = () => void this.attempt();
        globalThis.addEventListener?.('online', onOnline);
        this.toDispose.push(Disposable.create(() => globalThis.removeEventListener?.('online', onOnline)));
    }

    /** Tente une remontée. Silencieuse : l'absence de réseau n'est pas une erreur à signaler. */
    async attempt(): Promise<void> {
        try {
            const result = await this.harness.sync();

            if (result && (result.activitiesAccepted > 0 || result.resultsAccepted > 0)) {
                console.info(
                    `[gpoe26] Travail hors ligne remonté : ${result.activitiesAccepted} signal(aux), ` +
                    `${result.resultsAccepted} résultat(s).`);
            }
        } catch (error) {
            console.debug('[gpoe26] Synchronisation reportée :', error);
        }
    }

    dispose(): void {
        this.toDispose.dispose();
    }
}
