using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Cours.Service;

public record PageImage(string Token, Uri Url, string Alt);
public record HtmlCoursePage(string Title, string Markdown, List<PageImage> Images);

/// <summary>
/// Transforme une page de cours en Markdown : titres, paragraphes, listes, tableaux, gras, formules et images.
/// Le texte n'est jamais reformulé, et rien de la page (scripts, styles) n'est conservé.
/// </summary>
public static partial class HtmlCourseExtractor
{
    private const int MaxImages = 40;
    private const int MaxLabText = 600;
    private const string MathTag = "x-math";

    // Décor, commandes et éléments masqués aux lecteurs d'écran : rien de tout cela n'est du contenu de cours
    private static readonly string[] AlwaysRemoved =
        ["script", "style", "noscript", "nav", "form", "button", "input", "select", "textarea", "canvas", "iframe", "object", "embed", "template", "dialog", "audio", "video", "svg",
         "[hidden]", "[role=navigation]", "[aria-hidden=true]",
         // Outils d'édition, appels de note, sommaires et bandeaux des wikis et des blogs
         ".mw-editsection", "sup.reference", ".noprint", ".mw-jump-link", "#toc", ".toc", ".navbox", ".catlinks", ".hatnote", ".share", ".comments"];

    private static readonly string[] LabAttributes = ["data-lab", "data-sim", "data-simulation"];
    private static readonly string[] LabClasses = ["lab", "simulation", "simulator"];
    private static readonly HashSet<string> Formatting = ["strong", "b", "em", "i", "code", "a", "sup", "sub", MathTag];

    public static HtmlCoursePage Extract(Stream html, Uri baseUri) => Extract(new HtmlParser().ParseDocument(html), baseUri);

    public static HtmlCoursePage Extract(string html, Uri baseUri) => Extract(new HtmlParser().ParseDocument(html), baseUri);

    private static HtmlCoursePage Extract(IDocument document, Uri baseUri)
    {
        var usesMathDelimiters = document.Scripts.Any(s => Regex.IsMatch(s.Source ?? "", "mathjax|katex", RegexOptions.IgnoreCase));
        ConvertMathElements(document);

        var title = Clean(document.QuerySelector("main h1, article h1, h1")?.TextContent ?? "");
        if (title.Length == 0) title = Clean(document.Title ?? "");

        foreach (var noise in document.QuerySelectorAll(string.Join(",", AlwaysRemoved)).ToList()) noise.Remove();

        var root = ContentRoot(document);
        if (root.LocalName == "body")
            foreach (var chrome in root.QuerySelectorAll("header, footer, aside").ToList()) chrome.Remove();

        var writer = new Writer(baseUri, title, usesMathDelimiters);
        writer.Blocks(root);
        return new HtmlCoursePage(title, writer.Result(), writer.Images);
    }

    /// <summary>
    /// Le conteneur du cours : le plus resserré qui garde l'essentiel du texte,
    /// l'article plutôt que la page qui l'entoure (menus, langues, pied de page).
    /// </summary>
    private static IElement ContentRoot(IDocument document)
    {
        var body = document.Body!;
        var candidates = document
            .QuerySelectorAll("main, article, [role=main], #content, .content, .post, .chapter, .mw-parser-output, [itemprop=articleBody], .entry-content, .post-content, .article-content")
            .Select(c => (Element: c, Length: c.TextContent.Length))
            .ToList();
        if (candidates.Count == 0) return body;

        var longest = candidates.Max(c => c.Length);
        if (longest < body.TextContent.Length * 0.4) return body;
        return candidates.Where(c => c.Length >= longest * 0.7).MinBy(c => c.Length).Element;
    }

