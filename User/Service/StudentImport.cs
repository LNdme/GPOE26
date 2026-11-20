using System.Globalization;
using System.Text;

namespace User.Service;

/// <summary>
/// Ligne lue dans le fichier des élèves. Line : numéro de la ligne dans le fichier (ou la feuille, ou le tableau).
/// Extra : les colonnes que la plateforme ne connaît pas, gardées telles quelles avec leur titre (téléphone, parent…).
/// </summary>
public record ImportedStudent(int Line, string? Matricule, string LastName, string FirstName, string? ClassName, DateOnly? BirthDate,
    string? Email, Dictionary<string, string> Extra, List<string> Errors);

/// <summary>
/// Lecture de la liste des élèves (docs/PLAN-ETABLISSEMENT.md lot 3, docs/PLAN-ETABLISSEMENT-2.md lot 9).
/// Le fichier arrive en tableau de cellules : CSV (séparateur ; , ou tabulation, guillemets), feuille Excel ou tableau
/// Word (cf. ImportFileReader). La ligne des titres est cherchée parmi les premières lignes (un titre « Liste de la
/// Tle C » au-dessus est ignoré). Colonnes reconnues, sans tenir compte des accents ni des majuscules : matricule, nom,
/// prénom, classe (ou parcours, groupe, promotion), date de naissance, e-mail — ou « nom et prénom » en une colonne.
/// Les autres colonnes sont gardées dans Extra. Seul le nom est obligatoire : un matricule vide est généré, une classe vide
/// laisse l'élève sans classe. Les contrôles qui demandent la base (matricule déjà inscrit, classe inconnue) sont faits par l'appelant.
/// </summary>
public static class StudentImport
{
    public const int MaxRows = 2000;

    /// <summary>Lignes parcourues pour trouver celle des titres.</summary>
    private const int HeaderSearchRows = 10;

    private enum Column { Matricule, LastName, FirstName, FullName, ClassName, BirthDate, Email }

    private static readonly Dictionary<string, Column> Headers = new()
    {
        ["matricule"] = Column.Matricule, ["n matricule"] = Column.Matricule, ["no matricule"] = Column.Matricule,
        ["numero matricule"] = Column.Matricule, ["numero"] = Column.Matricule, ["id"] = Column.Matricule, ["identifiant"] = Column.Matricule,
        ["nom"] = Column.LastName, ["nom de famille"] = Column.LastName, ["last name"] = Column.LastName, ["surname"] = Column.LastName,
        ["prenom"] = Column.FirstName, ["prenoms"] = Column.FirstName, ["prenom s"] = Column.FirstName, ["first name"] = Column.FirstName,
        ["nom s"] = Column.LastName, ["noms"] = Column.LastName,
        ["nom et prenom"] = Column.FullName, ["nom et prenoms"] = Column.FullName, ["noms et prenoms"] = Column.FullName,
        ["nom et prenom s"] = Column.FullName, ["nom s et prenom s"] = Column.FullName,
        ["nom complet"] = Column.FullName, ["eleve"] = Column.FullName, ["name"] = Column.FullName,
        ["etudiant"] = Column.FullName, ["employe"] = Column.FullName, ["apprenant"] = Column.FullName, ["participant"] = Column.FullName,
        ["classe"] = Column.ClassName, ["class"] = Column.ClassName,
        // Entreprises, centres de formation : le parcours tient lieu de classe (groupe de l'institution)
        ["parcours"] = Column.ClassName, ["groupe"] = Column.ClassName, ["promotion"] = Column.ClassName, ["cohorte"] = Column.ClassName,
        ["date de naissance"] = Column.BirthDate, ["naissance"] = Column.BirthDate, ["ne le"] = Column.BirthDate,
        ["nee le"] = Column.BirthDate, ["ne e le"] = Column.BirthDate, ["date of birth"] = Column.BirthDate, ["birth date"] = Column.BirthDate,
        ["e mail"] = Column.Email, ["email"] = Column.Email, ["mail"] = Column.Email, ["courriel"] = Column.Email,
        ["adresse e mail"] = Column.Email, ["adresse email"] = Column.Email, ["adresse mail"] = Column.Email,
    };

