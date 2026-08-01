namespace GPOE26.Harness.Contract;

/// <summary>
/// Les types d'évènement du flux SSE d'un tour.
///
/// Des constantes plutôt que des littéraux dispersés : la suite de conformité et les
/// implémentations doivent parler des mêmes chaînes, et une faute de frappe dans un
/// `case` ne se voit pas — l'évènement est simplement ignoré, ce qui ressemble à un
/// modèle silencieux plutôt qu'à un bug.
/// </summary>
public static class HarnessEventType
{
    /// <summary>Étape du raisonnement, en clair, à afficher pendant l'attente.</summary>
    public const string Step = "step";

    /// <summary>Fragment de la réponse en cours de rédaction.</summary>
    public const string Token = "token";

    /// <summary>Le modèle a décidé d'appeler un outil.</summary>
    public const string ToolCall = "tool_call";

    /// <summary>L'outil a répondu.</summary>
    public const string ToolResult = "tool_result";

    /// <summary>Réponse finale, avec ses citations. Le tour est terminé.</summary>
    public const string Done = "done";

    /// <summary>Échec. Le tour est terminé, sans réponse.</summary>
    public const string Error = "error";

    /// <summary>Les types qu'un client doit savoir recevoir.</summary>
    public static readonly IReadOnlyList<string> All =
        [Step, Token, ToolCall, ToolResult, Done, Error];

    /// <summary>Un tour se termine sur l'un ou l'autre, jamais sur autre chose.</summary>
    public static bool IsTerminal(string type) => type is Done or Error;
}

/// <summary>
/// Un évènement du flux d'un tour.
///
/// ⚠️ Ce type est l'extension stricte de <c>Chat.Model.TutorStreamEvent</c> : mêmes noms,
/// même ordre, plus quatre champs. C'est délibéré — le client Web filtre sur
/// <c>evt.Type</c> par un switch sans branche par défaut
/// (<c>GPOE26.Web/Components/Pages/Learning/CourseView.razor</c>), donc un évènement
/// `tool_call` lui arrive et l'ignore au lieu de le faire tomber. C'est ce qui permet au
/// Web de basculer sur le harness sans changement visible.
///
/// Ne jamais renommer ni réordonner les sept premiers champs sans changer le client.
/// </summary>
/// <param name="Tool">Nom de l'outil appelé, sur `tool_call` et `tool_result`.</param>
/// <param name="ToolArguments">Arguments JSON de l'appel, sur `tool_call`.</param>
/// <param name="ToolResult">
/// Résumé du résultat, sur `tool_result`. **Résumé, pas la charge utile** : un outil de
/// recherche peut ramener plusieurs milliers de caractères, que personne n'affiche et qui
/// alourdiraient le flux pour rien.
/// </param>
/// <param name="Iteration">
/// Rang du tour de boucle, à partir de 1. Rend visible qu'un agent tourne, et permet de
/// mesurer combien d'allers-retours une question a coûté.
/// </param>
public record HarnessEvent(
    string Type,
    string? Step = null,
    string? Token = null,
    string? Reply = null,
    IReadOnlyList<string>? Citations = null,
    string? Intent = null,
    string? Error = null,
    string? Tool = null,
    string? ToolArguments = null,
    string? ToolResult = null,
    int? Iteration = null
)
{
    public static HarnessEvent OfStep(string label) =>
        new(HarnessEventType.Step, Step: label);

    public static HarnessEvent OfToken(string token) =>
        new(HarnessEventType.Token, Token: token);

    public static HarnessEvent OfToolCall(string tool, string? arguments, int iteration) =>
        new(HarnessEventType.ToolCall, Tool: tool, ToolArguments: arguments, Iteration: iteration);

    public static HarnessEvent OfToolResult(string tool, string? summary, int iteration) =>
        new(HarnessEventType.ToolResult, Tool: tool, ToolResult: summary, Iteration: iteration);

    public static HarnessEvent OfDone(string reply, IReadOnlyList<string>? citations = null, string? intent = null) =>
        new(HarnessEventType.Done, Reply: reply, Citations: citations, Intent: intent);

    public static HarnessEvent OfError(string message) =>
        new(HarnessEventType.Error, Error: message);

    public bool IsTerminal => HarnessEventType.IsTerminal(Type);
}
