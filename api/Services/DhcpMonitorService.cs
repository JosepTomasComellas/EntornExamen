using EntornExamen.Shared.DTOs;
using System.Text.RegularExpressions;

namespace EntornExamen.Api.Services;

/// <summary>
/// Llegeix /data/dhcpd.leases cada 5 segons i detecta canvis d'estat de les concessions DHCP.
/// Publica events a l'ExamenService quan detecta connexions o desconnexions.
/// </summary>
public class DhcpMonitorService(
    IServiceProvider services,
    IConfiguration config,
    ILogger<DhcpMonitorService> logger) : BackgroundService
{
    private readonly string _leasesPath =
        config["Examen:DhcpLeasesPath"] ?? "/data/dhcpd.leases";

    // Estat anterior: mac → (ip, activa)
    private readonly Dictionary<string, (string? Ip, bool Activa)> _estat = new();

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("DhcpMonitorService iniciat. Fitxer: {Path}", _leasesPath);

        while (!ct.IsCancellationRequested)
        {
            try { await ProcessarLeasesAsync(ct); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error processant leases DHCP");
            }
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }

    private async Task ProcessarLeasesAsync(CancellationToken ct)
    {
        if (!File.Exists(_leasesPath)) return;

        string contingut;
        try { contingut = await File.ReadAllTextAsync(_leasesPath, ct); }
        catch { return; }

        var concessions = ParseLeases(contingut);
        var macActuals  = concessions.Keys.ToHashSet();

        // Detecta connexions noves o canvis d'estat
        foreach (var (mac, (ip, activa)) in concessions)
        {
            if (!_estat.TryGetValue(mac, out var anterior))
            {
                // MAC nova
                if (activa)
                    await PublicarEventAsync("connected", mac, ip, ct);
                _estat[mac] = (ip, activa);
            }
            else if (anterior.Activa != activa || anterior.Ip != ip)
            {
                // Canvi d'estat
                var eventNom = activa ? "connected" : "disconnected";
                await PublicarEventAsync(eventNom, mac, ip, ct);
                _estat[mac] = (ip, activa);
            }
        }

        // Detecta MACs que ja no apareixen al fitxer (alliberades)
        var desaparegudes = _estat.Keys.Except(macActuals).ToList();
        foreach (var mac in desaparegudes)
        {
            if (_estat[mac].Activa)
                await PublicarEventAsync("disconnected", mac, null, ct);
            _estat.Remove(mac);
        }
    }

    private static Dictionary<string, (string? Ip, bool Activa)> ParseLeases(string contingut)
    {
        var result = new Dictionary<string, (string?, bool)>(StringComparer.OrdinalIgnoreCase);

        // Cada bloc: lease <ip> { ... }
        var blocs = Regex.Matches(contingut,
            @"lease\s+([\d\.]+)\s*\{([^}]*)\}",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        foreach (Match bloc in blocs)
        {
            var ip   = bloc.Groups[1].Value.Trim();
            var body = bloc.Groups[2].Value;

            var macMatch   = Regex.Match(body, @"hardware ethernet\s+([\da-fA-F:]{17});");
            var stateMatch = Regex.Match(body, @"binding state\s+(\w+);");

            if (!macMatch.Success) continue;

            var mac    = macMatch.Groups[1].Value.ToLowerInvariant();
            var state  = stateMatch.Success ? stateMatch.Groups[1].Value.ToLower() : "expired";
            var activa = state == "active";

            // En cas de duplicats (renovacions), l'últim guanya
            result[mac] = (ip, activa);
        }

        return result;
    }

    private async Task PublicarEventAsync(string eventNom, string mac, string? ip, CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var examen = scope.ServiceProvider.GetRequiredService<IExamenService>();
        await examen.ProcessDhcpEventAsync(new DhcpEventRequest(mac, ip, eventNom));
    }
}
