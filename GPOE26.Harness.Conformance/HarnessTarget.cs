using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;

namespace GPOE26.Harness.Conformance;

/// <summary>
/// Le harness soumis à la suite.
///
/// Deux modes, un seul jeu de tests :
///
/// · sans configuration, la suite démarre le stub en mémoire — c'est ce qui la rend
///   exécutable partout, y compris en intégration continue, sans rien lancer d'abord ;
/// · avec <c>HARNESS_TARGET</c>, elle interroge cette URL. C'est ainsi qu'on jugera le
///   harness .NET puis le harness Node : un test HTTP se moque de la langue du service
///   qu'il interroge.
///
/// Le même verdict doit tomber dans les deux modes. C'est tout l'intérêt d'avoir un
/// contrat plutôt qu'une implémentation de référence.
/// </summary>
public sealed class HarnessTarget : IDisposable
{
    public const string TargetVariable = "HARNESS_TARGET";

    // Mêmes valeurs que l'AppHost : la suite émet des jetons que le harness doit accepter.
    private const string JwtKey =
        "votre_cle_secrete_tres_longue_et_aleatoire_ici_changez_moi_en_production_cle_256_bits";
    private const string JwtIssuer = "GPOE2026";
    private const string JwtAudience = "GPOE2026Users";

    private readonly WebApplicationFactory<Program>? _factory;
    private readonly Uri? _remote;

    public HarnessTarget()
    {
        var target = Environment.GetEnvironmentVariable(TargetVariable);

        if (!string.IsNullOrWhiteSpace(target))
        {
            _remote = new Uri(target.TrimEnd('/') + "/");
            Description = $"cible distante {_remote}";
        }
        else
        {
            _factory = new WebApplicationFactory<Program>();
            Description = "stub en mémoire";
        }
    }

    /// <summary>Ce qui est jugé, pour l'écrire dans le tableau comparatif.</summary>
    public string Description { get; }

    /// <summary>Un client authentifié en tant qu'élève.</summary>
    public HttpClient AsStudent(Guid studentId) =>
        Authenticated(studentId, "Student", children: null);

    /// <summary>Un client authentifié en tant que parent des enfants indiqués.</summary>
    public HttpClient AsParent(Guid parentId, params Guid[] children) =>
        Authenticated(parentId, "Parent", string.Join(',', children));

    /// <summary>Un client sans jeton, pour vérifier que les endpoints sont bien fermés.</summary>
    public HttpClient Anonymous() => CreateClient();

    private HttpClient Authenticated(Guid userId, string role, string? children)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Token(userId, role, children));

        return client;
    }

    private HttpClient CreateClient() =>
        _factory is not null
            ? _factory.CreateClient()
            : new HttpClient { BaseAddress = _remote, Timeout = TimeSpan.FromMinutes(2) };

    /// <summary>
    /// Émet un jeton de la même forme que celui du service User : claims `sub`, `role`
    /// et, pour un parent, `children`. Les tests d'autorisation ne valent que si les
    /// jetons sont ceux que le harness verra en production.
    /// </summary>
    private static string Token(Guid userId, string role, string? children)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new("role", role),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        if (!string.IsNullOrWhiteSpace(children))
            claims.Add(new Claim("children", children));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: JwtIssuer,
            audience: JwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public void Dispose() => _factory?.Dispose();
}

/// <summary>
/// Une seule collection pour toute la suite : les tests de sabotage manipulent une
/// variable d'environnement, qui est globale au processus. Les laisser tourner en
/// parallèle des tests de conformité produirait des échecs aléatoires — le pire genre
/// de test, celui qu'on finit par ignorer.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HarnessCollection : ICollectionFixture<HarnessTarget>
{
    public const string Name = "harness";
}