    /// <summary>Formules déjà rendues (KaTeX, MathML) : on retrouve leur source LaTeX.</summary>
    private static void ConvertMathElements(IDocument document)
    {
        foreach (var script in document.QuerySelectorAll("script[type^='math/tex']").ToList())
            Replace(document, script, script.TextContent, (script.GetAttribute("type") ?? "").Contains("display", StringComparison.Ordinal));

        foreach (var math in document.QuerySelectorAll(".katex-display, .katex, math").ToList())
        {
            if (math.ParentElement is null || math.Closest(MathTag) is not null) continue;
            var tex = math.QuerySelector("annotation[encoding='application/x-tex']")?.TextContent;
            if (string.IsNullOrWhiteSpace(tex)) continue;
            var display = math.ClassList.Contains("katex-display") || math.GetAttribute("display") == "block";
            Replace(document, math, tex, display);
        }
    }

    private static void Replace(IDocument document, IElement element, string tex, bool display)
    {
        var replacement = document.CreateElement(MathTag);
        replacement.SetAttribute("data-tex", tex.Trim());
        if (display) replacement.SetAttribute("data-display", "1");
        element.Replace(replacement);
    }

    /// <summary>
    /// Simulation de la page d'origine : son code ne peut pas être repris, mais son emplacement est signalé
    /// à l'agent, qui propose le labo équivalent au même endroit.
    /// </summary>
    private static bool IsInteractive(IElement element) =>
        LabAttributes.Any(element.HasAttribute)
        || (LabClasses.Any(element.ClassList.Contains) && Clean(element.TextContent).Length <= MaxLabText);

    private static string Clean(string text) => Whitespace().Replace(text, " ").Trim();

    private sealed class Writer(Uri baseUri, string title, bool mathDelimiters)
    {
        private readonly StringBuilder _output = new();
        private readonly StringBuilder _loose = new();

        public List<PageImage> Images { get; } = new();

        public string Result() { Flush(); return BlankLines().Replace(_output.ToString(), "\n\n").Trim(); }

        private void Flush()
        {
            var text = _loose.ToString().Trim();
            _loose.Clear();
            if (text.Length > 0) Emit(text);
        }

        private void Emit(string block) => _output.Append(block).Append("\n\n");

        public void Blocks(INode parent)
        {
            foreach (var node in parent.ChildNodes)
            {
                if (node is not IElement element)
                {
                    if (node.NodeType == NodeType.Text) _loose.Append(EscapeKeepingMath(Space(node.TextContent)));
                    continue;
                }

                if (IsInteractive(element))
                {
                    Flush();
                    OriginalLab(element);
                    continue;
                }

                switch (element.LocalName)
                {
                    case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                        Heading(element);
                        break;
                    case "p":
                        Flush();
                        var paragraph = Inline(element).Trim();
                        if (paragraph.Length > 0) Emit(paragraph);
                        break;
                    case "ul" or "ol":
                        Flush();
                        List(element, 0);
                        _output.Append('\n');
                        break;
                    case "pre":
                        Flush();
                        Emit($"```\n{element.TextContent.Trim('\n')}\n```");
                        break;
                    case "blockquote":
                        Flush();
                        Quote(element);
                        break;
                    case "table":
                        Flush();
                        Table(element);
                        break;
                    case "hr":
                        Flush();
                        break;
                    case "figure":
                        Flush();
                        foreach (var image in element.QuerySelectorAll("img")) AddImage(image, block: true);
                        Caption(element);
                        break;
                    case "img":
                        Flush();
                        AddImage(element, block: true);
                        break;
                    case MathTag when element.HasAttribute("data-display"):
                        Flush();
                        Emit($"$$ {element.GetAttribute("data-tex")} $$");
                        break;
                    case "div" or "section" or "article" or "main" or "details" or "summary" or "header" or "footer" or "aside" or "center" or "dl" or "dd" or "dt":
                        Flush();
                        Blocks(element);
                        Flush();
                        break;
                    default:
                        InlineNode(element, _loose, plain: false);
                        break;
                }
            }
            Flush();
        }

