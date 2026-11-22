using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using GPOE26.UI.Models;
using GPOE26.UI.Services;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;

namespace GPOE26.Desktop.Services;

/// <summary>
/// Session persistante de l'application : le jeton est chiffré pour l'utilisateur Windows (DPAPI, « LOCAL=user »)
/// avant d'aller dans la base locale. Un autre compte Windows du poste ne peut pas le relire.
///
/// À côté du jeton, le dernier profil connecté est gardé (chiffré lui aussi) : quand le jeton a expiré
/// et que le serveur est injoignable, l'élève peut encore rouvrir sa copie locale en lecture seule.
/// </summary>
public sealed class DesktopSessionStore(LocalStore store) : ISessionStore
{
    private const string SessionKey = "session";
    private const string OfflineProfileKey = "offline-profile";

    public async Task<StoredSession?> LoadAsync() => await ReadAsync<StoredSession>(SessionKey);

    public async Task SaveAsync(StoredSession session)
    {
        await WriteAsync(SessionKey, session);
        await WriteAsync(OfflineProfileKey, session.Profile);
    }

    /// <summary>Jeton expiré ou refusé : on l'oublie, mais le profil hors connexion reste.</summary>
    public Task ClearAsync() => store.SetSettingAsync(SessionKey, null);

    /// <summary>Profil du dernier élève connecté sur ce poste, pour la lecture hors connexion.</summary>
    public Task<UserProfile?> OfflineProfileAsync() => ReadAsync<UserProfile>(OfflineProfileKey);

    /// <summary>Déconnexion voulue par l'utilisateur : plus de lecture hors connexion sans se reconnecter.</summary>
    public async Task ForgetAsync()
    {
        await store.SetSettingAsync(SessionKey, null);
        await store.SetSettingAsync(OfflineProfileKey, null);
    }

    private async Task<T?> ReadAsync<T>(string key) where T : class
    {
        if (await store.GetSettingAsync(key) is not { Length: > 0 } protectedText) return null;
        try
        {
            var provider = new DataProtectionProvider();
            var clear = await provider.UnprotectAsync(Convert.FromBase64String(protectedText).AsBuffer());
            return JsonSerializer.Deserialize<T>(CryptographicBuffer.ConvertBinaryToString(BinaryStringEncoding.Utf8, clear), LocalStore.Json);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or System.Runtime.InteropServices.COMException)
        {
            await store.SetSettingAsync(key, null);   // illisible (autre compte Windows, données abîmées) : on repart de zéro
            return null;
        }
    }

    private async Task WriteAsync<T>(string key, T value)
    {
        var provider = new DataProtectionProvider("LOCAL=user");
        var clear = CryptographicBuffer.ConvertStringToBinary(JsonSerializer.Serialize(value, LocalStore.Json), BinaryStringEncoding.Utf8);
        var sealedBuffer = await provider.ProtectAsync(clear);
        await store.SetSettingAsync(key, Convert.ToBase64String(sealedBuffer.ToArray()));
    }
}
