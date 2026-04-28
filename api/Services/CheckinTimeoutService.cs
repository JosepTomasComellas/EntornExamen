using EntornExamen.Api.Data;
using EntornExamen.Api.Data.Models;
using EntornExamen.Api.Hubs;
using EntornExamen.Shared.DTOs;
using Microsoft.EntityFrameworkCore;

namespace EntornExamen.Api.Services;

/// <summary>
/// Detecta alumnes connectats que han deixat de fer check-in.
/// Interval*2 → SenseCheckin, Interval*4 → Desconnectat + notificació al professor.
/// </summary>
public class CheckinTimeoutService(
    IServiceProvider services,
    IConfiguration config,
    ILogger<CheckinTimeoutService> logger) : BackgroundService
{
    private int IntervalSegons =>
        int.TryParse(config["Examen:CheckinIntervalSeconds"], out var iv) ? iv : 30;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("CheckinTimeoutService iniciat.");

        while (!ct.IsCancellationRequested)
        {
            try { await ProcessarTimeoutsAsync(); }
            catch (Exception ex) { logger.LogWarning(ex, "Error processant timeouts de check-in"); }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(15, IntervalSegons / 2)), ct);
        }
    }

    private async Task ProcessarTimeoutsAsync()
    {
        var interval = IntervalSegons;
        var ara = DateTime.UtcNow;

        using var scope = services.CreateScope();
        var db  = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hub = scope.ServiceProvider.GetRequiredService<ExamenHub>();

        var sessionsActives = await db.SessionsExamen
            .Where(s => s.Activa)
            .Select(s => s.Id)
            .ToListAsync();
        if (sessionsActives.Count == 0) return;

        // Connectats sense check-in en interval×2 → SenseCheckin
        var threshold1 = ara.AddSeconds(-interval * 2);
        var sensCheckin = await db.RegistresConnexio
            .Where(r => sessionsActives.Contains(r.SessioId) &&
                        r.Estat == EstatConnexio.Connectat &&
                        (r.UltimCheckinAt ?? r.ConnectatAt) < threshold1)
            .ToListAsync();
        foreach (var r in sensCheckin)
            r.Estat = EstatConnexio.SenseCheckin;

        // SenseCheckin durant interval×4 → Desconnectat + notificació
        var threshold2 = ara.AddSeconds(-interval * 4);
        var desconnectats = await db.RegistresConnexio
            .Include(r => r.Student)
            .Where(r => sessionsActives.Contains(r.SessioId) &&
                        r.Estat == EstatConnexio.SenseCheckin &&
                        (r.UltimCheckinAt ?? r.ConnectatAt) < threshold2)
            .ToListAsync();
        foreach (var r in desconnectats)
        {
            r.Estat = EstatConnexio.Desconnectat;
            r.DesconnectatAt = ara;
        }

        if (sensCheckin.Count > 0 || desconnectats.Count > 0)
            await db.SaveChangesAsync();

        foreach (var r in desconnectats)
        {
            logger.LogInformation(
                "Alumne desconnectat per timeout: {Mac} (sessió {SessioId})", r.MacAddress, r.SessioId);
            _ = hub.NotificaAlumneDesconnectatAsync(r.SessioId, new ExamenEventAlumne(
                r.StudentId, r.Student?.Nom, r.Student?.Cognoms,
                r.IpAssignada, r.MacAddress, ara));
        }
    }
}
