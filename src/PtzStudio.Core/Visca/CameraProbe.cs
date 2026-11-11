using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using PtzStudio.Core.Models;

namespace PtzStudio.Core.Visca;

/// <summary>Resultado do teste de uma combinação de porta e protocolo.</summary>
public sealed record ProbeResult(int Port, ViscaTransportMode Mode, bool Responded, double Milliseconds);

/// <summary>
/// "Descobrir porta e protocolo": envia a consulta de versão VISCA em cada combinação conhecida (porta × UDP puro / VISCA over IP)
/// e informa quais a câmera respondeu. Não depende de manual: só do que a câmera realmente responde.
/// </summary>
public static class CameraProbe
{
    /// <summary>1259: comum em câmeras genéricas. 52381: padrão do VISCA over IP.</summary>
    public static readonly int[] CommonPorts = { 1259, 52381 };

    public static async Task<List<ProbeResult>> ProbeAsync(string ip, int viscaAddress, int timeoutMs,
        IEnumerable<int>? ports = null, CancellationToken ct = default)
    {
        if (!IPAddress.TryParse((ip ?? "").Trim(), out var addr))
            throw new ArgumentException($"Endereço IP inválido: \"{ip}\".");
        var tasks = new List<Task<ProbeResult>>();
        foreach (int port in (ports ?? CommonPorts).Distinct())
            foreach (var mode in new[] { ViscaTransportMode.UdpPuro, ViscaTransportMode.ViscaOverIp })
                tasks.Add(ProbeOne(addr, port, mode, viscaAddress, timeoutMs, ct));
        return (await Task.WhenAll(tasks).ConfigureAwait(false)).ToList();
    }

    private static async Task<ProbeResult> ProbeOne(IPAddress addr, int port, ViscaTransportMode mode, int viscaAddress, int timeoutMs, CancellationToken ct)
    {
        try
        {
            using var udp = new UdpClient(addr.AddressFamily);
            try { udp.Client.IOControl(-1744830452, new byte[4], null); } catch { }
            udp.Connect(addr, port);
            var payload = ViscaCommands.VersionInquiry(viscaAddress);
            byte[] data = mode == ViscaTransportMode.ViscaOverIp ? ViscaFraming.Wrap(payload, 0, ViscaPayloadType.Inquiry) : payload;
            var sw = Stopwatch.StartNew();
            await udp.SendAsync(data, data.Length).ConfigureAwait(false);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Math.Max(100, timeoutMs));
            var r = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
            bool looksVisca = ViscaResponse.Parse(r.Buffer, mode).Kind != ViscaResponseKind.Desconhecida;
            return new ProbeResult(port, mode, looksVisca, sw.Elapsed.TotalMilliseconds);
        }
        catch
        {
            return new ProbeResult(port, mode, false, -1);
        }
    }
}
