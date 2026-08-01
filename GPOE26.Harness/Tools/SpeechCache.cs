using System.Security.Cryptography;
using System.Text;
using GPOE26.Ai;
using Microsoft.Extensions.Caching.Memory;

namespace GPOE26.Harness.Tools;

/// <summary>
/// La synthèse vocale, mise en cache sur l'empreinte du texte.
///
/// Un outil ne peut pas renvoyer d'audio : il renvoie une référence, et l'interface va
/// chercher les octets. Le cache existe parce qu'un élève réécoute volontiers la même
/// explication deux ou trois fois et que le TTS se facture au caractère — c'est le même
/// raisonnement que <c>/chat/voix</c>, repris ici.
/// </summary>
public sealed class SpeechCache(OpenRouterClient client, IMemoryCache cache)
{
    public async Task<string> SynthesizeAsync(string text, CancellationToken ct = default)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];

        if (cache.TryGetValue(CacheKey(key), out byte[]? _)) return key;

        var audio = await client.SynthesizeSpeechAsync(text, ct);
        if (audio.Length == 0) throw new OpenRouterException("Aucun audio n'a été produit.");

        cache.Set(CacheKey(key), audio, new MemoryCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromHours(2),
            Size = audio.Length,
        });

        return key;
    }

    public byte[]? Get(string key) =>
        cache.TryGetValue(CacheKey(key), out byte[]? audio) ? audio : null;

    private static string CacheKey(string key) => "voix:" + key;
}
