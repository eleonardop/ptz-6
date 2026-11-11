using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace PtzStudio.Core.Onvif;

public sealed record OnvifProfile(string Token, string Name, string Codec, int Width, int Height);

public sealed record OnvifStream(string Name, string Codec, int Width, int Height, string Uri)
{
    public int Area => Width * Height;
}

/// <summary>Resultado da consulta ONVIF: os fluxos de vídeo que a câmera informou.</summary>
public sealed class OnvifResult
{
    public bool Ok { get; init; }
    public string Message { get; init; } = "";
    public string DeviceUrl { get; init; } = "";
    public List<OnvifStream> Streams { get; init; } = new();

    /// <summary>Fluxo de maior resolução.</summary>
    public OnvifStream? Main => Streams.OrderByDescending(s => s.Area).FirstOrDefault();

    /// <summary>Fluxo secundário (menor resolução, de preferência H.264/H.265). Nulo se a câmera só tiver um.</summary>
    public OnvifStream? Sub
    {
        get
        {
            var main = Main;
            if (main == null || Streams.Count < 2) return null;
            var cand = Streams.Where(s => s.Uri != main.Uri && s.Area > 0 && s.Area < main.Area).ToList();
            if (cand.Count == 0) return null;
            var good = cand.Where(s => s.Codec == "H264" || s.Codec == "H265").ToList();
            return (good.Count > 0 ? good : cand).OrderBy(s => s.Area).First();
        }
    }
}

/// <summary>
/// Cliente ONVIF mínimo (só o necessário para descobrir os endereços RTSP): GetSystemDateAndTime, GetCapabilities,
/// GetProfiles e GetStreamUri. Sem bibliotecas externas. Se houver usuário e senha, usa o cabeçalho WS-Security (digest).
/// </summary>
public sealed class OnvifClient
{
    private readonly HttpClient _http;
    private readonly string _user;
    private readonly string _pass;
    private TimeSpan _offset = TimeSpan.Zero;   // diferença entre o relógio da câmera e o do PC (o digest depende da hora)

    public OnvifClient(string? user, string? password, int timeoutMs = 3000, HttpMessageHandler? handler = null)
    {
        _user = user ?? "";
        _pass = password ?? "";
        _http = handler == null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromMilliseconds(Math.Max(500, timeoutMs));
    }

    // ---------------- montagem das mensagens ----------------
    private static IEnumerable<XElement> Desc(XContainer c, string name) => c.Descendants().Where(x => x.Name.LocalName == name);

    private static string Esc(string s) => System.Security.SecurityElement.Escape(s) ?? "";

    public static string Envelope(string body, string? header = null) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
        "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\" xmlns:tds=\"http://www.onvif.org/ver10/device/wsdl\" " +
        "xmlns:trt=\"http://www.onvif.org/ver10/media/wsdl\" xmlns:tt=\"http://www.onvif.org/ver10/schema\">" +
        (header != null ? "<s:Header>" + header + "</s:Header>" : "") +
        "<s:Body>" + body + "</s:Body></s:Envelope>";

    /// <summary>Cabeçalho WS-Security UsernameToken com PasswordDigest = Base64(SHA1(nonce + data + senha)).</summary>
    public static string SecurityHeader(string user, string password, DateTime utcNow, byte[]? nonce = null)
    {
        nonce ??= RandomNumberGenerator.GetBytes(16);
        string created = utcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        var raw = nonce.Concat(Encoding.UTF8.GetBytes(created)).Concat(Encoding.UTF8.GetBytes(password)).ToArray();
        string digest = Convert.ToBase64String(SHA1.HashData(raw));
        return
            "<wsse:Security xmlns:wsse=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd\" " +
            "xmlns:wsu=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd\">" +
            "<wsse:UsernameToken>" +
            "<wsse:Username>" + Esc(user) + "</wsse:Username>" +
            "<wsse:Password Type=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordDigest\">" + digest + "</wsse:Password>" +
            "<wsse:Nonce EncodingType=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary\">" + Convert.ToBase64String(nonce) + "</wsse:Nonce>" +
            "<wsu:Created>" + created + "</wsu:Created>" +
            "</wsse:UsernameToken></wsse:Security>";
    }

