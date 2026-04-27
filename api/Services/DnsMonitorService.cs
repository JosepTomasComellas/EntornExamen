using EntornExamen.Shared.DTOs;
using System.Text.RegularExpressions;

namespace EntornExamen.Api.Services;

/// <summary>
/// Observa /data/dns-queries.log amb FileSystemWatcher i parseja noves línies.
/// Format esperat: "25-Apr-2025 10:23:45.123 client @0x... 192.168.100.101#54321 (google.com): query: google.com IN A"
/// </summary>
public class DnsMonitorService(
    IServiceProvider services,
    IConfiguration config,
    ILogger<DnsMonitorService> logger) : BackgroundService
{
    private readonly string _logPath =
        config["Examen:DnsLogPath"] ?? "/data/dns-queries.log";

    private long _posicioUltima = 0;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("DnsMonitorService iniciat. Fitxer: {Path}", _logPath);

        // Inicia al final del fitxer actual per no reprocessar l'historial
        if (File.Exists(_logPath))
            _posicioUltima = new FileInfo(_logPath).Length;

        using var watcher = new FileSystemWatcher
        {
            Path                  = Path.GetDirectoryName(_logPath) ?? "/data",
            Filter                = Path.GetFileName(_logPath),
            NotifyFilter          = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents   = true
        };

        var tcs = new TaskCompletionSource<bool>();

        watcher.Changed += async (_, _) =>
        {
            await ProcessarNovesLiniesAsync(ct);
        };

        // Mantén el servei actiu fins que es cancel·li
        try { await Task.Delay(Timeout.Infinite, ct); }
        catch (OperationCanceledException) { }
    }

    private async Task ProcessarNovesLiniesAsync(CancellationToken ct)
    {
        if (!File.Exists(_logPath)) return;

        try
        {
            await using var fs     = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var longitud           = fs.Length;
            if (longitud < _posicioUltima) _posicioUltima = 0; // rotació del fitxer
            if (longitud == _posicioUltima) return;

            fs.Seek(_posicioUltima, SeekOrigin.Begin);
            using var reader = new StreamReader(fs, leaveOpen: true);

            string? linia;
            while ((linia = await reader.ReadLineAsync(ct)) is not null)
            {
                var evt = ParseLinia(linia);
                if (evt is not null)
                    await PublicarEventAsync(evt, ct);
            }

            _posicioUltima = fs.Position;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error processant línies DNS");
        }
    }

    private static DnsEventRequest? ParseLinia(string linia)
    {
        // Format: "25-Apr-2025 10:23:45.123 client @0x... 192.168.100.101#port (domini): query: domini IN A ..."
        var m = Regex.Match(linia,
            @"^(\d{2}-\w{3}-\d{4} \d{2}:\d{2}:\d{2})\.\d+\s+client\s+\S+\s+([\d\.]+)#\d+\s+\(([^)]+)\):\s+query:",
            RegexOptions.IgnoreCase);

        if (!m.Success) return null;

        if (!DateTime.TryParse(m.Groups[1].Value, out var ts)) return null;
        var ip     = m.Groups[2].Value;
        var domini = m.Groups[3].Value.Trim('.').ToLower();

        return new DnsEventRequest(ip, domini, ts.ToUniversalTime());
    }

    private async Task PublicarEventAsync(DnsEventRequest evt, CancellationToken ct)
    {
        try
        {
            using var scope = services.CreateScope();
            var examen = scope.ServiceProvider.GetRequiredService<IExamenService>();
            await examen.ProcessDnsEventAsync(evt);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error publicant event DNS");
        }
    }
}
