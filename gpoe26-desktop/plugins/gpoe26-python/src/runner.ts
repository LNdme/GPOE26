import * as path from 'path';
import { Worker } from 'worker_threads';

/** Un test qui n'est pas passé, tel qu'on le montre à l'élève. */
export interface TestFailure {
    nom: string;
    message: string;
    trace: string;
}

export interface TestReport {
    total: number;
    echecs: TestFailure[];
    erreurs: TestFailure[];
    /** Ce que le programme a écrit : les `print` de l'élève, qu'il utilise pour se repérer. */
    sortie: string;
    journal: string;
    /** Le fichier n'a pas pu être chargé — faute de syntaxe, import manquant. */
    chargementEchoue?: boolean;
    /** L'exécution a été interrompue au bout du temps imparti. */
    interrompu?: boolean;
    reussi: boolean;
}

/**
 * Lance les tests d'un exercice.
 *
 * ⚠️ Le délai maximal n'est pas une politesse, c'est la seule protection réelle contre
 * une boucle infinie. Du JavaScript ou du WASM synchrone ne s'interrompt pas : aucun
 * `setTimeout` ne reprendra la main tant que le code de l'élève tourne. Seul
 * `worker.terminate()` y met fin, et il faut donc que ce code tourne dans un fil qu'on
 * puisse tuer.
 */
export class PythonRunner {

    /**
     * Dix secondes. Pyodide met à lui seul environ deux secondes à démarrer ; le reste
     * laisse largement de quoi exécuter les tests d'un exercice de lycée. Au-delà, il ne
     * s'agit plus d'un calcul long mais d'une boucle qui ne finira pas.
     */
    static readonly Timeout = 10_000;

    constructor(private readonly workerPath = path.join(__dirname, 'worker.mjs')) { }

    async run(files: Record<string, string>): Promise<TestReport> {
        const worker = new Worker(this.workerPath, {
            workerData: { files },
            // Le code de l'élève n'a aucune raison de lire les variables d'environnement
            // de la machine : elles peuvent contenir des jetons.
            env: {},
        });

        return await new Promise<TestReport>(resolve => {
            let settled = false;

            const finish = (report: TestReport) => {
                if (settled) return;
                settled = true;

                void worker.terminate();
                resolve(report);
            };

            const timer = setTimeout(() => finish({
                total: 0,
                echecs: [],
                erreurs: [],
                sortie: '',
                journal: '',
                interrompu: true,
                reussi: false,
            }), PythonRunner.Timeout);

            worker.on('message', message => {
                clearTimeout(timer);

                if (!message.ok) {
                    finish({
                        total: 0,
                        echecs: [],
                        erreurs: [{ nom: 'exécution', message: message.erreur, trace: message.erreur }],
                        sortie: '',
                        journal: '',
                        reussi: false,
                    });
                    return;
                }

                const rapport = message.rapport;
                finish({
                    ...rapport,
                    // Réussi veut dire : des tests ont tourné, et aucun n'a échoué. Un
                    // fichier vide passerait sinon pour un exercice réussi.
                    reussi: rapport.total > 0
                        && rapport.echecs.length === 0
                        && rapport.erreurs.length === 0,
                });
            });

            worker.on('error', error => {
                clearTimeout(timer);
                finish({
                    total: 0,
                    echecs: [],
                    erreurs: [{ nom: 'exécution', message: String(error.message), trace: String(error.stack) }],
                    sortie: '',
                    journal: '',
                    reussi: false,
                });
            });

            worker.on('exit', code => {
                // Un fil tué par le délai a déjà rendu son verdict ; ce cas-ci couvre une
                // sortie inattendue, qu'il vaut mieux signaler que laisser en attente.
                clearTimeout(timer);
                if (code !== 0) {
                    finish({
                        total: 0,
                        echecs: [],
                        erreurs: [{
                            nom: 'exécution',
                            message: `L'exécution s'est arrêtée (code ${code}).`,
                            trace: '',
                        }],
                        sortie: '',
                        journal: '',
                        reussi: false,
                    });
                }
            });
        });
    }
}