    // ---------------- leitura das respostas ----------------
    private static int ToInt(string? s) =>
        int.TryParse((s ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static string NormCodec(string? c)
    {
        var s = (c ?? "").Trim().ToUpperInvariant().Replace(".", "").Replace("-", "").Replace("_", "");
        return s == "HEVC" ? "H265" : s;
    }

    public static string? FaultText(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            if (!Desc(doc, "Fault").Any()) return null;
            return Desc(doc, "Text").FirstOrDefault()?.Value.Trim();
        }
        catch { return null; }
    }

    public static string? ParseMediaXAddr(string xml)
    {
        var doc = XDocument.Parse(xml);
        var media = Desc(doc, "Media").FirstOrDefault();
        var x = media == null ? null : Desc(media, "XAddr").FirstOrDefault();
        return x?.Value.Trim();
    }

    public static List<OnvifProfile> ParseProfiles(string xml)
    {
        var list = new List<OnvifProfile>();
        var doc = XDocument.Parse(xml);
        foreach (var p in Desc(doc, "Profiles"))
        {
            string token = (string?)p.Attribute("token") ?? "";
            if (token.Length == 0) continue;
            string name = p.Elements().FirstOrDefault(x => x.Name.LocalName == "Name")?.Value.Trim() ?? token;
            var venc = Desc(p, "VideoEncoderConfiguration").FirstOrDefault();
            string codec = venc == null ? "" : NormCodec(Desc(venc, "Encoding").FirstOrDefault()?.Value);
            var res = venc == null ? null : Desc(venc, "Resolution").FirstOrDefault();
            int w = res == null ? 0 : ToInt(Desc(res, "Width").FirstOrDefault()?.Value);
            int h = res == null ? 0 : ToInt(Desc(res, "Height").FirstOrDefault()?.Value);
            list.Add(new OnvifProfile(token, name, codec, w, h));
        }
        return list;
    }

    public static string? ParseStreamUri(string xml)
    {
        var doc = XDocument.Parse(xml);
        return Desc(doc, "Uri").FirstOrDefault()?.Value.Trim();
    }

    // ---------------- rede ----------------
    private async Task<(int Status, string Text)> PostRawAsync(string url, string body, bool auth, CancellationToken ct)
    {
        string? header = auth && _user.Length > 0 ? SecurityHeader(_user, _pass, DateTime.UtcNow + _offset) : null;
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(Envelope(body, header), Encoding.UTF8, "application/soap+xml")
        };
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        string text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ((int)resp.StatusCode, text);
    }

    private async Task<string> PostAsync(string url, string body, bool auth, CancellationToken ct)
    {
        var (status, text) = await PostRawAsync(url, body, auth, ct).ConfigureAwait(false);
        if (status >= 200 && status < 300) return text;
        string? fault = FaultText(text);
        if (status == 401 || (fault != null && fault.Contains("Authorized", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A câmera pediu usuário e senha (ou recusou os informados).");
        throw new InvalidOperationException(fault ?? $"A câmera respondeu HTTP {status}.");
    }

    /// <summary>Verdadeiro se o endereço responde como um serviço ONVIF (mesmo que recuse por falta de login).</summary>
    public async Task<bool> IsOnvifAsync(string deviceUrl, CancellationToken ct = default)
    {
        try
        {
            var (_, text) = await PostRawAsync(deviceUrl, "<tds:GetSystemDateAndTime/>", false, ct).ConfigureAwait(false);
            return text.Contains("Envelope", StringComparison.Ordinal);
        }
        catch { return false; }
    }

    private async Task SyncClockAsync(string url, CancellationToken ct)
    {
        try
        {
            var xml = await PostAsync(url, "<tds:GetSystemDateAndTime/>", false, ct).ConfigureAwait(false);
            var utc = Desc(XDocument.Parse(xml), "UTCDateTime").FirstOrDefault();
            if (utc == null) return;
            int G(string n) => ToInt(Desc(utc, n).FirstOrDefault()?.Value);
            var dt = new DateTime(G("Year"), G("Month"), G("Day"), G("Hour"), G("Minute"), G("Second"), DateTimeKind.Utc);
            _offset = dt - DateTime.UtcNow;
        }
        catch { /* sem acerto de hora: segue com o relógio do PC */ }
    }

    private static OnvifResult Fail(string message) => new() { Ok = false, Message = message };

    /// <summary>Consulta a câmera (serviço de dispositivo) e devolve os fluxos RTSP de cada perfil de vídeo.</summary>
    public async Task<OnvifResult> ResolveAsync(string deviceUrl, CancellationToken ct = default)
    {
        try
        {
            await SyncClockAsync(deviceUrl, ct).ConfigureAwait(false);

            string media = deviceUrl;
            try
            {
                var caps = await PostAsync(deviceUrl, "<tds:GetCapabilities><tds:Category>Media</tds:Category></tds:GetCapabilities>", true, ct).ConfigureAwait(false);
                var x = ParseMediaXAddr(caps);
                if (!string.IsNullOrEmpty(x)) media = x;
            }
            catch (InvalidOperationException) { /* tenta o mesmo endereço para os perfis */ }

            var profXml = await PostAsync(media, "<trt:GetProfiles/>", true, ct).ConfigureAwait(false);
            var profiles = ParseProfiles(profXml);
            if (profiles.Count == 0) return Fail("A câmera não informou nenhum perfil de vídeo.");

            var streams = new List<OnvifStream>();
            foreach (var p in profiles)
            {
                string body = "<trt:GetStreamUri><trt:StreamSetup><tt:Stream>RTP-Unicast</tt:Stream><tt:Transport><tt:Protocol>RTSP</tt:Protocol></tt:Transport></trt:StreamSetup>" +
                              "<trt:ProfileToken>" + Esc(p.Token) + "</trt:ProfileToken></trt:GetStreamUri>";
                try
                {
                    var ux = await PostAsync(media, body, true, ct).ConfigureAwait(false);
                    var uri = ParseStreamUri(ux);
                    if (!string.IsNullOrEmpty(uri)) streams.Add(new OnvifStream(p.Name, p.Codec, p.Width, p.Height, uri));
                }
                catch (InvalidOperationException) { /* este perfil não tem endereço RTSP: ignora */ }
            }
            if (streams.Count == 0) return Fail("A câmera não informou nenhum endereço RTSP (confira usuário e senha).");
            return new OnvifResult { Ok = true, DeviceUrl = deviceUrl, Streams = streams };
        }
        catch (OperationCanceledException) { return Fail("A câmera não respondeu a tempo."); }
        catch (Exception ex) { return Fail(ex.Message); }
    }
}
