namespace AutoCo.Api.Data.Models;

public class SessioExamen
{
    public int      Id               { get; set; }
    public int      ClassId          { get; set; }
    public int      ProfessorId      { get; set; }
    public string?  Titol            { get; set; }
    public string?  Descripcio       { get; set; }   // Instruccions visibles a l'alumne
    public string?  MissatgeActiu    { get; set; }   // Missatge push del professor als alumnes
    public DateTime IniciadaAt       { get; set; } = DateTime.UtcNow;
    public DateTime? TancadaAt       { get; set; }
    public bool     Activa           { get; set; } = true;

    public Class     Class     { get; set; } = null!;
    public Professor Professor { get; set; } = null!;
    public ICollection<RegistreConnexio> Registres { get; set; } = [];
}
