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
        var document = Markdown.Parse(markdown ?? "", Pipeline);
        var toc = document.Descendants<HeadingBlock>()
            .Where(h => h.Level is 2 or 3)
            .Select(h => new TocEntry(h.GetAttributes().Id ?? "", InlineText(h.Inline), h.Level))
            .Where(t => t.Id.Length > 0)
            .ToList();
        return (document.ToHtml(Pipeline), toc);
    }

    private static string InlineText(ContainerInline? inline) =>
        inline is null ? "" : string.Concat(inline.Descendants<LiteralInline>().Select(l => l.Content.ToString()));
}
