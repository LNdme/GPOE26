namespace GPOE26.ApiService.Model
{
    public class Hierarchy
    {
        public int Id { get; set; }
        public string Role { get; set; } = string.Empty; // ex: "Administration", "Enseignants", "Étudiants"
        public string Description { get; set; } = string.Empty;
        public string? Department { get; set; }
        public string? Specialization { get; set; }
        public string? ImageUrl { get; set; }
        public string? Name { get; set; }
        public string? PreName { get; set; }
        public string? Email { get; set; }
        public string? Citation { get; set; }
    }
}
