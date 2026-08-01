/*
 * Le code de l'élève tourne ici, et nulle part ailleurs.
 *
 * Un fil d'exécution dédié, pour une raison qui n'est pas cosmétique : on ne peut pas
 * interrompre du JavaScript ni du WASM synchrone. Une boucle infinie dans le code d'un
 * élève ne se règle pas par un `setTimeout` — il faut pouvoir tuer le fil qui l'exécute.
 * `worker.terminate()` le permet ; rien d'autre ne le permet.
 *
 * D'où deux enveloppes plutôt qu'une : le plugin tourne déjà dans le processus hôte des
 * plugins, séparé de l'atelier, et l'exécution tourne dans un fil de ce processus. Un
 * élève qui écrit `while True: pass` fige son exercice, pas son application.
 */

import { parentPort, workerData } from 'node:worker_threads';
import { loadPyodide } from 'pyodide';

/** Ce que Python écrit sur la sortie standard, rendu à l'élève tel quel. */
const output = [];

/**
 * Le programme qui exécute les tests et rend un compte rendu lisible.
 *
 * Écrit en Python plutôt qu'en JavaScript parce que c'est unittest qui connaît le
 * détail des échecs : l'assertion qui a lâché, la ligne, la valeur obtenue. Reconstruire
 * cela depuis JavaScript reviendrait à réécrire une partie d'unittest.
 */
const RUNNER = `
import io, json, sys, traceback, unittest

def _executer():
    chargeur = unittest.TestLoader()
    suite = chargeur.loadTestsFromNames(['test_exercice'])

    flux = io.StringIO()
    resultat = unittest.TextTestRunner(stream=flux, verbosity=2).run(suite)

    def _detail(cas, trace):
        # On ne renvoie que la dernière ligne significative : la pile complète parle du
        # lanceur de tests, pas de l'erreur de l'élève, et le noyer sous des chemins de
        # fichiers qu'il ne connaît pas ne l'aide en rien.
        lignes = [l.strip() for l in trace.strip().split('\\n') if l.strip()]
        return {
            'nom': cas.id().split('.')[-1],
            'message': lignes[-1] if lignes else 'échec',
            'trace': trace.strip(),
        }

    return {
        'total': resultat.testsRun,
        'echecs': [_detail(c, t) for c, t in resultat.failures],
        'erreurs': [_detail(c, t) for c, t in resultat.errors],
        'journal': flux.getvalue(),
    }

try:
    _rapport = _executer()
except Exception:
    # Une erreur ici n'est pas un test qui échoue : c'est le fichier de l'élève qui ne
    # se charge pas — une faute de syntaxe, un import manquant. À distinguer, sinon
    # l'élève cherche un bug de logique là où il a oublié un deux-points.
    _rapport = {
        'total': 0,
        'echecs': [],
        'erreurs': [{'nom': 'chargement', 'message': 'Le fichier de test n\\'a pas pu être chargé.',
                     'trace': traceback.format_exc()}],
        'journal': '',
        'chargementEchoue': True,
    }

json.dumps(_rapport)
`;

async function executer() {
    const pyodide = await loadPyodide({
        stdout: line => output.push(line),
        stderr: line => output.push(line),
    });

    // Les fichiers de l'exercice sont posés dans le système de fichiers virtuel de
    // Pyodide : le code de l'élève ne voit jamais le disque de sa machine.
    for (const [chemin, contenu] of Object.entries(workerData.files)) {
        pyodide.FS.writeFile(chemin, contenu, { encoding: 'utf8' });
    }

    pyodide.runPython("import sys; sys.path.insert(0, '')");

    const rapport = JSON.parse(pyodide.runPython(RUNNER));
    return { ...rapport, sortie: output.join('\n') };
}

executer().then(
    rapport => parentPort?.postMessage({ ok: true, rapport }),
    erreur => parentPort?.postMessage({ ok: false, erreur: String(erreur?.message ?? erreur) })
);
