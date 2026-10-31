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
    }

    public enum OrganizationType
    {
        School,
        Institution
    }
}
