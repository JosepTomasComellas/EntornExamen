using EntornExamen.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EntornExamen.Api.Services;

/// <summary>
/// Elimina sessions tancades fa més de X dies una vegada al dia.
/// Configurable via Examen:CleanupRetentionDays (per defecte: 30).
/// </summary>
public class SessioCleanupService(IServiceProvider services, IConfiguration config, ILogger<SessioCleanupService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("SessioCleanupService iniciat.");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await NetejarSessionsAntiguesAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error netejant sessions antigues.");
            }

            await Task.Delay(TimeSpan.FromHours(24), ct);
        }
    }

    private int RetentionDays =>
        int.TryParse(config["Examen:CleanupRetentionDays"], out var d) && d > 0 ? d : 30;

    private async Task NetejarSessionsAntiguesAsync()
    {
        var dies = RetentionDays;
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var limit = DateTime.UtcNow.AddDays(-dies);
        var sessions = await db.SessionsExamen
            .Where(s => !s.Activa && s.TancadaAt < limit)
            .Include(s => s.Registres)
                .ThenInclude(r => r.PeticiosDns)
            .ToListAsync();

        if (sessions.Count == 0) return;

        db.SessionsExamen.RemoveRange(sessions);
        await db.SaveChangesAsync();

        logger.LogInformation("Eliminades {Count} sessions tancades fa més de {Dies} dies.", sessions.Count, dies);
    }
}
