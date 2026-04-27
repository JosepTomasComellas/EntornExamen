namespace EntornExamen.Api.Data.Models;

public class Professor
{
    public int     Id           { get; set; }
    public string  Email        { get; set; } = null!;
    public string  PasswordHash { get; set; } = null!;
    public string  Nom          { get; set; } = null!;
    public string  Cognoms      { get; set; } = null!;
    public bool    IsAdmin      { get; set; }
    public DateTime CreatedAt   { get; set; } = DateTime.UtcNow;

    public string NomComplet => $"{Nom} {Cognoms}";
}
