using EntornExamen.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EntornExamen.Api.Services;

/// <summary>
/// Elimina sessions tancades fa més de 30 dies una vegada al dia.
/// </summary>
public class SessioCleanupService(IServiceProvider services, ILogger<SessioCleanupService> logger)
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

    private async Task NetejarSessionsAntiguesAsync()
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var limit = DateTime.UtcNow.AddDays(-30);
        var sessions = await db.SessionsExamen
            .Where(s => !s.Activa && s.TancadaAt < limit)
            .Include(s => s.Registres)
                .ThenInclude(r => r.PeticiosDns)
            .ToListAsync();

        if (sessions.Count == 0) return;

        db.SessionsExamen.RemoveRange(sessions);
        await db.SaveChangesAsync();

        logger.LogInformation("Eliminades {Count} sessions tancades fa més de 30 dies.", sessions.Count);
    }
}
