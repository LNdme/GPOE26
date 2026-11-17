using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace User.Model;

// ══════════════════════════════════════════════════════════════════════════════
// Contexte d'apprentissage (lot B1 du plan de l'agent noyau) : le système éducatif est une donnée,
// pas du code. Le Cameroun devient un préréglage parmi d'autres.
//
// Les anciens champs (SubSystem, ExamTarget, Level, Stage) restent en place et continuent d'être remplis :
// tant qu'une valeur du contexte n'a pas été choisie, elle est déduite d'eux à la lecture, sans rien écrire.
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>Tranche de niveau normalisée, commune à tous les pays (le niveau libre reste dans AppUser.Level).</summary>
public enum LevelBand
{
    Primary,
    LowerSecondary,
    UpperSecondary,
    Higher,
    /// <summary>Adulte qui apprend hors d'un cursus (reconversion, curiosité, formation continue).</summary>
    Adult
}

/// <summary>Pourquoi la personne apprend.</summary>
public enum LearningGoal
{
    Exam,
    Skill,
    Curiosity
}

/// <summary>Échelle de notes dans laquelle les résultats sont présentés à la personne.</summary>
public enum GradeScale
{
    Over20,
    Over10,
    Over100,
    Letters
}

/// <summary>Préréglage d'un pays : valeurs proposées par défaut, toutes modifiables.</summary>
public record CountryPreset(
    string Code, string Name, string Language, GradeScale GradeScale, string TimeZone,
    IReadOnlyList<string> Systems, IReadOnlyList<string> Exams);

/// <summary>Contexte effectif, tel que les agents et l'interface le lisent.</summary>
/// <param name="Country">Code ISO 3166-1 à deux lettres, « ZZ » pour un autre pays, null si inconnu.</param>
/// <param name="Inferred">
/// Champs que la personne n'a pas choisis : déduits de ses autres informations (pays, fuseau…) ou valeur par défaut (échelle).
/// </param>
public record LearningContextDto(
    string? Country,
    string? CountryName,
    string? EducationSystem,
    string? Level,
    LevelBand? LevelBand,
    LearningGoal? Goal,
    string? ExamTarget,
    DateOnly? ExamDate,
    GradeScale GradeScale,
    string? TimeZone,
    IReadOnlyList<string> Inferred);

public static class LearningContextRules
{
    /// <summary>Pays absent des préréglages, choisi explicitement : aucune valeur n'est plus déduite de l'ancien profil.</summary>
    public const string OtherCountry = "ZZ";

    /// <summary>Échelle par défaut quand rien ne permet de choisir : celle qu'utilise déjà la plateforme.</summary>
    public const GradeScale DefaultGradeScale = GradeScale.Over20;

    public static readonly IReadOnlyList<CountryPreset> Presets =
    [
        new("CM", "Cameroun", "fr", GradeScale.Over20, "Africa/Douala",
            ["Sous-système francophone", "Sous-système anglophone"],
            ["BEPC", "CAP", "Probatoire", "Baccalauréat", "GCE O Level", "GCE A Level"]),
        new("FR", "France", "fr", GradeScale.Over20, "Europe/Paris", [], ["Diplôme national du brevet", "Baccalauréat", "BTS"]),
        new("MX", "Mexique", "es", GradeScale.Over10, "America/Mexico_City", [], ["EXANI-I", "EXANI-II"]),
        new("CU", "Cuba", "es", GradeScale.Over100, "America/Havana", [], ["Pruebas de ingreso a la educación superior"]),
        new("SN", "Sénégal", "fr", GradeScale.Over20, "Africa/Dakar", [], ["BFEM", "Baccalauréat"]),
        new("CI", "Côte d'Ivoire", "fr", GradeScale.Over20, "Africa/Abidjan", [], ["BEPC", "Baccalauréat"]),
        new("GA", "Gabon", "fr", GradeScale.Over20, "Africa/Libreville", [], ["BEPC", "Baccalauréat"]),
    ];

    public static CountryPreset? Preset(string? code) =>
        code is null ? null : Presets.FirstOrDefault(p => p.Code == code);

    // Examens propres au Cameroun : « Baccalauréat » seul ne dit pas le pays
    private static readonly string[] CameroonExams = ["BEPC", "CAP", "Probatoire", "GCE"];

