using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace GPOE26.ServiceDefaults;

/// <summary>
/// Clé qui signe les jetons de connexion (Jwt:Key), lue au démarrage de chaque service.
/// Elle vient d'un secret (variable d'environnement Jwt__Key, user-secrets) : jamais d'un fichier du dépôt.
/// </summary>
public static class JwtSigningKey
{
    /// <summary>
    /// Clé d'exemple des fichiers appsettings.Development.json. Elle est publique (dans le dépôt) :
    /// tolérée en développement, refusée partout ailleurs, sinon n'importe qui signerait un jeton administrateur.
    /// </summary>
    public const string DevelopmentKey = "votre_cle_secrete_tres_longue_et_aleatoire_ici_changez_moi_en_production_cle_256_bits";

    private const int MinBytes = 32;   // HS256 : 256 bits

    public static string Require(IConfiguration config, IHostEnvironment environment) =>
        Validate(config["Jwt:Key"], environment.IsDevelopment());

    /// <summary>Renvoie la clé si elle est utilisable, sinon lève une erreur qui dit quoi faire.</summary>
    public static string Validate(string? key, bool isDevelopment)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException(
                "La clé de signature des jetons est absente. Définissez la variable d'environnement Jwt__Key (32 caractères au moins).");

        if (Encoding.UTF8.GetByteCount(key) < MinBytes)
            throw new InvalidOperationException("Jwt:Key est trop courte : 32 caractères au moins.");

        if (!isDevelopment && key == DevelopmentKey)
            throw new InvalidOperationException(
                "Jwt:Key est la clé d'exemple du dépôt : elle est publique. Définissez une clé secrète dans la variable d'environnement Jwt__Key.");

        return key;
    }
}
