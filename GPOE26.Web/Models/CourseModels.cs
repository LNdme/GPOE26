namespace GPOE26.Web.Models;

public class Course
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Duration { get; set; } = "";
    public string Icon { get; set; } = "";
    public bool Expanded { get; set; }
    public List<Lesson> Lessons { get; set; } = [];
}

public class Lesson
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Duration { get; set; } = "";
    public bool Done { get; set; }
    public bool Active { get; set; }
}
