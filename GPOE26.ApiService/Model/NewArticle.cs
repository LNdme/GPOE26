namespace GPOE26.ApiService.Model
{
    public class NewArticle
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Title { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string? Excerpt { get; set; }
        public string? ImageUrl { get; set; }
        public string Category { get; set; } = "Général"; // Général, Sport, Culturel, Administratif
        public bool IsPublished { get; set; } = false;
        public DateTime PublishedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
