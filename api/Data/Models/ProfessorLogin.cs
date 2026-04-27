namespace EntornExamen.Api.Data.Models;

public class ProfessorLogin
{
    public int      Id          { get; set; }
    public int      ProfessorId { get; set; }
    public DateTime CreatedAt   { get; set; } = DateTime.UtcNow;
}
