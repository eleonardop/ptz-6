using System.Text.RegularExpressions;

namespace PtzStudio.Core.Video;

/// <summary>Validação e montagem de endereços RTSP (rtsp://usuario:senha@ip:porta/caminho).</summary>
public static class RtspAddress
{
    public static bool IsValid(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u)) return false;
        return (u.Scheme == "rtsp" || u.Scheme == "rtsps") && !string.IsNullOrEmpty(u.Host);
    }

    /// <summary>Acrescenta usuário e senha ao endereço (com os caracteres especiais codificados). Se o endereço já traz usuário, não muda.</summary>
    public static string WithCredentials(string? url, string? user, string? password)
    {
        string u = (url ?? "").Trim();
        if (string.IsNullOrEmpty(user)) return u;
        int i = u.IndexOf("://", StringComparison.Ordinal);
        if (i < 0) return u;
        string rest = u.Substring(i + 3);
        string authority = rest.Split('/', '?')[0];
        if (authority.Contains('@')) return u;
        return u.Substring(0, i + 3) + Uri.EscapeDataString(user) + ":" + Uri.EscapeDataString(password ?? "") + "@" + rest;
    }

    private static readonly Regex PasswordPart = new(@"(?<=://)([^:@/]+):[^@/]*@", RegexOptions.Compiled);

    /// <summary>Esconde a senha para mostrar o endereço na tela.</summary>
    public static string Mask(string? url) => PasswordPart.Replace(url ?? "", "$1:***@");
}
