using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace GPOE26.UI.Services;

public record TocEntry(string Id, string Title, int Level);

/// <summary>
/// Rendu Markdown des cours. Le HTML brut est désactivé : un cours est écrit par un enseignant
/// et lu par des élèves, il ne doit pas pouvoir injecter de script dans leur page.
/// </summary>
public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseAdvancedExtensions()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        .Build();

    public static (string Html, List<TocEntry> Toc) Render(string markdown)
    {
        var document = Markdown.Parse(NormalizeMath(markdown ?? ""), Pipeline);
        var toc = document.Descendants<HeadingBlock>()
            .Where(h => h.Level is 2 or 3)
            .Select(h => new TocEntry(h.GetAttributes().Id ?? "", InlineText(h.Inline), h.Level))
            .Where(t => t.Id.Length > 0)
            .ToList();
        return (document.ToHtml(Pipeline), toc);
    }

    /// <summary>
    /// Les modèles d'IA écrivent souvent les formules en \( … \) ou \[ … \] : Markdown y verrait des parenthèses
    /// échappées. On les convertit en $ … $ et $$ … $$, la syntaxe reconnue par le rendu des formules.
    /// </summary>
    public static string NormalizeMath(string markdown) =>
        DisplayLine.Replace(InlineDelimiters.Replace(DisplayDelimiters.Replace(markdown, "$$$$$1$$$$"), "$$$1$$"), "$$$$\n$1\n$$$$");

    /// <summary>Texte ligne à ligne (transcription d'une copie) : chaque retour à la ligne est conservé.</summary>
    public static string RenderLines(string text) =>
        Render(string.Join("  \n", (text ?? "").Replace("\r", "").Split('\n'))).Html;

    // Une formule seule sur sa ligne s'affiche centrée : Markdig ne le fait que si les $$ sont sur leurs propres lignes
    private static readonly System.Text.RegularExpressions.Regex DisplayLine =
        new(@"^[ \t]*\$\$[ \t]*([^\n]+?)[ \t]*\$\$[ \t]*$", System.Text.RegularExpressions.RegexOptions.Multiline);

    // Une formule ne traverse pas un paragraphe et ne contient pas de lien : \[ … \] est alors un crochet échappé
    private static readonly System.Text.RegularExpressions.Regex DisplayDelimiters =
        new(@"\\\[((?:(?!\n[ \t]*\n|\]\().)+?)\\\](?!\()", System.Text.RegularExpressions.RegexOptions.Singleline);
    private static readonly System.Text.RegularExpressions.Regex InlineDelimiters =
        new(@"\\\(((?:(?!\n[ \t]*\n).)+?)\\\)", System.Text.RegularExpressions.RegexOptions.Singleline);

    private static string InlineText(ContainerInline? inline) =>
        inline is null ? "" : string.Concat(inline.Descendants<LiteralInline>().Select(l => l.Content.ToString()));
}
