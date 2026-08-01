using System.Text.RegularExpressions;
using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace GPOE26.Markdown;

/// <summary>Une entrée du sommaire du cours.</summary>
/// <param name="Id">Ancre du titre dans le HTML rendu.</param>
/// <param name="Level">1 pour H1, 2 pour H2, 3 pour H3.</param>
public sealed record OutlineEntry(string Id, string Text, int Level);

/// <summary>
/// Le rendu d'un cours, du Markdown au HTML.
///
/// Extrait du composant Blazor pour être exécutable ailleurs : le harness s'en sert pour
/// servir <c>GET /cours/{id}/rendu</c> à l'application bureau, qui n'a pas de Blazor. Le
/// composant, lui, ne fait plus que l'afficher.
///
/// Rien ici ne dépend d'un framework d'interface : c'est ce qui permet au Web et au
/// bureau de montrer exactement le même cours, avec les mêmes ancres.
/// </summary>
public static class CourseRenderer
{
    /// <summary>
    /// Pipeline partagé, et il doit l'être.
    ///
    /// Les ancres des titres viennent de l'extension AutoIdentifiers (incluse dans
    /// UseAdvancedExtensions) : construire le sommaire avec un pipeline différent de
    /// celui du rendu produirait des identifiants qui ne correspondent à rien dans le
    /// HTML, donc un sommaire dont les liens ne mènent nulle part.
    /// </summary>
    public static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseMathematics()
        // Le contenu vient d'un modèle et de la saisie libre de l'élève. DisableHtml
        // échappe le balisage brut au lieu de le laisser passer dans la page.
        .DisableHtml()
        .Build();

    /// <summary>Le cours en HTML, prêt à poser dans un conteneur de lecture.</summary>
    public static string ToHtml(string? markdown) =>
        string.IsNullOrWhiteSpace(markdown)
            ? string.Empty
            : Decorate(Markdig.Markdown.ToHtml(markdown, Pipeline));

    /// <summary>
    /// Le sommaire, bâti sur les titres H1 à H3.
    /// Les H4 et au-delà sont ignorés : passé trois niveaux, un sommaire dessert la lecture.
    /// </summary>
    public static IReadOnlyList<OutlineEntry> BuildOutline(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return [];

        var document = Markdig.Markdown.Parse(markdown, Pipeline);
        var entries = new List<OutlineEntry>();

        foreach (var heading in document.Descendants<HeadingBlock>())
        {
            if (heading.Level is < 1 or > 3) continue;

            var id = heading.GetAttributes().Id;
            if (string.IsNullOrEmpty(id)) continue;

            var text = heading.Inline is null
                ? string.Empty
                : string.Concat(heading.Inline.Descendants<LiteralInline>().Select(l => l.Content.ToString()));

            if (string.IsNullOrWhiteSpace(text)) continue;

            entries.Add(new OutlineEntry(id, text.Trim(), heading.Level));
        }

        return entries;
    }

    /// <summary>
    /// Extrait une grande partie du cours — un titre de niveau 2 et tout ce qui en dépend —
    /// pour un parcours qui se lit partie par partie.
    ///
    /// <paramref name="headingPath"/> est le chemin produit par le découpage
    /// (« Les fonctions affines › Le coefficient directeur ») ; seul le dernier segment est
    /// comparé, puisque c'est lui qui porte le titre de la partie. Renvoie le cours entier
    /// si la partie n'est pas retrouvée : mieux vaut trop montrer qu'une page vide.
    /// </summary>
    public static string ExtractPart(string? markdown, string? headingPath)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;
        if (string.IsNullOrWhiteSpace(headingPath)) return markdown;

        var target = headingPath.Split('›').Last().Trim();
        if (target.Length == 0) return markdown;

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var collected = new List<string>();
        var inPart = false;
        var inFence = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal))
                inFence = !inFence;

            if (!inFence && line.StartsWith("## ", StringComparison.Ordinal))
            {
                var title = line[3..].Trim();

                if (inPart) break;                  // partie suivante : on s'arrête
                if (!string.Equals(title, target, StringComparison.OrdinalIgnoreCase)) continue;

                inPart = true;
                collected.Add(line);
                continue;
            }

            // Un nouveau titre de niveau 1 clôt aussi la partie.
            if (inPart && !inFence && line.StartsWith("# ", StringComparison.Ordinal)) break;

            if (inPart) collected.Add(line);
        }

        return collected.Count > 0 ? string.Join("\n", collected).Trim() : markdown;
    }

    /// <summary>
    /// Retouches appliquées au HTML produit par Markdig, et non au Markdown source :
    /// DisableHtml() échapperait tout balisage injecté en amont.
    /// </summary>
    public static string Decorate(string html) => WrapTables(RenderCallouts(html));

    // ── Tableaux ─────────────────────────────────────────────────────────────────
    //
    // Un tableau plus large que la mesure du canvas ferait défiler la page entière
    // horizontalement. On l'enveloppe pour qu'il défile dans son propre conteneur.

    private static readonly Regex TablePattern = new(
        @"<table>.*?</table>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static string WrapTables(string html) =>
        html.Contains("<table>", StringComparison.Ordinal)
            ? TablePattern.Replace(html, static m => $"<div class=\"table-scroll\">{m.Value}</div>")
            : html;

    // ── Encadrés pédagogiques ────────────────────────────────────────────────────
    //
    // L'agent Structurateur produit des blocs de la forme :
    //
    //     > [!definition] Fonction dérivée
    //     > Le nombre dérivé de f en a est la limite…
    //
    // Markdig les rend comme une citation ordinaire. On les retransforme ici en
    // <aside class="callout callout-definition">, APRÈS Markdig : le faire avant serait
    // incompatible avec DisableHtml(), qui échapperait notre propre balisage.

    private static readonly Regex CalloutPattern = new(
        @"<blockquote>\s*<p>\s*\[!(?<kind>[a-zA-Z]+)\]\s*(?<title>[^<]*?)\s*</p>(?<body>.*?)</blockquote>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Dictionary<string, string> CalloutLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["definition"] = "Définition",
        ["theoreme"] = "Théorème",
        ["propriete"] = "Propriété",
        ["formule"] = "Formule",
        ["exemple"] = "Exemple",
        ["methode"] = "Méthode",
        ["remarque"] = "Remarque",
        ["attention"] = "Attention",
        ["astuce"] = "Astuce",
        ["resume"] = "À retenir",
    };

    private static string RenderCallouts(string html)
    {
        if (html.IndexOf("[!", StringComparison.Ordinal) < 0) return html;

        return CalloutPattern.Replace(html, static m =>
        {
            var kind = m.Groups["kind"].Value.ToLowerInvariant();
            if (!CalloutLabels.TryGetValue(kind, out var label))
                return m.Value; // type inconnu : on laisse la citation telle quelle

            var title = m.Groups["title"].Value;
            var heading = string.IsNullOrWhiteSpace(title) ? label : $"{label} — {title}";

            return $"<aside class=\"callout callout-{kind}\">"
                 + $"<p class=\"callout-title\">{heading}</p>"
                 + m.Groups["body"].Value
                 + "</aside>";
        });
    }
}
