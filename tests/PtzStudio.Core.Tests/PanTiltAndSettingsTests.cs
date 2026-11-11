using PtzStudio.Core.Control;
using PtzStudio.Core.Input;
using PtzStudio.Core.Models;
using PtzStudio.Core.Visca;
using Xunit;

namespace PtzStudio.Core.Tests;

/// <summary>Foco, Pan/Tilt completo (Analógico), polaridade dos eixos e opções da janela/bandeja.</summary>
public class PanTiltAndSettingsTests
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

    private static bool IsFocusStop(byte[] b) => b.Length == 6 && b[2] == 0x04 && b[3] == 0x08 && b[4] == 0x00;

    [Fact]
    public void FocusPulse_MovesFocus_ThenStops()
    {
        var (c, s) = Make();
        c.PulseFocus(1, 300, T0);
        c.Tick(0.02, T0);
        Assert.Contains(s.Sent, b => b.Length == 6 && b[3] == 0x08 && b[4] == 0x02);   // foco distante
        s.Sent.Clear();
        c.Tick(0.02, T0.AddMilliseconds(400));
        Assert.Contains(s.Sent, b => IsFocusStop(b));
    }

    // ---- Pan/Tilt completo (Analógico) ----
    private static InputSnapshot Snap(double x, double y)
    {
        var s = new InputSnapshot { Buttons = new bool[16] };
        for (int i = 0; i < s.Axes.Length; i++) s.Axes[i] = 0.5;
        s.Axes[0] = x; s.Axes[1] = y;
        return s;
    }

    [Fact]
    public void FullPanTilt_ClampsTheDiagonalVector_ToRadiusOne()
    {
        var map = new AxisMap { FullPanTilt = true, LimitDiagonal = true };
        var r = InputInterpreter.Interpret(Snap(1.0, 0.0), map, new double[8], Array.Empty<int>());   // canto: direita + cima
        Assert.True(r.X > 0 && r.Y > 0);
        Assert.True(Math.Sqrt(r.X * r.X + r.Y * r.Y) <= 1.0001);
        Assert.Equal(r.X, r.Y, 6);
    }

    [Fact]
    public void WithoutFullPanTilt_TheVectorIsNotLimited()
    {
        var map = new AxisMap { FullPanTilt = false };
        var r = InputInterpreter.Interpret(Snap(1.0, 0.0), map, new double[8], Array.Empty<int>());
        Assert.Equal(1.0, r.X, 6);
        Assert.Equal(1.0, r.Y, 6);
    }

    [Fact]
    public void FullPanTilt_CenteredStick_ProducesNoMovement()
    {
        var map = new AxisMap { FullPanTilt = true };
        var r = InputInterpreter.Interpret(Snap(0.5, 0.5), map, new double[8], Array.Empty<int>());
        Assert.Equal(0.0, r.X, 6);
        Assert.Equal(0.0, r.Y, 6);
    }

    [Fact]
    public void FullPanTilt_Diagonal_NeverExceedsEachAxisMaxSpeed()
    {
        var p = new MotionSettings();
        p.Pan.AccelPerSecond = 0; p.Tilt.AccelPerSecond = 0; p.Pan.NoiseThreshold = 0; p.Tilt.NoiseThreshold = 0;
        var proc = new PtzStudio.Core.Motion.MotionProcessor();
        var map = new AxisMap { FullPanTilt = true };
        var r = InputInterpreter.Interpret(Snap(1.0, 0.0), map, new double[8], Array.Empty<int>());
        var o = proc.Process(r.X, r.Y, 0, 0.02, p);
        Assert.True(Math.Abs(o.Pan) <= p.Pan.MaxSpeed + 1e-9);
        Assert.True(Math.Abs(o.Tilt) <= p.Tilt.MaxSpeed + 1e-9);
        Assert.True(o.Pan > 0 && o.Tilt > 0);   // diagonal: os dois eixos se movem ao mesmo tempo
    }

    [Fact]
    public void NewProfiles_HaveFullPanTiltEnabled_ButOldFilesKeepTheirBehavior()
    {
        Assert.All(AppSettings.CreateDefault().Profiles, pr => Assert.True(pr.AxisMap.FullPanTilt));
        var old = System.Text.Json.JsonSerializer.Deserialize<AxisMap>("{\"PanAxis\":0,\"TiltAxis\":1}")!;
        Assert.False(old.FullPanTilt);   // arquivo salvo antes desta opção: comportamento inalterado
    }

    [Fact]
    public void Layout_GetsTheNewButtons_WithoutRemovingSavedOnes()
    {
        var s = AppSettings.CreateDefault();
        s.Layout.Clear();
        s.Layout["A"] = 7;                      // personalização do usuário
        s.Layout["EXTRA1"] = 11;                // chave antiga: não pode ser apagada
        var n = PtzStudio.Core.Storage.SettingsStore.Normalize(s);
        Assert.Equal(7, n.Layout["A"]);
        Assert.Equal(11, n.Layout["EXTRA1"]);
        foreach (var k in new[] { "HOME", "SCREENSHOT", "M", "L4", "R4", "DPAD_UP", "LT", "RT" })
            Assert.True(n.Layout.ContainsKey(k), k);
    }

    [Fact]
    public void ReversedAxisPolarity_FlipsTheDirection_ForPanAndTilt()
    {
        var normal = new AxisMap { PanAxisReversed = false, TiltAxisReversed = false };
        var rev = new AxisMap { PanAxisReversed = true, TiltAxisReversed = true };
        var a = InputInterpreter.Interpret(Snap(1.0, 0.0), normal, new double[8], Array.Empty<int>());   // direita + cima
        var b = InputInterpreter.Interpret(Snap(1.0, 0.0), rev, new double[8], Array.Empty<int>());
        Assert.True(a.X > 0 && a.Y > 0);
        Assert.True(b.X < 0 && b.Y < 0);
        Assert.Equal(a.X, -b.X, 6);
        Assert.Equal(a.Y, -b.Y, 6);
    }

    [Fact]
    public void TraySettings_DefaultToOn_ForOldFilesWithoutThem()
    {
        var old = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{\"ActiveProfile\":\"Igreja\"}")!;
        Assert.True(old.MinimizeToTray);
        Assert.True(old.CloseToTray);
        Assert.Equal("Igreja", old.ActiveProfile);   // o que já estava salvo continua valendo
    }
}