    private static readonly string[] DateFormats = ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "d-M-yyyy", "dd.MM.yyyy"];

    /// <summary>Lit un CSV collé ou envoyé.</summary>
    public static (List<ImportedStudent> Rows, string? Error) Parse(string? csv) => ParseTable(SplitCsv(csv));

    /// <summary>
    /// Lit un tableau de cellules (lignes de CSV, de feuille Excel ou de tableau Word ; une ligne vide reste une liste vide).
    /// Error : problème qui empêche toute lecture (rien à lire, pas de colonne « nom »…).
    /// </summary>
    public static (List<ImportedStudent> Rows, string? Error) ParseTable(IReadOnlyList<IReadOnlyList<string>> table)
    {
        var lines = table.Select((cells, index) => (Cells: cells, Number: index + 1))
            .Where(l => l.Cells.Any(c => !string.IsNullOrWhiteSpace(c)))
            .ToList();
        if (lines.Count == 0) return ([], "Le fichier est vide.");

        // Ligne des titres : la première qui nomme une colonne « nom » (des lignes de titre peuvent la précéder)
        var headerIndex = lines.Take(HeaderSearchRows).ToList().FindIndex(l =>
            l.Cells.Select(Normalize).Any(h => Headers.TryGetValue(h, out var c) && c is Column.LastName or Column.FullName));
        if (headerIndex < 0)
            return ([], "Colonne « nom » introuvable : une ligne doit donner les titres des colonnes (matricule, nom, prénom, classe…).");

        var header = lines[headerIndex].Cells;
        var columns = new Dictionary<Column, int>();
        var extra = new List<(int Index, string Title)>();
        for (var i = 0; i < header.Count; i++)
        {
            var title = Clean(header[i]);
            if (title.Length == 0) continue;
            if (Headers.TryGetValue(Normalize(title), out var column))
            {
                if (!columns.TryAdd(column, i)) extra.Add((i, title));
            }
            else
                extra.Add((i, title));
        }

        var data = lines.Skip(headerIndex + 1).ToList();
        if (data.Count == 0) return ([], "Le fichier ne contient que la ligne des titres.");
        if (data.Count > MaxRows)
            return ([], $"Le fichier compte plus de {MaxRows} élèves : découpez-le, par exemple par niveau.");

        var rows = new List<ImportedStudent>();
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (cells, number) in data)
        {
            string Cell(int i) => i < cells.Count ? Clean(cells[i]) : "";
            string Get(Column c) => columns.TryGetValue(c, out var i) ? Cell(i) : "";

            var errors = new List<string>();
            var lastName = Get(Column.LastName);
            var firstName = Get(Column.FirstName);
            if (lastName.Length == 0 && Get(Column.FullName) is { Length: > 0 } full)
                lastName = full;
            if (lastName.Length == 0) errors.Add("nom manquant");
            if (lastName.Length > 100 || firstName.Length > 100) errors.Add("nom ou prénom trop long (100 caractères au plus)");

            var matricule = Get(Column.Matricule);
            if (matricule.Length > 40) errors.Add("matricule trop long (40 caractères au plus)");
            else if (matricule.Length > 0 && !seen.TryAdd(matricule, number))
                errors.Add($"matricule déjà donné ligne {seen[matricule]}");

            DateOnly? birth = null;
            var birthText = Get(Column.BirthDate);
            if (birthText.Length > 0)
            {
                if (DateOnly.TryParseExact(birthText, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    birth = date;
                else
                    errors.Add($"date de naissance illisible « {birthText} » (attendu : 31/12/2010)");
            }

            var email = Get(Column.Email);
            if (email.Length > 0 && !LooksLikeEmail(email)) errors.Add($"e-mail illisible « {email} »");

            var others = new Dictionary<string, string>();
            foreach (var (index, title) in extra)
                if (Cell(index) is { Length: > 0 } value)
                    others[others.ContainsKey(title) ? $"{title} ({index + 1})" : title] = value.Length > 500 ? value[..500] : value;

            rows.Add(new ImportedStudent(number, matricule.Length > 0 ? matricule : null, lastName, firstName,
                Get(Column.ClassName) is { Length: > 0 } cls ? cls : null, birth,
                email.Length > 0 ? email.ToLowerInvariant() : null, others, errors));
        }
        return (rows, null);
    }

    /// <summary>Remet un tableau en CSV (séparateur « ; », guillemets si besoin) : l'aperçu d'un fichier Excel ou Word est ensuite enregistré comme un CSV.</summary>
    public static string ToCsv(IReadOnlyList<IReadOnlyList<string>> table) =>
        string.Join("\n", table.Select(cells => string.Join(';', cells.Select(Quote))));

    private static string Quote(string cell) =>
        cell.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{cell.Replace("\"", "\"\"")}\"" : cell;

    private static bool LooksLikeEmail(string text) =>
        text.Length <= 256 && text.IndexOf('@') is > 0 and var at && text.IndexOf('.', at) > at + 1 && !text.Contains(' ');

    /// <summary>CSV → tableau. Séparateur : le plus présent sur les premières lignes (Excel en français enregistre avec « ; »).</summary>
    public static List<IReadOnlyList<string>> SplitCsv(string? csv)
    {
        var lines = (csv ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
            .Select((text, index) => index == 0 ? text.TrimStart('﻿') : text)
            .ToList();
        var separator = new[] { ';', ',', '\t' }
            .OrderByDescending(c => lines.Take(HeaderSearchRows).Max(l => (int?)l.Count(ch => ch == c)) ?? 0)
            .First();
        return lines.Select(l => (IReadOnlyList<string>)(string.IsNullOrWhiteSpace(l) ? [] : Split(l, separator))).ToList();
    }

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
