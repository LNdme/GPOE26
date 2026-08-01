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
        Teacher,

        /// <summary>Parent qui suit la progression de ses enfants.</summary>
        Parent
    }

    /// <summary>
    /// Code à usage unique généré par un élève pour qu'un parent se rattache à lui.
    ///
    /// C'est l'élève qui l'émet, jamais le parent : le lien ne peut donc pas exister
    /// sans qu'il l'ait voulu, et on ne peut pas se rattacher à un enfant au hasard en
    /// devinant son adresse.
    /// </summary>
    public class StudentLinkCode
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid StudentId { get; set; }
        public AppUser Student { get; set; } = null!;

        /// <summary>Code court, lisible à voix haute, sans caractères ambigus.</summary>
        public required string Code { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Un code permanent finirait par circuler : celui-ci ne vaut qu'une demi-heure.
        /// </summary>
        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(30);

        public DateTime? UsedAt { get; set; }
        public Guid? UsedByParentId { get; set; }

        public bool IsUsable(DateTime now) => UsedAt is null && ExpiresAt > now;

        /// <summary>Durée de validité d'un code fraîchement émis.</summary>
        public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Alphabet sans 0/O ni 1/I/L : un code se dicte souvent de vive voix entre
        /// un enfant et son parent.
        /// </summary>
        private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

        // GetString plutôt qu'un modulo sur des octets : 31 ne divise pas 256, donc
        // « octet % 31 » rendrait les huit premières lettres de l'alphabet plus
        // fréquentes que les autres.
        public static string NewCode() =>
            System.Security.Cryptography.RandomNumberGenerator.GetString(Alphabet, 8);
    }

    /// <summary>Lien de suivi entre un parent et un élève.</summary>
    public class ParentChild
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ParentId { get; set; }
        public AppUser Parent { get; set; } = null!;

        public Guid StudentId { get; set; }
        public AppUser Student { get; set; } = null!;

        public DateTime LinkedAt { get; set; } = DateTime.UtcNow;
    }
}
