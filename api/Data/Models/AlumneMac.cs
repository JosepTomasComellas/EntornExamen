namespace EntornExamen.Api.Data.Models;

public class AlumneMac
{
    public int     Id            { get; set; }
    public int     StudentId     { get; set; }
    public string  Mac           { get; set; } = null!;   // "aa:bb:cc:dd:ee:ff" (lowercase)
    public string? Dispositiu    { get; set; }             // "portàtil", "mòbil", etc.
    public DateTime PrimerCopVist { get; set; } = DateTime.UtcNow;

    public Student Student { get; set; } = null!;
}
