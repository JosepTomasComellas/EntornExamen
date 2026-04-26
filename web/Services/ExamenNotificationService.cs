using AutoCo.Shared.DTOs;
using System.Collections.Concurrent;

namespace AutoCo.Web.Services;

/// <summary>
/// Bus intern de notificacions de l'Entorn Examen.
/// Components Blazor hi subscriuen handlers; ExamenRedisSubscriber els dispara
/// quan arriben missatges Redis del publicador de l'API.
/// </summary>
public class ExamenNotificationService
{
    private readonly object _lock = new();

    // Canal sessioId → handlers del plafó professor
    private readonly ConcurrentDictionary<int, List<Func<string, object, Task>>> _sessioHandlers = new();

    // Canal studentId → handlers del portal alumne
    private readonly ConcurrentDictionary<int, List<Func<string, object, Task>>> _alumneHandlers = new();

    // ── Subscripció professor ─────────────────────────────────────────────────

    public void SubscriuSessio(int sessioId, Func<string, object, Task> handler)
    {
        lock (_lock)
        {
            var list = _sessioHandlers.GetOrAdd(sessioId, _ => []);
            list.Add(handler);
        }
    }

    public void DesubscriuSessio(int sessioId, Func<string, object, Task> handler)
    {
        lock (_lock)
        {
            if (!_sessioHandlers.TryGetValue(sessioId, out var list)) return;
            list.Remove(handler);
            if (list.Count == 0) _sessioHandlers.TryRemove(sessioId, out _);
        }
    }

    // ── Subscripció alumne ────────────────────────────────────────────────────

    public void SubscriuAlumne(int studentId, Func<string, object, Task> handler)
    {
        lock (_lock)
        {
            var list = _alumneHandlers.GetOrAdd(studentId, _ => []);
            list.Add(handler);
        }
    }

    public void DesubscriuAlumne(int studentId, Func<string, object, Task> handler)
    {
        lock (_lock)
        {
            if (!_alumneHandlers.TryGetValue(studentId, out var list)) return;
            list.Remove(handler);
            if (list.Count == 0) _alumneHandlers.TryRemove(studentId, out _);
        }
    }

    // ── Notificació (cridat per ExamenRedisSubscriber) ─────────────────────────

    public Task NotificaSessioAsync(int sessioId, string eventNom, object data) =>
        NotificarAsync(_sessioHandlers, sessioId, eventNom, data);

    public Task NotificaAlumneAsync(int studentId, string eventNom, object data) =>
        NotificarAsync(_alumneHandlers, studentId, eventNom, data);

    private async Task NotificarAsync(
        ConcurrentDictionary<int, List<Func<string, object, Task>>> dict,
        int clau, string eventNom, object data)
    {
        List<Func<string, object, Task>> snapshot;
        lock (_lock)
        {
            if (!dict.TryGetValue(clau, out var list) || list.Count == 0) return;
            snapshot = [.. list];
        }
        var tasks = snapshot.Select(h => InvokeSafe(h, eventNom, data));
        await Task.WhenAll(tasks);
    }

    private static async Task InvokeSafe(Func<string, object, Task> h, string evt, object data)
    {
        try { await h(evt, data); }
        catch { /* el circuit Blazor pot haver tancat */ }
    }
}
