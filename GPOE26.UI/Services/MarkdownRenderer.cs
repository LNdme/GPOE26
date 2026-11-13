using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Extensions.GenericAttributes;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace GPOE26.UI.Services;

public record TocEntry(string Id, string Title, int Level);

/// <summary>
/// Rendu Markdown des cours et des réponses de l'IA. Le texte vient d'un enseignant, d'un élève ou d'un modèle
/// et s'affiche chez d'autres personnes : il ne doit jamais pouvoir exécuter de script dans leur page.
/// Trois portes sont fermées :
///   - le HTML brut (DisableHtml) ;
///   - les attributs libres « {onmouseover=…} » (extension GenericAttributes retirée) ;
///   - les liens « javascript: », « data: »… (seuls http, https, mailto, tel et les liens relatifs sont gardés).
/// </summary>
public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = BuildPipeline();

    private static readonly HashSet<string> SafeSchemes = new(StringComparer.OrdinalIgnoreCase) { "http", "https", "mailto", "tel" };

    private static MarkdownPipeline BuildPipeline()
    {
        var builder = new MarkdownPipelineBuilder()
            .DisableHtml()
            .UseAdvancedExtensions()
            .UseAutoIdentifiers(AutoIdentifierOptions.GitHub);
        builder.Extensions.RemoveAll(extension => extension is GenericAttributesExtension);
        return builder.Build();
    }

    public static (string Html, List<TocEntry> Toc) Render(string markdown)
    {
        var document = Markdown.Parse(NormalizeMath(markdown ?? ""), Pipeline);
        NeutralizeUnsafeLinks(document);
        var toc = document.Descendants<HeadingBlock>()
            .Where(h => h.Level is 2 or 3)
            .Select(h => new TocEntry(h.GetAttributes().Id ?? "", InlineText(h.Inline), h.Level))
            .Where(t => t.Id.Length > 0)
            .ToList();
        return (document.ToHtml(Pipeline), toc);
    }

    /// <summary>Un lien ou une image dont l'adresse n'est pas sûre pointe vers « # » ; un lien automatique redevient du texte.</summary>
    private static void NeutralizeUnsafeLinks(MarkdownDocument document)
    {
        // Les liens internes (notes de bas de page, renvois vers un titre) calculent leur adresse eux-mêmes : toujours « #… »
        foreach (var link in document.Descendants<LinkInline>().Where(l => l.GetDynamicUrl is null && !IsSafeUrl(l.Url)).ToList())
            link.Url = "#";

        foreach (var auto in document.Descendants<AutolinkInline>().Where(a => !IsSafeUrl(a.Url)).ToList())
            auto.ReplaceBy(new LiteralInline(auto.Url));
    }

    /// <summary>
    /// Adresse relative, ou d'un schéma autorisé. Les espaces et caractères de contrôle sont ignorés avant le test,
    /// comme le fait le navigateur (une tabulation glissée dans « javascript: » ne le rend pas inoffensif).
    /// </summary>
    public static bool IsSafeUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return true;
        var cleaned = new string(url.Where(c => !char.IsControl(c) && !char.IsWhiteSpace(c)).ToArray());

        var colon = cleaned.IndexOf(':');
        if (colon < 0) return true;
        var separator = cleaned.IndexOfAny(['/', '?', '#']);
        if (separator >= 0 && separator < colon) return true;   // « dossier/a:b », « ?x=a:b » : pas un schéma
        return SafeSchemes.Contains(cleaned[..colon]);
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
