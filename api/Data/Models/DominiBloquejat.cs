namespace EntornExamen.Api.Data.Models;

public class DominiBloquejat
{
    public int      Id        { get; set; }
    public string   Domini    { get; set; } = "";
    public string?  Nota      { get; set; }
    public bool     Actiu     { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
