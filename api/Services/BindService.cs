using EntornExamen.Api.Data;
using EntornExamen.Api.Data.Models;
using EntornExamen.Shared.DTOs;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace EntornExamen.Api.Services;

public interface IBindService
{
    Task<List<DominiBloquejatDto>> GetDominisAsync();
    Task<DominiBloquejatDto?> AfegirDominiAsync(string domini, string? nota);
    Task<bool> EliminarDominiAsync(int id);
    Task<bool> ToggleActiuAsync(int id);
    Task<NetControlStatusDto> GetStatusAsync();
    Task<(bool Ok, string? Error)> AplicarCanavisAsync();
    Task<(bool Ok, string? Error)> SetDnsInterceptAsync(bool actiu);
}

public class BindService(AppDbContext db, IConfiguration cfg, ILogger<BindService> logger) : IBindService
{
    // Volum Docker muntat a /data/net-control/ al contenidor API
    // Al host: /var/lib/docker/volumes/entornexamen_net-control/_data/
    private string NetControlPath => cfg["NetControl:Path"] ?? "/data/net-control";
    private string RedirectIp     => cfg["NetControl:RedirectIp"] ?? "192.168.100.1";

    public async Task<List<DominiBloquejatDto>> GetDominisAsync() =>
        await db.DominisBlocats
            .OrderBy(d => d.Domini)
            .Select(d => new DominiBloquejatDto(d.Id, d.Domini, d.Nota, d.Actiu, d.CreatedAt))
            .ToListAsync();

    public async Task<DominiBloquejatDto?> AfegirDominiAsync(string domini, string? nota)
    {
        domini = domini.Trim().ToLowerInvariant().TrimStart('*').TrimStart('.');
        if (string.IsNullOrWhiteSpace(domini)) return null;

        if (await db.DominisBlocats.AnyAsync(d => d.Domini == domini))
            return null; // ja existeix

        var ent = new DominiBloquejat { Domini = domini, Nota = nota, Actiu = true };
        db.DominisBlocats.Add(ent);
        await db.SaveChangesAsync();
        return new DominiBloquejatDto(ent.Id, ent.Domini, ent.Nota, ent.Actiu, ent.CreatedAt);
    }

    public async Task<bool> EliminarDominiAsync(int id)
    {
        var ent = await db.DominisBlocats.FindAsync(id);
        if (ent is null) return false;
        db.DominisBlocats.Remove(ent);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ToggleActiuAsync(int id)
    {
        var ent = await db.DominisBlocats.FindAsync(id);
        if (ent is null) return false;
        ent.Actiu = !ent.Actiu;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<NetControlStatusDto> GetStatusAsync()
    {
        var count    = await db.DominisBlocats.CountAsync(d => d.Actiu);
        var triggerF = Path.Combine(NetControlPath, "reload-trigger");
        var interceptF = Path.Combine(NetControlPath, "dns-intercept");

        DateTime? ultimaAplicacio = null;
        if (File.Exists(triggerF) && DateTime.TryParse(await File.ReadAllTextAsync(triggerF), out var dt))
            ultimaAplicacio = dt;

        bool interceptActiu = false;
        if (File.Exists(interceptF))
            interceptActiu = (await File.ReadAllTextAsync(interceptF)).Trim() == "1";

        bool bindDisponible = Directory.Exists(NetControlPath);

        return new NetControlStatusDto(bindDisponible, interceptActiu, count, ultimaAplicacio, null);
    }

    public async Task<(bool Ok, string? Error)> AplicarCanavisAsync()
    {
        try
        {
            Directory.CreateDirectory(NetControlPath);

            var dominisActius = await db.DominisBlocats
                .Where(d => d.Actiu)
                .OrderBy(d => d.Domini)
                .ToListAsync();

            // ── blocked-zones.conf ────────────────────────────────────────────
            // Inclou una declaració de zona per cada domini bloquejat
            var sb = new StringBuilder();
            sb.AppendLine($"# EntornExamen — zones blocades auto-generades {DateTime.UtcNow:O}");
            sb.AppendLine($"# NO editar manualment. Regenerat per l'aplicació.");
            sb.AppendLine();
            foreach (var d in dominisActius)
            {
                sb.AppendLine($"zone \"{d.Domini}\" {{");
                sb.AppendLine("    type master;");
                sb.AppendLine("    file \"/etc/bind/entornexamen/blocked-zone.db\";");
                sb.AppendLine("    notify no;");
                sb.AppendLine("};");
                sb.AppendLine();
            }
            await File.WriteAllTextAsync(Path.Combine(NetControlPath, "blocked-zones.conf"), sb.ToString());

            // ── blocked-zone.db ───────────────────────────────────────────────
            // Fitxer de zona genèric que retorna l'IP del servidor per a qualsevol consulta
            var serial = DateTime.UtcNow.ToString("yyyyMMddHH");
            var zoneDb = $"""
                ; EntornExamen — zona bloquejada genèrica
                $TTL 60
                @ IN SOA ns.examen.local. admin.examen.local. ({serial} 60 60 60 60)
                @ IN NS ns.examen.local.
                @ IN A {RedirectIp}
                * IN A {RedirectIp}
                """;
            await File.WriteAllTextAsync(Path.Combine(NetControlPath, "blocked-zone.db"), zoneDb);

            // ── reload-trigger ────────────────────────────────────────────────
            await File.WriteAllTextAsync(Path.Combine(NetControlPath, "reload-trigger"),
                DateTime.UtcNow.ToString("O"));

            logger.LogInformation("Net-control escrit: {Count} dominis blocats", dominisActius.Count);
            return (true, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generant fitxers net-control");
            return (false, ex.Message);
        }
    }

    public async Task<(bool Ok, string? Error)> SetDnsInterceptAsync(bool actiu)
    {
        try
        {
            Directory.CreateDirectory(NetControlPath);
            await File.WriteAllTextAsync(
                Path.Combine(NetControlPath, "dns-intercept"),
                actiu ? "1" : "0");
            await File.WriteAllTextAsync(
                Path.Combine(NetControlPath, "reload-trigger"),
                DateTime.UtcNow.ToString("O"));
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
