namespace User.Model
{
    public class AppUser
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        
        /// <summary>Adresse email unique — sert d'identifiant de connexion</summary>
        public required string Email { get; set; }

        public required string Username { get; set; }

        /// <summary>
        /// Le mot de passe n'est JAMAIS stocké en clair.
        /// On stocke uniquement le hash BCrypt.
        /// </summary>
        public required string PasswordHash { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;



        // ─── Informations contextuelles pour le LLM ───────────────────────────────
        // Ces champs seront embarqués dans le JWT (claims) → les autres services
        // (chat, quiz) les liront directement sans appeler la DB User.

        /// <summary>Élève ou Enseignant</summary>
        public UserRole Role { get; set; } = UserRole.Student;

        /// <summary>
        /// Niveau scolaire de l'élève (ex: "3ème", "Terminale", "Licence 1").
        /// Null si l'utilisateur est un enseignant.
        /// Spécialité de l'enseignant (ex: "Mathématiques", "Physique", "Littérature") 
        /// filière de l'étudiant (ex: "ESF", "Electronique", "F4").
        /// Le LLM adaptera la complexité de ses réponses à ce niveau.
        /// </summary>
        public string? Level { get; set; }
        public string? Specialite { get; set; }
        public string? Filiere { get; set; }



        /// <summary>Préférence de langue pour les réponses du LLM</summary>
        public string Language { get; set; } = "fr";

        // ─── Métadonnées ──────────────────────────────────────────────────────────
        public DateTime? LastLoginAt { get; set; }
    }

    public enum UserRole
    {
        Student,
        Teacher
    }
}
