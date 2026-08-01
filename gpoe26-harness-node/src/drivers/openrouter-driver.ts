import { evt, type HarnessEvent } from '../contract.js';
import type { Driver, DriverTurn, TurnUsage } from './driver.js';

interface ChatMessage {
    role: 'system' | 'user' | 'assistant' | 'tool';
    content: string | null;
    tool_calls?: { id: string; type: 'function'; function: { name: string; arguments: string } }[];
    tool_call_id?: string;
}

/**
 * Le pilote OpenRouter, avec la boucle à outils écrite à la main.
 *
 * C'est là que les deux harness diffèrent vraiment. Côté .NET,
 * `FunctionInvokingChatClient` fournit cette boucle ; ici elle est écrite, ce qui donne
 * un contrôle plus fin — sur ce qu'on montre à l'élève entre deux appels, sur le moment
 * où l'on s'arrête — au prix du code qu'il faut maintenir.
 *
 * Le tableau comparatif dira si ce contrôle valait son prix.
 */
export class OpenRouterDriver implements Driver {

    readonly id = 'openrouter';

    private usage: TurnUsage | undefined;

    constructor(
        private readonly apiKey: string,
        private readonly model: string,
        private readonly baseUrl = 'https://openrouter.ai/api/v1',
    ) { }

    lastUsage(): TurnUsage | undefined {
        return this.usage;
    }

    async *run(turn: DriverTurn): AsyncIterable<HarnessEvent> {
        const messages: ChatMessage[] = [
            { role: 'system', content: turn.system },
            ...turn.messages.map(m => ({ role: m.role, content: m.content })),
        ];

        const byName = new Map(turn.tools.map(t => [t.name, t]));
        const tools = turn.tools.map(t => ({
            type: 'function' as const,
            function: { name: t.name, description: t.description, parameters: t.parameters },
        }));

        let inputTokens = 0;
        let outputTokens = 0;

        for (let iteration = 1; iteration <= turn.maxIterations; iteration++) {
            const response = await this.complete(messages, tools);

            inputTokens += response.usage?.prompt_tokens ?? 0;
            outputTokens += response.usage?.completion_tokens ?? 0;

            const choice = response.choices?.[0]?.message;
            if (!choice) {
                yield evt.error("Le modèle n'a rien renvoyé.");
                return;
            }

            const calls = choice.tool_calls ?? [];

            // Pas d'appel d'outil : le modèle a fini de réfléchir, c'est la réponse.
            if (calls.length === 0) {
                const reply = (choice.content ?? '').trim();
                this.usage = { inputTokens, outputTokens, iterations: iteration };

                if (reply.length === 0) {
                    yield evt.error("Je n'ai pas réussi à formuler de réponse. Reformulez votre question ?");
                    return;
                }

                // On diffuse la réponse par morceaux. Le tour n'est pas streamé au niveau
                // du modèle — on appelle en une fois pour pouvoir lire les appels d'outils
                // proprement — mais l'élève n'a aucune raison de recevoir un pavé d'un coup.
                for (const chunk of reply.match(/\S+\s*/g) ?? [reply]) {
                    yield evt.token(chunk);
                }

                yield evt.done(reply, citationsOf(reply));
                return;
            }

            messages.push({ role: 'assistant', content: choice.content ?? null, tool_calls: calls });

            for (const call of calls) {
                const tool = byName.get(call.function.name);

                yield evt.step(stepLabel(call.function.name));
                yield evt.toolCall(call.function.name, call.function.arguments, iteration);

                // Un outil hors table ne peut pas être appelé : il n'a pas été déclaré au
                // modèle. Qu'il en réclame un quand même signale une confusion de sa part,
                // pas un droit à lui accorder.
                if (!tool) {
                    messages.push({
                        role: 'tool',
                        tool_call_id: call.id,
                        content: `L'outil « ${call.function.name} » n'existe pas.`,
                    });
                    yield evt.toolResult(call.function.name, 'outil inconnu', iteration);
                    continue;
                }

                let result: string;
                try {
                    result = await tool.invoke(safeParse(call.function.arguments));
                } catch (error) {
                    // Un outil qui échoue termine le tour : continuer ferait répondre le
                    // modèle sur une information qu'il n'a pas, ce qui est pire qu'une erreur.
                    this.usage = { inputTokens, outputTokens, iterations: iteration };
                    yield evt.error(`L'outil ${call.function.name} a échoué : ${message(error)}`);
                    return;
                }

                messages.push({ role: 'tool', tool_call_id: call.id, content: result });
                yield evt.toolResult(call.function.name, summarize(result), iteration);
            }
        }

        // Le plafond est atteint : le modèle enchaîne les outils sans conclure. Mieux vaut
        // le dire que laisser tourner — chaque tour se paie, et l'élève ne voit défiler
        // que des étapes.
        this.usage = { inputTokens, outputTokens, iterations: turn.maxIterations };
        yield evt.error("Je n'arrive pas à conclure sur cette question. Reformulez-la ?");
    }

