import type { HarnessEvent } from '../contract.js';

/*
 * Le pilote de modèle.
 *
 * L'idée vient de QM, et c'est la meilleure de son architecture : la boucle à outils ne
 * dépend pas d'un fournisseur. QM route entre Claude, Codex, OpenCode et Pi ; ici entre
 * OpenRouter et un pilote factice. Ce qui compte n'est pas le nombre de pilotes mais la
 * couture — elle rend la boucle testable sans clé d'API, et c'est ce qui permet à ce
 * harness de subir la suite de conformité dans une intégration continue.
 */

export interface ToolDefinition {
    name: string;
    description: string;
    /** Schéma JSON des paramètres, tel qu'attendu par l'API du modèle. */
    parameters: Record<string, unknown>;
    /** Exécute l'outil. Le résultat repart au modèle tel quel. */
    invoke(args: Record<string, unknown>): Promise<string>;
}

export interface DriverTurn {
    system: string;
    messages: { role: 'user' | 'assistant'; content: string }[];
    tools: ToolDefinition[];
    maxIterations: number;
}

/** Ce qu'un tour a coûté, pour le tableau comparatif. */
export interface TurnUsage {
    inputTokens: number;
    outputTokens: number;
    iterations: number;
}

export interface Driver {
    readonly id: string;

    /**
     * Joue un tour et livre les évènements du contrat.
     *
     * C'est le pilote qui tient la boucle : appeler le modèle, exécuter les outils qu'il
     * demande, recommencer jusqu'à une réponse. Côté .NET, `FunctionInvokingChatClient`
     * la fournit ; ici elle est écrite — c'est précisément la différence que le match
     * doit mesurer.
     */
    run(turn: DriverTurn): AsyncIterable<HarnessEvent>;

    /** Ce que le dernier tour a consommé. Null si le pilote ne le rapporte pas. */
    lastUsage(): TurnUsage | undefined;

    /** Résume un échange, pour la compaction du contexte. */
    summarize(transcript: string): Promise<string>;
}
