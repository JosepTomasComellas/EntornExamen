namespace AutoCo.Api.Data.Models;

public enum EstatConnexio
{
    Connectat,       // Verd: connectat + check-in < 30s
    SenseCheckin,    // Groc: connectat però check-in > 30s
    Desconnectat,    // Vermell: desconnectat del WiFi
    NoConnectat      // Gris: alumne precarregat però mai connectat
}

public class RegistreConnexio
{
    public int      Id               { get; set; }
    public int      SessioId         { get; set; }
    public int?     StudentId        { get; set; }   // null si MAC desconeguda
    public string   MacAddress       { get; set; } = null!;
    public string?  IpAssignada      { get; set; }
    public DateTime ConnectatAt      { get; set; } = DateTime.UtcNow;
    public DateTime? DesconnectatAt  { get; set; }
    public DateTime? UltimCheckinAt  { get; set; }
    public EstatConnexio Estat       { get; set; } = EstatConnexio.NoConnectat;

    public SessioExamen   Sessio  { get; set; } = null!;
    public Student?       Student { get; set; }
    public ICollection<PeticioTdns> PeticiosDns { get; set; } = [];
}
