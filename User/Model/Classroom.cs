namespace User.Model
{
    /// <summary>
    /// Une classe (établissement scolaire) ou un groupe (autre institution) d'un espace.
    /// Les élèves y entrent avec leur code personnel.
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

        /// <summary>
        /// Ancien code de classe commun (ex : « K7P-Q2M »), qui ne sert plus : un élève entre par son code personnel
        /// (docs/PLAN-ETABLISSEMENT.md, lot 7). Gardé pour les classes qui en avaient un ; vide pour les nouvelles.
        /// </summary>
        public string? InviteCode { get; set; }

        public bool IsArchived { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public List<ClassroomMember> Members { get; set; } = new();
    }

    /// <summary>
    /// Une période d'appartenance d'un élève à une classe (docs/PLAN-APPRENANT-AU-CENTRE.md, lot J2). Le départ ne
    /// supprime plus la ligne : LeftAt est posé, et un retour crée une nouvelle période. Un filtre EF ne garde que les
    /// périodes en cours (LeftAt vide) ; l'historique et la visibilité lisent les autres avec IgnoreQueryFilters().
    /// </summary>
    public class ClassroomMember
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ClassroomId { get; set; }
        public Classroom Classroom { get; set; } = null!;

        public Guid StudentId { get; set; }

        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Fin de la période (départ, changement de classe, retrait) ; vide tant que l'élève est dans la classe.</summary>
        public DateTime? LeftAt { get; set; }
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
