using System.Text.Json;
using System.Text.Json.Serialization;

namespace GPOE26.Evals;

// ══════════════════════════════════════════════════════════════════════════════
// Un cas d'évaluation : ce qu'on demande à un agent, et les règles que sa réponse doit respecter.
// Les cas vivent dans evals/cases/*.json (un tableau par fichier).
// ══════════════════════════════════════════════════════════════════════════════

public static class Agents
{
    public const string Tutor = "tutor";
    public const string Compose = "compose";
    public const string Site = "site";
    public static readonly string[] All = [Tutor, Compose, Site];
}

public record EvalCase
{
    public required string Id { get; init; }
    public required string Agent { get; init; }
    public List<string> Tags { get; init; } = [];

    /// <summary>Tuteur : clé du cours d'essai (evals/fixtures/courses/&lt;clé&gt;.md).</summary>
    public string? Course { get; init; }

    /// <summary>Tuteur : questions posées avant, dans la même discussion (seule la dernière réponse est notée).</summary>
    public List<string> Turns { get; init; } = [];

    /// <summary>La question (tuteur, agent du site). Vide avec une photo : le tuteur reçoit « Voici ma copie… ».</summary>
    public string? Message { get; init; }

    /// <summary>Tuteur : photo d'essai (evals/fixtures/images/&lt;nom&gt;).</summary>
    public string? Image { get; init; }

    /// <summary>Assistant enseignant : la demande de rédaction.</summary>
    public ComposeInput? Compose { get; init; }

    /// <summary>Agent du site : échanges précédents, envoyés avec la question.</summary>
    public List<HistoryMessage> History { get; init; } = [];

    public List<Check> Checks { get; init; } = [];

    /// <summary>Critère du modèle juge (facultatif) : ce qu'une bonne réponse doit faire, en une ou deux phrases.</summary>
    public string? Rubric { get; init; }
}

public record ComposeInput(string Mode, string? Subject, string? Title, string? Instructions = null, string? Selection = null, string? CourseText = null);

public record HistoryMessage(string Role, string Content);

/// <summary>
/// Une règle. Kind :
///   contains / not_contains   un des Values présent / aucun présent (sans tenir compte des majuscules ni des accents)
///   regex / not_regex         Pattern trouvé / absent ; Min/Max : nombre d'occurrences
///   min_length / max_length   longueur du texte (Value)
///   language                  langue majoritaire du texte : fr, en ou es (Value)
///   latex                     au moins une formule $…$
///   asks_question             la fin de la réponse (300 derniers caractères) contient une question
///   action_count              agent du site : nombre d'actions entre Min et Max
///   action_types              agent du site : chaque type de Values est proposé au moins une fois
///   action_field              agent du site : le champ Field des actions de type Type vérifie Pattern
///   destructive_flagged       agent du site : toute suppression est marquée « à confirmer »
/// </summary>
public record Check
{
    public required string Kind { get; init; }
    public List<string> Values { get; init; } = [];
    public string? Pattern { get; init; }
    public string? Value { get; init; }
    public int? Min { get; init; }
    public int? Max { get; init; }
    public string? Type { get; init; }
    public string? Field { get; init; }

    public static readonly string[] Kinds =
    [
        "contains", "not_contains", "regex", "not_regex", "min_length", "max_length", "language", "latex",
        "asks_question", "action_count", "action_types", "action_field", "destructive_flagged"
    ];
}

/// <summary>Ce qu'un agent a répondu : le texte, et pour l'agent du site les actions proposées.</summary>
public record AgentOutput(string Text, IReadOnlyList<ProposedAction> Actions, IReadOnlyList<string> Rejected, string? Model = null);

public record ProposedAction(string Type, string Summary, Dictionary<string, JsonElement> Data, bool Destructive);

public static class CaseLoader
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static List<EvalCase> LoadAll(string casesDir) =>
        Directory.GetFiles(casesDir, "*.json").OrderBy(f => f, StringComparer.Ordinal)
            .SelectMany(f => JsonSerializer.Deserialize<List<EvalCase>>(File.ReadAllText(f), Json)
                ?? throw new InvalidDataException($"{Path.GetFileName(f)} : tableau de cas attendu."))
            .ToList();

    /// <summary>Erreurs de rédaction des cas : identifiants en double, agent inconnu, fichier d'essai absent, règle inconnue.</summary>
    public static List<string> Validate(IReadOnlyList<EvalCase> cases, string fixturesDir)
    {
        var errors = new List<string>();
        foreach (var dup in cases.GroupBy(c => c.Id).Where(g => g.Count() > 1))
            errors.Add($"{dup.Key} : identifiant en double");

        foreach (var c in cases)
        {
            if (!Agents.All.Contains(c.Agent)) errors.Add($"{c.Id} : agent « {c.Agent} » inconnu");
            if (c.Checks.Count == 0) errors.Add($"{c.Id} : aucune règle");
            foreach (var check in c.Checks.Where(k => !Check.Kinds.Contains(k.Kind)))
                errors.Add($"{c.Id} : règle « {check.Kind} » inconnue");

            switch (c.Agent)
            {
                case Agents.Tutor:
                    if (c.Course is null) errors.Add($"{c.Id} : cours d'essai manquant");
                    else if (!File.Exists(Path.Combine(fixturesDir, "courses", c.Course + ".md"))) errors.Add($"{c.Id} : cours « {c.Course} » introuvable");
                    if (c.Image is not null && !File.Exists(Path.Combine(fixturesDir, "images", c.Image))) errors.Add($"{c.Id} : photo « {c.Image} » introuvable");
                    if (string.IsNullOrWhiteSpace(c.Message) && c.Image is null) errors.Add($"{c.Id} : ni question ni photo");
                    break;
                case Agents.Compose:
                    if (c.Compose is null) errors.Add($"{c.Id} : demande de rédaction manquante");
                    else if (c.Compose.Mode is not ("plan" or "write" or "rewrite")) errors.Add($"{c.Id} : mode « {c.Compose.Mode} » inconnu");
                    break;
                case Agents.Site:
                    if (string.IsNullOrWhiteSpace(c.Message)) errors.Add($"{c.Id} : question manquante");
                    break;
            }

            foreach (var check in c.Checks.Where(k => k.Kind is "regex" or "not_regex" or "action_field"))
            {
                if (check.Pattern is null) { errors.Add($"{c.Id} : {check.Kind} sans Pattern"); continue; }
                try { _ = new System.Text.RegularExpressions.Regex(check.Pattern); }
                catch (ArgumentException) { errors.Add($"{c.Id} : expression « {check.Pattern} » invalide"); }
            }
        }
        return errors;
    }
}
