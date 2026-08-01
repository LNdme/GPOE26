import { ActivitySignal, AuthResponse, OfflineStepResult, RenderedCourseDto } from './harness-protocol';

/*
 * Ce que l'application garde sur la machine de l'élève.
 *
 * Le magasin vit côté backend et non dans le navigateur : c'est le seul endroit qui
 * survit au rechargement de la fenêtre, et le seul qui puisse écrire dans le trousseau
 * du système. Le frontend y accède par JSON-RPC, comme pour tout service Theia.
 *
 * ⚠️ Le plan prévoyait SQLite. On stocke en fichiers JSON, pour une raison qui n'était
 * pas visible à la conception : SQLite en Node demande soit une dépendance native à
 * compiler, soit `node:sqlite`, dont la présence dépend de la version de Node embarquée
 * par Electron. Le volume ici — quelques cours et une file de signaux — ne justifie pas
 * de faire dépendre l'installation d'une chaîne de compilation. Si la file dépassait un
 * jour la mémoire, ce serait le moment de revenir à une vraie base.
 */

export const LOCAL_STORE_PATH = '/services/gpoe26/store';
export const LocalStore = Symbol('LocalStore');

/** Un cours disponible hors ligne, avec ce qu'il faut pour le lire. */
export interface CachedCourse {
    course: RenderedCourseDto;
    /** Progression de lecture, entre 0 et 1, pour rouvrir où l'élève s'est arrêté. */
    readProgress: number;
    cachedAt: string;
}

/** Ce qui attend le retour du réseau. */
export interface PendingWork {
    activities: ActivitySignal[];
    results: OfflineStepResult[];
}

export interface StoredCredentials {
    token: string;
    expiresAt: string;
    refreshToken?: string;
    refreshExpiresAt?: string;
    profile: AuthResponse['profile'];
}

export interface LocalStore {
    // ── Cours ─────────────────────────────────────────────────────────────────
    listCourses(): Promise<CachedCourse[]>;
    getCourse(courseId: string): Promise<CachedCourse | undefined>;
    putCourse(course: RenderedCourseDto): Promise<void>;
    setReadProgress(courseId: string, progress: number): Promise<void>;

    // ── File d'attente ────────────────────────────────────────────────────────
    /**
     * Empile un signal d'activité. Il porte sa propre date : c'est elle qui sera
     * enregistrée au retour du réseau, pas l'heure de la synchronisation.
     */
    queueActivity(signal: ActivitySignal): Promise<void>;
    queueResult(result: OfflineStepResult): Promise<void>;
    peekPending(): Promise<PendingWork>;
    /**
     * Vide la file de ce qui a été accepté.
     *
     * On purge par comptage et non par identifiant : le serveur répond combien il a
     * retenu, et la file est ordonnée. Purger avant confirmation perdrait du travail ;
     * ne jamais purger le rejouerait indéfiniment.
     */
    clearPending(activities: number, results: number): Promise<void>;

    // ── Identité ──────────────────────────────────────────────────────────────
    readCredentials(): Promise<StoredCredentials | undefined>;
    writeCredentials(credentials: StoredCredentials): Promise<void>;
    clearCredentials(): Promise<void>;
}
