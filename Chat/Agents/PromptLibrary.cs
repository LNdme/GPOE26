using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Chat.Agents;

/// <summary>
/// Consignes des agents, rangées dans Chat/Agents/Prompts/*.md (ressources incorporées) plutôt que dans les routes :
/// on les relit, on les compare d'une version à l'autre, et les évaluations (evals/) disent si un changement les dégrade.
/// Une variable s'écrit {{nom}} ; une variable oubliée est une erreur, pas un texte laissé tel quel.
/// </summary>
public static partial class PromptLibrary
{
    private static readonly Assembly Assembly = typeof(PromptLibrary).Assembly;
    private const string Prefix = "Chat.Agents.Prompts.";
    private static readonly ConcurrentDictionary<string, string> Cache = new();

    /// <summary>Noms des consignes disponibles (sans extension), triés.</summary>
    public static IReadOnlyList<string> Names =>
        Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) && n.EndsWith(".md", StringComparison.Ordinal))
            .Select(n => n[Prefix.Length..^3])
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>Texte brut d'une consigne (fins de ligne normalisées, sans espaces de fin).</summary>
    public static string Get(string name) => Cache.GetOrAdd(name, static n =>
    {
        using var stream = Assembly.GetManifestResourceStream(Prefix + n + ".md")
            ?? throw new InvalidOperationException($"Consigne « {n} » introuvable (Chat/Agents/Prompts/{n}.md).");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().Replace("\r\n", "\n").TrimEnd();
    });

    /// <summary>Consigne avec ses variables remplacées. Toute variable de la consigne doit être fournie.</summary>
    public static string Render(string name, IReadOnlyDictionary<string, string>? values = null)
    {
        var text = Get(name);
        var missing = Variable().Matches(text).Select(m => m.Groups[1].Value).Distinct()
            .Where(v => values is null || !values.ContainsKey(v)).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException($"Consigne « {name} » : variable(s) non fournie(s) : {string.Join(", ", missing)}.");
        // Une seule passe : une valeur qui contiendrait « {{…}} » n'est pas remplacée à son tour
        return values is null ? text : Variable().Replace(text, m => values[m.Groups[1].Value]);
    }

    /// <summary>Version d'une consigne : 8 caractères de son empreinte, qui changent dès qu'un mot change.</summary>
    public static string Version(string name) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Get(name))))[..8].ToLowerInvariant();

    /// <summary>« site@1a2b3c4d, teacher@… » : écrit au démarrage pour relier un comportement à une version.</summary>
    public static string Describe() => string.Join(", ", Names.Select(n => $"{n}@{Version(n)}"));

    [GeneratedRegex(@"\{\{([a-z_]+)\}\}")] private static partial Regex Variable();
}
