namespace User.Service;

/// <summary>Règles pures des espaces (années scolaires, matricules), partagées par le code et la reprise des données.</summary>
public static class SpaceRules
{
    /// <summary>Mois de rentrée : une date à partir d'août appartient à l'année scolaire qui commence.</summary>
    public const int SchoolYearStartMonth = 8;

    /// <summary>Année scolaire contenant cette date : du 1er août au 31 juillet (ex : 2026-11-11 → « 2026-2027 »).</summary>
    public static (string Label, DateOnly StartsOn, DateOnly EndsOn) SchoolYearOf(DateOnly date)
    {
        var start = date.Month >= SchoolYearStartMonth ? date.Year : date.Year - 1;
        return ($"{start}-{start + 1}",
            new DateOnly(start, SchoolYearStartMonth, 1),
            new DateOnly(start + 1, SchoolYearStartMonth, 1).AddDays(-1));
    }

    /// <summary>
    /// Matricule généré : année d'entrée + numéro d'ordre dans l'espace (ex : « 2026-0007 »).
    /// Le numéro n'est jamais tronqué au-delà de 9999 : il reste unique.
    /// </summary>
    public static string Matricule(int entryYear, int number) => $"{entryYear:D4}-{number:D4}";

    /// <summary>
    /// Lit un libellé « 2027-2028 » (deux années qui se suivent) et donne ses dates, du 1er août au 31 juillet.
    /// Faux pour tout autre texte.
    /// </summary>
    public static bool TryParseSchoolYear(string? label, out (string Label, DateOnly StartsOn, DateOnly EndsOn) year)
    {
        year = default;
        var parts = label?.Trim().Split('-');
        if (parts is not { Length: 2 }
            || !int.TryParse(parts[0], out var start) || !int.TryParse(parts[1], out var end)
            || parts[0].Length != 4 || end != start + 1 || start is < 2000 or > 2100)
            return false;
        year = SchoolYearOf(new DateOnly(start, SchoolYearStartMonth, 1));
        return true;
    }

    private static readonly HashSet<string> ShortWords = ["DE", "DU", "DES", "LA", "LE", "LES", "ET", "D", "L", "EN", "AU", "AUX", "THE", "OF", "AND"];

    /// <summary>
    /// Code d'établissement proposé à partir du nom (Q10) : 4 à 10 lettres et chiffres, sans accents.
    /// « Lycée de démonstration » → « LYCDEMO » (3 lettres du premier mot, 4 du suivant, initiales des autres).
    /// L'appelant ajoute un chiffre si le code est déjà pris.
    /// </summary>
    public static string ShortCodeFrom(string name)
    {
        var words = StudentImport.Normalize(name).ToUpperInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => new string(w.Where(char.IsLetterOrDigit).ToArray()))
            .Where(w => w.Length > 0 && !ShortWords.Contains(w))
            .ToList();
        var code = words.Count switch
        {
            0 => "ECOLE",
            1 => words[0][..Math.Min(8, words[0].Length)],
            _ => words[0][..Math.Min(3, words[0].Length)] + words[1][..Math.Min(4, words[1].Length)]
                 + string.Concat(words.Skip(2).Select(w => w[0]))
        };
        code = code[..Math.Min(10, code.Length)];
        return code.Length >= 4 ? code : code.PadRight(4, 'X');
    }

    /// <summary>Un code d'établissement saisi est-il valable ? 4 à 10 lettres majuscules ou chiffres.</summary>
    public static bool IsValidShortCode(string? code) =>
        code is { Length: >= 4 and <= 10 } && code.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9');

    /// <summary>Nom d'utilisateur d'un élève entré par son matricule : « lycdemo.tc-101 » (lettres, chiffres, point, tiret).</summary>
    public static string MatriculeUsername(string shortCode, string matricule)
    {
        var clean = new string(StudentImport.Normalize(matricule).Replace(' ', '-').Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
        var name = $"{shortCode.ToLowerInvariant()}.{(clean.Length > 0 ? clean : "eleve")}";
        return name[..Math.Min(60, name.Length)];
    }

    /// <summary>Nom de l'espace créé pour un enseignant indépendant.</summary>
    public static string IndependentSpaceName(string username) => $"Espace de {username}";
}
