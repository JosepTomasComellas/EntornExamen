namespace EntornExamen.Api.Data.Models;

public class Class
{
    public int    Id           { get; set; }
    public string Name         { get; set; } = null!;
    public string? AcademicYear { get; set; }
    public DateTime CreatedAt  { get; set; } = DateTime.UtcNow;

    public ICollection<Student> Students { get; set; } = [];
}