    /// <summary>Contexte effectif : ce que la personne a choisi, sinon ce que disent ses anciennes informations.</summary>
    public static LearningContextDto Resolve(AppUser user)
    {
        var inferred = new List<string>();
        T Infer<T>(T chosen, Func<T> derive, string field)
        {
            if (chosen is not null) return chosen;
            var value = derive();
            if (value is not null) inferred.Add(field);
            return value;
        }

        var country = Infer(user.Country, () => LegacyCountry(user), "country");
        var preset = Preset(country);
        // Le sous-système est camerounais : il ne vaut plus pour une personne qui a choisi un autre pays
        var system = Infer(user.EducationSystem, () => country != "CM" ? null : user.SubSystem switch
        {
            SchoolSubSystem.Francophone => "Sous-système francophone",
            SchoolSubSystem.Anglophone => "Sous-système anglophone",
            _ => null
        }, "educationSystem");
        var band = Infer(user.LevelBand, () => user.Role == UserRole.Student ? BandOf(user.Level, user.Stage) : null, "levelBand");
        var goal = Infer(user.LearningGoal, () => user.ExamTarget is { Length: > 0 } ? LearningGoal.Exam : (LearningGoal?)null, "goal");
        var timeZone = Infer(user.TimeZone, () => preset?.TimeZone, "timeZone");

        // L'échelle a toujours une valeur : celle du pays, sinon celle qu'utilise déjà la plateforme
        var scale = Infer(user.GradeScale, () => (GradeScale?)(preset?.GradeScale ?? DefaultGradeScale), "gradeScale")!.Value;

        return new LearningContextDto(country, CountryName(country), system, user.Level, band, goal, user.ExamTarget, user.ExamDate, scale, timeZone, inferred);
    }

    /// <summary>Nom du pays pour les agents : celui du préréglage, sinon le nom local connu du système ; rien pour « ZZ ».</summary>
    public static string? CountryName(string? code)
    {
        if (code is null || code == OtherCountry) return null;
        if (Preset(code) is { } preset) return preset.Name;
        try { return new RegionInfo(code).NativeName; }
        catch (ArgumentException) { return null; }
    }

    /// <summary>Les comptes créés avant B1 n'ont pas de pays : le sous-système ou un examen camerounais le donne.</summary>
    private static string? LegacyCountry(AppUser user)
    {
        if (user.SubSystem is not null) return "CM";
        var exam = user.ExamTarget ?? "";
        return CameroonExams.Any(e => exam.Contains(e, StringComparison.OrdinalIgnoreCase)) ? "CM" : null;
    }

    /// <summary>
    /// Tranche déduite du niveau libre (classes du Cameroun, de France et du Mexique) ; à défaut, de l'étape
    /// (supérieur). Un niveau qu'on ne reconnaît pas ne donne rien plutôt qu'une tranche devinée.
    /// </summary>
    public static LevelBand? BandOf(string? level, EducationStage? stage)
    {
        var l = Normalize(level);
        if (l.Length > 0)
        {
            if (Has(l, "licence", "master", "doctorat", "bts", "hnd", "dut", "prepa", "universi", "l1", "l2", "l3", "m1", "m2",
                    "ingenieur", "year 1", "year 2", "year 3", "bachelor", "licenciatura"))
                return LevelBand.Higher;
            if (Has(l, "seconde", "2nde", "premiere", "1ere", "1re", "terminale", "tle", "lower sixth", "upper sixth",
                    "preparatoria", "bachillerato", "lycee"))
                return LevelBand.UpperSecondary;
            if (Has(l, "6e", "6eme", "5e", "5eme", "4e", "4eme", "3e", "3eme", "form 1", "form 2", "form 3", "form 4", "form 5",
                    "secundaria", "college"))
                return LevelBand.LowerSecondary;
            if (Has(l, "cp", "ce1", "ce2", "cm1", "cm2", "sil", "class 1", "class 2", "class 3", "class 4", "class 5", "class 6",
                    "primaire", "primaria"))
                return LevelBand.Primary;
        }
        return stage == EducationStage.Higher ? LevelBand.Higher : null;
    }

    // Mot entier (« 3e » ne doit pas reconnaître « 13e arrondissement », ni « l1 » dans « bl1 »)
    private static bool Has(string text, params string[] words) =>
        words.Any(w => Regex.IsMatch(text, $@"(^|[^a-z0-9]){Regex.Escape(w)}($|[^a-z0-9])"));

    private static string Normalize(string? value) =>
        new string((value ?? "").Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());

    /// <summary>Code pays accepté : deux lettres majuscules (ISO 3166-1), dont « ZZ » pour un autre pays.</summary>
    public static bool IsCountryCode(string code) => code.Length == 2 && code.All(c => c is >= 'A' and <= 'Z');

    /// <summary>Fuseau IANA connu du système (« Africa/Douala », « America/Mexico_City »…).</summary>
    public static bool IsTimeZone(string id) =>
        id.Length is > 0 and <= 64 && id.Contains('/') && TimeZoneInfo.TryFindSystemTimeZoneById(id, out _);
}
