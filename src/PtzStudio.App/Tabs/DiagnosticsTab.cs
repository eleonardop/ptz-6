using System.Windows;
using System.Windows.Controls;
using PtzStudio.App.Engine;
using PtzStudio.App.Ui;
using PtzStudio.Core.Input;
using PtzStudio.Core.Visca;

namespace PtzStudio.App.Tabs;

/// <summary>
/// Estado do controle e da câmera e testes individuais. Não mostra nem guarda histórico de comandos (TX/RX):
/// só os últimos erros de comunicação, na memória, para ajudar a descobrir o que deu errado.
/// </summary>
internal sealed class DiagnosticsTab : TabBase
{
    private readonly TextBlock _pad = Ux.Text("", 14);
    private readonly TextBlock _cam = Ux.Text("", 14);
    private readonly TextBlock _test = Ux.Text("", 14, true);
    private readonly TextBlock _prog = Ux.Text("", 14);
    private readonly TextBlock _issues = Ux.Text("", 13, false, Ux.Muted);
    private string _issuesShown = "\u0001";

    public DiagnosticsTab(AppEngine e) : base(e) { Init(); }

    protected override void Build()
    {
        _issuesShown = "\u0001";
        void Pulse(double x, double y, double z) => E.Controller.Pulse(x, y, z, 700, DateTime.UtcNow);

        var tests = Ux.V(
            Ux.Wrap(
                Ux.Btn("Testar conexão", async () =>
                {
                    _test.Text = "Testando…";
                    bool ok = await E.Client.TestConnectionAsync();
                    _test.Text = ok ? $"Câmera respondeu em {E.Client.LastRoundTripMs:0} ms" : "SEM resposta da câmera";
                }, 150, Ux.Accent),
                Ux.Btn("Pan →", () => Pulse(1, 0, 0), 90), Ux.Btn("Pan ←", () => Pulse(-1, 0, 0), 90),
                Ux.Btn("Tilt ↑", () => Pulse(0, 1, 0), 90), Ux.Btn("Tilt ↓", () => Pulse(0, -1, 0), 90),
                Ux.Btn("Zoom +", () => Pulse(0, 0, 0.6), 90), Ux.Btn("Zoom −", () => Pulse(0, 0, -0.6), 90),
                Ux.Btn("Home", () => E.Controller.Home(), 90),
                Ux.Btn("Testar parada", () => E.Controller.StopAll("teste de parada"), 140)),
            Ux.Wrap(
                Ux.Btn("Recalibrar repouso dos eixos", () => E.RecalibrateRest(), 250),
                Ux.Btn("Copiar diagnóstico", CopyDiagnostics, 190)),
            _test);

        var page = new StackPanel();
        page.Children.Add(Ux.Header("Diagnóstico", "Estado do controle e da câmera, e testes de cada função."));
        page.Children.Add(Ux.Group("Controle", _pad));
        page.Children.Add(Ux.Group("Câmera", _cam));
        page.Children.Add(Ux.Group("Testes individuais", tests));
        page.Children.Add(Ux.Group("Programa", _prog));
        page.Children.Add(Ux.Group("Últimos erros e avisos (apenas na memória)", _issues));
        Content = Ux.Scroll(page);
    }

    private static string Sup(ImageSupport s) => s switch
    {
        ImageSupport.Suportado => "confirmado pela câmera",
        ImageSupport.NaoSuportado => "não confirmado (a câmera não respondeu ou recusou)",
        _ => "ainda não detectado"
    };

    private string Report()
    {
        var p = E.Pad; var c = E.Camera; var cl = E.Client;
        var lines = new List<string>
        {
            $"PTZ Studio — diagnóstico {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            $"Controle: {(p.Connected ? $"{p.Name} (VID:PID {p.Vid:X4}:{p.Pid:X4}; {p.ButtonCount} botões, {p.AxisCount} eixos)" : "não detectado")}",
            $"Câmera: {c.Name} {c.IpAddress}:{c.Port} {(c.Transport == PtzStudio.Core.Models.ViscaTransportMode.UdpPuro ? "UDP puro" : "VISCA over IP")} endereço {c.ViscaAddress}",
            $"Estado: {cl.State} — {cl.StateDetail}; tempo de resposta {(cl.LastRoundTripMs >= 0 ? cl.LastRoundTripMs.ToString("0") + " ms" : "—")}",
            $"Tempo de abertura: {E.StartupMs:0} ms",
            $"Imagem: brilho {Sup(E.Image.Brilho.Support)}; exposição {Sup(E.Image.Exposicao.Support)}",
            "Últimos erros:"
        };
        var iss = E.RecentIssues;
        lines.AddRange(iss.Count == 0 ? new[] { "  (nenhum)" } : iss.Select(x => "  " + x));
        return string.Join(Environment.NewLine, lines);
    }

    private void CopyDiagnostics()
    {
        try { Clipboard.SetText(Report()); _test.Text = "Diagnóstico copiado. Cole em uma mensagem para pedir ajuda."; }
        catch (Exception ex) { _test.Text = "Não foi possível copiar: " + ex.Message; }
    }

    protected override void Refresh()
    {
        var p = E.Pad;
        if (!p.Connected) _pad.Text = "Nenhum controle detectado.\nConecte o GameSir por USB ou Bluetooth. Se ele estiver pareado mas não aparece, tente trocar o modo do controle.";
        else
        {
            var pressed = string.Join(", ", E.Last.Pressed.OrderBy(i => i).Select(VirtualButtons.Describe));
            var axes = E.LastSnapshot == null ? "" : string.Join("   ", E.LastSnapshot.Axes.Select((v, i) => $"{InputSnapshot.AxisNames[i]}={v:0.00}").Take(Math.Max(p.AxisCount, 1)));
            _pad.Text = $"Nome: {p.Name}\nVID:PID = {p.Vid:X4}:{p.Pid:X4}  (o par muda conforme o modo/conexão; compare USB × Bluetooth)\n" +
                        $"Tipo de conexão: o Windows não informa diretamente; use VID:PID e o modo do controle.\n" +
                        $"Capacidades: {p.ButtonCount} botões, {p.AxisCount} eixos, {p.PovCount} direcional(is)\n" +
                        $"Eixos: {axes}\nPressionados agora: {(pressed.Length == 0 ? "nenhum" : pressed)}";
        }
        var c = E.Camera; var cl = E.Client;
        _cam.Text = $"{c.Name} — {c.IpAddress}:{c.Port} — {(c.Transport == PtzStudio.Core.Models.ViscaTransportMode.UdpPuro ? "UDP puro" : "VISCA over IP")}\n" +
                    $"Estado: {cl.State} — {cl.StateDetail}\n" +
                    $"Tempo de resposta: {(cl.LastRoundTripMs >= 0 ? cl.LastRoundTripMs.ToString("0") + " ms" : "—")}\n" +
                    $"Enviado agora: Pan {E.Controller.SentPan}  Tilt {E.Controller.SentTilt}  Zoom {E.Controller.SentZoom}";
        _prog.Text = $"Tempo de abertura (até a primeira tela): {E.StartupMs:0} ms\nAjustes de imagem: brilho {Sup(E.Image.Brilho.Support)} · exposição {Sup(E.Image.Exposicao.Support)}";

        var iss = E.RecentIssues;
        string text = iss.Count == 0 ? "Nenhum erro registrado." : string.Join("\n", iss.Reverse());
        if (text != _issuesShown) { _issuesShown = text; _issues.Text = text; }
    }
}
