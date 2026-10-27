namespace User.Model
{
    /// <summary>
    /// Rattachement d'un parent (ou tuteur) à un élève.
    /// - Le parent saisit le code de l'élève → lien Pending, que l'élève accepte ou refuse.
    /// - Le parent crée lui-même le compte de l'élève → lien Active immédiatement.
    /// </summary>
    public class ParentChildLink
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ParentId { get; set; }

        public Guid ChildId { get; set; }

        public LinkStatus Status { get; set; } = LinkStatus.Pending;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? RespondedAt { get; set; }
    }

    public enum LinkStatus
    {
        Pending,
        Active,
        Rejected
    }
}
