namespace EntornExamen.Api.Data.Models;

public class PeticioTdns
{
    public int      Id          { get; set; }
    public int      RegistreId  { get; set; }
    public string   Domini      { get; set; } = null!;
    public DateTime Timestamp   { get; set; } = DateTime.UtcNow;
    public bool     EsExterna   { get; set; }   // true si domini != "examen.local"

    public RegistreConnexio Registre { get; set; } = null!;
}
