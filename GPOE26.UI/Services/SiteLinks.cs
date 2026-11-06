namespace GPOE26.UI.Services;

/// <summary>
/// Pages du site web hors de l'espace d'apprentissage (accueil, éditeur de cours).
/// Sur le Web, ce sont des chemins relatifs ; l'application Windows donne l'adresse du site :
/// les liens absolus s'ouvrent alors dans le navigateur.
/// </summary>
public class SiteLinks
{
    public string SiteUrl { get; init; } = "";

    public string Home => string.IsNullOrEmpty(SiteUrl) ? "/" : SiteUrl;
    public string NewCourse => $"{SiteUrl}/cours/create";
    public string EditCourse(Guid id) => $"{SiteUrl}/cours/edit/{id}";
}
