namespace EntornExamen.Web.Services;

public class ExamenCircuitState
{
    public int?    StudentId          { get; set; }
    public string? Email              { get; set; }
    /// <summary>IP real del client (capturada des de X-Real-IP de nginx a App.razor).</summary>
    public string  ClientIp           { get; set; } = "";
    /// <summary>Indica si el circuit SignalR del navegador està connectat.</summary>
    public bool    IsCircuitConnected     { get; set; } = true;
    /// <summary>Activa un check-in immediat quan el circuit es recupera.</summary>
    public bool    PendingImmediateCheckin { get; set; }
}
