using Microsoft.UI.Xaml;

namespace GPOE26.Desktop.Services;

/// <summary>
/// Taille du texte. L'application reste petite et dense (police de terminal, 12 px) ;
/// le réglage n'ajoute qu'un ou deux points, pour qui a du mal à lire petit.
/// Le cours, au milieu, a sa propre taille, plus grande, qui suit le même réglage.
/// </summary>
public static class Typography
{
    public const string SettingKey = "text-size";

    public static readonly (string Value, string Label)[] Choices =
    [
        ("normal", "Normale"),
        ("plus", "Un peu plus grande"),
        ("grand", "Plus grande"),
    ];

    /// <summary>Points ajoutés à toutes les tailles : 0, 1 ou 2.</summary>
    public static int Step(string? value) => value switch { "plus" => 1, "grand" => 2, _ => 0 };

    /// <summary>Taille de base du texte de l'application (12 px au réglage « Normale »).</summary>
    public static double Body(int step) => 12 + step;

    /// <summary>
    /// Met à jour les tailles des contrôles WinUI (boutons, champs, listes, menus, info-bulles).
    /// Appelé avant la création de la fenêtre ; ensuite, un changement de thème force la relecture des ressources.
    /// </summary>
    public static void Apply(ResourceDictionary resources, int step)
    {
        resources["ControlContentThemeFontSize"] = 12.0 + step;
        resources["ContentControlFontSize"] = 12.0 + step;
        resources["BodyTextBlockFontSize"] = 12.0 + step;
        resources["CaptionTextBlockFontSize"] = 11.0 + step;
        resources["SubtitleTextBlockFontSize"] = 16.0 + step;
        resources["TitleTextBlockFontSize"] = 20.0 + step;
        resources["ToolTipContentThemeFontSize"] = 11.0 + step;
        resources["TextControlThemeMinHeight"] = 28.0 + step * 2;
        resources["ListViewItemMinHeight"] = 30.0 + step * 2;
    }
}
