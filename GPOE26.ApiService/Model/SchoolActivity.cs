namespace GPOE26.ApiService.Model
{
    public class SchoolActivity
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;           // ex: "Club Robotique"
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;       // Sport, Art, Science, Club
        public string? Schedule { get; set; }                      // ex: "Mardi 15h-17h"
        public string? ResponsibleTeacher { get; set; }
        public string? ImageUrl { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
