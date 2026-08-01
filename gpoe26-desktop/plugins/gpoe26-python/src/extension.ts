import * as vscode from 'vscode';
import { Exercise, collectFiles, loadExercise, materialize } from './exercise';
import { PythonRunner, TestReport } from './runner';

/**
 * Le type d'exercice « Python », premier plugin de GPOE26.
 *
 * C'est un plugin et non une extension Theia, et la différence est le fait principal :
 * il tourne dans le processus hôte des plugins, séparé de l'atelier. Une erreur ici ne
 * fait pas tomber l'application de l'élève ; et le code de l'élève, lui, tourne encore
 * un cran plus loin, dans un fil qu'on peut tuer.
 *
 * Le cœur n'en connaît rien : il ne cite ce plugin nulle part. Un type d'exercice
 * s'ajoute en le déposant, sans recompiler l'application.
 */

/** Ce que le harness reçoit comme état courant quand l'élève interroge le répétiteur. */
const TUTOR_COMMAND = 'gpoe26.repetiteur.demander';

let runner: PythonRunner;
let output: vscode.OutputChannel;
let lastReport: TestReport | undefined;

export function activate(context: vscode.ExtensionContext): void {
    runner = new PythonRunner(vscode.Uri.joinPath(context.extensionUri, 'lib', 'worker.mjs').fsPath);
    output = vscode.window.createOutputChannel('Exercice Python');

    context.subscriptions.push(
        output,
        vscode.commands.registerCommand('gpoe26.python.executerTests', executerTests),
        vscode.commands.registerCommand('gpoe26.python.ouvrirExercice', ouvrirExercice),
        vscode.commands.registerCommand('gpoe26.python.demanderAide', demanderAide),
    );

    void ouvrirExercice();
}

// ── Ouvrir ────────────────────────────────────────────────────────────────────

async function ouvrirExercice(): Promise<void> {
    const loaded = await loadExercise();
    if (!loaded) return;

    const { exercise, folder } = loaded;
    await materialize(exercise, folder);

    const file = vscode.Uri.joinPath(folder, exercise.fichierEleve);
    const document = await vscode.workspace.openTextDocument(file);
    await vscode.window.showTextDocument(document);

    output.clear();
    output.appendLine(`── ${exercise.titre} ──`);
    output.appendLine('');
    output.appendLine(exercise.enonce);
    output.appendLine('');
    output.appendLine('Lancez les tests quand vous voulez : ils tournent sur votre machine, sans réseau.');
    output.show(true);
}

// ── Exécuter ──────────────────────────────────────────────────────────────────

async function executerTests(): Promise<void> {
    const loaded = await loadExercise();
    if (!loaded) {
        void vscode.window.showWarningMessage("Aucun exercice n'est ouvert.");
        return;
    }

    const { exercise, folder } = loaded;
    const files = await collectFiles(exercise, folder);

    output.clear();
    output.appendLine('Exécution des tests…');
    output.show(true);

    const report = await vscode.window.withProgress(
        { location: vscode.ProgressLocation.Window, title: 'Exécution des tests' },
        () => runner.run(files));

    lastReport = report;
    afficher(report, exercise);
}

function afficher(report: TestReport, exercise: Exercise): void {
    output.clear();

    if (report.interrompu) {
        // Le message le plus utile qu'on puisse donner ici : ce n'est pas une panne, c'est
        // un programme qui ne s'arrête pas, et l'élève doit savoir quoi chercher.
        output.appendLine('⏱  Vos tests ont été interrompus : le programme ne s\'arrêtait pas.');
        output.appendLine('');
        output.appendLine('C\'est souvent une boucle dont la condition ne devient jamais fausse.');
        output.appendLine('Relisez vos `while` : qu\'est-ce qui change à chaque tour ?');
        return;
    }

    if (report.chargementEchoue) {
        output.appendLine('⚠  Votre fichier n\'a pas pu être chargé.');
        output.appendLine('');
        output.appendLine('Ce n\'est pas une erreur de raisonnement : Python n\'arrive pas à lire votre code.');
        output.appendLine('Cherchez une faute de frappe — un deux-points oublié, une parenthèse non fermée.');
        output.appendLine('');
        output.appendLine(report.erreurs[0]?.trace ?? '');
        return;
    }

    if (report.sortie.trim().length > 0) {
        output.appendLine('Ce que votre programme a affiché :');
        output.appendLine(report.sortie.trim());
        output.appendLine('');
    }

    if (report.reussi) {
        output.appendLine(`✓  Les ${report.total} tests passent.`);
        output.appendLine('');
        output.appendLine('Vous pouvez valider cette étape.');

        void vscode.window.showInformationMessage(
            `${exercise.titre} — les ${report.total} tests passent.`);
        return;
    }

    const rates = [...report.echecs, ...report.erreurs];
    output.appendLine(`✗  ${rates.length} test(s) sur ${report.total} ne passent pas.`);
    output.appendLine('');

    for (const echec of rates) {
        output.appendLine(`  ${echec.nom}`);
        output.appendLine(`    ${echec.message}`);
        output.appendLine('');
    }

    output.appendLine('Le répétiteur peut vous mettre sur la piste sans donner la réponse :');
    output.appendLine('commande « Demander de l\'aide au répétiteur ».');
}

// ── Demander de l'aide ────────────────────────────────────────────────────────

/**
 * Passe la main au répétiteur, avec l'état courant.
 *
 * Le plugin ne parle pas au harness : il est isolé, et c'est voulu. Il passe par une
 * commande que le socle enregistre — le pont documenté entre l'hôte des plugins et
 * l'interface.
 *
 * Ce qu'on transmet est ce que l'agent ne peut pas deviner : le code de l'élève et ce que
 * ses tests ont dit. C'est aussi ce qui rend inutile, pour l'instant, un aller-retour
 * d'appel d'outil vers la machine de l'élève — l'agent voit déjà l'état.
 */
async function demanderAide(): Promise<void> {
    const loaded = await loadExercise();
    if (!loaded) return;

    const { exercise, folder } = loaded;
    const files = await collectFiles(exercise, folder);

    const contexte: Record<string, string> = {
        'Exercice': exercise.titre,
        'Énoncé': exercise.enonce,
        'Code de l\'élève': files[exercise.fichierEleve] ?? '(vide)',
    };

    if (lastReport) {
        contexte['Résultat des tests'] = lastReport.interrompu
            ? 'Interrompu : le programme ne s\'arrêtait pas.'
            : lastReport.reussi
                ? `Les ${lastReport.total} tests passent.`
                : [...lastReport.echecs, ...lastReport.erreurs]
                    .map(e => `${e.nom} : ${e.message}`)
                    .join('\n');
    }

    try {
        await vscode.commands.executeCommand(TUTOR_COMMAND, {
            courseId: exercise.courseId,
            stepId: exercise.stepId,
            message: 'Je bloque sur cet exercice. Peux-tu me mettre sur la piste ?',
            contexte,
        });
    } catch {
        // Le socle n'a pas enregistré la commande : on le dit plutôt que d'échouer en
        // silence, l'élève attendrait une réponse qui ne viendrait jamais.
        void vscode.window.showWarningMessage(
            "Le répétiteur n'est pas disponible depuis cet exercice.");
    }
}

export function deactivate(): void {
    // Rien à défaire : les fils d'exécution sont tués à la fin de chaque lancement.
}
