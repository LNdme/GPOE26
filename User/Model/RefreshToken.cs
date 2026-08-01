using System.Security.Cryptography;
using System.Text;

namespace User.Model;

/// <summary>
/// Un jeton de rafraîchissement : de quoi obtenir un nouveau jeton d'accès sans redemander
/// le mot de passe.
///
/// Le jeton d'accès dure une heure, ce qui convient au Web. L'application bureau doit
/// fonctionner plusieurs jours hors ligne : sans jeton de rafraîchissement, l'élève
/// devrait se reconnecter chaque matin — donc trouver du réseau avant de pouvoir réviser,
/// ce qui vide le hors ligne de son intérêt.
///
/// ⚠️ On stocke l'EMPREINTE du jeton, jamais le jeton. Une base lue par un tiers ne doit
/// pas lui donner les moyens de se faire passer pour un élève. C'est le même raisonnement
/// que pour un mot de passe, et il vaut ici parce qu'un jeton de rafraîchissement dure
/// bien plus longtemps qu'un jeton d'accès.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;

    /// <summary>Empreinte SHA-256 du jeton, en hexadécimal.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }

    /// <summary>Date d'utilisation. Un jeton ne sert qu'une fois — voir la rotation.</summary>
    public DateTime? UsedAt { get; set; }

    /// <summary>
    /// Jeton émis en échange de celui-ci.
    ///
    /// Sert à détecter un rejeu : si un jeton déjà utilisé se représente, c'est qu'une
    /// copie circule. On révoque alors toute la chaîne plutôt que de laisser deux
    /// porteurs coexister.
    /// </summary>
    public Guid? ReplacedBy { get; set; }

    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// Durée de vie. Trente jours : assez pour qu'un élève parti en vacances retrouve son
    /// compte au retour, assez court pour qu'un appareil perdu cesse d'être utile.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    public bool IsUsable(DateTime now) =>
        UsedAt is null && RevokedAt is null && now < ExpiresAt;

    /// <summary>
    /// Produit un jeton et son empreinte. Le jeton en clair n'existe qu'ici et dans la
    /// réponse HTTP : il n'est ni journalisé, ni conservé.
    /// </summary>
    public static (string token, string hash) Create()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        return (token, Hash(token));
    }

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
