using EntornExamen.Api.Data;
using EntornExamen.Api.Data.Models;
using EntornExamen.Api.Hubs;
using EntornExamen.Shared.DTOs;
using ExcelDataReader;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace EntornExamen.Api.Services;

public interface IExamenService
{
    Task<List<SessioExamenDto>> GetSessionsAsync(int professorId, bool isAdmin);
    Task<(SessioExamenDto? Sessio, string? Error)> CreateSessioAsync(CreateSessioRequest req, int professorId);
    Task<ExamenDashboardDto?> GetDashboardAsync(int sessioId, int professorId, bool isAdmin);
    Task<(bool Ok, string? Error)> TancarSessioAsync(int sessioId, int professorId, bool isAdmin);
    Task<(bool Ok, string? Error)> ReobrirSessioAsync(int sessioId, int professorId, bool isAdmin);
    Task<(bool Ok, string? Error)> SetMissatgeAsync(int sessioId, string? text, int professorId, bool isAdmin);
    Task<(byte[] Content, string FileName)?> ExportarCsvAsync(int sessioId, int professorId, bool isAdmin);
    Task<(CheckinResponse? Resp, string? Error)> CheckinAsync(CheckinRequest req, string clientIp);
    Task ProcessDhcpEventAsync(DhcpEventRequest req);
    Task ProcessDnsEventAsync(DnsEventRequest req);
    Task<(ImportacioAlumnesResult Result, string? Error)> ImportarAlumnesAsync(Stream htmlStream, int professorId, bool isAdmin);
    Task<(ImportacioAlumnesResult Result, string? Error)> ImportarAlumnesXlsAsync(Stream xlsStream, int classId, bool isAdmin);
    Task<bool> UploadStudentFotoAsync(int studentId, Stream foto, string wwwrootPath);
    Task<(ImportacioFotosResult Result, string? Error)> ImportarFotosAsync(Stream zipStream, string wwwrootPath);
    Task<(bool Ok, string? Error)> EliminarSessioAsync(int sessioId, int professorId, bool isAdmin);
    Task<(bool Ok, string? Error)> SortirAsync(string clientIp);
    Task<(bool Ok, string? Error)> ExpulsarAsync(int sessioId, int studentId, int professorId, bool isAdmin);
    Task<List<AlumneMacDto>> GetMacsAsync(bool isAdmin);
    Task<bool> DeleteMacAsync(int id, bool isAdmin);
}

public class ExamenService(AppDbContext db, ExamenHub hub, IConfiguration config) : IExamenService
{
    private static string NormalitzaMac(string mac) =>
        mac.Trim().ToLowerInvariant();

    private static string FotoUrl(int studentId) =>
        $"/fotos/alumnes/{studentId}.jpg";

    private static string? FotoUrlSiExisteix(int studentId, string wwwrootPath)
    {
        var path = Path.Combine(wwwrootPath, "fotos", "alumnes", $"{studentId}.jpg");
        return File.Exists(path) ? FotoUrl(studentId) : null;
    }

    private string? TryFotoUrl(int studentId)
    {
        var wwwroot = config["Examen:WebWwwrootPath"] ?? "";
        return string.IsNullOrEmpty(wwwroot)
            ? FotoUrl(studentId)
            : FotoUrlSiExisteix(studentId, wwwroot);
    }

    private static RegistreConnexioDto ToDto(RegistreConnexio r, int maxDns = 10) =>
        new(r.Id, r.SessioId,
            r.StudentId, r.Student?.Nom, r.Student?.Cognoms,
            r.Student?.Email, r.Student?.NumLlista,
            r.StudentId.HasValue ? FotoUrl(r.StudentId.Value) : null,
            r.MacAddress, r.IpAssignada,
            r.ConnectatAt, r.DesconnectatAt, r.UltimCheckinAt,
            (EstatConnexioDto)(int)r.Estat,
            r.PeticiosDns.OrderByDescending(p => p.Timestamp).Take(maxDns)
                .Select(p => new PeticioTdnsDto(p.Id, p.Domini, p.Timestamp, p.EsExterna))
                .ToList());

    private int CheckinIntervalSegons =>
        int.TryParse(config["Examen:CheckinIntervalSeconds"], out var iv) ? iv : 30;

    private SessioExamenDto ToSessioDto(SessioExamen s, int total = 0, int connectats = 0) =>
        new(s.Id, s.ClassId, s.Class?.Name ?? "", s.ProfessorId, s.Professor?.NomComplet ?? "",
            s.Titol, s.Descripcio, s.MissatgeActiu,
            s.IniciadaAt, s.TancadaAt, s.Activa, total, connectats, CheckinIntervalSegons);

    // ─── Sessions ─────────────────────────────────────────────────────────────

