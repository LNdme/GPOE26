/*
 * Le contrat, côté harness B.
 *
 * Miroir de GPOE26.Harness.Contract, comme celui de l'application bureau. La duplication
 * est assumée : le contrat est une frontière entre implémentations, et une frontière
 * partagée par référence n'en est plus une — si les deux harness importaient les mêmes
 * types, un changement dans l'un passerait dans l'autre sans qu'on l'ait voulu.
 *
 * C'est la suite de conformité qui garantit qu'ils disent la même chose, pas le compilateur.
 *
 * ⚠️ Les enums transitent en NOMBRES : l'ordre porte du sens.
 */

export enum SessionKind {
    Lecture = 0,
    Exercice = 1,
    Code = 2,
    Suivi = 3,
}

export interface OpenSessionRequest {
    courseId: string;
    kind: SessionKind;
    studentId?: string;
}

export interface SessionDto {
    id: string;
    studentId: string;
    courseId: string;
    kind: SessionKind;
    startedAt: string;
    lastActivityAt: string;
    turns: number;
    compactedAt?: string;
    tools: string[];
}

export interface TurnRequest {
    message: string;
    context?: Record<string, string>;
}

export interface TurnDto {
    index: number;
    role: 'user' | 'assistant';
    content: string;
    at: string;
    citations?: string[];
}

export interface SessionHistoryDto {
    session: SessionDto;
    turns: TurnDto[];
}

export const HarnessEventType = {
    Step: 'step',
    Token: 'token',
    ToolCall: 'tool_call',
    ToolResult: 'tool_result',
    Done: 'done',
    Error: 'error',
} as const;

export interface HarnessEvent {
    type: string;
    step?: string;
    token?: string;
    reply?: string;
    citations?: string[];
    intent?: string;
    error?: string;
    tool?: string;
    toolArguments?: string;
    toolResult?: string;
    iteration?: number;
}

export function isTerminal(event: HarnessEvent): boolean {
    return event.type === HarnessEventType.Done || event.type === HarnessEventType.Error;
}

export const evt = {
    step: (label: string): HarnessEvent => ({ type: HarnessEventType.Step, step: label }),
    token: (token: string): HarnessEvent => ({ type: HarnessEventType.Token, token }),
    toolCall: (tool: string, args: string | undefined, iteration: number): HarnessEvent =>
        ({ type: HarnessEventType.ToolCall, tool, toolArguments: args, iteration }),
    toolResult: (tool: string, summary: string | undefined, iteration: number): HarnessEvent =>
        ({ type: HarnessEventType.ToolResult, tool, toolResult: summary, iteration }),
    done: (reply: string, citations?: string[]): HarnessEvent =>
        ({ type: HarnessEventType.Done, reply, citations }),
    error: (message: string): HarnessEvent => ({ type: HarnessEventType.Error, error: message }),
};

// ── Outils ────────────────────────────────────────────────────────────────────

export const ToolNames = {
    ChercherDansLeCours: 'chercher_dans_le_cours',
    LireAVoixHaute: 'lire_a_voix_haute',
    OuEnEstLEleve: 'ou_en_est_l_eleve',
    GenererExercice: 'generer_exercice',
    CorrigerReponse: 'corriger_reponse',
    LireFichier: 'lire_fichier',
    EcrireFichier: 'ecrire_fichier',
    ExecuterTests: 'executer_tests',
} as const;

export interface ToolDescriptor {
    name: string;
    description: string;
    parameters: string;
    mutating: boolean;
}

const LECTURE = [ToolNames.ChercherDansLeCours, ToolNames.LireAVoixHaute, ToolNames.OuEnEstLEleve];
const EXERCICE = [...LECTURE, ToolNames.GenererExercice, ToolNames.CorrigerReponse];
const CODE = [...EXERCICE, ToolNames.LireFichier, ToolNames.EcrireFichier, ToolNames.ExecuterTests];

/**
 * Enregistrement ≠ exposition — la même table que côté .NET, écrite ici plutôt
 * qu'importée, pour la raison dite en tête de fichier.
 *
 * Une session de suivi n'expose aucun outil : un parent consulte, il n'agit pas. Ce n'est
 * pas un choix d'affichage — un outil non exposé est inappelable, quoi que le modèle décide.
 */
export function toolsFor(kind: SessionKind): string[] {
    switch (kind) {
        case SessionKind.Lecture: return [...LECTURE];
        case SessionKind.Exercice: return [...EXERCICE];
        case SessionKind.Code: return [...CODE];
        default: return [];
    }
}

export function toolAllowed(kind: SessionKind, tool: string): boolean {
    return toolsFor(kind).includes(tool);
}

/**
 * Qui peut ouvrir quelle nature de session.
 *
 * Un parent ou un enseignant ne peut ouvrir qu'un suivi : lui laisser une session de
 * lecture au nom de son enfant reviendrait à lui donner le répétiteur de l'enfant, et
 * donc à brouiller la frontière entre suivre et lire par-dessus l'épaule.
 */
export function canOpen(role: string | undefined, kind: SessionKind): boolean {
    const parentOrTeacher = role?.toLowerCase() === 'parent' || role?.toLowerCase() === 'teacher';
    return parentOrTeacher ? kind === SessionKind.Suivi : kind !== SessionKind.Suivi;
}

// ── Cours et synchronisation ──────────────────────────────────────────────────

export interface OutlineEntryDto { level: number; title: string; anchor: string; }

export interface RenderedCourseDto {
    courseId: string;
    title: string;
    subject: string;
    html: string;
    outline: OutlineEntryDto[];
    renderedAt: string;
}

export interface SyncBatch {
    activities: unknown[];
    results: unknown[];
}

export interface SyncResult {
    activitiesAccepted: number;
    resultsAccepted: number;
    rejected: string[];
}
