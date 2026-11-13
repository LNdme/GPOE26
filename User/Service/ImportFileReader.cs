using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace User.Service;

/// <summary>
/// Transforme le fichier de la liste des élèves en tableau de cellules pour StudentImport
/// (docs/PLAN-ETABLISSEMENT-2.md, lot 9) : CSV, classeur Excel (.xlsx, première feuille non vide) ou document Word
/// (.docx, premier tableau qui contient une ligne de titres). Rien n'est exécuté ni gardé : seul le texte des cellules est lu.
/// </summary>
public static class ImportFileReader
{
    public const long MaxBytes = 5 * 1024 * 1024;

    public enum Kind { Csv, Excel, Word, Unknown }

    public static Kind KindOf(string? fileName) => Path.GetExtension(fileName ?? "").ToLowerInvariant() switch
    {
        ".csv" or ".txt" => Kind.Csv,
        ".xlsx" or ".xlsm" => Kind.Excel,
        ".docx" => Kind.Word,
        _ => Kind.Unknown
    };

    /// <summary>Le tableau du fichier, ou un message si le fichier ne peut pas être lu.</summary>
    public static (List<IReadOnlyList<string>> Table, string? Error) Read(Stream content, string? fileName)
    {
        try
        {
            return KindOf(fileName) switch
            {
                Kind.Csv => (StudentImport.SplitCsv(DecodeText(content)), null),
                Kind.Excel => ReadExcel(content),
                Kind.Word => ReadWord(content),
                _ => ([], "Format non pris en charge : envoyez un fichier Excel (.xlsx), Word (.docx) ou CSV. Un ancien fichier .xls ou .doc s'enregistre au nouveau format depuis Excel ou Word (« Enregistrer sous »).")
            };
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or FormatException
                                       or DocumentFormat.OpenXml.Packaging.OpenXmlPackageException or ArgumentException)
        {
            return ([], "Fichier illisible : est-il bien un fichier Excel ou Word, et non protégé par un mot de passe ?");
        }
    }

    /// <summary>Texte d'un CSV : UTF-8 s'il l'est, sinon Windows-1252 / Latin-1 (CSV d'Excel en français).</summary>
    public static string DecodeText(Stream content)
    {
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        var bytes = buffer.ToArray();
        try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
    }

    /// <summary>
    /// Première feuille qui contient des données. Chaque cellule est lue comme Excel l'affiche (un matricule « 00123 »
    /// garde ses zéros) ; une date devient « jj/mm/aaaa ». Une cellule fusionnée donne sa valeur à toute la fusion.
    /// </summary>
    private static (List<IReadOnlyList<string>>, string?) ReadExcel(Stream content)
    {
        using var workbook = new XLWorkbook(content);
        var sheet = workbook.Worksheets.FirstOrDefault(s => s.RangeUsed() is not null);
        if (sheet?.RangeUsed() is not { } used) return ([], "Le classeur est vide.");

        var lastColumn = used.LastColumn().ColumnNumber();
        var table = new List<IReadOnlyList<string>>();
        foreach (var row in sheet.Rows(used.FirstRow().RowNumber(), used.LastRow().RowNumber()))
        {
            var cells = new List<string>();
            for (var c = 1; c <= lastColumn; c++)
            {
                var cell = row.Cell(c);
                var source = cell.IsMerged() ? cell.MergedRange().FirstCell() : cell;
                cells.Add(CellText(source));
            }
            table.Add(cells);
        }
        return (table, null);
    }

    private static string CellText(IXLCell cell)
    {
        if (cell.IsEmpty()) return "";
        if (cell.DataType == XLDataType.DateTime)
            return cell.GetDateTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var shown = cell.GetFormattedString();
        return string.IsNullOrWhiteSpace(shown) ? cell.GetString() : shown;
    }

    /// <summary>
    /// Premier tableau du document qui contient une colonne « nom » (sinon le premier tableau).
    /// Une cellule fusionnée sur plusieurs colonnes occupe ces colonnes ; fusionnée vers le bas, elle reprend la valeur du dessus.
    /// </summary>
    private static (List<IReadOnlyList<string>>, string?) ReadWord(Stream content)
    {
        using var document = WordprocessingDocument.Open(content, false);
        var tables = document.MainDocumentPart?.Document?.Body?.Descendants<W.Table>().ToList() ?? [];
        if (tables.Count == 0)
            return ([], "Aucun tableau dans ce document Word : la liste des élèves doit être dans un tableau (une ligne de titres, puis un élève par ligne).");

        var read = tables.Select(ReadWordTable).ToList();
        var withHeader = read.FirstOrDefault(t => StudentImport.ParseTable(t).Error is null);
        return (withHeader ?? read[0], null);
    }

    private static List<IReadOnlyList<string>> ReadWordTable(W.Table table)
    {
        var rows = new List<IReadOnlyList<string>>();
        List<string>? above = null;
        foreach (var row in table.Elements<W.TableRow>())
        {
            var cells = new List<string>();
            foreach (var cell in row.Elements<W.TableCell>())
            {
                var props = cell.TableCellProperties;
                var span = props?.GridSpan?.Val?.Value ?? 1;
                var text = string.Join(" ", cell.Elements<W.Paragraph>().Select(p => p.InnerText.Trim()).Where(t => t.Length > 0));
                // Fusion verticale : la cellule continue celle du dessus
                if (props?.VerticalMerge is { } merge && (merge.Val is null || merge.Val.Value == W.MergedCellValues.Continue)
                    && above is not null && cells.Count < above.Count)
                    text = above[cells.Count];
                for (var i = 0; i < span; i++) cells.Add(text);
            }
            rows.Add(cells);
            above = cells;
        }
        return rows;
    }
}