    async summarize(transcript: string): Promise<string> {
        const response = await this.complete([
            {
                role: 'system',
                content: 'Résume cet échange entre un élève et son répétiteur, en 6 puces au plus. '
                    + "Garde ce sur quoi l'élève a buté et ce qui a été éclairci ; jette le reste.",
            },
            { role: 'user', content: transcript },
        ], []);

        return (response.choices?.[0]?.message?.content ?? '').trim();
    }

    private async complete(messages: ChatMessage[], tools: unknown[]): Promise<any> {
        const response = await fetch(`${this.baseUrl}/chat/completions`, {
            method: 'POST',
            headers: {
                'content-type': 'application/json',
                'authorization': `Bearer ${this.apiKey}`,
                'x-title': 'GPOE26',
            },
            body: JSON.stringify({
                model: this.model,
                messages,
                max_tokens: 1600,
                ...(tools.length > 0 ? { tools } : {}),
            }),
        });

        const payload = await response.json() as any;

        // OpenRouter signale certaines erreurs dans un objet `error` avec un statut 200 :
        // ne vérifier que le code HTTP laisserait passer une réponse vide.
        if (!response.ok || payload?.error) {
            throw new Error(payload?.error?.message ?? `OpenRouter a répondu ${response.status}`);
        }

        return payload;
    }
}

function safeParse(raw: string): Record<string, unknown> {
    try {
        return JSON.parse(raw || '{}');
    } catch {
        return {};
    }
}

function message(error: unknown): string {
    return error instanceof Error ? error.message : String(error);
}

/** Le flux transporte un résumé du résultat, pas le résultat : une recherche ramène des milliers de caractères. */
function summarize(result: string): string {
    const first = result.split('\n', 1)[0]?.trim() ?? '';
    return first.length <= 120 ? first : `${first.slice(0, 120)}…`;
}

function citationsOf(reply: string): string[] {
    const citations: string[] = [];

    for (const match of reply.matchAll(/\[§\s*([^\]]+)\]/g)) {
        const heading = match[1]!.trim();
        if (heading && !citations.includes(heading)) citations.push(heading);
    }

    return citations;
}

/** Ce que l'élève voit pendant qu'un outil tourne : une phrase, pas un nom technique. */
function stepLabel(tool: string): string {
    const labels: Record<string, string> = {
        chercher_dans_le_cours: 'Recherche dans votre cours…',
        ou_en_est_l_eleve: 'Vérification de votre parcours…',
        lire_a_voix_haute: 'Préparation de la lecture…',
        generer_exercice: "Préparation d'un exercice…",
        corriger_reponse: 'Correction de votre réponse…',
        lire_fichier: 'Lecture de votre fichier…',
        ecrire_fichier: 'Modification de votre fichier…',
        executer_tests: 'Exécution de vos tests…',
    };

    return labels[tool] ?? 'Travail en cours…';
}
