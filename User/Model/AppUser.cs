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

    /// <summary>
    /// Les rôles d'un compte.
    ///
    /// ⚠️ **Les nouvelles valeurs s'ajoutent à la fin.** Le rôle est persisté en texte
    /// (<c>UserContext</c>, <c>HasConversion&lt;string&gt;()</c>), donc l'ordre n'a pas
    /// d'incidence sur les données existantes — mais il en aurait si quelqu'un retirait
    /// un jour cette conversion, et une insertion au milieu transformerait alors
    /// silencieusement des élèves en administrateurs.
    ///
    /// ⚠️ **<see cref="Admin"/> et <see cref="Editor"/> ne s'obtiennent pas en
    /// s'inscrivant.** <c>POST /auth/register</c> les refuse explicitement : sans ce
    /// refus, n'importe qui se déclarerait administrateur du site. Ils se donnent par
    /// promotion, depuis un compte qui les détient déjà.
    /// </summary>
    public enum UserRole
    {
        Student,
        Teacher,

        /// <summary>Parent qui suit la progression de ses enfants.</summary>
        Parent,

        /// <summary>
        /// Administrateur du site : tout le contenu, l'équipe de direction, les tarifs
        /// et les comptes. C'est lui qui promeut les autres.
        /// </summary>
        Admin,

        /// <summary>
        /// Rédacteur : publie actualités, événements et activités. Il ne touche ni à
        /// l'équipe de direction, ni aux tarifs, ni aux comptes — un rédacteur alimente
        /// le site, il ne le gouverne pas.
        /// </summary>
        Editor
    }

    /// <summary>Ce que chaque rôle a le droit de faire sur le contenu du site.</summary>
    public static class RolePermissions
    {
        /// <summary>
        /// Rôles qu'un visiteur peut se donner lui-même en s'inscrivant.
        ///
        /// La liste est blanche, et non noire, à dessein : un rôle ajouté à
        /// <see cref="UserRole"/> dans six mois sera refusé par défaut plutôt
        /// qu'autorisé par oubli.
        /// </summary>
        public static readonly IReadOnlySet<UserRole> SelfAssignable =
            new HashSet<UserRole> { UserRole.Student, UserRole.Teacher, UserRole.Parent };

        /// <summary>Peut publier du contenu éditorial.</summary>
        public static bool CanPublish(UserRole role) =>
            role is UserRole.Admin or UserRole.Editor;

        /// <summary>Peut gouverner le site : équipe, tarifs, comptes.</summary>
        public static bool CanAdminister(UserRole role) => role is UserRole.Admin;
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
