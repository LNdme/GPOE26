using System.Globalization;
using System.Text;

namespace User.Service;

/// <summary>Ligne lue dans le fichier des élèves. Line : numéro de ligne dans le fichier (1 = en-tête).</summary>
public record ImportedStudent(int Line, string? Matricule, string LastName, string FirstName, string? ClassName, DateOnly? BirthDate, List<string> Errors);

/// <summary>
/// Lecture du fichier des élèves (docs/PLAN-ETABLISSEMENT.md, lot 3) : un CSV tel qu'Excel ou LibreOffice l'enregistrent
/// (séparateur ; , ou tabulation, guillemets), avec une ligne d'en-tête. Colonnes reconnues, sans tenir compte
/// des accents ni des majuscules : matricule, nom, prénom, classe, date de naissance — ou « nom et prénom » en une colonne.
/// Seul le nom est obligatoire : un matricule vide est généré, une classe vide laisse l'élève sans classe.
/// Les contrôles qui demandent la base (matricule déjà inscrit, classe inconnue) sont faits par l'appelant.
/// </summary>
public static class StudentImport
{
    public const int MaxRows = 2000;

    private enum Column { Matricule, LastName, FirstName, FullName, ClassName, BirthDate }

    private static readonly Dictionary<string, Column> Headers = new()
    {
        ["matricule"] = Column.Matricule, ["n matricule"] = Column.Matricule, ["no matricule"] = Column.Matricule,
        ["numero matricule"] = Column.Matricule, ["numero"] = Column.Matricule, ["id"] = Column.Matricule,
        ["nom"] = Column.LastName, ["nom de famille"] = Column.LastName, ["last name"] = Column.LastName, ["surname"] = Column.LastName,
        ["prenom"] = Column.FirstName, ["prenoms"] = Column.FirstName, ["prenom s"] = Column.FirstName, ["first name"] = Column.FirstName,
        ["nom s"] = Column.LastName, ["noms"] = Column.LastName,
        ["nom et prenom"] = Column.FullName, ["nom et prenoms"] = Column.FullName, ["noms et prenoms"] = Column.FullName,
        ["nom et prenom s"] = Column.FullName, ["nom s et prenom s"] = Column.FullName,
        ["nom complet"] = Column.FullName, ["eleve"] = Column.FullName, ["name"] = Column.FullName,
        ["classe"] = Column.ClassName, ["class"] = Column.ClassName,
        ["date de naissance"] = Column.BirthDate, ["naissance"] = Column.BirthDate, ["ne le"] = Column.BirthDate,
        ["nee le"] = Column.BirthDate, ["ne e le"] = Column.BirthDate, ["date of birth"] = Column.BirthDate, ["birth date"] = Column.BirthDate,
    };

    private static readonly string[] DateFormats = ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "d-M-yyyy", "dd.MM.yyyy"];

    /// <summary>Lit le fichier. Error : problème qui empêche toute lecture (pas d'en-tête, pas de colonne « nom »…).</summary>
    public static (List<ImportedStudent> Rows, string? Error) Parse(string? csv)
    {
        var lines = (csv ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
            .Select((text, index) => (Text: text.TrimStart('﻿'), Number: index + 1))
            .Where(l => !string.IsNullOrWhiteSpace(l.Text.Replace(";", "").Replace(",", "").Replace("\t", "")))
            .ToList();
        if (lines.Count == 0) return ([], "Le fichier est vide.");

        var separator = DetectSeparator(lines[0].Text);
        var header = Split(lines[0].Text, separator).Select(Normalize).ToList();
        var columns = new Dictionary<Column, int>();
        for (var i = 0; i < header.Count; i++)
            if (Headers.TryGetValue(header[i], out var column))
                columns.TryAdd(column, i);

        if (!columns.ContainsKey(Column.LastName) && !columns.ContainsKey(Column.FullName))
            return ([], "Colonne « nom » introuvable : la première ligne doit donner les titres des colonnes (matricule, nom, prénom, classe…).");
        if (lines.Count - 1 > MaxRows)
            return ([], $"Le fichier compte plus de {MaxRows} élèves : découpez-le, par exemple par niveau.");

        var rows = new List<ImportedStudent>();
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (text, number) in lines.Skip(1))
        {
            var cells = Split(text, separator);
            string Cell(Column c) => columns.TryGetValue(c, out var i) && i < cells.Count ? Clean(cells[i]) : "";

            var errors = new List<string>();
            var lastName = Cell(Column.LastName);
            var firstName = Cell(Column.FirstName);
            if (lastName.Length == 0 && Cell(Column.FullName) is { Length: > 0 } full)
                lastName = full;
            if (lastName.Length == 0) errors.Add("nom manquant");
            if (lastName.Length > 100 || firstName.Length > 100) errors.Add("nom ou prénom trop long (100 caractères au plus)");

            var matricule = Cell(Column.Matricule);
            if (matricule.Length > 40) errors.Add("matricule trop long (40 caractères au plus)");
            else if (matricule.Length > 0 && !seen.TryAdd(matricule, number))
                errors.Add($"matricule déjà donné ligne {seen[matricule]}");

            DateOnly? birth = null;
            var birthText = Cell(Column.BirthDate);
            if (birthText.Length > 0)
            {
                if (DateOnly.TryParseExact(birthText, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    birth = date;
                else
                    errors.Add($"date de naissance illisible « {birthText} » (attendu : 31/12/2010)");
            }

            rows.Add(new ImportedStudent(number, matricule.Length > 0 ? matricule : null, lastName, firstName,
                Cell(Column.ClassName) is { Length: > 0 } cls ? cls : null, birth, errors));
        }

        return rows.Count == 0 ? ([], "Le fichier ne contient que la ligne des titres.") : (rows, null);
    }

    /// <summary>Le séparateur le plus présent dans la ligne d'en-tête (Excel en français enregistre avec « ; »).</summary>
    private static char DetectSeparator(string header) =>
        new[] { ';', ',', '\t' }.OrderByDescending(c => header.Count(ch => ch == c)).First();

    /// <summary>Découpe une ligne CSV : un champ entre guillemets peut contenir le séparateur, "" vaut un guillemet.</summary>
    public static List<string> Split(string line, char separator)
    {
        var cells = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else current.Append(ch);
            }
            else if (ch == '"' && current.ToString().Trim().Length == 0) { current.Clear(); quoted = true; }
            else if (ch == separator) { cells.Add(current.ToString()); current.Clear(); }
            else current.Append(ch);
        }
        cells.Add(current.ToString());
        return cells;
    }

    private static string Clean(string cell) => string.Join(' ', cell.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Titre de colonne comparable : minuscules, sans accents ni ponctuation (« N° Matricule » → « n matricule »).</summary>
    public static string Normalize(string text)
    {
        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var kept = decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsLetterOrDigit(c) ? c : ' ');
        return string.Join(' ', new string(kept.ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
