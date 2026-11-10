using System.Text;

namespace Cours.Service;

/// <summary>Caractère placé dans la page (coordonnées PDF : l'axe vertical monte).</summary>
public record PdfGlyph(string Text, double Left, double Right, double Baseline, double Size, string Font, bool Vector = false)
{
    public double Center => (Left + Right) / 2;
    public bool IsItalic => Font.Contains("Italic", StringComparison.OrdinalIgnoreCase) || Font.Contains("Oblique", StringComparison.OrdinalIgnoreCase);
    public bool IsSymbol => Font.Contains("Symbol", StringComparison.OrdinalIgnoreCase) || IsAccentFont;
    public bool IsAccentFont => Font.Contains("MTExtra", StringComparison.OrdinalIgnoreCase);
    public bool IsBold => Font.Contains("Bold", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Trait de fraction (Y : sa hauteur dans la page).</summary>
public record PdfBar(double Left, double Right, double Y);

/// <summary>
/// Reconstruit les formules à partir de la position des caractères : indices, exposants, fractions, symboles.
/// Ne dépend d'aucun modèle d'IA : le résultat est déterminé par ce qui est imprimé dans le PDF.
/// </summary>
public static class PdfFormulaBuilder
{
    private const double ScriptSizeRatio = 0.88;

    private static readonly Dictionary<string, string> Symbols = new()
    {
        ["−"] = "-", ["–"] = "-", ["⋅"] = @"\cdot ", ["·"] = @"\cdot ", ["∙"] = @"\cdot ", ["×"] = @"\times ", ["÷"] = @"\div ",
        ["⇒"] = @"\Rightarrow ", ["⇔"] = @"\Leftrightarrow ", ["→"] = @"\rightarrow ", ["←"] = @"\leftarrow ", ["↔"] = @"\leftrightarrow ",
        ["∑"] = @"\sum ", ["∏"] = @"\prod ", ["∫"] = @"\int ", ["∞"] = @"\infty ", ["∂"] = @"\partial ", ["∇"] = @"\nabla ",
        ["≈"] = @"\approx ", ["≠"] = @"\neq ", ["≤"] = @"\leq ", ["≥"] = @"\geq ", ["≡"] = @"\equiv ", ["±"] = @"\pm ", ["∝"] = @"\propto ",
        ["α"] = @"\alpha ", ["β"] = @"\beta ", ["γ"] = @"\gamma ", ["δ"] = @"\delta ", ["ε"] = @"\varepsilon ", ["ζ"] = @"\zeta ", ["η"] = @"\eta ",
        ["θ"] = @"\theta ", ["λ"] = @"\lambda ", ["μ"] = @"\mu ", ["µ"] = @"\mu ", ["ν"] = @"\nu ", ["ξ"] = @"\xi ", ["π"] = @"\pi ", ["ρ"] = @"\rho ",
        ["σ"] = @"\sigma ", ["τ"] = @"\tau ", ["φ"] = @"\varphi ", ["ϕ"] = @"\phi ", ["χ"] = @"\chi ", ["ψ"] = @"\psi ", ["ω"] = @"\omega ",
        ["Γ"] = @"\Gamma ", ["Δ"] = @"\Delta ", ["∆"] = @"\Delta ", ["Θ"] = @"\Theta ", ["Λ"] = @"\Lambda ", ["Π"] = @"\Pi ", ["Σ"] = @"\Sigma ",
        ["Φ"] = @"\Phi ", ["Ψ"] = @"\Psi ", ["Ω"] = @"\Omega ",
        ["’"] = "'", ["%"] = @"\%", ["#"] = @"\#", ["&"] = @"\&", ["_"] = @"\_", ["{"] = @"\{", ["}"] = @"\}", ["$"] = @"\$",
    };

    /// <summary>
    /// Une formule n'est écrite que si sa lecture est sûre. Fractions imbriquées et sommes à bornes
    /// passent en capture d'image : une image exacte vaut mieux qu'une équation fausse.
    /// </summary>
    public static bool IsReliable(IReadOnlyList<PdfGlyph> glyphs, IReadOnlyList<PdfBar> bars)
    {
        var nested = bars.Any(a => bars.Any(b => a != b && b.Right - b.Left > a.Right - a.Left && a.Left >= b.Left - 1 && a.Right <= b.Right + 1));
        if (nested) return false;

        foreach (var bar in bars)
        {
            var span = glyphs.Where(g => g.Center >= bar.Left - 1 && g.Center <= bar.Right + 1).ToList();
            if (!span.Any(g => g.Baseline > bar.Y) || !span.Any(g => g.Baseline <= bar.Y)) return false;
        }

        var size = glyphs.Max(g => g.Size);
        return !glyphs.Where(g => g.Text == "∑").Any(sum =>
            glyphs.Any(g => g != sum && g.Size < size * ScriptSizeRatio && g.Center >= sum.Left && g.Center <= sum.Right));
    }

    /// <summary>Formule complète : chaque trait de fraction sépare ce qui est au-dessus de ce qui est en dessous.</summary>
    public static string ToLatex(IReadOnlyList<PdfGlyph> glyphs, IReadOnlyList<PdfBar> bars)
    {
        if (glyphs.Count == 0) return "";
        if (bars.Count == 0) return Row(glyphs);

        // Le trait le plus long est la fraction principale : les autres sont dedans ou à côté
        var bar = bars.OrderByDescending(b => b.Right - b.Left).First();
        var others = bars.Where(b => b != bar).ToList();
        bool Inside(double x) => x >= bar.Left - 3 && x <= bar.Right + 3;

        // Ce qui est sur l'axe du trait (« = » juste à côté) n'est ni au numérateur ni au dénominateur
        var numerator = glyphs.Where(g => Inside(g.Center) && g.Baseline > bar.Y + 1.5).ToList();
        var denominator = glyphs.Where(g => Inside(g.Center) && g.Baseline < bar.Y - 6).ToList();
        var rest = glyphs.Except(numerator).Except(denominator).ToList();
        var middle = (bar.Left + bar.Right) / 2;
        var left = rest.Where(g => g.Center < middle).ToList();
        var right = rest.Where(g => g.Center >= middle).ToList();

        double Mid(PdfBar b) => (b.Left + b.Right) / 2;
        return ToLatex(left, others.Where(b => Mid(b) < bar.Left).ToList())
            + $@"\frac{{{ToLatex(numerator, others.Where(b => Mid(b) >= bar.Left && Mid(b) <= bar.Right && b.Y > bar.Y).ToList())}}}"
            + $"{{{ToLatex(denominator, others.Where(b => Mid(b) >= bar.Left && Mid(b) <= bar.Right && b.Y <= bar.Y).ToList())}}}"
            + ToLatex(right, others.Where(b => Mid(b) > bar.Right).ToList());
    }

    /// <summary>Ligne de texte courant : les mots restent du texte, les expressions passent en $…$.</summary>
    public static string InlineLine(IReadOnlyList<PdfGlyph> line)
    {
        var glyphs = WithoutAccents(line).OrderBy(g => g.Left).ToList();
        if (glyphs.Count == 0) return "";

        var (mainSize, mainBaseline) = MainLine(glyphs);
        var words = SplitWords(glyphs, mainSize);
        var isMath = words.Select((w, i) => IsStrongMath(w, words, i, mainSize, mainBaseline)).ToArray();

        // Les chiffres et opérateurs qui touchent une expression en font partie : « U = 0 »
        for (var i = 1; i < words.Count; i++) if (isMath[i - 1] && IsWeakMath(words[i])) isMath[i] = true;
        for (var i = words.Count - 2; i >= 0; i--) if (isMath[i + 1] && IsWeakMath(words[i])) isMath[i] = true;

        var sb = new StringBuilder();
        var run = new List<PdfGlyph>();
        var boldRun = new List<string>();

        void Space() { if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' '); }
        void FlushBold()
        {
            if (boldRun.Count == 0) return;
            Space();
            sb.Append("**").Append(string.Join(" ", boldRun)).Append("**");
            boldRun.Clear();
        }
        void FlushMath(string after = "")
        {
            if (run.Count == 0) return;
            Space();
            sb.Append('$').Append(Row(run)).Append('$').Append(after);
            run.Clear();
        }

        for (var i = 0; i < words.Count; i++)
        {
            var word = words[i];
            if (isMath[i])
            {
                FlushBold();
                var trailing = word[^1].Text is "." or "," or ";" or ":" && word.Count > 1 && !word[^1].IsSymbol ? word[^1].Text : "";
                run.AddRange(trailing.Length > 0 ? word.Take(word.Count - 1) : word);
                if (trailing.Length > 0) FlushMath(trailing);
                continue;
            }

            FlushMath();
            var text = PdfMarkdownBuilder.Escape(string.Concat(word.Select(g => g.Text)));
            if (word.Any(g => g.Text.Any(char.IsLetterOrDigit)) && word.Count(g => g.IsBold) * 2 > word.Count)
            {
                boldRun.Add(text);
                continue;
            }
            if (boldRun.Count > 0 && word.All(g => g.IsBold)) { boldRun.Add(text); continue; }
            FlushBold();
            Space();
            sb.Append(text);
        }
        FlushMath();
        FlushBold();
        return sb.ToString().Trim();
    }

    private static List<List<PdfGlyph>> SplitWords(List<PdfGlyph> glyphs, double mainSize)
    {
        var words = new List<List<PdfGlyph>>();
        foreach (var glyph in glyphs.Where(g => !string.IsNullOrWhiteSpace(g.Text)))
        {
            if (words.Count > 0 && glyph.Left - words[^1][^1].Right <= mainSize * 0.16)
                words[^1].Add(glyph);
            else
                words.Add([glyph]);
        }
        return words;
    }

    private static bool IsStrongMath(List<PdfGlyph> word, List<List<PdfGlyph>> words, int index, double mainSize, double mainBaseline)
    {
        if (word.Any(g => g.IsSymbol)) return true;
        if (word.Any(g => IsScript(g, mainSize, mainBaseline))) return true;

        var letters = word.Count(g => g.Text.Any(char.IsLetter));
        if (letters == 0 || letters > 2 || !word.All(g => g.IsItalic || !g.Text.Any(char.IsLetter))) return false;

        // Un mot court au milieu d'une phrase en italique reste du texte
        bool LongItalic(int i) => i >= 0 && i < words.Count && words[i].Count >= 4 && words[i].All(g => g.IsItalic);
        return !LongItalic(index - 1) && !LongItalic(index + 1);
    }

    private static bool IsWeakMath(List<PdfGlyph> word) =>
        word.All(g => g.Text.All(c => char.IsDigit(c) || "=+-−/().,'".Contains(c)))
        && word.Any(g => g.Text.Any(c => char.IsDigit(c) || "=+−/".Contains(c)));

    private static bool IsScript(PdfGlyph g, double mainSize, double mainBaseline) =>
        g.Size < mainSize * ScriptSizeRatio && Math.Abs(g.Baseline - mainBaseline) > mainSize * 0.08;

    /// <summary>Taille et ligne de base du texte principal, sans se laisser tromper par les indices.</summary>
    private static (double Size, double Baseline) MainLine(IReadOnlyList<PdfGlyph> glyphs)
    {
        var candidates = glyphs.Where(g => !g.IsSymbol).DefaultIfEmpty(glyphs[0]).ToList();
        var size = candidates.Max(g => g.Size);
        var baseline = candidates.Where(g => g.Size >= size - 0.5)
            .GroupBy(g => Math.Round(g.Baseline))
            .OrderByDescending(g => g.Count())
            .First().First().Baseline;
        return (size, baseline);
    }

    private static string Row(IReadOnlyList<PdfGlyph> input)
    {
        if (input.Count == 0) return "";

        var glyphs = WithoutAccents(input).OrderBy(g => g.Left).ToList();
        if (glyphs.Count == 0) return "";

        var (mainSize, mainBaseline) = MainLine(glyphs);
        var sb = new StringBuilder();

        for (var i = 0; i < glyphs.Count;)
        {
            var glyph = glyphs[i];
            if (IsScript(glyph, mainSize, mainBaseline))
            {
                // Indice et exposant d'une même lettre forment un seul groupe : « u_{Cf}^{2} »
                var end = i;
                while (end < glyphs.Count && IsScript(glyphs[end], mainSize, mainBaseline)) end++;
                var scripts = glyphs.GetRange(i, end - i);
                var subs = scripts.Where(g => g.Baseline < mainBaseline).ToList();
                var sups = scripts.Where(g => g.Baseline >= mainBaseline).ToList();
                if (subs.Count > 0) sb.Append("_{").Append(Tokens(subs, script: true)).Append('}');
                if (sups.Count > 0) sb.Append("^{").Append(Tokens(sups, script: true)).Append('}');
                i = end;
                continue;
            }

            var stop = i;
            while (stop < glyphs.Count && !IsScript(glyphs[stop], mainSize, mainBaseline)) stop++;
            sb.Append(Tokens(glyphs.GetRange(i, stop - i), script: false));
            i = stop;
        }
        return sb.ToString().Trim();
    }

    /// <summary>Accent de vecteur : un glyphe dédié posé au-dessus d'une lettre, reporté sur cette lettre.</summary>
    private static List<PdfGlyph> WithoutAccents(IReadOnlyList<PdfGlyph> input)
    {
        var accents = input.Where(g => g.IsAccentFont).ToList();
        var glyphs = input.Except(accents).ToList();
        if (accents.Count == 0 || glyphs.Count == 0) return glyphs;

        var marked = accents.Select(a => glyphs.OrderBy(g => Math.Abs(g.Center - a.Center)).First()).ToHashSet();
        return glyphs.Select(g => marked.Contains(g) ? g with { Vector = true } : g).ToList();
    }

    private static string Tokens(List<PdfGlyph> glyphs, bool script)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < glyphs.Count;)
        {
            // Une espace visible entre deux éléments est conservée : les mots ne se collent pas
            if (i > 0 && !script && NeedsSpace(glyphs[i - 1], glyphs[i])) sb.Append(@"\ ");

            // Plusieurs lettres droites à la suite sont un mot, pas un produit de variables
            var end = i;
            while (end < glyphs.Count && IsRomanLetter(glyphs[end]) && (end == i || glyphs[end].Left - glyphs[end - 1].Right < glyphs[end].Size * 0.9)) end++;
            if (end - i >= 1 && IsRomanLetter(glyphs[i]))
            {
                var word = new StringBuilder();
                for (var k = i; k < end; k++)
                {
                    if (k > i && glyphs[k].Left - glyphs[k - 1].Right > glyphs[k].Size * 0.18) word.Append(' ');
                    word.Append(EscapeText(glyphs[k].Text));
                }
                var text = word.ToString();
                sb.Append(script || text.Length == 1 ? $@"\mathrm{{{text}}}" : $@"\text{{{text}}}");
                i = end;
                continue;
            }

            var glyph = glyphs[i];
            var token = Symbols.TryGetValue(glyph.Text, out var mapped) ? mapped : glyph.Text;
            if (glyph.Vector) token = $@"\vec{{{token.Trim()}}}";
            else if (glyph.IsBold && glyph.IsItalic && glyph.Text.All(char.IsLetter)) token = $@"\boldsymbol{{{token}}}";
            sb.Append(token);
            i++;
        }
        return sb.ToString();
    }

    private static bool NeedsSpace(PdfGlyph previous, PdfGlyph current)
    {
        var gap = current.Left - previous.Right;
        return gap > current.Size * 0.8 || (gap > current.Size * 0.18 && IsRomanLetter(previous) && IsRomanLetter(current));
    }

    /// <summary>« (I-26) » lu dans l'équation devient son numéro, placé à droite.</summary>
    public static string WithEquationTag(string latex) =>
        System.Text.RegularExpressions.Regex.Replace(latex, @"(?:\\ )?\(\\mathrm\{([IVX]+)\}-(\d+)\)\s*$", m => $@" \qquad\text{{({m.Groups[1].Value}-{m.Groups[2].Value})}}");

    private static bool IsRomanLetter(PdfGlyph g) => !g.IsItalic && !g.IsSymbol && g.Text.Length > 0 && g.Text.All(char.IsLetter);

    private static string EscapeText(string text) => text
        .Replace("\\", @"\textbackslash ")
        .Replace("{", @"\{").Replace("}", @"\}").Replace("%", @"\%").Replace("&", @"\&")
        .Replace("#", @"\#").Replace("_", @"\_").Replace("$", @"\$");
}

