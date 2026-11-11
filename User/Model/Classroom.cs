namespace User.Model
{
    /// <summary>
    /// Une classe (établissement scolaire) ou un groupe (autre institution) animé par un enseignant.
    /// Les élèves la rejoignent avec son code d'invitation.
    /// </summary>
    public class Classroom
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public required string Name { get; set; }

        public ClassroomKind Kind { get; set; } = ClassroomKind.Class;

        /// <summary>
        /// Professeur principal, gardé pendant la transition vers ClassroomTeachers (plusieurs enseignants par classe).
        /// Vide si son compte a été supprimé : la classe, elle, est conservée.
        /// </summary>
        public Guid? TeacherId { get; set; }

        public Guid? OrganizationId { get; set; }

        public Guid? SchoolYearId { get; set; }

        /// <summary>Niveau (ex: "Terminale", "Upper Sixth") — null pour un groupe hors scolaire</summary>
        public string? Level { get; set; }

        public SchoolSubSystem? SubSystem { get; set; }

        public string? Subject { get; set; }

        /// <summary>Code partagé aux élèves pour rejoindre la classe (ex: "K7P-Q2M")</summary>
        public required string InviteCode { get; set; }

        public bool IsArchived { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public List<ClassroomMember> Members { get; set; } = new();
    }

    public class ClassroomMember
    {
        public Guid ClassroomId { get; set; }
        public Classroom Classroom { get; set; } = null!;

        public Guid StudentId { get; set; }

        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    }

    public enum ClassroomKind
    {
        Class,
        Group
    }

    /// <summary>Place de l'utilisateur connecté dans une classe (renvoyée aux autres services par /classrooms).</summary>
    public enum ClassroomRole
    {
        /// <summary>Enseignant affecté à la classe (une matière au moins).</summary>
        Teacher,
        /// <summary>Responsable ou gestionnaire de l'espace de la classe.</summary>
        Manager,
        Student
    }
}
