using EntornExamen.Shared.DTOs;
using StackExchange.Redis;
using System.Text.Json;

namespace EntornExamen.Web.Services;

/// <summary>
/// Subscriu canals Redis de l'Entorn Examen i delega les notificacions
/// a ExamenNotificationService perquè arribin als components Blazor.
/// Channels: examen:sessio:{id} (professor) i examen:alumne:{id} (alumne).
/// </summary>
public class ExamenRedisSubscriber(
    IConnectionMultiplexer redis,
    ExamenNotificationService notif,
    ILogger<ExamenRedisSubscriber> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true
    };

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var sub = redis.GetSubscriber();

        // Subscripcions genèriques per a tots els canals examen:sessio:* i examen:alumne:*
        await sub.SubscribeAsync(
            RedisChannel.Pattern("examen:sessio:*"),
            (channel, message) => ProcessarMissatgeAsync(channel, message, esSessio: true));

        await sub.SubscribeAsync(
            RedisChannel.Pattern("examen:alumne:*"),
            (channel, message) => ProcessarMissatgeAsync(channel, message, esSessio: false));

        logger.LogInformation("ExamenRedisSubscriber actiu — escoltant canals Redis");

        try { await Task.Delay(Timeout.Infinite, ct); }
        catch (OperationCanceledException) { }
    }

    private void ProcessarMissatgeAsync(RedisChannel channel, RedisValue message, bool esSessio)
    {
        try
        {
            var nomCanal = channel.ToString();
            // Extrau l'ID del canal: "examen:sessio:{id}" o "examen:alumne:{id}"
            var parts = nomCanal.Split(':');
            if (parts.Length < 3 || !int.TryParse(parts[2], out var id)) return;

            if (string.IsNullOrEmpty(message)) return;

            using var doc = JsonDocument.Parse(message.ToString());
            var root = doc.RootElement;

            var eventNom = root.TryGetProperty("Event", out var ev) ? ev.GetString() ?? "" : "";
            var dataJson = root.TryGetProperty("Data", out var d) ? d.GetRawText() : "{}";

            // Deserialitza el payload concret
            object data = eventNom switch
            {
                "AlumneConnectat" or "AlumneDesconnectat" or "AlumneDesconnectatVoluntari"
                    or "NouCheckin" or "MacDesconeguda" =>
                    JsonSerializer.Deserialize<ExamenEventAlumne>(dataJson, _json)!,
                "NovaPeticioExterna" =>
                    JsonSerializer.Deserialize<ExamenEventDns>(dataJson, _json)!,
                "MissatgeActualitzat" or "MissatgeProfessor" =>
                    JsonSerializer.Deserialize<ExamenEventMissatge>(dataJson, _json)!,
                _ => new object()
            };

            if (esSessio)
                _ = notif.NotificaSessioAsync(id, eventNom, data);
            else
                _ = notif.NotificaAlumneAsync(id, eventNom, data);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error processant missatge Redis de l'Entorn Examen");
        }
    }
}
