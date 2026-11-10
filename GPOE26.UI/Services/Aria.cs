namespace GPOE26.UI.Services;

/// <summary>
/// États ARIA des composants. Blazor écrit un booléen vrai comme un attribut vide (aria-pressed="")
/// et retire un booléen faux : un lecteur d'écran n'annoncerait jamais l'onglet actif ni le bouton enfoncé.
/// Ces attributs doivent donc recevoir un texte.
/// </summary>
public static class Aria
{
    /// <summary>aria-selected, aria-pressed, aria-expanded, aria-checked : "true" ou "false".</summary>
    public static string State(bool value) => value ? "true" : "false";

    /// <summary>aria-current : le type de l'élément actif ("step", "page"…), attribut absent pour les autres.</summary>
    public static string? Current(bool value, string kind = "step") => value ? kind : null;
}
