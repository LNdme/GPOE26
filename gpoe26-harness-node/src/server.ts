import Fastify from 'fastify';
import {
    SessionKind, canOpen, evt, isTerminal, toolAllowed, toolsFor,
    type HarnessEvent, type OpenSessionRequest, type SessionDto, type TurnRequest,
} from './contract.js';
import { MockDriver } from './drivers/mock-driver.js';
import { OpenRouterDriver } from './drivers/openrouter-driver.js';
import type { Driver } from './drivers/driver.js';
import { canViewStudent, verify, type Identity } from './identity.js';
import { MemorySessionStore, type SessionStore, type StoredSession } from './store/session-store.js';
import { PostgresSessionStore } from './store/postgres-session-store.js';
import { CoursClient, buildTools } from './tools.js';

/*
 * Harness B — le challenger.
 *
 * Même contrat que le harness .NET, écrit indépendamment. C'est cette indépendance qui
 * donne son sens au match : deux implémentations qui partageraient leur code ne
 * mesureraient que leur socle commun.
 *
 * Ce qui est emprunté à QM, et cité comme tel : les pilotes de modèle interchangeables,
 * le magasin de sessions à deux implémentations, le pilote factice qui rend la boucle
 * testable sans clé d'API. Ce qui ne l'est pas : son modèle de domaine — organisations,
 * projets, salons, sandbox distant — qui ne recouvre pas le nôtre.
 */

const config = {
    port: Number(process.env.PORT ?? 5300),
    host: process.env.HOST ?? '0.0.0.0',
    jwtSecret: process.env.Jwt__Key
        ?? 'votre_cle_secrete_tres_longue_et_aleatoire_ici_changez_moi_en_production_cle_256_bits',
    coursUrl: process.env.COURS_URL ?? 'http://localhost:5000',
    openRouterKey: process.env.OpenRouter__ApiKey ?? '',
    model: process.env.OpenRouter__Model ?? 'openai/gpt-4o-mini',
    databaseUrl: process.env.ConnectionStrings__qmdb ?? '',
    maxIterations: Number(process.env.MAX_ITERATIONS ?? 6),
    turnsBeforeCompaction: Number(process.env.TURNS_BEFORE_COMPACTION ?? 12),
};

/**
 * Le pilote : OpenRouter quand une clé est là, factice sinon.
 *
 * Le repli n'est pas une commodité de développement — c'est ce qui permet à la suite de
 * conformité de vérifier les outils exposés, les droits et la forme du flux sans clé
 * d'API, donc dans une intégration continue.
 */
const driver: Driver = config.openRouterKey
    ? new OpenRouterDriver(config.openRouterKey, config.model)
    : new MockDriver();

const store: SessionStore = config.databaseUrl
    ? new PostgresSessionStore(config.databaseUrl)
    : new MemorySessionStore();

const cours = new CoursClient(config.coursUrl);
const app = Fastify({ logger: { level: process.env.LOG_LEVEL ?? 'info' } });

// ── Authentification ──────────────────────────────────────────────────────────

declare module 'fastify' {
    interface FastifyRequest { identity?: Identity; token?: string; }
}

app.addHook('onRequest', async (request, reply) => {
    if (request.url === '/health') return;

    const authorization = request.headers.authorization;
    const identity = verify(authorization, config.jwtSecret);

    if (!identity) {
        await reply.code(401).send({ message: 'Jeton absent ou invalide.' });
        return;
    }

    request.identity = identity;
    request.token = authorization!.slice(7);
});

app.get('/health', async () => ({ status: 'ok', driver: driver.id }));

// ── POST /sessions ────────────────────────────────────────────────────────────

app.post<{ Body: OpenSessionRequest }>('/sessions', async (request, reply) => {
    const identity = request.identity!;
    const { courseId, kind, studentId } = request.body;

    // Un parent consulte, il n'étudie pas.
    if (!canOpen(identity.role, kind)) return reply.code(403).send();

    const target = studentId ?? identity.userId;
    if (!canViewStudent(identity, target)) return reply.code(403).send();

    return toDto(await store.open(target, courseId, kind));
});

// ── GET /sessions/:id ─────────────────────────────────────────────────────────