        private void Heading(IElement element)
        {
            Flush();
            var text = Clean(Spaced(element, plain: true, spaceAll: false));
            if (text.Length == 0 || (element.LocalName == "h1" && Clean(element.TextContent) == title)) return;
            var level = element.LocalName switch { "h1" or "h2" => 2, "h3" => 3, _ => 4 };
            Emit($"{new string('#', level)} {text}");
        }

        /// <summary>Une réplique par ligne, le nom de l'orateur séparé de ses paroles.</summary>
        private void Quote(IElement quote)
        {
            var parts = quote.Children.Where(c => c.LocalName is "p" or "div").ToList();
            var lines = (parts.Count > 0 ? parts : [quote])
                .Select(p => Clean(Spaced(p, plain: false, spaceAll: true)))
                .Where(l => l.Length > 0)
                .Select(l => "> " + l)
                .ToList();
            if (lines.Count > 0) Emit(string.Join("\n>\n", lines));
        }

        private void OriginalLab(IElement lab)
        {
            var name = Clean(lab.QuerySelector("h1, h2, h3, h4, h5, h6")?.TextContent ?? lab.GetAttribute("aria-label") ?? "");
            Emit($"**{LabProposalRules.OriginalLabMarker} {EscapeKeepingMath(name.Length > 0 ? name : "simulation")}**");
            Caption(lab);
        }

        private void Caption(IElement figure)
        {
            var caption = figure.QuerySelector("figcaption");
            if (caption is null) return;
            var text = Inline(caption).Trim();
            if (text.Length > 0) Emit("*" + text.Replace("*", "") + "*");
        }

        private void List(IElement list, int depth)
        {
            var number = 1;
            foreach (var item in list.Children.Where(c => c.LocalName == "li"))
            {
                var nested = item.Children.Where(c => c.LocalName is "ul" or "ol").ToList();
                nested.ForEach(n => n.Remove());
                var marker = list.LocalName == "ol" ? $"{number++}." : "-";
                _output.Append(new string(' ', depth * 3)).Append(marker).Append(' ').Append(Inline(item).Trim().Replace("\n", " ")).Append('\n');
                foreach (var child in nested) List(child, depth + 1);
            }
        }

        private void Table(IElement table)
        {
            var rows = table.QuerySelectorAll("tr").Select(r => r.Children.Where(c => c.LocalName is "td" or "th")
                .Select(c => Inline(c).Replace("\n", " ").Replace("|", "\\|").Trim()).ToList()).Where(r => r.Count > 0).ToList();
            if (rows.Count == 0) return;

            var columns = rows.Max(r => r.Count);
            string Row(List<string> cells) => "| " + string.Join(" | ", cells.Concat(Enumerable.Repeat("", columns - cells.Count))) + " |";
            var lines = new List<string> { Row(rows[0]), "|" + string.Concat(Enumerable.Repeat("---|", columns)) };
            lines.AddRange(rows.Skip(1).Select(Row));
            Emit(string.Join("\n", lines));
        }

        /// <summary>
        /// Texte dont les morceaux sont des éléments distincts (numéro de section, nom de l'orateur) :
        /// la mise en page les séparait, une espace les sépare ici.
        /// </summary>
        private string Spaced(IElement element, bool plain, bool spaceAll)
        {
            var sb = new StringBuilder();
            foreach (var node in element.ChildNodes)
            {
                var separate = node is IElement child && (spaceAll || !Formatting.Contains(child.LocalName));
                if (separate) sb.Append(' ');
                InlineNode(node, sb, plain);
                if (separate) sb.Append(' ');
            }
            return sb.ToString();
        }

        private string Inline(INode parent, bool plain = false)
        {
            var sb = new StringBuilder();
            foreach (var node in parent.ChildNodes) InlineNode(node, sb, plain);
            return sb.ToString();
        }

