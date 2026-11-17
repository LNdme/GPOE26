using GPOE26.UI.Models;

namespace GPOE26.UI.Services;

/// <summary>
/// Fuseau horaire d'affichage et de saisie des dates.
/// En Blazor Server, DateTime.ToLocalTime() utiliserait le fuseau du SERVEUR, pas celui de l'élève :
/// toutes les conversions passent donc par ce fuseau.
///
/// Lot B3 : c'est le fuseau de la personne connectée (son contexte d'apprentissage : choisi, ou celui de son pays).
/// Le serveur web sert plusieurs personnes à la fois : leur fuseau est posé pour chaque activité de leur circuit
/// (<see cref="Use"/>, appelé par l'hôte), jamais dans une variable partagée. Sans personne connue, c'est le fuseau
/// de l'hôte (<see cref="Zone"/> : App:TimeZone, sinon <see cref="DefaultZoneId"/> en dernier recours).
/// </summary>
public static class AppTime
{
    public const string DefaultZoneId = "Africa/Douala";

    /// <summary>Fuseau de l'hôte, utilisé quand celui de la personne est inconnu.</summary>
    public static TimeZoneInfo Zone { get; set; } = Find(DefaultZoneId);

    private static readonly AsyncLocal<Func<TimeZoneInfo?>?> PersonZone = new();

    /// <summary>Fuseau en vigueur pour le code en cours : celui de la personne s'il est connu, sinon celui de l'hôte.</summary>
    public static TimeZoneInfo Current => PersonZone.Value?.Invoke() ?? Zone;

    /// <summary>
    /// Pose, pour la suite du flux d'exécution en cours (une activité de circuit), la façon de connaître le fuseau de
    /// la personne, puis la retire. Lue à chaque conversion : un profil restauré pendant l'activité compte aussitôt.
    /// Null ou inconnu : le fuseau de l'hôte s'applique.
    /// </summary>
    public static IDisposable Use(Func<TimeZoneInfo?> zone)
    {
        var previous = PersonZone.Value;
        PersonZone.Value = zone;
        return new Restore(previous);
    }

    public static IDisposable Use(TimeZoneInfo? zone) => Use(() => zone);

    private sealed class Restore(Func<TimeZoneInfo?>? previous) : IDisposable
    {
        public void Dispose() => PersonZone.Value = previous;
    }

    /// <summary>Fuseau d'un profil (contexte d'apprentissage), ou null s'il n'en donne pas de valide.</summary>
    public static TimeZoneInfo? ZoneOf(UserProfile? profile) => TryFind(profile?.LearningContext?.TimeZone);

    public static DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Current);

    /// <summary>Date saisie dans un champ datetime-local (heure du fuseau) → UTC.</summary>
    public static DateTime ToUtc(DateTime local) =>
        local.Kind == DateTimeKind.Utc ? local : TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Current);

    public static DateTime Now => ToLocal(DateTime.UtcNow);

    public static TimeZoneInfo Find(string id) => TryFind(id) ?? TimeZoneInfo.Local;

    public static TimeZoneInfo? TryFind(string? id) =>
        !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id.Trim(), out var zone) ? zone : null;
}