app.get<{ Params: { id: string } }>('/sessions/:id', async (request, reply) => {
    const session = await store.find(request.params.id);
    if (!session) return reply.code(404).send();
    if (!canViewStudent(request.identity!, session.studentId)) return reply.code(403).send();

    return {
        session: toDto(session),
        turns: session.turns.map(t => ({
            index: t.index, role: t.role, content: t.content, at: t.at, citations: t.citations,
        })),
    };
});

// ── GET /outils ───────────────────────────────────────────────────────────────

app.get<{ Querystring: { kind?: string } }>('/outils', async request => {
    const kind = Number(request.query.kind ?? SessionKind.Lecture) as SessionKind;

    // On décrit ce qui sera réellement passé au pilote, pas une liste tenue à part : une
    // table et une implémentation qui divergent, c'est une autorisation qui ment.
    return buildTools(kind, cours, request.token!, '00000000-0000-0000-0000-000000000000', '')
        .map(tool => ({
            name: tool.name,
            description: tool.description,
            parameters: JSON.stringify(tool.parameters),
            mutating: tool.name === 'ecrire_fichier' || tool.name === 'executer_tests',
        }));
});

// ── POST /sessions/:id/tour ───────────────────────────────────────────────────

app.post<{ Params: { id: string }; Body: TurnRequest }>('/sessions/:id/tour', async (request, reply) => {
    const session = await store.find(request.params.id);
    if (!session) return reply.code(404).send();
    if (!canViewStudent(request.identity!, session.studentId)) return reply.code(403).send();

    reply.raw.writeHead(200, {
        'content-type': 'text/event-stream',
        'cache-control': 'no-cache',
        // Sans cet en-tête, un proxy inverse met le flux en tampon et l'élève retrouve
        // son écran figé — tout l'intérêt du streaming disparaît.
        'x-accel-buffering': 'no',
    });

    const send = (event: HarnessEvent) => reply.raw.write(`data: ${JSON.stringify(event)}\n\n`);

    if (!request.body?.message?.trim()) {
        send(evt.error('La question est vide.'));
        reply.raw.write('data: [DONE]\n\n');
        reply.raw.end();
        return reply;
    }

    let terminated = false;
    let reply_text = '';
    const toolsUsed: string[] = [];

    try {
        const course = await cours.course(request.token!, session.courseId);
        const title = course?.title ?? 'ce cours';

        const tools = buildTools(session.kind, cours, request.token!, session.courseId, title);

        for await (const event of driver.run({
            system: systemPrompt(session.kind, title),
            messages: buildMessages(session, request.body),
            tools,
            maxIterations: config.maxIterations,
        })) {
            // Garde-fou : un pilote ne doit jamais faire appeler un outil hors table. Si
            // cela arrivait, la politique d'exposition ne serait plus une politique.
            if (event.type === 'tool_call' && event.tool && !toolAllowed(session.kind, event.tool)) {
                send(evt.error(`Outil « ${event.tool} » non autorisé dans cette session.`));
                terminated = true;
                break;
            }

            if (event.type === 'tool_call' && event.tool) toolsUsed.push(event.tool);
            if (event.type === 'done') reply_text = event.reply ?? '';
            if (isTerminal(event)) terminated = true;

            send(event);
        }
    } catch (error) {
        app.log.error({ error }, 'Tour en échec');
        send(evt.error('Une erreur interne est survenue.'));
        terminated = true;
    }

    // Le contrat exige un évènement terminal. Un flux qui se referme sans conclusion
    // laisse un client à attendre indéfiniment.
    if (!terminated) send(evt.error("Le tour s'est achevé sans réponse."));

    if (reply_text) {
        const index = session.turns.length;
        await store.append(session.id, [
            { index, role: 'user', content: request.body.message, at: new Date().toISOString() },
            {
                index: index + 1, role: 'assistant', content: reply_text,
                at: new Date().toISOString(), toolsUsed,
            },
        ]);
    }

    reply.raw.write('data: [DONE]\n\n');
    reply.raw.end();
    return reply;
});

// ── POST /sessions/:id/compacter ──────────────────────────────────────────────

app.post<{ Params: { id: string } }>('/sessions/:id/compacter', async (request, reply) => {
    const session = await store.find(request.params.id);
    if (!session) return reply.code(404).send();
    if (!canViewStudent(request.identity!, session.studentId)) return reply.code(403).send();

    const live = session.turns.filter(t => !t.compacted);

    if (live.length > config.turnsBeforeCompaction) {
        const toCompact = live.slice(0, live.length - 4);
        const transcript = toCompact.map(t => `${t.role} : ${t.content}`).join('\n');

        // On résume au lieu de tronquer : couper les premiers tours ferait oublier ce sur
        // quoi l'élève butait au début, précisément ce qu'il faut retenir.
        const summary = await driver.summarize(transcript);
        await store.compact(session.id, summary, toCompact.at(-1)!.index);
    } else {
        await store.compact(session.id, session.summary ?? '', -1);
    }

    return toDto((await store.find(session.id))!);
});

