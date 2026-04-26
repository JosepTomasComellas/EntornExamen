namespace AutoCo.Api.Data.Models;

public class Student
{
    public int    Id           { get; set; }
    public int    ClassId      { get; set; }
    public int    NumLlista    { get; set; }
    public string Nom          { get; set; } = null!;
    public string Cognoms      { get; set; } = null!;
    public string Email        { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public DateTime CreatedAt  { get; set; } = DateTime.UtcNow;

    public string NomComplet => $"{Nom} {Cognoms}";

    public string? Dni  { get; set; }   // Per associar foto

    public Class                    Class            { get; set; } = null!;
    public ICollection<GroupMember> GroupMemberships { get; set; } = [];
    public ICollection<ModuleExclusion> Exclusions   { get; set; } = [];
    public ICollection<AlumneMac>       Macs         { get; set; } = [];
    public ICollection<RegistreConnexio> Registres   { get; set; } = [];
}
