using Microsoft.AspNetCore.Components.Server.Circuits;

namespace EntornExamen.Web.Services;

/// <summary>
/// Detecta quan el navegador de l'alumne perd la connexió SignalR.
/// Espera 90 s per permetre reconnexions breus (salvapantalles, suspensió breu de WiFi).
/// Si no hi ha reconnexió, marca l'alumne com a desconnectat a l'API.
/// </summary>
public class ExamenCircuitHandler(
    ExamenCircuitState state,
    ApiClient api,
    IConfiguration config,
    ILogger<ExamenCircuitHandler> logger) : CircuitHandler
{
    private CancellationTokenSource? _cts;

    private int GracePeriodSeconds =>
        int.TryParse(config["Examen:CircuitGracePeriodSeconds"], out var v) && v > 0 ? v : 90;

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        if (state.StudentId is null) return Task.CompletedTask;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _ = ProgramarDesconnexioAsync(state.StudentId.Value, state.Email ?? "?", _cts.Token);
        return Task.CompletedTask;
    }

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        return Task.CompletedTask;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        return Task.CompletedTask;
    }

    private async Task ProgramarDesconnexioAsync(int studentId, string email, CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(GracePeriodSeconds), ct);
            logger.LogInformation(
                "Circuit tancat {GracePeriodSeconds}s sense reconnexió per a {Email}. Marcant com a desconnectat.",
                GracePeriodSeconds, email);
            await api.SortirCircuitAsync(studentId);
        }
        catch (OperationCanceledException)
        {
            // reconnectat dins del període de gràcia, ignorar
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error marcant desconnexió de circuit per a {Email}", email);
        }
    }
}
