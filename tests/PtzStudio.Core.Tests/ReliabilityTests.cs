using System.Net;
using System.Net.Sockets;
using PtzStudio.Core.Control;
using PtzStudio.Core.Models;
using PtzStudio.Core.Storage;
using PtzStudio.Core.Visca;
using Xunit;

namespace PtzStudio.Core.Tests;

/// <summary>Reforço da parada, padrões de perfil, sanitização, migração do layout, cliente VISCA e descoberta de porta.</summary>
public class ReliabilityTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0);

    private static (PtzController c, FakeSender s) Make()
    {
        var s = new FakeSender();
        var p = new Profile();
        foreach (var a in new[] { p.Motion.Pan, p.Motion.Tilt, p.Motion.Zoom }) { a.AccelPerSecond = 0; a.DecelPerSecond = 0; a.NoiseThreshold = 0; }
        p.Motion.PanTiltSpeedPercent = 100;
        var cam = new CameraConfig { CommandIntervalMs = 0 };
        return (new PtzController(s, () => cam, () => p), s);
    }

    private static DateTime Run(PtzController c, DateTime from, int ms, int stepMs = 20)
    {
        var t = from;
        for (int e = 0; e < ms; e += stepMs) { t = t.AddMilliseconds(stepMs); c.Tick(stepMs / 1000.0, t); }
        return t;
    }

    // ---------- P1: reforço tardio da parada ----------
    [Fact]
    public void Stop_IsReinforced_OverTime_ThenStopsRepeating()
    {
        var (c, s) = Make();
        c.SetAnalog(InputSource.Fisico, 1, 0, 0, T0); c.Tick(0.02, T0);            // move
        c.SetAnalog(InputSource.Fisico, 0, 0, 0, T0.AddMilliseconds(20));
        var t = T0.AddMilliseconds(20); c.Tick(0.02, t);                           // 1ª parada
        int afterFirst = s.Sent.Count(b => s.IsPanTiltStop(b));
        Assert.Equal(1, afterFirst);
        t = Run(c, t, 3000);                                                      // 3 s sem nenhuma entrada
        int total = s.Sent.Count(b => s.IsPanTiltStop(b));
        Assert.True(total >= 4, $"esperava reforços tardios, houve {total} paradas");
        int settled = total;
        Run(c, t, 3000);
        Assert.Equal(settled, s.Sent.Count(b => s.IsPanTiltStop(b)));              // não repete para sempre
    }

    [Fact]
    public void Stop_Reinforcement_DoesNotCancelAZoomInProgress()
    {
        var (c, s) = Make();
        c.SetAnalog(InputSource.Fisico, 1, 0, 0, T0); c.Tick(0.02, T0);
        var t = T0.AddMilliseconds(20);
        for (int i = 0; i < 100; i++)                                              // solta o Pan, segura o Zoom por 2 s
        {
            c.SetAnalog(InputSource.Fisico, 0, 0, 1, t);
            c.Tick(0.02, t);
            t = t.AddMilliseconds(20);
        }
        Assert.Contains(s.Sent, b => s.IsPanTiltStop(b));
        Assert.DoesNotContain(s.Sent, b => b.Length == 6 && b[2] == 0x04 && b[3] == 0x07 && b[4] == 0x00);   // nenhuma parada de zoom
    }

    [Fact]
    public void PresetRecall_IsNotInterruptedByStopRepeats()
    {
        var (c, s) = Make();
        c.Tick(0.02, T0);
        c.RecallPreset(3);
        s.Sent.Clear();
        Run(c, T0, 3000);
        Assert.DoesNotContain(s.Sent, b => s.IsPanTiltStop(b));                    // parar repetidamente interromperia o deslocamento
    }

    // ---------- Ações removidas não existem mais nos perfis novos ----------
    [Fact]
    public void DefaultProfiles_HaveNoRemovedActions()
    {
        foreach (var p in AppSettings.CreateDefault().Profiles)
            Assert.DoesNotContain(p.Bindings, b => b.Action == ButtonAction.ParadaEmergencia || b.Action == ButtonAction.AlternarVelocidade);
    }

    // ---------- P4: sanitização ----------
    [Fact]
    public void Sanitizer_FixesImpossibleValues_ButKeepsTheUsersOwn()
    {
        var s = AppSettings.CreateDefault();
        var m = s.Profiles[0].Motion;
        m.Pan.Deadzone = 0.0;            // ajuste legítimo do usuário
        m.Pan.NoiseThreshold = 0.1;      // máximo permitido pela tela
        m.Tilt.Sensitivity = double.NaN; // impossível
        m.Zoom.MaxSpeed = 999;           // fora da faixa
        s.Cameras[0].Port = 70000;
        s.Cameras[0].ViscaAddress = 0;
        SettingsStore.Normalize(s);
        Assert.Equal(0.0, m.Pan.Deadzone);
        Assert.Equal(0.1, m.Pan.NoiseThreshold);
        Assert.Equal(1.0, m.Tilt.Sensitivity);
        Assert.Equal(8, m.Zoom.MaxSpeed);
        Assert.Equal(65535, s.Cameras[0].Port);
        Assert.Equal(1, s.Cameras[0].ViscaAddress);
    }

    // ---------- Restaurar padrões ----------
    [Fact]
    public void DefaultMotionFor_ReturnsFactoryValues_PerProfile()
    {
        var pregacao = AppSettings.DefaultMotionFor("Pregação");
        var louvor = AppSettings.DefaultMotionFor("Louvor");
        Assert.True(louvor.Pan.MaxSpeed > pregacao.Pan.MaxSpeed);
        Assert.Equal(0.12, pregacao.Pan.Deadzone);
        Assert.Equal(AppSettings.DefaultMotionFor("Igreja").Pan.MaxSpeed, AppSettings.DefaultMotionFor("Nome que não existe").Pan.MaxSpeed);
    }

    // ---------- M2: migração do layout ----------
    [Fact]
    public void LayoutMigration_KeepsFunctionsOnTheSameDrawnButton_EvenWhenTwoButtonsSwap()
    {
        var s = AppSettings.CreateDefault();
        var prof = s.Profiles[0];
        int a = s.Layout["A"], b = s.Layout["B"];
        prof.Bindings.Clear();
        prof.Bindings.Add(new ButtonBinding { Button = a, Action = ButtonAction.AcionarPreset, Parameter = 4 });
        prof.Bindings.Add(new ButtonBinding { Button = b, Action = ButtonAction.AcionarPreset, Parameter = 3 });
        prof.Bindings.Add(new ButtonBinding { Button = s.Layout["X"], Modifier = a, Action = ButtonAction.Parar });

        LayoutMigration.Apply(s, new Dictionary<string, int> { ["A"] = b, ["B"] = a, ["M"] = 20 });   // A e B trocados; M novo

        Assert.Equal(b, s.Layout["A"]); Assert.Equal(a, s.Layout["B"]); Assert.Equal(20, s.Layout["M"]);
        Assert.Equal(b, prof.Bindings[0].Button);                    // "Preset 4" continua no botão A do desenho
        Assert.Equal(a, prof.Bindings[1].Button);                    // "Preset 3" continua no botão B do desenho
        Assert.Equal(b, prof.Bindings[2].Modifier);                  // o modificador (A) também acompanha
    }

    // ---------- M4: cliente VISCA e descoberta, em loopback ----------
    private sealed class FakeCamera : IDisposable
    {
        private readonly UdpClient _udp = new(new IPEndPoint(IPAddress.Loopback, 0));
        private readonly CancellationTokenSource _cts = new();
        private readonly object _l = new();
        private readonly List<byte[]> _got = new();
        private readonly ViscaTransportMode _mode;
        public int Port => ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;

        public FakeCamera(ViscaTransportMode mode)
        {
            _mode = mode;
            try { _udp.Client.IOControl(-1744830452, new byte[4], null); } catch { }
            _ = Task.Run(Loop);
        }

        private async Task Loop()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var r = await _udp.ReceiveAsync(_cts.Token);
                    var data = r.Buffer;
                    if (_mode == ViscaTransportMode.ViscaOverIp) { if (data.Length < 8) continue; data = data[8..]; }
                    lock (_l) _got.Add(data);
                    if (data.Length >= 5 && data[1] == 0x09)                     // consulta -> responde como uma câmera
                    {
                        byte[] reply = { 0x90, 0x50, 0x00, 0x20, 0xFF };
                        byte[] outp = _mode == ViscaTransportMode.ViscaOverIp ? ViscaFraming.Wrap(reply, 0, ViscaPayloadType.Command) : reply;
                        await _udp.SendAsync(outp, outp.Length, r.RemoteEndPoint);
                    }
                }
                catch { if (_cts.IsCancellationRequested) return; }
            }
        }

        public List<byte[]> Received { get { lock (_l) return _got.ToList(); } }

        public async Task<bool> WaitFor(Func<List<byte[]>, bool> cond, int ms = 2500)
        {
            var end = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < end) { if (cond(Received)) return true; await Task.Delay(20); }
            return cond(Received);
        }

        public void Dispose() { _cts.Cancel(); _udp.Close(); }
    }

    private static CameraConfig CamFor(FakeCamera f, ViscaTransportMode mode) => new()
    {
        IpAddress = "127.0.0.1", Port = f.Port, Transport = mode, HealthCheck = false, TimeoutMs = 800
    };

    // pacote de MOVIMENTO de Pan/Tilt (81 01 06 01 ... e não a parada 03 03)
    private static bool IsMove(byte[] b) => b.Length == 9 && b[2] == 0x06 && b[3] == 0x01 && !(b[6] == 0x03 && b[7] == 0x03);

    [Fact]
    public async Task Client_SendsCommands_AndConnectsWhenCameraAnswers()
    {
        using var cam = new FakeCamera(ViscaTransportMode.UdpPuro);
        using var cl = new ViscaClient();
        cl.Connect(CamFor(cam, ViscaTransportMode.UdpPuro));
        Assert.True(await cl.TestConnectionAsync());
        for (int i = 0; i < 50 && cl.State != ConnectionState.Conectado; i++) await Task.Delay(20);   // o estado é atualizado pela thread de recepção
        Assert.Equal(ConnectionState.Conectado, cl.State);
        cl.Send(ViscaCommands.PanTiltDrive(1, 5, 5, 24, 20));
        Assert.True(await cam.WaitFor(r => r.Any(IsMove)));
    }

    [Fact]
    public async Task Client_InvalidIp_FailsFast_WithoutExceptionOrDns()
    {
        using var cl = new ViscaClient();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        cl.Connect(new CameraConfig { IpAddress = "isto-nao-e-um-ip", Port = 1259, HealthCheck = false });
        Assert.True(sw.ElapsedMilliseconds < 1000);
        Assert.Equal(ConnectionState.ErroDeComunicacao, cl.State);
        Assert.False(await cl.TestConnectionAsync());
    }

    [Fact]
    public async Task Probe_FindsOnlyTheCombinationTheCameraAnswers()
    {
        using var raw = new FakeCamera(ViscaTransportMode.UdpPuro);
        using var hdr = new FakeCamera(ViscaTransportMode.ViscaOverIp);
        var res = await CameraProbe.ProbeAsync("127.0.0.1", 1, 400, new[] { raw.Port, hdr.Port });
        var ok = res.Where(r => r.Responded).ToList();
        Assert.Equal(2, ok.Count);
        Assert.Contains(ok, r => r.Port == raw.Port && r.Mode == ViscaTransportMode.UdpPuro);
        Assert.Contains(ok, r => r.Port == hdr.Port && r.Mode == ViscaTransportMode.ViscaOverIp);
        Assert.DoesNotContain(ok, r => r.Port == raw.Port && r.Mode == ViscaTransportMode.ViscaOverIp);
    }

    [Fact]
    public async Task Probe_RejectsInvalidIp()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => CameraProbe.ProbeAsync("abc", 1, 200));
    }
}
