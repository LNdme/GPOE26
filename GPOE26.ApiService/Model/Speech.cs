namespace GPOE26.ApiService.Model
{
    public class Speech
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string AuthorName { get; set; } = string.Empty;   // ex: "M. Ndongo"
        public string AuthorRole { get; set; } = string.Empty;   // ex: "Proviseur"
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string? Excerpt { get; set; }
        public string? AvatarUrl { get; set; }
        public string Occasion { get; set; } = string.Empty;     // ex: "Rentrée 2025-2026"
        public DateTime DeliveredAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
