using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using User.Model;

namespace User.Service
{
    public class Jwtservice(IConfiguration config)
    {

        /// <summary>Durée maximale d'une session renouvelée sans ressaisir le mot de passe (Jwt:MaxSessionHours, 12 h par défaut).</summary>
        public TimeSpan MaxSession => TimeSpan.FromHours(config.GetValue("Jwt:MaxSessionHours", 12));

        /// <summary>Une session ouverte à cette date peut-elle encore être prolongée ?</summary>
        public static bool CanRenew(DateTime signedInAt, DateTime now, TimeSpan maxSession) =>
            signedInAt <= now && now - signedInAt < maxSession;

        /// <summary>Date de la connexion par mot de passe, lue dans le jeton (claim "auth_time"). Null si absente.</summary>
        public static DateTime? SignedInAt(ClaimsPrincipal principal) =>
            long.TryParse(principal.FindFirst("auth_time")?.Value, out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
                : null;

        /// <param name="signedInAt">
        /// Date de la connexion par mot de passe. Conservée d'un renouvellement à l'autre :
        /// c'est elle, pas la date du jeton, qui borne la durée de la session.
        /// </param>
        public (string token, DateTime expiresAt) GenerateToken(AppUser user, Entitlement entitlement, DateTime? signedInAt = null)
        {
            var jwtKey = config["Jwt:Key"]
                ?? throw new InvalidOperationException("Jwt:Key manquant dans la configuration.");

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            // ─── Claims embarqués dans le token ───────────────────────────────────
            // Tous les services qui reçoivent ce JWT liront ces infos sans DB.
            //
            // Le service Chat/LLM utilisera :
            //   "role"     → pour adapter le ton de l'agent
            //   "level"    → pour adapter la complexité des réponses
            //   "subjects" → pour personnaliser les exemples
            //   "lang"     → pour répondre dans la bonne langue
            //   "premium"  → pour réserver les fonctions Premium aux abonnés
            var claims = new[]
            {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("username", user.Username),
            new Claim("role", user.Role.ToString()),
            new Claim("level", user.Level ?? ""),
            new Claim("stage", user.Stage?.ToString() ?? ""),
            new Claim("specialite", user.Specialite ?? ""),
            new Claim("filiere", user.Filiere ?? ""),
            new Claim("lang", user.Language),
            // "premium" → les services Chat/Quiz réservent le tuteur IA et les quiz illimités aux abonnés
            new Claim("premium", entitlement.IsPremium ? "true" : "false"),
            new Claim("plan", entitlement.Plan?.ToString() ?? ""),
            // "site_editor" → le service du site autorise la gestion du contenu (en plus du rôle Admin)
            new Claim("site_editor", user.SiteEditor ? "true" : "false"),
            new Claim("auth_time", new DateTimeOffset(DateTime.SpecifyKind(signedInAt ?? DateTime.UtcNow, DateTimeKind.Utc)).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

            var expiresAt = DateTime.UtcNow.AddMinutes(
                config.GetValue<int>("Jwt:ExpirationMinutes", 60));

            var token = new JwtSecurityToken(
                issuer: config["Jwt:Issuer"],
                audience: config["Jwt:Audience"],
                claims: claims,
                expires: expiresAt,
                signingCredentials: credentials
            );

            return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
        }
    }
    
}

