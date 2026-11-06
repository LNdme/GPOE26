using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GPOE26.UI.Services;

namespace GPOE26.Desktop.Services;

/// <summary>
/// Session conservée entre deux lancements, chiffrée pour le compte Windows de l'utilisateur (DPAPI) :
/// un autre compte de l'ordinateur ne peut pas la lire.
/// </summary>
public class ProtectedSessionStore : ISessionStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("GPOE26.Session");
    private readonly string _path = Path.Combine(FileSystem.AppDataDirectory, "session.bin");

    public async Task<StoredSession?> LoadAsync()
    {
        if (!File.Exists(_path)) return null;
        var bytes = ProtectedData.Unprotect(await File.ReadAllBytesAsync(_path), Entropy, DataProtectionScope.CurrentUser);
        return JsonSerializer.Deserialize<StoredSession>(bytes);
    }

    public async Task SaveAsync(StoredSession session)
    {
        Directory.CreateDirectory(FileSystem.AppDataDirectory);
        var bytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(session), Entropy, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(_path, bytes);
    }

    public Task ClearAsync()
    {
        if (File.Exists(_path)) File.Delete(_path);
        return Task.CompletedTask;
    }
}
