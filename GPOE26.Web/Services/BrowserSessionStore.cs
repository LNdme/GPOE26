using GPOE26.UI.Services;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace GPOE26.Web.Services;

/// <summary>
/// Session conservée dans le localStorage du navigateur, chiffrée et signée par la protection
/// des données d'ASP.NET Core : le jeton n'est ni lisible ni modifiable côté navigateur.
/// Nécessite l'interopérabilité JS, donc un rendu interactif sans prérendu (cf. App.razor).
/// </summary>
public class BrowserSessionStore(ProtectedLocalStorage storage) : ISessionStore
{
    private const string Key = "gpoe26.session";

    public async Task<StoredSession?> LoadAsync()
    {
        var result = await storage.GetAsync<StoredSession>(Key);
        return result.Success ? result.Value : null;
    }

    public async Task SaveAsync(StoredSession session) => await storage.SetAsync(Key, session);

    public async Task ClearAsync() => await storage.DeleteAsync(Key);
}
