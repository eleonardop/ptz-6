using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using PtzStudio.Core.Control;
using PtzStudio.Core.Input;
using PtzStudio.Core.Models;
using PtzStudio.Core.Storage;
using PtzStudio.Core.Visca;
using Xunit;

namespace PtzStudio.Core.Tests;

/// <summary>Velocidade Pan/Tilt em porcentagem (por perfil), zoom independente, migração dos níveis antigos e imagem da câmera.</summary>
public class SpeedAndImageTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ptz-speed-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static (PtzController c, FakeSender s) Make(int panTiltPercent, int zoomPercent = 50, InputSource src = InputSource.Fisico)
    {
        var s = new FakeSender();
        var p = new Profile();
        foreach (var a in new[] { p.Motion.Pan, p.Motion.Tilt, p.Motion.Zoom }) { a.AccelPerSecond = 0; a.DecelPerSecond = 0; a.NoiseThreshold = 0; }
        p.Motion.PanTiltSpeedPercent = panTiltPercent;
        p.Motion.VirtualZoomPercent = zoomPercent;
        var cam = new CameraConfig { CommandIntervalMs = 0 };
        var c = new PtzController(s, () => cam, () => p) { ActiveSource = src };
        return (c, s);
    }

    private static byte[] FirstPanTilt(FakeSender s) => s.Sent.First(b => s.IsPanTiltMove(b));
    private static byte[]? FirstZoom(FakeSender s) => s.Sent.FirstOrDefault(b => b.Length == 6 && b[2] == 0x04 && b[3] == 0x07 && b[4] != 0x00);

    // ---------------- velocidade Pan/Tilt ----------------
    [Theory]
    [InlineData(100, 12)]
    [InlineData(50, 6)]
    [InlineData(25, 3)]
    public void PanTiltPercent_ScalesTheSentSpeed(int percent, int expectedPan)
    {
        var (c, s) = Make(percent);
        c.SetAnalog(InputSource.Fisico, 1, 0, 0, T0); c.Tick(0.02, T0);
        Assert.Equal(expectedPan, FirstPanTilt(s)[4]);       // byte da velocidade de Pan
    }

    [Fact]
    public void OnePercent_StillMoves_AtTheSlowestSpeed()
    {
        var (c, s) = Make(1);
        c.SetAnalog(InputSource.Fisico, 1, 1, 0, T0); c.Tick(0.02, T0);
        var b = FirstPanTilt(s);
        Assert.Equal(1, b[4]); Assert.Equal(1, b[5]);
    }

    [Fact]
    public void PanTiltPercent_RespectsTheCameraLimit()
    {
        var s = new FakeSender();
        var p = new Profile();
        p.Motion.Pan.AccelPerSecond = 0; p.Motion.Pan.NoiseThreshold = 0; p.Motion.Pan.MaxSpeed = 24;
        p.Motion.PanTiltSpeedPercent = 100;
        var cam = new CameraConfig { CommandIntervalMs = 0, MaxPanSpeed = 8 };     // a câmera só aceita até 8
        var c = new PtzController(s, () => cam, () => p);
        c.SetAnalog(InputSource.Fisico, 1, 0, 0, T0); c.Tick(0.02, T0);
        Assert.True(FirstPanTilt(s)[4] <= 8);
    }

    [Fact]
    public void PanTiltPercent_DoesNotChangeTheZoom()
    {
        var (slow, s1) = Make(10);
        var (fast, s2) = Make(100);
        slow.SetAnalog(InputSource.Fisico, 0, 0, 1, T0); slow.Tick(0.02, T0);
        fast.SetAnalog(InputSource.Fisico, 0, 0, 1, T0); fast.Tick(0.02, T0);
        Assert.NotNull(FirstZoom(s1));
        Assert.Equal(FirstZoom(s2), FirstZoom(s1));
    }

    [Fact]
    public void VirtualZoomPercent_ScalesOnlyTheOnScreenZoom()
    {
        var (full, s1) = Make(100, 100, InputSource.Virtual);
        var (half, s2) = Make(100, 40, InputSource.Virtual);
        full.SetAnalog(InputSource.Virtual, 0, 0, 1, T0); full.Tick(0.02, T0);
        half.SetAnalog(InputSource.Virtual, 0, 0, 1, T0); half.Tick(0.02, T0);
        Assert.True(FirstZoom(s1)![4] > FirstZoom(s2)![4]);

        var (phys, s3) = Make(100, 40, InputSource.Fisico);                     // zoom do controle físico: sem esse fator
        phys.SetAnalog(InputSource.Fisico, 0, 0, 1, T0); phys.Tick(0.02, T0);
        Assert.Equal(FirstZoom(s1), FirstZoom(s3));
    }

    // ---------------- por perfil / migração ----------------
    [Fact]
    public void SpeedIsStoredPerProfile_AndOneProfileNeverChangesTheOthers()
    {
        var s = AppSettings.CreateDefault();
        s.Profiles.First(p => p.Name == "Louvor").Motion.PanTiltSpeedPercent = 35;
        s.Profiles.First(p => p.Name == "Pregação").Motion.PanTiltSpeedPercent = 55;
        s.Profiles.First(p => p.Name == "Igreja").Motion.PanTiltSpeedPercent = 75;
        new SettingsStore(_dir).Save(s);

        var r = new SettingsStore(_dir).Load();
        Assert.Equal(35, r.Profiles.First(p => p.Name == "Louvor").Motion.PanTiltSpeedPercent);
        Assert.Equal(55, r.Profiles.First(p => p.Name == "Pregação").Motion.PanTiltSpeedPercent);
        Assert.Equal(75, r.Profiles.First(p => p.Name == "Igreja").Motion.PanTiltSpeedPercent);

        r.Profiles.First(p => p.Name == "Louvor").Motion.PanTiltSpeedPercent = 20;      // altera só o Louvor
        new SettingsStore(_dir).Save(r);
        var r2 = new SettingsStore(_dir).Load();
        Assert.Equal(20, r2.Profiles.First(p => p.Name == "Louvor").Motion.PanTiltSpeedPercent);
        Assert.Equal(55, r2.Profiles.First(p => p.Name == "Pregação").Motion.PanTiltSpeedPercent);
        Assert.Equal(75, r2.Profiles.First(p => p.Name == "Igreja").Motion.PanTiltSpeedPercent);
    }

    [Fact]
    public void NewAndDefaultProfiles_GetAReasonableSpeed()
    {
        foreach (var p in AppSettings.CreateDefault().Profiles)
            Assert.InRange(p.Motion.PanTiltSpeedPercent, 20, 100);
        Assert.Equal(65, AppSettings.CreateDefault().Profiles[0].Motion.PanTiltSpeedPercent);   // o antigo "nível 2"
    }

    [Fact]
    public void OldFileWithThreeLevels_IsConvertedToPercent_WithoutLosingTheProfile()
    {
        Directory.CreateDirectory(_dir);
        var st = new SettingsStore(_dir);
        File.WriteAllText(st.FilePath,
            "{\"Profiles\":[{\"Name\":\"Louvor\",\"Motion\":{\"SpeedLevels\":[0.35,0.65,1.0],\"DefaultSpeedLevel\":0}," +
            "\"Bindings\":[{\"Button\":3,\"Action\":\"ParadaEmergencia\"},{\"Button\":0,\"Action\":\"AlternarVelocidade\"},{\"Button\":1,\"Action\":\"Home\"}]}]," +
            "\"ActiveProfile\":\"Louvor\"}");
        var r = st.Load();
        var louvor = r.Profiles.First(p => p.Name == "Louvor");
        Assert.Equal(35, louvor.Motion.PanTiltSpeedPercent);                                   // nível 1 (35%) virou 35%
        Assert.Null(louvor.Motion.SpeedLevels);
        Assert.DoesNotContain(louvor.Bindings, b => b.Action == ButtonAction.ParadaEmergencia || b.Action == ButtonAction.AlternarVelocidade);
        Assert.Contains(louvor.Bindings, b => b.Action == ButtonAction.Home);                  // o resto do perfil fica
    }

    [Fact]
    public void LegacyFields_AreNoLongerWritten()
    {
        var s = AppSettings.CreateDefault();
        new SettingsStore(_dir).Save(s);
        string json = File.ReadAllText(new SettingsStore(_dir).FilePath);
        Assert.DoesNotContain("SpeedLevels", json);
        Assert.DoesNotContain("DefaultSpeedLevel", json);
        Assert.Contains("PanTiltSpeedPercent", json);
    }

    [Fact]
    public void Sanitizer_KeepsPercentsInsideOneToHundred()
    {
        var s = AppSettings.CreateDefault();
        s.Profiles[0].Motion.PanTiltSpeedPercent = 0;
        s.Profiles[1].Motion.PanTiltSpeedPercent = 500;
        s.Profiles[0].Motion.VirtualZoomPercent = -5;
        SettingsStore.Normalize(s);
        Assert.Equal(1, s.Profiles[0].Motion.PanTiltSpeedPercent);
        Assert.Equal(100, s.Profiles[1].Motion.PanTiltSpeedPercent);
        Assert.Equal(1, s.Profiles[0].Motion.VirtualZoomPercent);
    }

    // ---------------- comandos de imagem ----------------
    [Fact]
    public void ImageCommands_HaveTheStandardViscaFormat()
    {
        Assert.Equal(new byte[] { 0x81, 0x01, 0x04, 0xA1, 0x00, 0x00, 0x02, 0x0A, 0xFF }, ViscaCommands.BrightnessDirect(1, 42));
        Assert.Equal(new byte[] { 0x81, 0x09, 0x04, 0xA1, 0xFF }, ViscaCommands.BrightnessInquiry(1));
        Assert.Equal(new byte[] { 0x81, 0x01, 0x04, 0x3E, 0x02, 0xFF }, ViscaCommands.ExpCompPower(1, true));
        Assert.Equal(new byte[] { 0x81, 0x01, 0x04, 0x3E, 0x03, 0xFF }, ViscaCommands.ExpCompPower(1, false));
        Assert.Equal(new byte[] { 0x81, 0x01, 0x04, 0x0E, 0x02, 0xFF }, ViscaCommands.ExpCompStep(1, +3));
        Assert.Equal(new byte[] { 0x81, 0x01, 0x04, 0x0E, 0x03, 0xFF }, ViscaCommands.ExpCompStep(1, -1));
        Assert.Equal(new byte[] { 0x81, 0x01, 0x04, 0x0E, 0x00, 0xFF }, ViscaCommands.ExpCompStep(1, 0));
        Assert.Equal(new byte[] { 0x81, 0x01, 0x04, 0x4E, 0x00, 0x00, 0x00, 0x07, 0xFF }, ViscaCommands.ExpCompDirect(1, 7));
        Assert.Equal(new byte[] { 0x81, 0x01, 0x04, 0x4E, 0x00, 0x00, 0x00, 0x0E, 0xFF }, ViscaCommands.ExpCompDirect(1, 99));   // limitado a 14
        Assert.Equal(new byte[] { 0x81, 0x09, 0x04, 0x3E, 0xFF }, ViscaCommands.ExpCompPowerInquiry(1));
        Assert.Equal(new byte[] { 0x81, 0x09, 0x04, 0x4E, 0xFF }, ViscaCommands.ExpCompPositionInquiry(1));
    }

    [Fact]
    public void ImageReplies_AreParsed()
    {
        Assert.True(ViscaCommands.TryReadValue(new byte[] { 0x90, 0x50, 0x00, 0x00, 0x02, 0x0A, 0xFF }, out int v));
        Assert.Equal(42, v);
        Assert.False(ViscaCommands.TryReadValue(new byte[] { 0x90, 0x60, 0x02, 0xFF }, out _));          // erro de sintaxe
        Assert.True(ViscaCommands.TryReadOnOff(new byte[] { 0x90, 0x50, 0x02, 0xFF }, out bool on)); Assert.True(on);
        Assert.True(ViscaCommands.TryReadOnOff(new byte[] { 0x90, 0x50, 0x03, 0xFF }, out on)); Assert.False(on);
        Assert.False(ViscaCommands.TryReadOnOff(null, out _));
    }

    [Fact]
    public void Router_HoldsBrightnessAndExposure_WhileTheButtonIsPressed()
    {
        var prof = new Profile();
        prof.Bindings.Clear();
        prof.Bindings.Add(new ButtonBinding { Button = 3, Action = ButtonAction.BrilhoMais });
        prof.Bindings.Add(new ButtonBinding { Button = 4, Action = ButtonAction.ExposicaoMenos });
        var r = new ButtonRouter();
        r.Update(new HashSet<int> { 3, 4 }, prof);
        Assert.Equal(1, r.HeldBright);
        Assert.Equal(-1, r.HeldExposure);
        r.Update(new HashSet<int>(), prof);
        Assert.Equal(0, r.HeldBright);
        Assert.Equal(0, r.HeldExposure);
    }

    // ---------------- imagem na câmera (UDP em loopback) ----------------
    private sealed class ScriptedCamera : IDisposable
    {
        private readonly UdpClient _udp = new(new IPEndPoint(IPAddress.Loopback, 0));
        private readonly CancellationTokenSource _cts = new();
        private readonly object _l = new();
        private readonly List<byte[]> _got = new();
        private readonly Func<byte[], byte[]?> _reply;
        public int Port => ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;

        public ScriptedCamera(Func<byte[], byte[]?> reply)
        {
            _reply = reply;
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
                    lock (_l) _got.Add(r.Buffer);
                    var rep = _reply(r.Buffer);
                    if (rep != null) await _udp.SendAsync(rep, rep.Length, r.RemoteEndPoint);
                }
                catch { if (_cts.IsCancellationRequested) return; }
            }
        }

        public List<byte[]> Received { get { lock (_l) return _got.ToList(); } }
        public int Count(byte[] packet) => Received.Count(b => b.SequenceEqual(packet));

        public async Task<bool> WaitFor(Func<List<byte[]>, bool> cond, int ms = 2500)
        {
            var end = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < end) { if (cond(Received)) return true; await Task.Delay(20); }
            return cond(Received);
        }

        public void Dispose() { _cts.Cancel(); _udp.Close(); }
    }

    /// <summary>Câmera que responde a tudo: versão, brilho, compensação de exposição.</summary>
    private static byte[]? FullCamera(byte[] q, int bright = 5, int expPos = 9, bool expOn = false)
    {
        if (q.Length < 5 || q[1] != 0x09) return null;
        if (q[2] == 0x00 && q[3] == 0x02) return new byte[] { 0x90, 0x50, 0x00, 0x20, 0xFF };
        if (q[2] == 0x04 && q[3] == 0xA1) return new byte[] { 0x90, 0x50, 0x00, 0x00, (byte)(bright >> 4), (byte)(bright & 0x0F), 0xFF };
        if (q[2] == 0x04 && q[3] == 0x3E) return new byte[] { 0x90, 0x50, (byte)(expOn ? 2 : 3), 0xFF };
        if (q[2] == 0x04 && q[3] == 0x4E) return new byte[] { 0x90, 0x50, 0x00, 0x00, 0x00, (byte)expPos, 0xFF };
        return new byte[] { 0x90, 0x60, 0x02, 0xFF };
    }

    /// <summary>Câmera que só responde à versão e recusa (erro de sintaxe) qualquer outra consulta.</summary>
    private static byte[]? BasicCamera(byte[] q)
    {
        if (q.Length < 5 || q[1] != 0x09) return null;
        if (q[2] == 0x00 && q[3] == 0x02) return new byte[] { 0x90, 0x50, 0x00, 0x20, 0xFF };
        return new byte[] { 0x90, 0x60, 0x02, 0xFF };
    }

    private static async Task<(ViscaClient client, CameraImage image)> Connected(ScriptedCamera cam)
    {
        var cfg = new CameraConfig { IpAddress = "127.0.0.1", Port = cam.Port, HealthCheck = false, TimeoutMs = 800 };
        var cl = new ViscaClient();
        cl.Connect(cfg);
        Assert.True(await cl.TestConnectionAsync());
        for (int i = 0; i < 50 && cl.State != ConnectionState.Conectado; i++) await Task.Delay(20);
        return (cl, new CameraImage(cl, () => cfg));
    }

    [Fact]
    public async Task Detect_ReadsTheCamerasValues_ThenStepsSendTheRealCommands()
    {
        using var cam = new ScriptedCamera(q => FullCamera(q, bright: 5, expPos: 9, expOn: false));
        var (cl, img) = await Connected(cam);
        using (cl)
        {
            await img.DetectAsync();
            Assert.Equal(ImageSupport.Suportado, img.Brilho.Support);
            Assert.Equal(5, img.Brilho.Value);
            Assert.Equal(ImageSupport.Suportado, img.Exposicao.Support);
            Assert.Equal(9, img.Exposicao.Value);

            img.Step(ImageParam.Brilho, +1);
            Assert.True(await cam.WaitFor(r => r.Any(b => b.SequenceEqual(ViscaCommands.BrightnessDirect(1, 6)))));
            Assert.Equal(6, img.Brilho.Value);

            img.Step(ImageParam.Exposicao, +1);
            Assert.True(await cam.WaitFor(r => r.Any(b => b.SequenceEqual(ViscaCommands.ExpCompStep(1, +1)))));
            Assert.Equal(1, cam.Count(ViscaCommands.ExpCompPower(1, true)));      // a compensação foi ligada antes do primeiro ajuste
            img.Step(ImageParam.Exposicao, -1);
            Assert.True(await cam.WaitFor(r => r.Any(b => b.SequenceEqual(ViscaCommands.ExpCompStep(1, -1)))));
            Assert.Equal(1, cam.Count(ViscaCommands.ExpCompPower(1, true)));      // e não é ligada de novo
        }
    }

    [Fact]
    public async Task SliderValue_IsSentAsAnAbsoluteCommand()
    {
        using var cam = new ScriptedCamera(q => FullCamera(q, expOn: true));
        var (cl, img) = await Connected(cam);
        using (cl)
        {
            await img.DetectAsync();
            img.SetValue(ImageParam.Exposicao, 12);
            Assert.True(await cam.WaitFor(r => r.Any(b => b.SequenceEqual(ViscaCommands.ExpCompDirect(1, 12)))));
            Assert.Equal(0, cam.Count(ViscaCommands.ExpCompPower(1, true)));      // já estava ligada
            img.RestoreInitial(ImageParam.Exposicao);
            Assert.True(await cam.WaitFor(r => r.Any(b => b.SequenceEqual(ViscaCommands.ExpCompStep(1, 0)))));
        }
    }

    [Fact]
    public async Task WhenTheCameraRefuses_NothingIsSent_AndTheUserIsTold()
    {
        using var cam = new ScriptedCamera(BasicCamera);
        var (cl, img) = await Connected(cam);
        using (cl)
        {
            var notices = new List<string>();
            img.Notice += notices.Add;
            await img.DetectAsync();
            Assert.Equal(ImageSupport.NaoSuportado, img.Brilho.Support);
            Assert.Equal(ImageSupport.NaoSuportado, img.Exposicao.Support);
            Assert.False(string.IsNullOrWhiteSpace(img.Brilho.Note));

            img.Step(ImageParam.Brilho, +1);
            img.Step(ImageParam.Exposicao, +1);
            img.SetValue(ImageParam.Exposicao, 3);
            await Task.Delay(300);
            Assert.DoesNotContain(cam.Received, b => b.Length >= 5 && b[1] == 0x01 && b[2] == 0x04 && (b[3] == 0xA1 || b[3] == 0x0E || b[3] == 0x3E || b[3] == 0x4E));
            Assert.NotEmpty(notices);
        }
    }

    [Fact]
    public async Task HoldingAButton_RepeatsTheAdjustment_AndStopsWhenReleased()
    {
        int bright = 10;                                                         // a câmera simulada "lembra" o que recebeu
        using var cam = new ScriptedCamera(q =>
        {
            if (q.Length == 9 && q[1] == 0x01 && q[2] == 0x04 && q[3] == 0xA1) bright = ((q[6] & 0x0F) << 4) | (q[7] & 0x0F);
            return FullCamera(q, bright: bright);
        });
        var (cl, img) = await Connected(cam);
        using (cl)
        {
            await img.DetectAsync();
            img.SetHold(ImageParam.Brilho, +1, T0);                                  // primeiro passo na hora
            img.Tick(T0.AddMilliseconds(200));                                       // ainda no atraso inicial: nada novo
            img.Tick(T0.AddMilliseconds(500));
            img.Tick(T0.AddMilliseconds(700));
            img.SetHold(ImageParam.Brilho, 0, T0.AddMilliseconds(750));              // soltou
            int sent = 0;
            Assert.True(await cam.WaitFor(r => (sent = r.Count(b => b.Length == 9 && b[3] == 0xA1)) >= 3));
            img.Tick(T0.AddMilliseconds(2000)); img.Tick(T0.AddMilliseconds(3000));
            await Task.Delay(200);
            Assert.Equal(sent, cam.Received.Count(b => b.Length == 9 && b[3] == 0xA1));      // depois de soltar, nada mais é enviado
            Assert.Equal(13, img.Brilho.Value);                                              // 10 + 3 passos
        }
    }

    [Fact]
    public async Task WithoutAConnection_DetectionDoesNothingAndExplainsWhy()
    {
        using var cl = new ViscaClient();                                 // nunca conectado
        var img = new CameraImage(cl, () => new CameraConfig());
        await img.DetectAsync();
        Assert.Equal(ImageSupport.Desconhecido, img.Brilho.Support);
        Assert.Contains("Conecte", img.Brilho.Note);
    }
}
