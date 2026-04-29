namespace EntornExamen.Api.Data.Models;

public class SessioExamenRecurs
{
    public int SessioId { get; set; }
    public int RecursId { get; set; }

    public SessioExamen Sessio { get; set; } = null!;
    public RecursExamen Recurs { get; set; } = null!;
}