        private void InlineNode(INode node, StringBuilder sb, bool plain)
        {
            if (node is not IElement element)
            {
                if (node.NodeType == NodeType.Text) sb.Append(EscapeKeepingMath(Space(node.TextContent)));
                return;
            }

            switch (element.LocalName)
            {
                case "br":
                    sb.Append(plain ? " " : "  \n");
                    break;
                case "strong" or "b" when !plain:
                    Wrap(sb, Inline(element), "**");
                    break;
                case "em" or "i" when !plain:
                    Wrap(sb, Inline(element), "*");
                    break;
                case "code":
                    var code = Clean(element.TextContent);
                    if (code.Length > 0) sb.Append('`').Append(code.Replace("`", "'")).Append('`');
                    break;
                case MathTag:
                    var tex = element.GetAttribute("data-tex");
                    sb.Append(element.HasAttribute("data-display") ? $"\n\n$$ {tex} $$\n\n" : $"${tex}$");
                    break;
                case "img":
                    sb.Append(AddImage(element, block: false));
                    break;
                case "a" when !plain && LinkTarget(element) is { } target:
                    var label = Inline(element, plain: true).Trim();
                    if (label.Length > 0) sb.Append('[').Append(label).Append("](").Append(target).Append(')');
                    break;
                default:
                    sb.Append(Inline(element, plain));
                    break;
            }
        }

        private static void Wrap(StringBuilder sb, string inner, string mark)
        {
            var text = inner.Trim();
            if (text.Length == 0) { sb.Append(inner); return; }
            if (inner.StartsWith(' ')) sb.Append(' ');
            sb.Append(mark).Append(text).Append(mark);
            if (inner.EndsWith(' ')) sb.Append(' ');
        }

        private string? LinkTarget(IElement link)
        {
            var href = link.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href) || href.StartsWith('#')) return null;
            return Uri.TryCreate(baseUri, href, out var target) && target.Scheme is "http" or "https"
                ? target.AbsoluteUri.Replace(")", "%29", StringComparison.Ordinal)
                : null;
        }

        private string AddImage(IElement image, bool block)
        {
            if (Images.Count >= MaxImages) return "";
            var source = image.GetAttribute("src") ?? image.GetAttribute("data-src");
            if (string.IsNullOrWhiteSpace(source) || source.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return "";
            if (!Uri.TryCreate(baseUri, source, out var url) || url.Scheme is not ("http" or "https")) return "";

            // Pictogrammes et pixels de suivi n'ont pas leur place dans un cours
            if ((int.TryParse(image.GetAttribute("width"), out var width) && width < 40)
                || (int.TryParse(image.GetAttribute("height"), out var height) && height < 40)) return "";

            var token = $"[[IMG:{Images.Count + 1}]]";
            Images.Add(new PageImage(token, url, Clean(image.GetAttribute("alt") ?? "")));
            if (block) Emit(token);
            return block ? "" : $"\n\n{token}\n\n";
        }

        private string EscapeKeepingMath(string text)
        {
            var pattern = mathDelimiters ? MathOrDollars() : MathDelimited();
            var sb = new StringBuilder();
            var position = 0;
            foreach (Match match in pattern.Matches(text))
            {
                sb.Append(PdfMarkdownBuilder.Escape(text[position..match.Index])).Append(match.Value);
                position = match.Index + match.Length;
            }
            return sb.Append(PdfMarkdownBuilder.Escape(text[position..])).ToString();
        }

        private static string Space(string text) => Whitespace().Replace(text, " ");
    }

    [GeneratedRegex(@"\s+")] private static partial Regex Whitespace();
    [GeneratedRegex(@"\n{3,}")] private static partial Regex BlankLines();
    [GeneratedRegex(@"\\\(.+?\\\)|\\\[.+?\\\]|\$\$.+?\$\$")] private static partial Regex MathDelimited();
    [GeneratedRegex(@"\\\(.+?\\\)|\\\[.+?\\\]|\$\$.+?\$\$|\$[^$\n]{1,200}?\$")] private static partial Regex MathOrDollars();
}
