import { injectable } from '@theia/core/shared/inversify';
import * as fs from 'fs/promises';
import * as os from 'os';
import * as path from 'path';
import { ActivitySignal, OfflineStepResult, RenderedCourseDto } from '../common/harness-protocol';
import { CachedCourse, LocalStore, PendingWork, StoredCredentials } from '../common/store-protocol';

/**
 * Le magasin local, en fichiers JSON.
 *
 * Un cours par fichier, une file d'attente, un fichier d'identité. Le volume le permet
 * largement — quelques cours et une file de signaux — et cela évite de faire dépendre
 * l'installation d'une chaîne de compilation native.
 *
 * Deux précautions qui comptent plus que le format :
 *
 *  · **Écriture atomique.** On écrit dans un fichier temporaire puis on renomme. Une
 *    coupure de courant au mauvais moment laisserait sinon un JSON tronqué, et l'élève
 *    perdrait le cours qu'il venait de télécharger — au moment précis où il n'a plus de
 *    réseau pour le retélécharger.
 *
 *  · **La file n'est jamais purgée avant confirmation.** Perdre du travail est pire que
 *    le remonter deux fois : le serveur est idempotent sur ces signaux.
 */
@injectable()
export class FileLocalStore implements LocalStore {

    private readonly root = process.env.GPOE26_DATA_DIR
        ?? path.join(os.homedir(), '.gpoe26');

    private readonly coursesDir = path.join(this.root, 'cours');
    private readonly queueFile = path.join(this.root, 'file-attente.json');
    private readonly credentialsFile = path.join(this.root, 'identite.json');

    // ── Cours ────────────────────────────────────────────────────────────────

    async listCourses(): Promise<CachedCourse[]> {
        await fs.mkdir(this.coursesDir, { recursive: true });

        const files = await fs.readdir(this.coursesDir);
        const courses: CachedCourse[] = [];

        for (const file of files) {
            if (!file.endsWith('.json')) continue;

            const course = await this.readJson<CachedCourse>(path.join(this.coursesDir, file));
            if (course) courses.push(course);
        }

        return courses;
    }

    async getCourse(courseId: string): Promise<CachedCourse | undefined> {
        return await this.readJson<CachedCourse>(this.courseFile(courseId));
    }

    async putCourse(course: RenderedCourseDto): Promise<void> {
        // On conserve la progression de lecture d'une version à l'autre : rafraîchir un
        // cours ne doit pas renvoyer l'élève au début.
        const existing = await this.getCourse(course.courseId);

        await this.writeJson(this.courseFile(course.courseId), {
            course,
            readProgress: existing?.readProgress ?? 0,
            cachedAt: new Date().toISOString()
        } satisfies CachedCourse);
    }

    async setReadProgress(courseId: string, progress: number): Promise<void> {
        const cached = await this.getCourse(courseId);
        if (!cached) return;

        // On ne recule jamais : revenir en haut de la page pour relire un passage ne doit
        // pas effacer le fait qu'on a lu jusqu'au bout.
        cached.readProgress = Math.max(cached.readProgress, Math.min(1, Math.max(0, progress)));
        await this.writeJson(this.courseFile(courseId), cached);
    }

    // ── File d'attente ───────────────────────────────────────────────────────

    async queueActivity(signal: ActivitySignal): Promise<void> {
        const pending = await this.peekPending();
        pending.activities.push(signal);
        await this.writeJson(this.queueFile, pending);
    }

    async queueResult(result: OfflineStepResult): Promise<void> {
        const pending = await this.peekPending();
        pending.results.push(result);
        await this.writeJson(this.queueFile, pending);
    }

    async peekPending(): Promise<PendingWork> {
        return await this.readJson<PendingWork>(this.queueFile)
            ?? { activities: [], results: [] };
    }

    async clearPending(activities: number, results: number): Promise<void> {
        const pending = await this.peekPending();

        // On retire par le début, dans l'ordre d'empilement : le serveur a répondu combien
        // il retenait, pas lesquels. Ce qui a été refusé reste donc en tête et repartira.
        pending.activities = pending.activities.slice(activities);
        pending.results = pending.results.slice(results);

        await this.writeJson(this.queueFile, pending);
    }

    // ── Identité ─────────────────────────────────────────────────────────────

    async readCredentials(): Promise<StoredCredentials | undefined> {
        return await this.readJson<StoredCredentials>(this.credentialsFile);
    }

    async writeCredentials(credentials: StoredCredentials): Promise<void> {
        await this.writeJson(this.credentialsFile, credentials);

        // Lisible par le seul propriétaire : le jeton de rafraîchissement vaut trente
        // jours d'accès au compte.
        await fs.chmod(this.credentialsFile, 0o600).catch(() => undefined);
    }

    async clearCredentials(): Promise<void> {
        await fs.rm(this.credentialsFile, { force: true });
    }

    // ── Interne ──────────────────────────────────────────────────────────────

    private courseFile(courseId: string): string {
        // L'identifiant vient du serveur et devrait être un GUID, mais il finit dans un
        // chemin : on le nettoie plutôt que de faire confiance.
        const safe = courseId.replace(/[^a-zA-Z0-9-]/g, '');
        return path.join(this.coursesDir, `${safe}.json`);
    }

    private async readJson<T>(file: string): Promise<T | undefined> {
        try {
            return JSON.parse(await fs.readFile(file, 'utf8')) as T;
        } catch (error) {
            const code = (error as NodeJS.ErrnoException).code;
            if (code === 'ENOENT') return undefined;

            // Fichier illisible ou corrompu : on ne fait pas tomber l'application pour
            // autant. L'élève perd ce fichier, pas sa session.
            console.warn(`[gpoe26] ${file} illisible :`, error);
            return undefined;
        }
    }

    private async writeJson(file: string, value: unknown): Promise<void> {
        await fs.mkdir(path.dirname(file), { recursive: true });

        // Écriture atomique : un fichier temporaire puis un renommage. Sans cela, une
        // coupure en pleine écriture laisserait un JSON tronqué — et l'élève perdrait le
        // cours qu'il venait de télécharger, au moment précis où il n'a plus de réseau.
        const temporary = `${file}.${process.pid}.tmp`;

        await fs.writeFile(temporary, JSON.stringify(value, undefined, 2), 'utf8');
        await fs.rename(temporary, file);
    }
}
