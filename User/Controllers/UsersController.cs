using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.JsonPatch;
using User.Data;
using User.DTOs;
using User.Model;

namespace User.Controllers
{
    /// <summary>
    /// Ce controller existe UNIQUEMENT pour le PATCH.
    ///
    /// Pourquoi pas en Minimal API ?
    /// JsonPatch (RFC 6902) envoie un tableau d'opérations au lieu d'un objet JSON classique.
    /// ASP.NET Core a besoin du pipeline MVC complet pour désérialiser ce format.
    /// Les Minimal APIs ne le supportent pas nativement.
    ///
    /// Format attendu par le client (Content-Type: application/json-patch+json) :
    /// [
    ///   { "op": "replace", "path": "/level", "value": "Terminale" },
    ///   { "op": "replace", "path": "/filiere", "value": "F4" },
    ///   { "op": "remove",  "path": "/specialite" }
    /// ]
    ///
    /// Différence PUT vs PATCH :
    ///   PUT   → le client envoie TOUT le profil, les champs omis deviennent null
    ///   PATCH → le client envoie SEULEMENT les champs à modifier, le reste est intact
    /// </summary>
    /// 


    [Route("auth")]
    [Authorize]
    [ApiController]
    public class UsersController(UserContext db) : ControllerBase
    {
        /// <summary>Extrait l'Id utilisateur depuis les claims du JWT</summary>
        private Guid GetUserId()
        {
            var value = User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? User.FindFirstValue("sub")
                        ?? throw new UnauthorizedAccessException("Id introuvable dans le token.");
            return Guid.Parse(value);
        }

        // ── PATCH /auth/me ────────────────────────────────────────────────────────
        [HttpPatch("me")]
        [Consumes("application/json-patch+json")]
        [ProducesResponseType(typeof(UserProfileDto), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> PatchMe(
            [FromBody] JsonPatchDocument<UpdateProfileRequest> patchDoc)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var user = await db.AppUsers.FindAsync(GetUserId());
            if (user is null) return NotFound();

            // On applique le patch sur un DTO intermédiaire — jamais directement
            // sur l'entité AppUser. Sinon le client pourrait tenter de modifier
            // des champs sensibles : Id, PasswordHash, CreatedAt, Role...
            var dto = new UpdateProfileRequest(
                user.Username,
                user.Level,
                user.Specialite,
                user.Filiere,
                user.Language
            );

            patchDoc.ApplyTo(dto, ModelState);
            if (!ModelState.IsValid) return BadRequest(ModelState);

            // Cohérence métier après application du patch
            if (user.Role == UserRole.Student && dto.Specialite is not null)
                return BadRequest(new { message = "Un élève ne peut pas avoir de spécialité." });

            if (user.Role == UserRole.Teacher && dto.Filiere is not null)
                return BadRequest(new { message = "Un enseignant ne peut pas avoir de filière." });

            // Appliquer uniquement les champs autorisés vers l'entité
            if (dto.Username is not null) user.Username = dto.Username;
            if (dto.Level is not null) user.Level = dto.Level;
            if (dto.Specialite is not null) user.Specialite = dto.Specialite;
            if (dto.Filiere is not null) user.Filiere = dto.Filiere;
            if (dto.Language is not null) user.Language = dto.Language;

            await db.SaveChangesAsync();
            return Ok(new UserProfileDto(user));
        }
    }
}
