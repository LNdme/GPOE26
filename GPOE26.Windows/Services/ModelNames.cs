namespace GPOE26.Desktop.Services;

/// <summary>Libellés lisibles des identifiants de modèles (« nvidia/nemotron-3.5-super-vl:free » → « nemotron-3.5-super-vl »).</summary>
public static class ModelNames
{
    public const string Automatic = "Automatique";

    public static string Display(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return Automatic;
        if (id.Equals("openrouter/free", StringComparison.OrdinalIgnoreCase)) return "Routeur gratuit OpenRouter";
        var name = id.Contains('/') ? id[(id.LastIndexOf('/') + 1)..] : id;
        return IsFree(id) ? name[..name.LastIndexOf(':')] : name;
    }

    public static string Provider(string id) => id.Contains('/') ? id[..id.IndexOf('/')] : "";

    public static bool IsFree(string id) => id.EndsWith(":free", StringComparison.OrdinalIgnoreCase);
}
