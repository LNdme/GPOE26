namespace User.Model
{
    public class AppUser
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Adresse e-mail unique. Facultative pour un élève entré par son code personnel :
        /// il se connecte alors avec son nom d'utilisateur.
        /// </summary>
        public string? Email { get; set; }

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

        /// <summary>Élève, Enseignant ou Parent : ce que la personne fait sur la plateforme.</summary>
        public UserRole Role { get; set; } = UserRole.Student;

        /// <summary>
        /// Administrateur : gère le site, la plateforme (abonnements, licences) et les accès.
        /// C'est un droit ajouté au compte, comme SiteEditor : un enseignant nommé administrateur reste enseignant
        /// et garde ses classes, ses cours et ses évaluations.
        /// </summary>
        public bool IsAdmin { get; set; }

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

        /// <summary>Secondaire ou supérieur : règle le niveau d'exigence du tuteur et des exercices.</summary>
        public EducationStage? Stage { get; set; }



        /// <summary>Préférence de langue pour les réponses du LLM</summary>
        public string Language { get; set; } = "fr";

        // ─── Profil d'apprentissage (élève) — rempli pendant l'inscription guidée ──

        /// <summary>Sous-système éducatif camerounais (francophone / anglophone)</summary>
        public SchoolSubSystem? SubSystem { get; set; }

        /// <summary>Examen visé (ex: "BEPC", "Baccalauréat", "GCE A Level")</summary>
        public string? ExamTarget { get; set; }
        public DateOnly? ExamDate { get; set; }

        /// <summary>Matières que l'élève veut renforcer</summary>
        public List<string> FocusSubjects { get; set; } = new();

        /// <summary>Temps d'étude quotidien visé, en minutes</summary>
        public int? DailyStudyMinutes { get; set; }

        /// <summary>
        /// Code personnel que l'élève communique à son parent pour être rattaché.
        /// Null pour les autres rôles.
        /// </summary>
        public string? LinkCode { get; set; }

        // ─── Profil enseignant ────────────────────────────────────────────────────

        public List<string> TeachingSubjects { get; set; } = new();

        /// <summary>Établissement scolaire, autre institution, ou à son compte</summary>
        public TeachingContext? TeachingContext { get; set; }

        /// <summary>
        /// Structure de rattachement vérifiée : renseignée quand l'utilisateur
        /// active la clé de licence d'une organisation.
        /// </summary>
        public Guid? OrganizationId { get; set; }

        /// <summary>Nom déclaré à l'inscription (non vérifié) quand il n'y a pas de clé</summary>
        public string? OrganizationName { get; set; }

        /// <summary>
        /// Éditeur du site de l'établissement : peut publier actualités, événements, activités et équipe,
        /// sans être administrateur. Indépendant du rôle (un enseignant peut être éditeur).
        /// </summary>
        public bool SiteEditor { get; set; }

        // ─── Métadonnées ──────────────────────────────────────────────────────────
        public DateTime? LastLoginAt { get; set; }
    }

    /// <summary>Enseignement secondaire (élève) ou supérieur (étudiant).</summary>
    public enum EducationStage
    {
        Secondary,
        Higher
    }

    /// <summary>
    /// Rôle sur la plateforme. L'administration n'en fait pas partie : c'est le droit AppUser.IsAdmin.
    /// (Une ancienne valeur « Admin » en base est convertie au démarrage, cf. UserSchema.)
    /// </summary>
    public enum UserRole
    {
        Student,
        Teacher,
        Parent
    }

    public enum SchoolSubSystem
    {
        Francophone,
        Anglophone
    }

    public enum TeachingContext
    {
        /// <summary>Collège, lycée, université…</summary>
        School,
        /// <summary>Entreprise, startup, centre de formation, ONG…</summary>
        Institution,
        /// <summary>Répétiteur, cours particuliers</summary>
        Independent
    }
}