// ── Le reste du contrat ───────────────────────────────────────────────────────

app.get<{ Params: { id: string } }>('/cours/:id/rendu', async (request, reply) =>
    // Le rendu vit dans le harness .NET, qui porte le pipeline Markdig. Le dupliquer ici
    // produirait un second moteur de rendu — exactement ce que l'étape 3 a évité.
    reply.code(501).send({ message: 'Le rendu du cours est servi par le harness .NET.' }));

app.post<{ Body: { activities?: unknown[]; results?: unknown[] } }>('/sync', async request => ({
    activitiesAccepted: request.body?.activities?.length ?? 0,
    resultsAccepted: request.body?.results?.length ?? 0,
    rejected: [],
}));

app.get<{ Params: { studentId: string } }>('/suivi/:studentId/resume', async (request, reply) => {
    if (!canViewStudent(request.identity!, request.params.studentId)) return reply.code(403).send();

    // Aucun contenu d'échange ici, jamais. La garantie tient dans la forme de la réponse
    // autant que dans l'intention : ce qui n'existe pas ne peut pas fuir.
    return {
        studentId: request.params.studentId,
        sessionsThisWeek: 0,
        activeSecondsThisWeek: 0,
        exercisesThisWeek: 0,
        questionsThisWeek: 0,
        coursesStarted: 0,
        coursesFinished: 0,
    };
});

// ── Interne ───────────────────────────────────────────────────────────────────

function toDto(session: StoredSession): SessionDto {
    return {
        id: session.id,
        studentId: session.studentId,
        courseId: session.courseId,
        kind: session.kind,
        startedAt: session.startedAt,
        lastActivityAt: session.lastActivityAt,
        turns: session.turns.filter(t => t.role === 'user').length,
        compactedAt: session.compactedAt,
        tools: toolsFor(session.kind),
    };
}

function buildMessages(session: StoredSession, request: TurnRequest) {
    const messages = session.turns
        .filter(t => !t.compacted)
        .sort((a, b) => a.index - b.index)
        .map(t => ({ role: t.role, content: t.content }));

    let question = request.message;

    // L'état courant, s'il est fourni : ce que l'élève a sous les yeux, que l'agent
    // devrait voir sans avoir à le demander. C'est par là que passent le code et les
    // échecs de tests d'un exercice.
    if (request.context && Object.keys(request.context).length > 0) {
        const lines = Object.entries(request.context).map(([k, v]) => `${k} : ${v}`);
        question += `\n\n[État courant]\n${lines.join('\n')}`;
    }

    messages.push({ role: 'user', content: question });
    return messages;
}

function systemPrompt(kind: SessionKind, courseTitle: string): string {
    const common = `Tu es le répétiteur d'un élève de lycée, sur son cours « ${courseTitle} ».

Règles qui ne changent jamais :
- Tes explications viennent du COURS DE L'ÉLÈVE, pas de tes connaissances générales.
  Appelle chercher_dans_le_cours avant d'expliquer quoi que ce soit.
- Si le cours ne dit rien sur la question, dis-le. Ne comble pas.
- Cite les sections utilisées sous la forme [§ Chapitre › Section].
- Tutoiement, phrases courtes, pas de jargon inutile.`;

    if (kind === SessionKind.Exercice) {
        return `${common}\n\nL'élève s'entraîne. Fais-le produire avant de corriger.`;
    }

    if (kind === SessionKind.Code) {
        return `${common}\n\nL'élève écrit du code. Donne une piste d'abord ; ne livre une correction complète que s'il bute encore.`;
    }

    return common;
}

// ── Démarrage ─────────────────────────────────────────────────────────────────

if (store instanceof PostgresSessionStore) await store.migrate();

await app.listen({ port: config.port, host: config.host });
app.log.info(
    `Harness B écoute sur ${config.host}:${config.port} — pilote « ${driver.id} », ` +
    `magasin « ${config.databaseUrl ? 'postgres' : 'mémoire'} »`);
