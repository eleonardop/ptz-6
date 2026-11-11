using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PtzStudio.Core.Video;

/// <summary>Um endereço RTSP que a câmera respondeu. Status 200 = abre sem senha; 401 = responde, mas pede usuário e senha.</summary>
public sealed record RtspCandidate(string Url, int Status);

/// <summary>
/// Alternativa quando a câmera não tem ONVIF: envia DESCRIBE para os caminhos RTSP mais comuns e informa quais responderam.
/// Não usa senha: caminhos que só pedem login aparecem com status 401 e não dá para ter certeza de que existem.
/// </summary>
public static class RtspProbe
{
    public static readonly string[] CommonPaths =
    {
        "/1", "/11", "/12", "/stream1", "/stream2", "/live/ch0", "/live/ch1", "/ch0", "/ch1", "/av0_0", "/av0_1",
        "/h264", "/Streaming/Channels/101", "/Streaming/Channels/102",
        "/cam/realmonitor?channel=1&subtype=0", "/cam/realmonitor?channel=1&subtype=1", "/profile1", "/media/video1"
    };

    public static async Task<List<RtspCandidate>> ProbeAsync(string ip, int port = 554, int timeoutMs = 2000,
        IEnumerable<string>? paths = null, CancellationToken ct = default)
    {
        if (!IPAddress.TryParse((ip ?? "").Trim(), out var addr))
            throw new ArgumentException($"Endereço IP inválido: \"{ip}\".");
        var found = new List<RtspCandidate>();
        bool first = true;
        foreach (var path in paths ?? CommonPaths)
        {
            ct.ThrowIfCancellationRequested();
            int status = await DescribeAsync(addr, port, path, timeoutMs, ct).ConfigureAwait(false);
            if (status < 0 && first) break;      // a porta nem abre: não adianta testar os outros caminhos
            first = false;
            if (status == 200 || status == 401)
                found.Add(new RtspCandidate(port == 554 ? $"rtsp://{addr}{path}" : $"rtsp://{addr}:{port}{path}", status));
        }
        return found;
    }

    /// <summary>Devolve o código RTSP da resposta (200, 401, 404...), 0 se a resposta não parece RTSP, ou -1 se não houve conexão.</summary>
    private static async Task<int> DescribeAsync(IPAddress addr, int port, string path, int timeoutMs, CancellationToken ct)
    {
        try
        {
            using var tcp = new TcpClient(addr.AddressFamily);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Math.Max(300, timeoutMs));
            await tcp.ConnectAsync(addr, port, cts.Token).ConfigureAwait(false);
            var stream = tcp.GetStream();
            string url = $"rtsp://{addr}:{port}{path}";
            var req = Encoding.ASCII.GetBytes($"DESCRIBE {url} RTSP/1.0\r\nCSeq: 1\r\nAccept: application/sdp\r\nUser-Agent: PTZStudio\r\n\r\n");
            await stream.WriteAsync(req, 0, req.Length, cts.Token).ConfigureAwait(false);
            var buf = new byte[512];
            int n = await stream.ReadAsync(buf, 0, buf.Length, cts.Token).ConfigureAwait(false);
            var parts = Encoding.ASCII.GetString(buf, 0, n).Split(' ', 3);
            if (parts.Length >= 2 && parts[0].StartsWith("RTSP/", StringComparison.Ordinal) && int.TryParse(parts[1], out int code)) return code;
            return 0;
        }
        catch { return -1; }
    }
}
