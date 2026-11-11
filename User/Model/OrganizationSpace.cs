namespace User.Model
{
    // Espaces d'établissement et d'enseignant indépendant : voir docs/PLAN-ETABLISSEMENT.md.
    // Un enseignant indépendant a son propre espace (Organization.Type = Independent) : un seul modèle pour les deux cas.

    /// <summary>
    /// Rôle d'une personne dans un espace. Une même personne peut avoir plusieurs rôles (une ligne par rôle) :
    /// l'enseignant indépendant est Owner et Teacher de son espace.
    /// </summary>
    public class OrganizationMember
    {
        public Guid OrganizationId { get; set; }

        public Guid UserId { get; set; }

        public OrganizationRole Role { get; set; }

        public MemberStatus Status { get; set; } = MemberStatus.Active;

        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    }

    public enum OrganizationRole
    {
        /// <summary>Responsable du compte : désigné par l'admin de la plateforme, nomme les gestionnaires.</summary>
        Owner,
        /// <summary>Gestionnaire : classes, enseignants, élèves et codes, sans nommer d'autres gestionnaires.</summary>
        Manager,
        Teacher
    }

    public enum MemberStatus
    {
        Active,
        Removed
    }

    /// <summary>Année scolaire d'un espace (ex : « 2026-2027 »). Une seule est en cours à la fois.</summary>
    public class SchoolYear
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid OrganizationId { get; set; }

        public required string Label { get; set; }

        public DateOnly StartsOn { get; set; }

        public DateOnly EndsOn { get; set; }

        public bool IsCurrent { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>Enseignant d'une classe pour une matière ("" : toutes matières). IsMain : professeur principal.</summary>
    public class ClassroomTeacher
    {
        public Guid ClassroomId { get; set; }

        public Guid TeacherId { get; set; }

        public string Subject { get; set; } = "";

        public bool IsMain { get; set; }

        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Élève de la liste officielle d'un espace. La ligne existe avant le compte : UserId est rempli
    /// quand l'élève active son code. Elle survit d'une année à l'autre (passer en classe supérieure = changer ClassroomId).
    /// </summary>
    public class OrganizationStudent
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid OrganizationId { get; set; }

        /// <summary>Unique dans l'espace. Importé par l'établissement, ou généré (AAAA-NNNN).</summary>
        public required string Matricule { get; set; }

        public required string LastName { get; set; }

        public string FirstName { get; set; } = "";

        public DateOnly? BirthDate { get; set; }

        /// <summary>E-mail donné par l'établissement (facultatif) : recopié sur le compte à l'activation s'il est libre.</summary>
        public string? Email { get; set; }

        /// <summary>
        /// Colonnes en plus du fichier importé (téléphone, parent, filière…), en JSON { titre : valeur },
        /// gardées telles quelles pour l'établissement (docs/PLAN-ETABLISSEMENT-2.md, P4).
        /// </summary>
        public string? ExtraJson { get; set; }

        public Guid? ClassroomId { get; set; }

        public Guid? UserId { get; set; }

        public StudentStatus Status { get; set; } = StudentStatus.Pending;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ActivatedAt { get; set; }
    }

    public enum StudentStatus
    {
        /// <summary>Inscrit sur la liste, code pas encore activé.</summary>
        Pending,
        /// <summary>Compte activé : occupe une place de licence.</summary>
        Active,
        /// <summary>Révoqué par le responsable : la place est libérée.</summary>
        Revoked,
        /// <summary>A quitté l'espace.</summary>
        Left
    }

    /// <summary>Code personnel à usage unique, gardé en clair pour être revu et réimprimé.</summary>
    public class AccessCode
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public required string Code { get; set; }

        public Guid OrganizationId { get; set; }

        public AccessCodeKind Kind { get; set; }

        /// <summary>Élève visé (codes élèves). Vide pour un code « en blanc » ou un code enseignant / responsable.</summary>
        public Guid? OrganizationStudentId { get; set; }

        /// <summary>Code « en blanc » : classe où l'élève entrera (il saisit lui-même son nom à l'activation).</summary>
        public Guid? ClassroomId { get; set; }

        public AccessCodeStatus Status { get; set; } = AccessCodeStatus.Issued;

        public DateTime ExpiresAt { get; set; }

        public DateTime? UsedAt { get; set; }

        public Guid? UsedByUserId { get; set; }

        public Guid? CreatedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public enum AccessCodeKind
    {
        Student,
        Teacher,
        Owner
    }

    public enum AccessCodeStatus
    {
        Issued,
        Used,
        Revoked,
        Expired
    }

    /// <summary>Le schéma a joué cette reprise de données (cf. UserDataMigrations).</summary>
    public class SchemaVersion
    {
        public int Version { get; set; }

        public required string Description { get; set; }

        public DateTime AppliedAt { get; set; } = DateTime.UtcNow;
    }
}
