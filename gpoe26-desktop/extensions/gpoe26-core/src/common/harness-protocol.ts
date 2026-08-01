/*
 * Le contrat du harness, côté TypeScript.
 *
 * Miroir de GPOE26.Harness.Contract. Deux règles à ne pas perdre de vue :
 *
 *  · Les enums transitent en NOMBRES entre les services — aucun convertisseur de chaînes
 *    n'est enregistré côté .NET. L'ordre des valeurs porte donc du sens : on ajoute à la
 *    fin, on ne réordonne jamais.
 *
 *  · Le schéma d'évènements est une extension stricte de celui du Web. Un évènement d'un
 *    type inconnu doit être ignoré, pas rejeté : c'est ce qui permet au harness d'évoluer
 *    sans casser les clients déjà déployés.
 */

/** Ce que l'élève est en train de faire. Détermine les outils exposés. */
export enum SessionKind {
    Lecture = 0,
    Exercice = 1,
    Code = 2,
    Suivi = 3
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

// ── Évènements du flux ────────────────────────────────────────────────────────

export const HarnessEventType = {
    Step: 'step',
    Token: 'token',
    ToolCall: 'tool_call',
    ToolResult: 'tool_result',
    Done: 'done',
    Error: 'error'
} as const;

export type HarnessEventTypeValue = typeof HarnessEventType[keyof typeof HarnessEventType];

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

/** Un tour se termine sur `done` ou `error`, jamais sur autre chose. */
export function isTerminal(event: HarnessEvent): boolean {
    return event.type === HarnessEventType.Done || event.type === HarnessEventType.Error;
}

// ── Le cours rendu ────────────────────────────────────────────────────────────

export interface OutlineEntryDto {
    level: number;
    title: string;
    anchor: string;
}

export interface RenderedCourseDto {
    courseId: string;
    title: string;
    subject: string;
    html: string;
    outline: OutlineEntryDto[];
    renderedAt: string;
}

// ── Synchronisation hors ligne ────────────────────────────────────────────────

export enum StudyActivityKind {
    Lecture = 0,
    Question = 1,
    Etape = 2,
    Exercice = 3
}

/**
 * Un signal d'activité, horodaté par son émetteur.
 *
 * `occurredAt` est ce qui rend le travail hors ligne mesurable : sans lui, une séance
 * d'hier soir remontée ce matin serait datée de la synchronisation et ferait apparaître
 * une nuit entière de révision.
 */
export interface ActivitySignal {
    courseId: string;
    kind: StudyActivityKind;
    occurredAt: string;
}

export interface OfflineStepResult {
    courseId: string;
    stepId: string;
    score: number;
    total: number;
    completedAt: string;
    weakHeadings?: string[];
    studentAnswer?: string;
    correctionSummary?: string;
    /** Verdict produit sur l'appareil de l'élève, donc falsifiable : jamais présenté comme une note vérifiée. */
    selfAssessed?: boolean;
}

export interface SyncBatch {
    activities: ActivitySignal[];
    results: OfflineStepResult[];
}

export interface SyncResult {
    activitiesAccepted: number;
    resultsAccepted: number;
    rejected: string[];
}

// ── Authentification ──────────────────────────────────────────────────────────

export interface UserProfile {
    id: string;
    email: string;
    username: string;
    role: string;
    level?: string;
    specialite?: string;
    filiere?: string;
    language: string;
}

export interface AuthResponse {
    token: string;
    expiresAt: string;
    profile: UserProfile;
    refreshToken?: string;
    refreshExpiresAt?: string;
}
