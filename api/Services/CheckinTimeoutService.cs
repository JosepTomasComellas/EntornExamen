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

    private double SenseCheckinFactor =>
        double.TryParse(config["Examen:SenseCheckinFactor"],
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var f) && f > 0 ? f : 2.0;

    private double DesconnectatFactor =>
        double.TryParse(config["Examen:DesconnectatFactor"],
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var f) && f > 0 ? f : 4.0;

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
        var interval          = IntervalSegons;
        var senseCheckinFactor = SenseCheckinFactor;
        var desconnectatFactor = DesconnectatFactor;
        var ara = DateTime.UtcNow;

        using var scope = services.CreateScope();
        var db  = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hub = scope.ServiceProvider.GetRequiredService<ExamenHub>();

        var sessionsActives = await db.SessionsExamen
            .Where(s => s.Activa)
            .Select(s => s.Id)
            .ToListAsync();
        if (sessionsActives.Count == 0) return;

        // Connectats sense check-in en interval×SenseCheckinFactor → SenseCheckin
        var threshold1 = ara.AddSeconds(-interval * senseCheckinFactor);
        var sensCheckin = await db.RegistresConnexio
            .Include(r => r.Student)
            .Where(r => sessionsActives.Contains(r.SessioId) &&
                        r.Estat == EstatConnexio.Connectat &&
                        (r.UltimCheckinAt ?? r.ConnectatAt) < threshold1)
            .ToListAsync();
        foreach (var r in sensCheckin)
            r.Estat = EstatConnexio.SenseCheckin;

        // SenseCheckin durant interval×DesconnectatFactor → Desconnectat + notificació
        var threshold2 = ara.AddSeconds(-interval * desconnectatFactor);
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

        // Notifica en temps real els canvis a SenseCheckin (taronja al plafó del professor)
        foreach (var r in sensCheckin)
        {
            _ = hub.NotificaAlumneSenseCheckinAsync(r.SessioId, new ExamenEventAlumne(
                r.StudentId, r.Student?.Nom, r.Student?.Cognoms,
                r.IpAssignada, r.MacAddress, ara));
        }

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
