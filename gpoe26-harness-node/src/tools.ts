import { SessionKind, ToolNames, toolsFor } from './contract.js';
import type { ToolDefinition } from './drivers/driver.js';

/**
 * Accès au service Cours, avec relais du JWT.
 *
 * Le jeton de l'élève est réémis tel quel : les endpoints de Cours filtrent sur le
 * propriétaire, et c'est ce relais qui garantit qu'un élève ne travaille que ses propres
 * cours. Un appel sous une identité de service contournerait le contrôle même qu'on veut
 * conserver.
 */
export class CoursClient {

    constructor(private readonly baseUrl: string) { }

    async search(token: string, courseId: string, query: string, k = 5): Promise<Passage[]> {
        return await this.json<Passage[]>(token, `/cours/${courseId}/search`, {
            method: 'POST',
            body: JSON.stringify({ query, k }),
        }) ?? [];
    }

    async course(token: string, courseId: string): Promise<CourseSnapshot | undefined> {
        return await this.json<CourseSnapshot>(token, `/cours/${courseId}`);
    }

    async journey(token: string, courseId: string): Promise<Journey | undefined> {
        return await this.json<Journey>(token, `/cours/${courseId}/parcours`);
    }

    private async json<T>(token: string, path: string, init: RequestInit = {}): Promise<T | undefined> {
        try {
            const response = await fetch(`${this.baseUrl}${path}`, {
                ...init,
                headers: {
                    'content-type': 'application/json',
                    'authorization': `Bearer ${token}`,
                    ...(init.headers ?? {}),
                },
            });

            return response.ok ? await response.json() as T : undefined;
        } catch {
            return undefined;
        }
    }
}

export interface Passage { chunkId: string; headingPath: string; content: string; score: number; }
export interface CourseSnapshot { id: string; title: string; subject: string; formattedMarkdown?: string; extractedText?: string; }
export interface Journey { steps: { title: string; status: string; score?: number; total?: number }[]; passedCount: number; }

/**
 * Les outils exposés à une session.
 *
 * C'est ici qu'« enregistrement ≠ exposition » cesse d'être une intention : ce que le
 * pilote reçoit est exactement ce que la table autorise. Un outil non listé n'est pas
 * caché, il est inappelable.
 */
export function buildTools(
    kind: SessionKind,
    cours: CoursClient,
    token: string,
    courseId: string,
    courseTitle: string,
): ToolDefinition[] {

    const all: Record<string, ToolDefinition> = {
        [ToolNames.ChercherDansLeCours]: {
            name: ToolNames.ChercherDansLeCours,
            description: "Cherche dans le cours de l'élève les passages qui traitent d'une question. "
                + 'À utiliser avant toute explication : les réponses doivent venir du cours.',
            parameters: {
                type: 'object',
                properties: {
                    requete: { type: 'string', description: 'Ce qu\'on cherche, formulé comme dans le cours.' },
                    nombre: { type: 'integer', description: '5 pour une notion précise, 12 pour une vue d\'ensemble.' },
                },
                required: ['requete'],
            },
            invoke: async args => {
                const passages = await cours.search(
                    token, courseId, String(args.requete ?? courseTitle),
                    clamp(Number(args.nombre ?? 5), 1, 20));

                if (passages.length === 0) {
                    return "Aucun passage du cours ne traite de cela. Dis-le à l'élève plutôt que d'inventer.";
                }

                return passages
                    .map(p => `--- [§ ${p.headingPath}] ---\n${p.content}`)
                    .join('\n\n');
            },
        },

        [ToolNames.OuEnEstLEleve]: {
            name: ToolNames.OuEnEstLEleve,
            description: "Donne l'état du parcours de l'élève sur ce cours : ce qu'il a validé, ce qui reste.",
            parameters: { type: 'object', properties: {} },
            invoke: async () => {
                const journey = await cours.journey(token, courseId);
                if (!journey?.steps?.length) return "Le parcours de ce cours n'est pas encore construit.";

                const lines = journey.steps.map(s => {
                    const score = s.score != null && s.total != null ? ` (${s.score}/${s.total})` : '';
                    return `- ${s.title} : ${s.status}${score}`;
                });

                return `${journey.passedCount} étape(s) validée(s) sur ${journey.steps.length}.\n${lines.join('\n')}`;
            },
        },

        [ToolNames.LireAVoixHaute]: {
            name: ToolNames.LireAVoixHaute,
            description: "Prépare la lecture à voix haute d'une explication, pour un élève qui comprend mieux en écoutant.",
            parameters: {
                type: 'object',
                properties: { texte: { type: 'string', description: 'Le texte à lire. Court.' } },
                required: ['texte'],
            },
            // La synthèse vocale vit dans le harness .NET, qui porte déjà le cache audio.
            // Dupliquer la passerelle ici ferait payer deux fois le même son.
            invoke: async () => 'La lecture à voix haute est indisponible depuis ce harness.',
        },

        [ToolNames.GenererExercice]: {
            name: ToolNames.GenererExercice,
            description: 'Produit un exercice de consolidation sur le cours, ou sur une de ses parties.',
            parameters: {
                type: 'object',
                properties: { partie: { type: 'string', description: 'Partie visée. Vide pour tout le cours.' } },
            },
            invoke: async args => {
                const passages = await cours.search(
                    token, courseId, String(args.partie ?? courseTitle), args.partie ? 6 : 10);

                return passages.length === 0
                    ? "Le cours ne fournit pas de quoi construire un exercice."
                    : `Construis un exercice à partir de ces extraits :\n\n${
                        passages.map(p => `--- [§ ${p.headingPath}] ---\n${p.content}`).join('\n\n')}`;
            },
        },

        [ToolNames.CorrigerReponse]: {
            name: ToolNames.CorrigerReponse,
            description: "Rassemble ce qu'il faut pour corriger une réponse de l'élève : les passages du cours qui la concernent.",
            parameters: {
                type: 'object',
                properties: {
                    enonce: { type: 'string' },
                    reponse: { type: 'string' },
                },
                required: ['enonce', 'reponse'],
            },
            invoke: async args => {
                const passages = await cours.search(token, courseId, String(args.enonce ?? ''), 6);

                return `Extraits du cours pour corriger :\n\n${
                    passages.map(p => `--- [§ ${p.headingPath}] ---\n${p.content}`).join('\n\n')}`;
            },
        },
    };

    // L'espace de travail vit sur la machine de l'élève, où les tests tournent en WASM
    // hors ligne. Ces outils sont des délégations : ils disent honnêtement qu'ils ne
    // peuvent pas agir, plutôt que d'échouer d'une façon que le modèle interpréterait mal.
    const pending = "L'espace de travail de l'élève n'est pas accessible depuis cette session. "
        + 'Demande-lui ce que contient son fichier ou ce que disent ses tests.';

    for (const [name, params] of [
        [ToolNames.LireFichier, { chemin: { type: 'string' } }],
        [ToolNames.EcrireFichier, { chemin: { type: 'string' }, contenu: { type: 'string' } }],
        [ToolNames.ExecuterTests, {}],
    ] as const) {
        all[name] = {
            name,
            description: `Outil d'espace de travail (${name}).`,
            parameters: { type: 'object', properties: params },
            invoke: async () => pending,
        };
    }

    return toolsFor(kind).map(name => all[name]).filter((t): t is ToolDefinition => t !== undefined);
}

function clamp(value: number, min: number, max: number): number {
    return Number.isFinite(value) ? Math.min(max, Math.max(min, value)) : min;
}