/// <summary>Ordre de lecture d'une page, une ou deux colonnes, y compris les éléments qui ne sont pas du texte.</summary>
public static class PdfLayout
{
    private const double CrossMargin = 15;

    /// <summary>Rang de lecture de chaque boîte. Top : position relative (0 = haut).</summary>
    public static int[] Order(double pageWidth, IReadOnlyList<(double Left, double Right, double Top)> boxes)
    {
        var center = pageWidth / 2;
        bool Full(int i) => boxes[i].Left < center - CrossMargin && boxes[i].Right > center + CrossMargin;
        bool IsLeft(int i) => boxes[i].Right <= center + CrossMargin;

        var indexes = Enumerable.Range(0, boxes.Count).ToList();
        var columns = indexes.Where(i => !Full(i)).ToList();
        var twoColumns = columns.Count(IsLeft) >= 2 && columns.Count(i => !IsLeft(i)) >= 2;

        var sequence = new List<int>();
        if (!twoColumns)
        {
            sequence.AddRange(indexes.OrderBy(i => boxes[i].Top));
        }
        else
        {
            // Un élément sur toute la largeur (titre) ferme la section en cours : colonne gauche, puis droite
            var separators = indexes.Where(Full).OrderBy(i => boxes[i].Top).ToList();
            var previous = double.NegativeInfinity;
            foreach (var separator in separators.Select(i => (int?)i).Append(null))
            {
                var limit = separator is { } s ? boxes[s].Top : double.PositiveInfinity;
                var section = columns.Where(i => boxes[i].Top >= previous && boxes[i].Top < limit).ToList();
                sequence.AddRange(section.Where(IsLeft).OrderBy(i => boxes[i].Top));
                sequence.AddRange(section.Where(i => !IsLeft(i)).OrderBy(i => boxes[i].Top));
                if (separator is { } current) sequence.Add(current);
                previous = limit;
            }
        }

        var ranks = new int[boxes.Count];
        for (var rank = 0; rank < sequence.Count; rank++) ranks[sequence[rank]] = rank;
        return ranks;
    }
}
