using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using User.Model;

namespace User.Service
{
    public class Jwtservice(IConfiguration config)
    {

        public (string token, DateTime expiresAt) GenerateToken(AppUser user)
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
            var claims = new[]
            {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("username", user.Username),
            new Claim("role", user.Role.ToString()),
            new Claim("level", user.Level ?? ""),
            new Claim("specialite", user.Specialite ?? ""),
            new Claim("filiere", user.Filiere ?? ""),
            new Claim("lang", user.Language),
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

