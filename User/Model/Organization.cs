namespace User.Model
{
    /// <summary>
    /// Structure qui achète une licence pour ses enseignants (et éventuellement ses élèves) :
    /// un établissement scolaire ou une autre institution (entreprise, startup, centre de formation…).
    /// </summary>
    public class Organization
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public required string Name { get; set; }

        public OrganizationType Type { get; set; } = OrganizationType.School;

        public string? City { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Dernier numéro de matricule généré (AAAA-NNNN) ; incrémenté par un UPDATE atomique.</summary>
        public int MatriculeSeq { get; set; }
    }

    public enum OrganizationType
    {
        School,
        Institution,
        /// <summary>Espace d'un enseignant indépendant (répétiteur, cours particuliers), dont il est le responsable.</summary>
        Independent
    }
}
