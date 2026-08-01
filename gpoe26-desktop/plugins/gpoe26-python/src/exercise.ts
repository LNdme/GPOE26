import * as vscode from 'vscode';

/**
 * Un exercice de code, tel qu'il est déposé dans l'espace de travail de l'élève.
 *
 * Le fichier est écrit par l'application quand l'élève ouvre l'étape ; le plugin ne fait
 * que le lire. C'est ce qui permet à l'exercice d'exister hors ligne : il est sur le
 * disque, avec ses tests, et rien de ce qui suit ne demande le réseau.
 */
export interface Exercise {
    id: string;
    courseId: string;
    /** L'étape du parcours que cet exercice valide, s'il en valide une. */
    stepId?: string;
    titre: string;
    /** L'énoncé, en Markdown. */
    enonce: string;
    /** Le fichier que l'élève modifie. Les autres sont fournis et ne se touchent pas. */
    fichierEleve: string;
    fichiers: Record<string, string>;
}

export const EXERCISE_FILE = 'exercice.json';

/**
 * Charge l'exercice de l'espace de travail ouvert.
 *
 * Renvoie `undefined` plutôt que de lever : ouvrir un dossier qui n'est pas un exercice
 * est le cas ordinaire, pas une erreur.
 */
export async function loadExercise(): Promise<{ exercise: Exercise; folder: vscode.Uri } | undefined> {
    const folders = vscode.workspace.workspaceFolders;
    if (!folders?.length) return undefined;

    for (const folder of folders) {
        const file = vscode.Uri.joinPath(folder.uri, EXERCISE_FILE);

        try {
            const bytes = await vscode.workspace.fs.readFile(file);
            const exercise = JSON.parse(new TextDecoder().decode(bytes)) as Exercise;

            if (exercise.fichierEleve && exercise.fichiers) {
                return { exercise, folder: folder.uri };
            }
        } catch {
            // Dossier sans exercice, ou fichier illisible : on essaie le suivant.
        }
    }

    return undefined;
}

/**
 * Les fichiers à exécuter : ceux de l'exercice, mais avec la version de l'élève pour
 * celui qu'il modifie.
 *
 * On lit le disque plutôt que l'éditeur ouvert, et on lit l'éditeur s'il a des
 * modifications non enregistrées — sinon l'élève lance ses tests, voit l'ancien résultat,
 * et cherche l'erreur dans du code qu'il vient de corriger.
 */
export async function collectFiles(
    exercise: Exercise, folder: vscode.Uri): Promise<Record<string, string>> {

    const files = { ...exercise.fichiers };
    const studentFile = vscode.Uri.joinPath(folder, exercise.fichierEleve);

    const open = vscode.workspace.textDocuments.find(d => d.uri.toString() === studentFile.toString());
    if (open) {
        files[exercise.fichierEleve] = open.getText();
        return files;
    }

    try {
        const bytes = await vscode.workspace.fs.readFile(studentFile);
        files[exercise.fichierEleve] = new TextDecoder().decode(bytes);
    } catch {
        // Fichier absent : on garde la version de départ. L'élève verra ses tests échouer
        // sur le code fourni, ce qui est exactement l'état de son exercice.
    }

    return files;
}

/** Dépose les fichiers de départ, sans écraser le travail déjà commencé. */
export async function materialize(exercise: Exercise, folder: vscode.Uri): Promise<void> {
    for (const [name, content] of Object.entries(exercise.fichiers)) {
        const file = vscode.Uri.joinPath(folder, name);

        // Le fichier de l'élève ne s'écrase jamais : il contient son travail.
        if (name === exercise.fichierEleve) {
            try {
                await vscode.workspace.fs.stat(file);
                continue;
            } catch {
                // Absent : c'est le premier passage, on pose la version de départ.
            }
        }

        await vscode.workspace.fs.writeFile(file, new TextEncoder().encode(content));
    }
}
