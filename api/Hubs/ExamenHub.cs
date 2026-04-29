using EntornExamen.Shared.DTOs;
using StackExchange.Redis;
using System.Text.Json;

namespace EntornExamen.Api.Hubs;

/// <summary>
/// Publicador d'events de l'Entorn Examen cap a Redis.
/// El web (Blazor) subscriu via ExamenRedisSubscriber i notifica components en temps real.
/// Channels: examen:sessio:{id} per al professor, examen:alumne:{id} per a l'alumne.
/// </summary>
public class ExamenHub(IConnectionMultiplexer redis)
{
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // ── Helpers de canal ───────────────────────────────────────────────────────
    public static string CanalSessio(int sessioId)   => $"examen:sessio:{sessioId}";
    public static string CanalAlumne(int studentId)  => $"examen:alumne:{studentId}";

    // ── Publicació cap al professor ────────────────────────────────────────────

    public Task NotificaAlumneConnectatAsync(int sessioId, ExamenEventAlumne evt) =>
        PublicarAsync(CanalSessio(sessioId), "AlumneConnectat", evt);

    public Task NotificaAlumneDesconnectatAsync(int sessioId, ExamenEventAlumne evt) =>
        PublicarAsync(CanalSessio(sessioId), "AlumneDesconnectat", evt);

    public Task NotificaNouCheckinAsync(int sessioId, ExamenEventAlumne evt) =>
        PublicarAsync(CanalSessio(sessioId), "NouCheckin", evt);

    public Task NotificaNovaPeticioExternaAsync(int sessioId, ExamenEventDns evt) =>
        PublicarAsync(CanalSessio(sessioId), "NovaPeticioExterna", evt);

    public Task NotificaMacDesconegudaAsync(int sessioId, ExamenEventAlumne evt) =>
        PublicarAsync(CanalSessio(sessioId), "MacDesconeguda", evt);

    public Task NotificaMissatgeActualitzatSessioAsync(int sessioId, ExamenEventMissatge evt) =>
        PublicarAsync(CanalSessio(sessioId), "MissatgeActualitzat", evt);

    // ── Publicació cap a l'alumne ──────────────────────────────────────────────

    public Task NotificaMissatgeProfessorAsync(int studentId, ExamenEventMissatge evt) =>
        PublicarAsync(CanalAlumne(studentId), "MissatgeProfessor", evt);

    public Task NotificaSessioTancadaAsync(int studentId) =>
        PublicarAsync(CanalAlumne(studentId), "SessioTancadaGlobal", new { });

    public Task NotificaAlumneExpulsatAsync(int studentId) =>
        PublicarAsync(CanalAlumne(studentId), "AlumneExpulsat", new { });

    public Task NotificaRecursosActualitzatsAsync(int studentId, List<RecursExamenDto> recursos) =>
        PublicarAsync(CanalAlumne(studentId), "RecursosActualitzats", recursos);

    // ── Publicació a tots els alumnes d'una sessió (via canal sessió) ──────────
    public Task NotificaSessioTancadaGlobalAsync(int sessioId) =>
        PublicarAsync(CanalSessio(sessioId), "SessioTancadaGlobal", new { });

    public Task NotificaSortidaVoluntariaAsync(int sessioId, ExamenEventAlumne evt) =>
        PublicarAsync(CanalSessio(sessioId), "AlumneDesconnectatVoluntari", evt);

    // ── Infraestructura ────────────────────────────────────────────────────────

    private async Task PublicarAsync<T>(string canal, string eventNom, T payload)
    {
        if (redis is null) return;
        try
        {
            var envelope = new { Event = eventNom, Data = payload };
            var json = JsonSerializer.Serialize(envelope, _json);
            var pub = redis.GetSubscriber();
            await pub.PublishAsync(RedisChannel.Literal(canal), json);
        }
        catch
        {
            // Redis no disponible → s'ignora; el sistema degrada sense caure
        }
    }
}
