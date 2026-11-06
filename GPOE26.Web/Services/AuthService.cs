using GPOE26.UI.Services;
using GPOE26.Web.Models;

namespace GPOE26.Web.Services;

/// <summary>
/// Façade de compatibilité pour les pages historiques du site (menu, cours, apprentissage) :
/// l'état de connexion est désormais porté par SessionState (bibliothèque GPOE26.UI),
/// persisté dans le navigateur et partagé avec l'espace d'apprentissage.
/// </summary>
public class AuthService(SessionState session)
{
    public event Action? OnAuthChanged
    {
        add => session.Changed += value;
        remove => session.Changed -= value;
    }

    public bool IsAuthenticated => session.IsAuthenticated;
    public string? Token => session.Token;

    public UserProfileDto? Profile => session.Profile is { } p
        ? new UserProfileDto(p.Id, p.Email, p.Username, p.Role.ToString(), p.Level, p.Specialite, p.Filiere, p.Language, p.CreatedAt)
        : null;

    public Task LogoutAsync() => session.SignOutAsync();
}
