namespace GPOE26.UI.Services;

/// <summary>
/// Fuseau horaire d'affichage et de saisie des dates.
/// En Blazor Server, DateTime.ToLocalTime() utiliserait le fuseau du SERVEUR, pas celui de l'élève :
/// toutes les conversions passent donc par ce fuseau (Cameroun par défaut, configurable par l'hôte).
/// </summary>
public static class AppTime
{
    public static TimeZoneInfo Zone { get; set; } = Find("Africa/Douala");

    public static DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    /// <summary>Date saisie dans un champ datetime-local (heure du fuseau) → UTC.</summary>
    public static DateTime ToUtc(DateTime local) =>
        local.Kind == DateTimeKind.Utc ? local : TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Zone);

    public static DateTime Now => ToLocal(DateTime.UtcNow);

    public static TimeZoneInfo Find(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Local; }
    }
}