    public async Task<List<SessioExamenDto>> GetSessionsAsync(int professorId, bool isAdmin)
    {
        var q = db.SessionsExamen
            .Include(s => s.Class)
            .Include(s => s.Professor)
            .Include(s => s.Registres)
            .AsQueryable();

        if (!isAdmin)
            q = q.Where(s => s.ProfessorId == professorId);

        var sessions = await q.OrderByDescending(s => s.IniciadaAt).ToListAsync();

        return sessions.Select(s =>
        {
            var connectats = s.Registres.Count(r => r.Estat == EstatConnexio.Connectat || r.Estat == EstatConnexio.SenseCheckin);
            return ToSessioDto(s, s.Registres.Count, connectats);
        }).ToList();
    }

    public async Task<(SessioExamenDto? Sessio, string? Error)> CreateSessioAsync(
        CreateSessioRequest req, int professorId)
    {
        // Restricció: no pot haver-hi dues sessions actives per la mateixa classe
        var existent = await db.SessionsExamen
            .FirstOrDefaultAsync(s => s.ClassId == req.ClassId && s.Activa);
        if (existent is not null)
            return (null, "Ja hi ha una sessió activa per a aquesta classe.");

        var classe = await db.Classes.FindAsync(req.ClassId);
        if (classe is null)
            return (null, "Classe no trobada.");

        var sessio = new SessioExamen
        {
            ClassId     = req.ClassId,
            ProfessorId = professorId,
            Titol       = req.Titol?.Trim(),
            Descripcio  = req.Descripcio?.Trim(),
            IniciadaAt  = DateTime.UtcNow,
            Activa      = true
        };
        db.SessionsExamen.Add(sessio);
        await db.SaveChangesAsync();

        await db.Entry(sessio).Reference(s => s.Class).LoadAsync();
        await db.Entry(sessio).Reference(s => s.Professor).LoadAsync();
        return (ToSessioDto(sessio), null);
    }

    public async Task<ExamenDashboardDto?> GetDashboardAsync(
        int sessioId, int professorId, bool isAdmin)
    {
        var sessio = await db.SessionsExamen
            .Include(s => s.Class)
            .Include(s => s.Professor)
            .Include(s => s.Registres)
                .ThenInclude(r => r.Student)
            .Include(s => s.Registres)
                .ThenInclude(r => r.PeticiosDns)
            .FirstOrDefaultAsync(s => s.Id == sessioId &&
                (isAdmin || s.ProfessorId == professorId));

        if (sessio is null) return null;

        var connectats = sessio.Registres.Count(r =>
            r.Estat is EstatConnexio.Connectat or EstatConnexio.SenseCheckin);

        var sessioDto = ToSessioDto(sessio, sessio.Registres.Count, connectats);

        var alumnes = sessio.Registres
            .OrderBy(r => r.Student?.NumLlista ?? int.MaxValue)
            .ThenBy(r => r.Student?.Cognoms ?? r.MacAddress)
            .Select(r => new ExamenAlumneDto(
                r.StudentId, r.Student?.Nom, r.Student?.Cognoms, r.Student?.Email,
                r.Student?.NumLlista,
                r.StudentId.HasValue ? TryFotoUrl(r.StudentId.Value) : null,
                r.MacAddress, r.IpAssignada,
                r.ConnectatAt, r.UltimCheckinAt, (EstatConnexioDto)(int)r.Estat,
                r.PeticiosDns.OrderByDescending(p => p.Timestamp).Take(15)
                    .Select(p => new PeticioTdnsDto(p.Id, p.Domini, p.Timestamp, p.EsExterna))
                    .ToList()))
            .ToList();

        return new ExamenDashboardDto(sessioDto, alumnes);
    }

