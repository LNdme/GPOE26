namespace User.Model
{
    /// <summary>
    /// Ce que la personne regarde (docs/PLAN-APPRENANT-AU-CENTRE.md, lot J3). OfficialResults : résultats des
    /// évaluations assignées, jamais retirables pendant l'appartenance (décision D3) ; Progress : maîtrise par cours ;
    /// WeakPoints : notions fragiles. Les discussions avec le tuteur ne sont partagées avec personne.
    /// </summary>
    public enum SharingScope
    {
        OfficialResults,
        Progress,
        WeakPoints
    }

    /// <summary>
    /// Une personne majeure ne partage plus une portée (Progress ou WeakPoints) avec un espace ou avec un parent.
    /// Exactement un des deux (contrainte CHECK) ; supprimer la ligne partage à nouveau.
    /// </summary>
    public class SharingOptOut
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid LearnerId { get; set; }
        public Guid? OrganizationId { get; set; }
        public Guid? GuardianId { get; set; }
        public SharingScope Scope { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
