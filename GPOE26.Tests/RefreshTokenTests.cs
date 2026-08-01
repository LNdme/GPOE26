using User.Model;

namespace GPOE26.Tests;

/// <summary>
/// Le jeton de rafraîchissement, sans lequel l'application bureau ne pourrait pas
/// fonctionner plusieurs jours hors ligne — l'élève devrait trouver du réseau chaque
/// matin avant de pouvoir réviser, ce qui viderait le hors ligne de son intérêt.
///
/// Ce qui se teste ici tient en deux garanties : le jeton en clair ne se déduit pas de ce
/// qu'on stocke, et un jeton ne sert qu'une fois.
/// </summary>
public class RefreshTokenTests
{
    [Fact]
    public void Un_jeton_ne_se_deduit_pas_de_son_empreinte()
    {
        var (token, hash) = RefreshToken.Create();

        // Une base lue par un tiers ne doit pas lui donner les moyens de se faire passer
        // pour un élève : c'est le même raisonnement que pour un mot de passe, et il vaut
        // d'autant plus qu'un jeton de rafraîchissement dure trente jours.
        Assert.DoesNotContain(token, hash, StringComparison.Ordinal);
        Assert.Equal(64, hash.Length); // SHA-256 en hexadécimal
    }

    [Fact]
    public void Deux_jetons_emis_ne_sont_jamais_identiques()
    {
        var tokens = Enumerable.Range(0, 200).Select(_ => RefreshToken.Create().token).ToList();
        Assert.Equal(tokens.Count, tokens.Distinct().Count());
    }

    [Fact]
    public void L_empreinte_est_reproductible()
    {
        // Sans cela, un jeton légitime ne se retrouverait jamais en base.
        var (token, hash) = RefreshToken.Create();
        Assert.Equal(hash, RefreshToken.Hash(token));
    }

    [Fact]
    public void Un_jeton_transite_sans_caractere_a_echapper()
    {
        // Le jeton passe en JSON, en en-tête et dans un trousseau système : les caractères
        // du Base64 ordinaire (+ / =) s'y échappent différemment selon l'endroit.
        foreach (var _ in Enumerable.Range(0, 50))
        {
            var (token, _) = RefreshToken.Create();
            Assert.DoesNotContain('+', token);
            Assert.DoesNotContain('/', token);
            Assert.DoesNotContain('=', token);
        }
    }

    // ── Utilisabilité ─────────────────────────────────────────────────────────

    private static RefreshToken Fresh(DateTime now) =>
        new() { CreatedAt = now, ExpiresAt = now.Add(RefreshToken.Lifetime) };

    [Fact]
    public void Un_jeton_neuf_est_utilisable()
    {
        var now = DateTime.UtcNow;
        Assert.True(Fresh(now).IsUsable(now));
    }

    /// <summary>
    /// Un jeton ne sert qu'une fois : c'est ce qui permet de détecter qu'une copie
    /// circule. S'il restait utilisable, deux porteurs coexisteraient sans qu'on
    /// puisse les départager.
    /// </summary>
    [Fact]
    public void Un_jeton_deja_utilise_ne_ressert_pas()
    {
        var now = DateTime.UtcNow;
        var token = Fresh(now);
        token.UsedAt = now;

        Assert.False(token.IsUsable(now));
    }

    [Fact]
    public void Un_jeton_revoque_ne_sert_plus()
    {
        var now = DateTime.UtcNow;
        var token = Fresh(now);
        token.RevokedAt = now;

        Assert.False(token.IsUsable(now));
    }

    [Fact]
    public void Un_jeton_expire_ne_sert_plus()
    {
        var now = DateTime.UtcNow;
        var token = Fresh(now.AddDays(-31));

        Assert.False(token.IsUsable(now));
    }

    /// <summary>
    /// Trente jours : assez pour qu'un élève parti en vacances retrouve son compte au
    /// retour, assez court pour qu'un appareil perdu cesse d'être utile.
    /// </summary>
    [Fact]
    public void Un_appareil_hors_ligne_une_semaine_retrouve_son_compte()
    {
        var now = DateTime.UtcNow;
        var token = Fresh(now.AddDays(-7));

        Assert.True(token.IsUsable(now));
        Assert.Equal(TimeSpan.FromDays(30), RefreshToken.Lifetime);
    }
}