    public async Task<(bool Ok, string? Error)> TancarSessioAsync(
        int sessioId, int professorId, bool isAdmin)
    {
        var sessio = await db.SessionsExamen
            .Include(s => s.Registres)
            .FirstOrDefaultAsync(s => s.Id == sessioId &&
                (isAdmin || s.ProfessorId == professorId));
        if (sessio is null) return (false, "Sessió no trobada.");

        sessio.Activa     = false;
        sessio.TancadaAt  = DateTime.UtcNow;

        // Recull els IDs dels alumnes connectats ABANS de desconnectar-los
        var studentsConnectats = sessio.Registres
            .Where(r => r.Estat is EstatConnexio.Connectat or EstatConnexio.SenseCheckin
                     && r.StudentId.HasValue)
            .Select(r => r.StudentId!.Value)
            .Distinct()
            .ToList();

        foreach (var r in sessio.Registres.Where(r =>
            r.Estat is EstatConnexio.Connectat or EstatConnexio.SenseCheckin))
        {
            r.Estat           = EstatConnexio.Desconnectat;
            r.DesconnectatAt  = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();

        // Notifica el professor (canal sessió)
        _ = hub.NotificaSessioTancadaGlobalAsync(sessioId);
        // Notifica cada alumne individualment (canal alumne)
        foreach (var sid in studentsConnectats)
            _ = hub.NotificaSessioTancadaAsync(sid);

        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> ReobrirSessioAsync(
        int sessioId, int professorId, bool isAdmin)
    {
        // Comprova que no hi hagi una altra sessió activa per la mateixa classe
        var sessio = await db.SessionsExamen
            .FirstOrDefaultAsync(s => s.Id == sessioId &&
                (isAdmin || s.ProfessorId == professorId));
        if (sessio is null) return (false, "Sessió no trobada.");

        var conflicte = await db.SessionsExamen
            .AnyAsync(s => s.ClassId == sessio.ClassId && s.Activa && s.Id != sessioId);
        if (conflicte)
            return (false, "Ja hi ha una altra sessió activa per a aquesta classe.");

        sessio.Activa    = true;
        sessio.TancadaAt = null;
        await db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> SetMissatgeAsync(
        int sessioId, string? text, int professorId, bool isAdmin)
    {
        var sessio = await db.SessionsExamen
            .Include(s => s.Registres)
            .FirstOrDefaultAsync(s => s.Id == sessioId &&
                (isAdmin || s.ProfessorId == professorId));
        if (sessio is null) return (false, "Sessió no trobada.");

        sessio.MissatgeActiu = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        await db.SaveChangesAsync();

        if (sessio.MissatgeActiu is not null)
        {
            // Notifica el professor (per reflectir el canvi a la sessió)
            _ = hub.NotificaMissatgeActualitzatSessioAsync(sessioId,
                new ExamenEventMissatge(sessio.MissatgeActiu));

            // Notifica cada alumne connectat individualment
            var studentIds = sessio.Registres
                .Where(r => r.StudentId.HasValue &&
                    r.Estat is EstatConnexio.Connectat or EstatConnexio.SenseCheckin)
                .Select(r => r.StudentId!.Value)
                .Distinct();

            foreach (var sid in studentIds)
                _ = hub.NotificaMissatgeProfessorAsync(sid,
                    new ExamenEventMissatge(sessio.MissatgeActiu));
        }
        return (true, null);
    }

    // ─── Exportació CSV ───────────────────────────────────────────────────────

    public async Task<(byte[] Content, string FileName)?> ExportarCsvAsync(
        int sessioId, int professorId, bool isAdmin)
    {
        var sessio = await db.SessionsExamen
            .Include(s => s.Class)
            .Include(s => s.Registres)
                .ThenInclude(r => r.Student)
            .Include(s => s.Registres)
                .ThenInclude(r => r.PeticiosDns)
            .FirstOrDefaultAsync(s => s.Id == sessioId &&
                (isAdmin || s.ProfessorId == professorId));
        if (sessio is null) return null;

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(";",
            "\"NumLlista\"", "\"Nom\"", "\"Cognoms\"", "\"Email\"",
            "\"MAC\"", "\"IP\"", "\"ConnectatAt\"", "\"UltimCheckin\"",
            "\"Estat\"", "\"DNS_Externes\""));

        foreach (var r in sessio.Registres
            .OrderBy(r => r.Student?.NumLlista ?? int.MaxValue)
            .ThenBy(r => r.Student?.Cognoms ?? r.MacAddress))
        {
            var dns = r.PeticiosDns.Where(p => p.EsExterna)
                .Select(p => p.Domini).Distinct().Take(20);
            sb.AppendLine(string.Join(";",
                r.Student?.NumLlista.ToString() ?? "",
                $"\"{r.Student?.Nom ?? ""}\"",
                $"\"{r.Student?.Cognoms ?? ""}\"",
                $"\"{r.Student?.Email ?? ""}\"",
                $"\"{r.MacAddress}\"",
                $"\"{r.IpAssignada ?? ""}\"",
                $"\"{r.ConnectatAt:dd/MM/yyyy HH:mm:ss}\"",
                $"\"{r.UltimCheckinAt?.ToString("dd/MM/yyyy HH:mm:ss") ?? ""}\"",
                $"\"{r.Estat}\"",
                $"\"{string.Join(" | ", dns)}\""));
        }

        var titol = sessio.Titol?.Replace(" ", "_") ?? "sessio";
        var nom   = $"examen_{titol}_{DateTime.Now:yyyyMMdd}.csv";
        return (Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(), nom);
    }

    // ─── Check-in alumne ──────────────────────────────────────────────────────

    public async Task<(CheckinResponse? Resp, string? Error)> CheckinAsync(CheckinRequest req, string clientIp)
    {
        if (System.Net.IPAddress.TryParse(clientIp, out var ipAddr) && ipAddr.IsIPv4MappedToIPv6)
            clientIp = ipAddr.MapToIPv4().ToString();

        var emailNorm = req.Email.Trim().ToLower();

        var student = await db.Students
            .Include(s => s.Class)
            .Include(s => s.Macs)
            .FirstOrDefaultAsync(s => s.Email.ToLower() == emailNorm);

        if (student is null)
            return (null, "Email no reconegut al sistema.");

        // Busca sessió activa per la classe de l'alumne
        var sessio = await db.SessionsExamen
            .FirstOrDefaultAsync(s => s.ClassId == student.ClassId && s.Activa);

        if (sessio is null)
            return (null, "No hi ha examen actiu per a la teva classe.");

        var ara = DateTime.UtcNow;

        // Busca el registre de connexió per IP (assignada per DHCP) o per alumne ja identificat
        var registre = await db.RegistresConnexio
            .FirstOrDefaultAsync(r => r.SessioId == sessio.Id &&
                (r.StudentId == student.Id ||
                 (!string.IsNullOrEmpty(clientIp) && r.IpAssignada == clientIp)));

        // Comprovació d'una sola estació: sempre comprova si l'alumne té un registre actiu des d'una altra IP
        var altreRegistre = await db.RegistresConnexio
            .FirstOrDefaultAsync(r => r.SessioId == sessio.Id &&
                r.StudentId == student.Id &&
                r.IpAssignada != clientIp &&
                r.Estat != EstatConnexio.Desconnectat);
        if (altreRegistre is not null)
            return (null, $"Ja estàs connectat des d'una altra estació ({altreRegistre.IpAssignada}).");

        string mac = "";
        if (registre is not null)
        {
            // Identifica l'alumne en el registre existent
            registre.StudentId      = student.Id;
            registre.UltimCheckinAt = ara;
            registre.Estat          = EstatConnexio.Connectat;
            mac = registre.MacAddress;

            // Associa MAC a l'alumne si no existia
            if (!string.IsNullOrEmpty(mac) && !student.Macs.Any(m => m.Mac == mac))
            {
                db.AlumneMacs.Add(new AlumneMac
                {
                    StudentId     = student.Id,
                    Mac           = mac,
                    PrimerCopVist = ara
                });
            }
        }
        else
        {
            // Crea un registre manual (sense DHCP conegut)
            registre = new RegistreConnexio
            {
                SessioId       = sessio.Id,
                StudentId      = student.Id,
                MacAddress     = clientIp,   // Sense MAC disponible, usem la IP com a identificador
                IpAssignada    = clientIp,
                ConnectatAt    = ara,
                UltimCheckinAt = ara,
                Estat          = EstatConnexio.Connectat
            };
            db.RegistresConnexio.Add(registre);
        }

        await db.SaveChangesAsync();

        // Notifica el professor
        _ = hub.NotificaNouCheckinAsync(sessio.Id, new ExamenEventAlumne(
            student.Id, student.Nom, student.Cognoms, clientIp, mac, ara));

        return (new CheckinResponse(
            new CheckinAlumneInfo(student.Id, student.Nom, student.Cognoms,
                student.Class.Name, TryFotoUrl(student.Id)),
            new CheckinSessioInfo(sessio.Id, sessio.Titol, sessio.Descripcio,
                sessio.MissatgeActiu, CheckinIntervalSegons)),
            null);
    }

    // ─── Processament DHCP ────────────────────────────────────────────────────

    public async Task ProcessDhcpEventAsync(DhcpEventRequest req)
    {
        var mac = NormalitzaMac(req.Mac);
        var ara = DateTime.UtcNow;

        // Busca sessions actives
        var sessions = await db.SessionsExamen.Where(s => s.Activa).Select(s => s.Id).ToListAsync();
        if (sessions.Count == 0) return;

        var registre = await db.RegistresConnexio
            .Include(r => r.Student)
            .FirstOrDefaultAsync(r => sessions.Contains(r.SessioId) && r.MacAddress == mac);

        if (req.Event == "connected")
        {
            if (registre is not null)
            {
                registre.IpAssignada = req.Ip;
                registre.Estat       = EstatConnexio.Connectat;
                await db.SaveChangesAsync();

                _ = hub.NotificaAlumneConnectatAsync(registre.SessioId, new ExamenEventAlumne(
                    registre.StudentId, registre.Student?.Nom, registre.Student?.Cognoms,
                    req.Ip, mac, ara));
            }
            else
            {
                // MAC desconeguda — registra igualment
                var sessioActiva = await db.SessionsExamen.FirstOrDefaultAsync(s => s.Activa);
                if (sessioActiva is not null)
                {
                    var nouRegistre = new RegistreConnexio
                    {
                        SessioId   = sessioActiva.Id,
                        MacAddress = mac,
                        IpAssignada = req.Ip,
                        ConnectatAt = ara,
                        Estat       = EstatConnexio.Connectat
                    };
                    db.RegistresConnexio.Add(nouRegistre);
                    await db.SaveChangesAsync();

                    _ = hub.NotificaMacDesconegudaAsync(sessioActiva.Id, new ExamenEventAlumne(
                        null, null, null, req.Ip, mac, ara));
                }
            }
        }
        else if (req.Event == "disconnected" && registre is not null)
        {
            registre.Estat          = EstatConnexio.Desconnectat;
            registre.DesconnectatAt = ara;
            await db.SaveChangesAsync();

            _ = hub.NotificaAlumneDesconnectatAsync(registre.SessioId, new ExamenEventAlumne(
                registre.StudentId, registre.Student?.Nom, registre.Student?.Cognoms,
                registre.IpAssignada, mac, ara));
        }
    }

    // ─── Processament DNS ─────────────────────────────────────────────────────

    public async Task ProcessDnsEventAsync(DnsEventRequest req)
    {
        var registre = await db.RegistresConnexio
            .Include(r => r.Student)
            .FirstOrDefaultAsync(r => r.IpAssignada == req.Ip &&
                r.Estat != EstatConnexio.Desconnectat);
        if (registre is null) return;

        var esExterna = !req.Domini.EndsWith(".examen.local",
            StringComparison.OrdinalIgnoreCase) && req.Domini != "examen.local";

        db.PeticiosDns.Add(new PeticioTdns
        {
            RegistreId = registre.Id,
            Domini     = req.Domini,
            Timestamp  = req.Timestamp,
            EsExterna  = esExterna
        });
        await db.SaveChangesAsync();

        if (esExterna)
        {
            _ = hub.NotificaNovaPeticioExternaAsync(registre.SessioId, new ExamenEventDns(
                registre.StudentId, registre.Student?.Nom, req.Domini, req.Timestamp));
        }
    }

    // ─── Eliminació de sessió tancada ────────────────────────────────────────

    public async Task<(bool Ok, string? Error)> EliminarSessioAsync(
        int sessioId, int professorId, bool isAdmin)
    {
        var sessio = await db.SessionsExamen
            .Include(s => s.Registres)
                .ThenInclude(r => r.PeticiosDns)
            .FirstOrDefaultAsync(s => s.Id == sessioId &&
                (isAdmin || s.ProfessorId == professorId));
        if (sessio is null) return (false, "Sessió no trobada.");
        if (sessio.Activa) return (false, "No es pot eliminar una sessió activa. Tanca-la primer.");

        db.SessionsExamen.Remove(sessio);
        await db.SaveChangesAsync();
        return (true, null);
    }

    // ─── Sortida voluntària de l'alumne ──────────────────────────────────────

    public async Task<(bool Ok, string? Error)> SortirAsync(string clientIp)
    {
        if (System.Net.IPAddress.TryParse(clientIp, out var ipAddr) && ipAddr.IsIPv4MappedToIPv6)
            clientIp = ipAddr.MapToIPv4().ToString();

        var registre = await db.RegistresConnexio
            .Include(r => r.Student)
            .FirstOrDefaultAsync(r => r.IpAssignada == clientIp &&
                r.Estat != EstatConnexio.Desconnectat);

        if (registre is null)
            return (false, "No s'ha trobat cap connexió activa per a aquesta IP.");

        registre.Estat          = EstatConnexio.Desconnectat;
        registre.DesconnectatAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        _ = hub.NotificaSortidaVoluntariaAsync(registre.SessioId, new ExamenEventAlumne(
            registre.StudentId, registre.Student?.Nom, registre.Student?.Cognoms,
            clientIp, registre.MacAddress, registre.DesconnectatAt.Value));

        return (true, null);
    }

    // ─── Expulsió d'alumne pel professor ──────────────────────────────────────

    public async Task<(bool Ok, string? Error)> ExpulsarAsync(
        int sessioId, int studentId, int professorId, bool isAdmin)
    {
        var sessio = await db.SessionsExamen
            .FirstOrDefaultAsync(s => s.Id == sessioId &&
                (isAdmin || s.ProfessorId == professorId));
        if (sessio is null) return (false, "Sessió no trobada.");

        var registre = await db.RegistresConnexio
            .Include(r => r.Student)
            .FirstOrDefaultAsync(r => r.SessioId == sessioId &&
                r.StudentId == studentId &&
                r.Estat != EstatConnexio.Desconnectat);
        if (registre is null) return (false, "L'alumne no està connectat.");

        registre.Estat          = EstatConnexio.Desconnectat;
        registre.DesconnectatAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Notifica l'alumne individualment (per mostrar missatge d'expulsió)
        _ = hub.NotificaAlumneExpulsatAsync(studentId);

        // Notifica el professor del desconnectat
        _ = hub.NotificaAlumneDesconnectatAsync(sessioId, new ExamenEventAlumne(
            registre.StudentId, registre.Student?.Nom, registre.Student?.Cognoms,
            registre.IpAssignada, registre.MacAddress, registre.DesconnectatAt.Value));

        return (true, null);
    }

    // ─── Gestió de MACs ──────────────────────────────────────────────────────

    public async Task<List<AlumneMacDto>> GetMacsAsync(bool isAdmin)
    {
        if (!isAdmin) return [];

        return await db.AlumneMacs
            .Include(m => m.Student)
                .ThenInclude(s => s.Class)
            .OrderBy(m => m.Student != null ? m.Student.Class!.Name : "")
            .ThenBy(m => m.Student != null ? m.Student.Cognoms : "")
            .ThenBy(m => m.PrimerCopVist)
            .Select(m => new AlumneMacDto(
                m.Id, m.StudentId,
                m.Student != null ? m.Student.Nom : "—",
                m.Student != null ? m.Student.Cognoms : "—",
                m.Student != null ? m.Student.Email : "—",
                m.Student != null && m.Student.Class != null ? m.Student.Class.Name : "—",
                m.Mac, m.Dispositiu, m.PrimerCopVist,
                m.StudentId > 0 ? TryFotoUrl(m.StudentId) : null))
            .ToListAsync();
    }

    public async Task<bool> DeleteMacAsync(int id, bool isAdmin)
    {
        if (!isAdmin) return false;
        var mac = await db.AlumneMacs.FindAsync(id);
        if (mac is null) return false;
        db.AlumneMacs.Remove(mac);
        await db.SaveChangesAsync();
        return true;
    }

    // ─── Importació alumnes (HTML disfressat de XLS) ──────────────────────────

    public async Task<(ImportacioAlumnesResult Result, string? Error)> ImportarAlumnesAsync(
        Stream htmlStream, int professorId, bool isAdmin)
    {
        if (!isAdmin) return (new ImportacioAlumnesResult(0, 0, 0, []), "Sense permisos.");

        string htmlContent;
        using (var reader = new StreamReader(htmlStream, Encoding.GetEncoding(1252)))
            htmlContent = await reader.ReadToEndAsync();

        // Extrau nom de classe del títol o de la primera fila significativa
        var nomClasse = ExtrauNomClasse(htmlContent);
        if (string.IsNullOrWhiteSpace(nomClasse))
            return (new ImportacioAlumnesResult(0, 0, 0, ["No s'ha pogut detectar el nom de la classe."]), null);

        var classe = await db.Classes.FirstOrDefaultAsync(c => c.Name == nomClasse);
        if (classe is null)
        {
            classe = new Class { Name = nomClasse };
            db.Classes.Add(classe);
            await db.SaveChangesAsync();
        }

        var files = ParseFilesHtml(htmlContent);
        var importats = 0; var actualitzats = 0; var saltats = 0;
        var errors = new List<string>();

        foreach (var fila in files)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(fila.Baixa)) { saltats++; continue; }
                if (string.IsNullOrWhiteSpace(fila.Nom) && string.IsNullOrWhiteSpace(fila.Cognoms))
                    { saltats++; continue; }

                var email = string.IsNullOrWhiteSpace(fila.Email)
                    ? GeneraEmail(fila.Nom, fila.Cognoms)
                    : fila.Email.Trim().ToLower();

                var student = await db.Students.FirstOrDefaultAsync(s => s.Email == email);
                if (student is null)
                {
                    student = new Student
                    {
                        ClassId      = classe.Id,
                        Nom          = fila.Nom.Trim(),
                        Cognoms      = fila.Cognoms.Trim(),
                        Email        = email,
                        NumLlista    = fila.NumLlista,
                        Dni          = fila.Dni?.Trim(),
                        PasswordHash = null
                    };
                    db.Students.Add(student);
                    importats++;
                }
                else
                {
                    student.Nom      = fila.Nom.Trim();
                    student.Cognoms  = fila.Cognoms.Trim();
                    student.NumLlista = fila.NumLlista;
                    if (!string.IsNullOrWhiteSpace(fila.Dni))
                        student.Dni = fila.Dni.Trim();
                    actualitzats++;
                }
            }
            catch (Exception ex) { errors.Add($"Fila {fila.NumLlista}: {ex.Message}"); }
        }

        await db.SaveChangesAsync();
        return (new ImportacioAlumnesResult(importats, actualitzats, saltats, errors), null);
    }

    private static string? ExtrauNomClasse(string html)
    {
        // Cerca patrons com "S1SX (2025)" o "ASIX1 2025"
        var m = Regex.Match(html, @"([A-Z][A-Z0-9]{2,8})\s*\(?\d{4}\)?",
            RegexOptions.IgnoreCase);
        return m.Success ? m.Value.Trim() : null;
    }

    private static List<FilaAlumne> ParseFilesHtml(string html)
    {
        var result = new List<FilaAlumne>();
        // Cerca files de taula <tr>
        var rowMatches = Regex.Matches(html, @"<tr[^>]*>(.*?)</tr>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        foreach (Match row in rowMatches)
        {
            var cells = Regex.Matches(row.Groups[1].Value, @"<t[dh][^>]*>(.*?)</t[dh]>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase)
                .Select(m => StripHtml(m.Groups[1].Value).Trim())
                .ToList();

            if (cells.Count < 4) continue;
            if (!int.TryParse(cells[0], out var num)) continue; // capçalera

            result.Add(new FilaAlumne(
                num,
                cells.ElementAtOrDefault(1) ?? "",  // Cognoms
                cells.ElementAtOrDefault(2) ?? "",  // Nom
                cells.ElementAtOrDefault(3),        // DNI
                cells.ElementAtOrDefault(4),        // Baixa
                cells.ElementAtOrDefault(5)));      // Email
        }
        return result;
    }

    private static string StripHtml(string html) =>
        Regex.Replace(html, "<[^>]+>", "").Trim();

    private static string GeneraEmail(string nom, string cognoms)
    {
        var n = Normalitza(nom.Split(' ')[0]);
        var c = Normalitza(cognoms.Split(' ')[0]);
        return $"{n}.{c}@sarria.salesians.cat";
    }

    private static readonly char[] _accentFrom = "àáâãäåèéêëìíîïòóôõöùúûüçñÀÁÂÃÄÅÈÉÊËÌÍÎÏÒÓÔÕÖÙÚÛÜÇÑ".ToCharArray();
    private static readonly char[] _accentTo   = "aaaaaaeeeeiiiioooooauuuucnAAAAAAEEEEIIIIOOOOOUUUUCN".ToCharArray();

    private static string Normalitza(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s.ToLower())
        {
            var idx = Array.IndexOf(_accentFrom, c);
            sb.Append(idx >= 0 ? _accentTo[idx] : c);
        }
        return Regex.Replace(sb.ToString(), "[^a-z0-9]", "");
    }

    private sealed record FilaAlumne(
        int NumLlista, string Cognoms, string Nom, string? Dni, string? Baixa, string? Email);

    // ─── Importació alumnes (XLS natiu EPSS) ─────────────────────────────────

    public async Task<(ImportacioAlumnesResult Result, string? Error)> ImportarAlumnesXlsAsync(
        Stream xlsStream, int classId, bool isAdmin)
    {
        if (!isAdmin) return (new ImportacioAlumnesResult(0, 0, 0, []), "Sense permisos.");

        var classe = await db.Classes.FindAsync(classId);
        if (classe is null) return (new ImportacioAlumnesResult(0, 0, 0, []), "Classe no trobada.");

        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        // Copia a memòria per poder rellegir si cal
        using var ms = new MemoryStream();
        await xlsStream.CopyToAsync(ms);
        ms.Position = 0;

        List<List<string>> files;
        try
        {
            using var reader = ExcelReaderFactory.CreateReader(ms, new ExcelReaderConfiguration
                { FallbackEncoding = System.Text.Encoding.GetEncoding(1252) });
            var ds = reader.AsDataSet(new ExcelDataSetConfiguration
                { ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = false } });
            if (ds.Tables.Count == 0) return (new ImportacioAlumnesResult(0, 0, 0, ["Fitxer buit."]), null);
            var sheet = ds.Tables[0];
            files = [];
            for (int r = 0; r < sheet.Rows.Count; r++)
            {
                var cols = new List<string>();
                for (int c = 0; c < sheet.Columns.Count; c++)
                    cols.Add(sheet.Rows[r][c]?.ToString()?.Trim() ?? "");
                files.Add(cols);
            }
        }
        catch (ExcelDataReader.Exceptions.HeaderException)
        {
            // Fallback: EPSS exporta HTML amb extensió .xls
            ms.Position = 0;
            var html = await new StreamReader(ms, System.Text.Encoding.GetEncoding(1252)).ReadToEndAsync();
            files = ParseHtmlTable(html);
            if (files.Count == 0)
                return (new ImportacioAlumnesResult(0, 0, 0,
                    ["Format no reconegut. Cal XLS/XLSX o HTML exportat d'EPSS."]), null);
        }

        // Detecta si el fitxer té capçalera (fila 0 = nom classe, 1 = buida, 2 = headers, 3+ = dades)
        // o bé comença directament per les capçaleres (fila 0 = headers, 1+ = dades)
        int dataStart = 3;
        if (files.Count > 0 && files[0].Count > 0)
        {
            var primeraCel = files[0][0];
            if (int.TryParse(primeraCel, out _) || primeraCel.Equals("Num.", StringComparison.OrdinalIgnoreCase))
                dataStart = 1; // comença per la fila de dades / capçaleres → salt d'1
        }

        if (files.Count <= dataStart)
            return (new ImportacioAlumnesResult(0, 0, 0, ["Fitxer sense dades d'alumnes."]), null);

        var importats = 0; var actualitzats = 0; var saltats = 0;
        var errors = new List<string>();

        for (int r = dataStart; r < files.Count; r++)
        {
            var row = files[r];
            if (row.Count < 6) { saltats++; continue; }

            var numStr = row[0];
            if (!int.TryParse(numStr, out var num)) { saltats++; continue; }

            var cognoms = row[1];
            var nom     = row[2];
            var dni     = row.Count > 3 ? row[3] : "";
            var baixa   = row[5]; // col 5: Baixa
            var email   = row.Count > 9 ? row[9].ToLower() : "";

            if (!string.IsNullOrWhiteSpace(baixa)) { saltats++; continue; }
            if (string.IsNullOrWhiteSpace(nom) && string.IsNullOrWhiteSpace(cognoms)) { saltats++; continue; }

            if (string.IsNullOrWhiteSpace(email))
                email = GeneraEmail(nom, cognoms);

            try
            {
                var student = await db.Students.FirstOrDefaultAsync(s => s.Email == email);
                if (student is null)
                {
                    db.Students.Add(new Student
                    {
                        ClassId      = classe.Id,
                        Nom          = nom,
                        Cognoms      = cognoms,
                        Email        = email,
                        NumLlista    = num,
                        Dni          = string.IsNullOrWhiteSpace(dni) ? null : dni,
                        PasswordHash = null
                    });
                    importats++;
                }
                else
                {
                    student.Nom       = nom;
                    student.Cognoms   = cognoms;
                    student.NumLlista = num;
                    if (!string.IsNullOrWhiteSpace(dni)) student.Dni = dni;
                    actualitzats++;
                }
            }
            catch (Exception ex) { errors.Add($"Fila {num}: {ex.Message}"); }
        }

        await db.SaveChangesAsync();
        return (new ImportacioAlumnesResult(importats, actualitzats, saltats, errors), null);
    }

    private static List<List<string>> ParseHtmlTable(string html)
    {
        var rows = new List<List<string>>();
        var rowMatches = Regex.Matches(html, @"<tr\b[^>]*>(.*?)</tr>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        foreach (Match rowMatch in rowMatches)
        {
            var cols = new List<string>();
            var cellMatches = Regex.Matches(rowMatch.Groups[1].Value,
                @"<t[dh]\b[^>]*>(.*?)</t[dh]>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            foreach (Match cellMatch in cellMatches)
            {
                var text = Regex.Replace(cellMatch.Groups[1].Value, @"<[^>]+>", "").Trim();
                cols.Add(System.Net.WebUtility.HtmlDecode(text));
            }
            if (cols.Count > 0) rows.Add(cols);
        }
        return rows;
    }

    // ─── Foto manual per alumne ───────────────────────────────────────────────

    public async Task<bool> UploadStudentFotoAsync(int studentId, Stream foto, string wwwrootPath)
    {
        var destDir = Path.Combine(wwwrootPath, "fotos", "alumnes");
        Directory.CreateDirectory(destDir);
        var dest = Path.Combine(destDir, $"{studentId}.jpg");
        using var fs = File.Create(dest);
        await foto.CopyToAsync(fs);
        return true;
    }

    // ─── Importació fotos ─────────────────────────────────────────────────────

    public async Task<(ImportacioFotosResult Result, string? Error)> ImportarFotosAsync(
        Stream zipStream, string wwwrootPath)
    {
        var destDir = Path.Combine(wwwrootPath, "fotos", "alumnes");
        Directory.CreateDirectory(destDir);

        var importades = 0;
        var noTrobades = new List<string>();
        var errors = new List<string>();

        // Carrega tots els DNIs a memòria per evitar Regex en LINQ (no traduïble a SQL)
        var studentsWithDni = await db.Students
            .Where(s => s.Dni != null)
            .Select(s => new { s.Id, Dni = s.Dni! })
            .ToListAsync();

        try
        {
            using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);
            foreach (var entry in zip.Entries.Where(e =>
                e.Name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                e.Name.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)))
            {
                // Nom del fitxer = DNI sencer o part numèrica (format EPSS: sense lletra)
                var dni    = Path.GetFileNameWithoutExtension(entry.Name).Trim().ToUpper();
                var dniNum = Regex.Replace(dni, @"[^0-9]", "");
                var match  = studentsWithDni.FirstOrDefault(s =>
                    s.Dni.ToUpper() == dni ||
                    Regex.Replace(s.Dni, @"[^0-9]", "") == dniNum);

                if (match is null)
                {
                    noTrobades.Add(entry.Name);
                    continue;
                }

                try
                {
                    var dest = Path.Combine(destDir, $"{match.Id}.jpg");
                    using var entryStream = entry.Open();
                    using var fs = File.Create(dest);
                    await entryStream.CopyToAsync(fs);
                    importades++;
                }
                catch (Exception ex) { errors.Add($"{entry.Name}: {ex.Message}"); }
            }
        }
        catch (Exception ex)
        {
            return (new ImportacioFotosResult(0, [], [ex.Message]), null);
        }

        return (new ImportacioFotosResult(importades, noTrobades, errors), null);
    }
}
