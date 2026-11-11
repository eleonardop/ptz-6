using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

namespace PtzStudio.Core.Onvif;

/// <summary>WS-Discovery: pergunta na rede local quais dispositivos ONVIF existem (multicast 239.255.255.250:3702).</summary>
public static class OnvifDiscovery
{
    public static List<string> ParseXAddrs(string xml)
    {
        var list = new List<string>();
        try
        {
            var doc = XDocument.Parse(xml);
            foreach (var x in doc.Descendants().Where(e => e.Name.LocalName == "XAddrs"))
                list.AddRange(x.Value.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries));
        }
        catch { }
        return list;
    }

    public static async Task<List<string>> ProbeAsync(int timeoutMs, CancellationToken ct = default)
    {
        var result = new List<string>();
        try
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork);
            try { udp.Client.IOControl(-1744830452, new byte[4], null); } catch { }
            string probe =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<e:Envelope xmlns:e=\"http://www.w3.org/2003/05/soap-envelope\" xmlns:w=\"http://schemas.xmlsoap.org/ws/2004/08/addressing\" " +
                "xmlns:d=\"http://schemas.xmlsoap.org/ws/2005/04/discovery\" xmlns:dn=\"http://www.onvif.org/ver10/network/wsdl\">" +
                "<e:Header><w:MessageID>uuid:" + Guid.NewGuid() + "</w:MessageID>" +
                "<w:To e:mustUnderstand=\"true\">urn:schemas-xmlsoap-org:ws:2005:04:discovery</w:To>" +
                "<w:Action e:mustUnderstand=\"true\">http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</w:Action></e:Header>" +
                "<e:Body><d:Probe><d:Types>dn:NetworkVideoTransmitter</d:Types></d:Probe></e:Body></e:Envelope>";
            var data = Encoding.UTF8.GetBytes(probe);
            await udp.SendAsync(data, data.Length, new IPEndPoint(IPAddress.Parse("239.255.255.250"), 3702)).ConfigureAwait(false);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Math.Max(300, timeoutMs));
            while (!cts.IsCancellationRequested)
            {
                var r = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
                result.AddRange(ParseXAddrs(Encoding.UTF8.GetString(r.Buffer)));
            }
        }
        catch { /* fim do tempo de espera ou rede sem multicast */ }
        return result.Distinct().ToList();
    }
}

/// <summary>
/// Descoberta automática do fluxo de vídeo: testa o serviço ONVIF no IP da câmera (portas comuns), tenta a descoberta
/// multicast se nada responder e, com o serviço achado, lê os perfis e os endereços RTSP.
/// </summary>
public static class OnvifFinder
{
    public static readonly int[] CommonPorts = { 80, 8000, 8080, 2020, 8899, 5000 };

    private static OnvifResult Fail(string m) => new() { Ok = false, Message = m };

    public static async Task<OnvifResult> FindAsync(string ip, string? user, string? password, CancellationToken ct = default)
    {
        if (!IPAddress.TryParse((ip ?? "").Trim(), out _)) return Fail("Endereço IP da câmera inválido.");
        ip = ip!.Trim();
        var client = new OnvifClient(user, password, 3000);

        var urls = CommonPorts.Select(p => $"http://{ip}:{p}/onvif/device_service").ToList();
        var probes = await Task.WhenAll(urls.Select(async u => (Url: u, Ok: await client.IsOnvifAsync(u, ct).ConfigureAwait(false)))).ConfigureAwait(false);
        var live = probes.Where(p => p.Ok).Select(p => p.Url).ToList();

        if (live.Count == 0)
        {
            var found = await OnvifDiscovery.ProbeAsync(2000, ct).ConfigureAwait(false);
            live = found.Where(x => x.Contains(ip, StringComparison.Ordinal)).ToList();
        }
        if (live.Count == 0)
            return Fail("A câmera não respondeu ao ONVIF. Ela pode não ter esse recurso (ou ele está desligado): informe o endereço RTSP manualmente.");

        string last = "";
        foreach (var u in live)
        {
            var r = await client.ResolveAsync(u, ct).ConfigureAwait(false);
            if (r.Ok) return r;
            last = r.Message;
        }
        return Fail(last.Length > 0 ? last : "Não foi possível ler os fluxos de vídeo da câmera.");
    }
}
