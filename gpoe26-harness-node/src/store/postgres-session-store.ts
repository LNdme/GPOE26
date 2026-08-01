import pg from 'pg';
import type { SessionKind } from '../contract.js';
import type { SessionStore, StoredSession, StoredTurn } from './session-store.js';

/**
 * Le magasin persistant.
 *
 * Base propre (`qmdb`) plutôt que la table du harness .NET : les deux implémentations
 * doivent pouvoir tourner en même temps sur les mêmes élèves pendant la comparaison. Un
 * schéma partagé les obligerait à s'accorder sur autre chose que le contrat, ce qui
 * fausserait le match.
 */
export class PostgresSessionStore implements SessionStore {

    private readonly pool: pg.Pool;

    constructor(connectionString: string) {
        this.pool = new pg.Pool({ connectionString });
    }

    /** Crée le schéma s'il manque. Une seule table, en JSONB : ce harness ne l'interroge jamais autrement que par identifiant. */
    async migrate(): Promise<void> {
        await this.pool.query(`
            CREATE TABLE IF NOT EXISTS sessions (
                id            uuid PRIMARY KEY,
                student_id    uuid NOT NULL,
                course_id     uuid NOT NULL,
                kind          smallint NOT NULL,
                started_at    timestamptz NOT NULL,
                last_activity timestamptz NOT NULL,
                compacted_at  timestamptz,
                summary       text,
                turns         jsonb NOT NULL DEFAULT '[]'::jsonb
            );

            CREATE INDEX IF NOT EXISTS sessions_student_course
                ON sessions (student_id, course_id, last_activity DESC);
        `);
    }

    async open(studentId: string, courseId: string, kind: SessionKind): Promise<StoredSession> {
        const now = new Date().toISOString();

        const { rows } = await this.pool.query(
            `INSERT INTO sessions (id, student_id, course_id, kind, started_at, last_activity)
             VALUES (gen_random_uuid(), $1, $2, $3, $4, $4)
             RETURNING id`,
            [studentId, courseId, kind, now]);

        return {
            id: rows[0].id, studentId, courseId, kind,
            startedAt: now, lastActivityAt: now, turns: [],
        };
    }

    async find(id: string): Promise<StoredSession | undefined> {
        const { rows } = await this.pool.query('SELECT * FROM sessions WHERE id = $1', [id]);
        const row = rows[0];
        if (!row) return undefined;

        return {
            id: row.id,
            studentId: row.student_id,
            courseId: row.course_id,
            kind: row.kind as SessionKind,
            startedAt: new Date(row.started_at).toISOString(),
            lastActivityAt: new Date(row.last_activity).toISOString(),
            compactedAt: row.compacted_at ? new Date(row.compacted_at).toISOString() : undefined,
            summary: row.summary ?? undefined,
            turns: (row.turns ?? []) as StoredTurn[],
        };
    }

    async append(id: string, turns: StoredTurn[]): Promise<void> {
        // Concaténation côté base : deux onglets ouverts sur le même cours écriraient
        // sinon l'un par-dessus l'autre.
        await this.pool.query(
            `UPDATE sessions
                SET turns = turns || $2::jsonb,
                    last_activity = now()
              WHERE id = $1`,
            [id, JSON.stringify(turns)]);
    }

    async compact(id: string, summary: string, upToIndex: number): Promise<void> {
        const session = await this.find(id);
        if (!session) return;

        for (const turn of session.turns) {
            if (turn.index <= upToIndex) turn.compacted = true;
        }

        await this.pool.query(
            `UPDATE sessions SET turns = $2::jsonb, summary = $3, compacted_at = now() WHERE id = $1`,
            [id, JSON.stringify(session.turns), summary]);
    }

    async close(): Promise<void> {
        await this.pool.end();
    }
}
