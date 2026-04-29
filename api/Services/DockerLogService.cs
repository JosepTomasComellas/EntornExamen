using EntornExamen.Shared.DTOs;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace EntornExamen.Api.Services;

public interface IDockerLogService
{
    Task<DockerLogsResponse> GetLogsAsync(string container, int lines = 200);
    bool IsAvailable();
}

public class DockerLogService(IConfiguration config, ILogger<DockerLogService> logger) : IDockerLogService
{
    private const string SocketPath = "/var/run/docker.sock";

    private string[] AllowedContainers =>
        config["Docker:AllowedContainers"]?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        ?? ["entornexamen-api", "entornexamen-web", "entornexamen-nginx", "entornexamen-db", "entornexamen-redis"];

    public bool IsAvailable() => File.Exists(SocketPath);

    public async Task<DockerLogsResponse> GetLogsAsync(string container, int lines = 200)
    {
        if (!AllowedContainers.Contains(container))
            return new DockerLogsResponse(container, [], false, "Contenidor no permès.");

        if (!IsAvailable())
            return new DockerLogsResponse(container, [], false,
                "Docker socket no disponible. Afegeix /var/run/docker.sock al servei 'api' del docker-compose.");

        lines = Math.Clamp(lines, 10, 1000);

        try
        {
            using var http = CreateDockerClient();
            var url = $"http://localhost/containers/{container}/logs?stdout=true&stderr=true&tail={lines}&timestamps=true";
            var resp = await http.GetAsync(url);

            if (!resp.IsSuccessStatusCode)
                return new DockerLogsResponse(container, [], false,
                    $"Error Docker API: {resp.StatusCode}. El contenidor pot no estar en marxa.");

            await using var stream = await resp.Content.ReadAsStreamAsync();
            var lines_ = await ParseMultiplexedStreamAsync(stream);
            return new DockerLogsResponse(container, lines_, true, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error llegint logs Docker de {Container}", container);
            return new DockerLogsResponse(container, [], false, ex.Message);
        }
    }

    private static HttpClient CreateDockerClient()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (ctx, ct) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(SocketPath), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
        };
        return new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
    }

    // Docker usa un format multiplexat: 8 bytes capçalera + N bytes contingut
    private static async Task<List<DockerLogLineDto>> ParseMultiplexedStreamAsync(Stream stream)
    {
        var result = new List<DockerLogLineDto>();
        var header = new byte[8];

        while (true)
        {
            var read = await ReadExactAsync(stream, header, 8);
            if (read < 8) break;

            var streamType = header[0]; // 1=stdout, 2=stderr
            var frameSize  = (int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4));
            if (frameSize == 0) continue;

            var buf  = new byte[frameSize];
            var nRead = await ReadExactAsync(stream, buf, frameSize);
            if (nRead <= 0) break;

            var text = Encoding.UTF8.GetString(buf, 0, nRead);
            foreach (var raw in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var (ts, msg) = SplitTimestamp(raw);
                result.Add(new DockerLogLineDto(streamType == 2 ? "stderr" : "stdout", ts, msg));
            }
        }

        return result;
    }

    // Format Docker: "2024-01-15T10:30:00.000000000Z missatge"
    private static (string Timestamp, string Message) SplitTimestamp(string line)
    {
        var sp = line.IndexOf(' ');
        if (sp > 0 && sp < 40 && line[..sp].Contains('T'))
            return (line[..sp], line[(sp + 1)..]);
        return ("", line);
    }

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buf, int count)
    {
        int total = 0;
        while (total < count)
        {
            var n = await stream.ReadAsync(buf.AsMemory(total, count - total));
            if (n == 0) break;
            total += n;
        }
        return total;
    }
}
