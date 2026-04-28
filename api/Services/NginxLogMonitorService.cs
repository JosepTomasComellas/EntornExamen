using EntornExamen.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EntornExamen.Api.Services;

public class NginxLogMonitorService(
    IServiceProvider services,
    IConfiguration config,
    ILogger<NginxLogMonitorService> logger) : BackgroundService
{
    private long _lastPosition;
    private string LogPath => config["Examen:NginxLogPath"] ?? "/data/nginx/access.log";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("NginxLogMonitorService iniciat. Fitxer: {Path}", LogPath);

        if (File.Exists(LogPath))
            _lastPosition = new FileInfo(LogPath).Length;

        while (!ct.IsCancellationRequested)
        {
            try { await ProcessarLogAsync(); }
            catch (Exception ex) { logger.LogWarning(ex, "Error processant log nginx"); }

            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }

    private async Task ProcessarLogAsync()
    {
        if (!File.Exists(LogPath)) return;

        var fi = new FileInfo(LogPath);
        if (fi.Length <= _lastPosition)
        {
            if (fi.Length < _lastPosition) _lastPosition = 0; // log rotat
            return;
        }

        var byIp = new Dictionary<string, (long Bytes, int Count)>();

        using (var fs = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            fs.Seek(_lastPosition, SeekOrigin.Begin);
            using var sr = new StreamReader(fs);
            string? line;
            while ((line = await sr.ReadLineAsync(ct)) is not null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<NginxLogEntry>(line);
                    if (entry is null || string.IsNullOrEmpty(entry.Ip)) continue;

                    var (bytes, count) = byIp.GetValueOrDefault(entry.Ip);
                    byIp[entry.Ip] = (bytes + entry.Bytes, count + 1);
                }
                catch { /* línia malformada, ignorar */ }
            }
            _lastPosition = fs.Position;
        }

        if (byIp.Count == 0) return;

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sessionsActives = await db.SessionsExamen
            .Where(s => s.Activa)
            .Select(s => s.Id)
            .ToListAsync(ct);

        if (sessionsActives.Count == 0) return;

        var ips = byIp.Keys.ToList();
        var registres = await db.RegistresConnexio
            .Where(r => sessionsActives.Contains(r.SessioId) &&
                        r.IpAssignada != null &&
                        ips.Contains(r.IpAssignada!))
            .ToListAsync(ct);

        bool changed = false;
        foreach (var r in registres)
        {
            if (r.IpAssignada is null || !byIp.TryGetValue(r.IpAssignada, out var stats)) continue;
            r.BytesEnviats = (r.BytesEnviats ?? 0) + stats.Bytes;
            r.NumRequestes = (r.NumRequestes ?? 0) + stats.Count;
            changed = true;
        }

        if (changed)
            await db.SaveChangesAsync(ct);
    }

    private sealed record NginxLogEntry(
        [property: JsonPropertyName("ip")]    string Ip,
        [property: JsonPropertyName("bytes")] long   Bytes);
}
