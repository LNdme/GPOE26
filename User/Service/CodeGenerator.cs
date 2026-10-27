using System.Security.Cryptography;

namespace User.Service;

/// <summary>
/// Génère les codes lisibles partagés entre utilisateurs :
///   - code d'invitation de classe :  "K7P-Q2M"
///   - code de rattachement élève :   "AQ4-7KXM"
///   - clé d'abonnement :             "FAM-7Q2K-9XRM"
///
/// Alphabet sans caractères ambigus (pas de 0/O, 1/I/L) : les codes sont souvent
/// dictés au téléphone ou recopiés depuis un SMS.
/// </summary>
public static class CodeGenerator
{
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public static string InviteCode() => $"{Random(3)}-{Random(3)}";

    public static string LinkCode() => $"{Random(3)}-{Random(4)}";

    /// <summary>8 caractères aléatoires (31⁸ ≈ 8,5 × 10¹¹ combinaisons) : une clé a une valeur marchande.</summary>
    public static string SubscriptionKey(string prefix) => $"{prefix}-{Random(4)}-{Random(4)}";

    /// <summary>Mise en forme canonique d'un code saisi par l'utilisateur (espaces, minuscules).</summary>
    public static string Normalize(string code) => code.Trim().ToUpperInvariant().Replace(' ', '-');

    private static string Random(int length) =>
        string.Create(length, 0, (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        });
}
