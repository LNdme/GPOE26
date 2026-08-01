import { evt, type HarnessEvent } from '../contract.js';
import type { Driver, DriverTurn, TurnUsage } from './driver.js';

/**
 * Un pilote qui ne parle à aucun modèle.
 *
 * L'idée vient de QM, qui embarque un `mock-harness` à côté de ses vrais pilotes. Elle
 * vaut plus qu'il n'y paraît : sans elle, la suite de conformité exigerait une clé d'API
 * pour vérifier des choses qui n'ont rien à voir avec un modèle — que les outils exposés
 * suivent la table, qu'un parent n'accède pas à l'enfant d'un autre, qu'un tour se
 * termine toujours.
 *
 * Il joue toujours la même partition : une étape, un appel d'outil, son résultat, la
 * rédaction, la fin. La forme du flux devient donc vérifiable indépendamment de ce qu'un
 * modèle aurait décidé.
 */
export class MockDriver implements Driver {

    readonly id = 'mock';

    private usage: TurnUsage | undefined;

    lastUsage(): TurnUsage | undefined {
        return this.usage;
    }

    async *run(turn: DriverTurn): AsyncIterable<HarnessEvent> {
        const question = turn.messages.at(-1)?.content ?? '';

        yield evt.step('Analyse de votre question…');

        if (turn.tools.length > 0) {
            const tool = turn.tools[0]!;
            yield evt.toolCall(tool.name, '{"requete":"mock"}', 1);

            // Convention de la suite de conformité : ce marqueur demande de faire échouer
            // le premier outil, pour vérifier qu'un échec devient une erreur visible et
            // non un tour qui fait comme si de rien n'était.
            if (question.includes('__fail_tool__')) {
                this.usage = { inputTokens: 0, outputTokens: 0, iterations: 1 };
                yield evt.error(`L'outil ${tool.name} a échoué.`);
                return;
            }

            try {
                const result = await tool.invoke({ requete: question.slice(0, 80) });
                yield evt.toolResult(tool.name, result.split('\n', 1)[0]?.slice(0, 120) ?? '', 1);
            } catch (error) {
                this.usage = { inputTokens: 0, outputTokens: 0, iterations: 1 };
                yield evt.error(`L'outil ${tool.name} a échoué : ${String(error)}`);
                return;
            }
        }

        const reply = 'Réponse du pilote factice. Ce harness ne consulte aucun modèle dans ce mode.';
        for (const chunk of reply.match(/\S+\s*/g) ?? [reply]) {
            yield evt.token(chunk);
        }

        this.usage = { inputTokens: 0, outputTokens: 0, iterations: 1 };
        yield evt.done(reply, ['Chapitre 1 › Introduction']);
    }

    async summarize(transcript: string): Promise<string> {
        return `Résumé factice de ${transcript.length} caractères.`;
    }
}
