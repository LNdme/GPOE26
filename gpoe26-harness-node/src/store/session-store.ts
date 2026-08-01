import { randomUUID } from 'node:crypto';
import type { SessionKind } from '../contract.js';

export interface StoredTurn {
    index: number;
    role: 'user' | 'assistant';
    content: string;
    at: string;
    citations?: string[];
    toolsUsed?: string[];
    compacted?: boolean;
}

export interface StoredSession {
    id: string;
    studentId: string;
    courseId: string;
    kind: SessionKind;
    startedAt: string;
    lastActivityAt: string;
    compactedAt?: string;
    summary?: string;
    turns: StoredTurn[];
}

/**
 * Le magasin de sessions.
 *
 * Deux implémentations derrière une interface — l'idée vient de QM, qui a exactement ce
 * découpage. Elle paie ici plus qu'ailleurs : la suite de conformité peut interroger ce
 * harness sans base de données, donc dans une intégration continue, alors qu'elle vérifie
 * des règles qui n'ont rien à voir avec la persistance.
 */
export interface SessionStore {
    open(studentId: string, courseId: string, kind: SessionKind): Promise<StoredSession>;
    find(id: string): Promise<StoredSession | undefined>;
    append(id: string, turns: StoredTurn[]): Promise<void>;
    compact(id: string, summary: string, upToIndex: number): Promise<void>;
    close(): Promise<void>;
}

/** En mémoire : pour les tests, et pour un développement qui ne veut pas lancer Postgres. */
export class MemorySessionStore implements SessionStore {

    private readonly sessions = new Map<string, StoredSession>();

    async open(studentId: string, courseId: string, kind: SessionKind): Promise<StoredSession> {
        const now = new Date().toISOString();
        const session: StoredSession = {
            id: randomUUID(),
            studentId, courseId, kind,
            startedAt: now,
            lastActivityAt: now,
            turns: [],
        };

        this.sessions.set(session.id, session);
        return session;
    }

    async find(id: string): Promise<StoredSession | undefined> {
        return this.sessions.get(id);
    }

    async append(id: string, turns: StoredTurn[]): Promise<void> {
        const session = this.sessions.get(id);
        if (!session) return;

        session.turns.push(...turns);
        session.lastActivityAt = new Date().toISOString();
    }

    async compact(id: string, summary: string, upToIndex: number): Promise<void> {
        const session = this.sessions.get(id);
        if (!session) return;

        // On marque plutôt qu'on ne supprime : l'historique reste consultable, seul le
        // contexte envoyé au modèle rétrécit. Un élève qui rouvre une session doit
        // pouvoir relire ce qu'il a demandé, même si l'agent ne le rejoue plus.
        for (const turn of session.turns) {
            if (turn.index <= upToIndex) turn.compacted = true;
        }

        session.summary = summary;
        session.compactedAt = new Date().toISOString();
    }

    async close(): Promise<void> {
        this.sessions.clear();
    }
}
