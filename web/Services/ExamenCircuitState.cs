namespace EntornExamen.Web.Services;

public class ExamenCircuitState
{
    public int?    StudentId { get; set; }
    public string? Email     { get; set; }
    /// <summary>IP real del client (capturada des de X-Real-IP de nginx a App.razor).</summary>
    public string  ClientIp  { get; set; } = "";
}
