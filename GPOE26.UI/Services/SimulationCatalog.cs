using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace GPOE26.UI.Services;

public record SimOption(string Value, string Label);
public record SimChoice(string Key, string Label, SimOption[] Options);
public record SimNumber(string Key, string Label, double Default, double Min, double Max, double Step, string? Unit = null);
public record SimText(string Key, string Label, string Default, string? Help = null, bool Required = false);

/// <summary>Un type de laboratoire proposé dans l'éditeur de cours.</summary>
public record SimType(string Id, string Name, string Description, string Title, string Caption,
    SimChoice[] Choices, SimNumber[] Numbers, SimText[] Texts);

/// <summary>Ce que l'enseignant a réglé, avant écriture dans le cours.</summary>
public class SimulationDraft
{
    public string Title { get; set; } = "";
    public string Caption { get; set; } = "";
    public Dictionary<string, string> Choices { get; } = new();
    public Dictionary<string, double> Numbers { get; } = new();
    public Dictionary<string, string> Texts { get; } = new();

    public string? PredictQuestion { get; set; }
    public string? PredictOptions { get; set; }
    public int PredictAnswer { get; set; } = 1;
    public string? PredictExplain { get; set; }

    public string? CheckQuestion { get; set; }
    public string? CheckAnswer { get; set; }
    public string? CheckUnit { get; set; }
    public string? CheckHint { get; set; }
}

/// <summary>
/// Catalogue des laboratoires et écriture du bloc inséré dans le cours.
/// Le cours ne contient qu'une description : le code des simulations reste celui de la plateforme.
/// </summary>
public static class SimulationCatalog
{
    public static readonly SimType[] Types =
    [
        new("dc-circuit", "Circuit électrique", "Courant visible dans des résistances en série ou en parallèle.",
            "Résistances en parallèle", "Faites varier R1 : le courant se répartit-il comme vous l'aviez prévu ?",
            [
                new("topology", "Montage", [new("parallel", "En parallèle"), new("series", "En série")]),
                new("branches", "Résistances", [new("2", "2"), new("3", "3"), new("1", "1")]),
            ],
            [
                new("voltage", "Tension U", 12, 1, 48, 0.5, "V"),
                new("R1", "R1", 100, 10, 1000, 10, "Ω"),
                new("R2", "R2", 220, 10, 1000, 10, "Ω"),
                new("R3", "R3", 470, 10, 1000, 10, "Ω"),
            ], []),
        new("transient", "Régime transitoire", "Oscilloscope d'un circuit RC, RL ou RLC.",
            "Charge d'un condensateur", "Mesurez le temps pour atteindre 63 % de la valeur finale.",
            [new("kind", "Circuit", [new("RC", "RC"), new("RL", "RL"), new("RLC", "RLC")])],
            [
                new("voltage", "Tension U", 5, 1, 24, 0.5, "V"),
                new("R", "R", 1000, 10, 10000, 10, "Ω"),
                new("C", "C", 10, 1, 1000, 1, "µF"),
                new("L", "L", 100, 1, 2000, 1, "mH"),
            ], []),
        new("young", "Fentes d'Young", "Une figure d'interférence construite impact par impact.",
            "Fentes d'Young, particule par particule", "Poussez la visibilité vers 0 : que deviennent les franges ?",
            [],
            [
                new("slit", "Écart entre les fentes", 4, 1, 8, 0.5),
                new("visibility", "Visibilité de départ", 1, 0, 1, 0.05),
            ], []),
        new("qubit", "Qubit", "État sur la sphère de Bloch et statistiques de mesure.",
            "Préparer et mesurer un qubit", "Comparez la fréquence observée à la probabilité théorique.",
            [],
            [
                new("theta", "θ", 60, 0, 180, 1, "°"),
                new("phi", "φ", 45, 0, 360, 1, "°"),
            ], []),
        new("plot", "Tracé à curseurs", "N'importe quelle fonction, dans n'importe quelle matière.",
            "Étude d'une fonction", "Identifiez l'effet de chaque paramètre sur la courbe.",
            [],
            [
                new("xmin", "x minimum", -10, -1000, 1000, 1),
                new("xmax", "x maximum", 10, -1000, 1000, 1),
            ],
            [
                new("expr", "Fonction de x", "a*sin(b*x)", "Fonctions : sin, cos, tan, exp, ln, log, sqrt, abs. Constantes : pi, e. Puissance : ^", Required: true),
                new("expr2", "Deuxième fonction (facultatif)", ""),
                new("params", "Curseurs", "a:0:5:1, b:0.1:4:1", "Pour chaque curseur : nom:minimum:maximum:valeur de départ, séparés par des virgules."),
            ]),
    ];

    public static SimType Find(string id) => Types.First(t => t.Id == id);

    public static SimulationDraft NewDraft(SimType type)
    {
        var draft = new SimulationDraft { Title = type.Title, Caption = type.Caption };
        foreach (var choice in type.Choices) draft.Choices[choice.Key] = choice.Options[0].Value;
        foreach (var number in type.Numbers) draft.Numbers[number.Key] = number.Default;
        foreach (var field in type.Texts) draft.Texts[field.Key] = field.Default;
        return draft;
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Bloc Markdown prêt à insérer dans le cours.</summary>
    public static string ToMarkdown(SimType type, SimulationDraft draft)
    {
        var config = new Dictionary<string, object?> { ["type"] = type.Id, ["title"] = draft.Title.Trim(), ["caption"] = draft.Caption.Trim() };

        foreach (var choice in draft.Choices)
            config[choice.Key] = int.TryParse(choice.Value, out var number) ? number : choice.Value;
        foreach (var value in draft.Numbers)
            config[value.Key] = value.Value;
        foreach (var field in draft.Texts.Where(t => !string.IsNullOrWhiteSpace(t.Value)))
            config[field.Key] = field.Value.Trim();

        var options = (draft.PredictOptions ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!string.IsNullOrWhiteSpace(draft.PredictQuestion) && options.Length >= 2)
            config["predict"] = new Dictionary<string, object?>
            {
                ["question"] = draft.PredictQuestion.Trim(),
                ["options"] = options,
                ["answer"] = Math.Clamp(draft.PredictAnswer - 1, 0, options.Length - 1),
                ["explain"] = draft.PredictExplain?.Trim() ?? ""
            };

        if (!string.IsNullOrWhiteSpace(draft.CheckQuestion)
            && double.TryParse(draft.CheckAnswer?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var answer))
            config["check"] = new Dictionary<string, object?>
            {
                ["question"] = draft.CheckQuestion.Trim(),
                ["answer"] = answer,
                ["unit"] = draft.CheckUnit?.Trim() ?? "",
                ["hint"] = draft.CheckHint?.Trim() ?? ""
            };

        return $"\n\n```simulation\n{JsonSerializer.Serialize(config, Options)}\n```\n\n";
    }
}
