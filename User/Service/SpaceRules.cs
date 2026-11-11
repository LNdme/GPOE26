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

    /// <summary>Nom de l'espace créé pour un enseignant indépendant.</summary>
    public static string IndependentSpaceName(string username) => $"Espace de {username}";
}
