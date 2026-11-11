using PtzStudio.Core.Control;
using PtzStudio.Core.Input;
using PtzStudio.Core.Models;
using PtzStudio.Core.Visca;
using Xunit;

namespace PtzStudio.Core.Tests;

public class FakeSender : IViscaSender
{
    public List<byte[]> Sent = new();
    public void Send(byte[] payload) => Sent.Add(payload);
    public bool IsPanTiltStop(byte[] b) => b.Length == 9 && b[2] == 0x06 && b[6] == 0x03 && b[7] == 0x03;
    public bool IsPanTiltMove(byte[] b) => b.Length == 9 && b[2] == 0x06 && b[3] == 0x01 && !(b[6] == 3 && b[7] == 3);
}

public class ControllerTests
{
    private static (PtzController c, FakeSender s, Profile p) Make()
    {
        var s = new FakeSender();
        var p = new Profile();
        foreach (var a in new[] { p.Motion.Pan, p.Motion.Tilt, p.Motion.Zoom }) { a.AccelPerSecond = 0; a.DecelPerSecond = 0; a.NoiseThreshold = 0; }
        p.Motion.PanTiltSpeedPercent = 100;
        var cam = new CameraConfig { CommandIntervalMs = 0 };
        var c = new PtzController(s, () => cam, () => p);
        return (c, s, p);
    }

    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0);

    [Fact]
    public void Moves_ThenStops_WhenStickReturnsToNeutral()
    {
        var (c, s, _) = Make();
        c.SetAnalog(InputSource.Fisico, 1, 0, 0, T0);
        c.Tick(0.02, T0);
        Assert.Contains(s.Sent, b => s.IsPanTiltMove(b));
        s.Sent.Clear();
        var t = T0.AddMilliseconds(20);
        c.SetAnalog(InputSource.Fisico, 0, 0, 0, t);
        c.Tick(0.02, t);
        Assert.Contains(s.Sent, b => s.IsPanTiltStop(b));
    }

    [Fact]
    public void DoesNotRepeatIdenticalCommands_EveryTick()
    {
        var (c, s, _) = Make();
        for (int i = 0; i < 10; i++)
        {
            var t = T0.AddMilliseconds(20 * i);
            c.SetAnalog(InputSource.Fisico, 1, 0, 0, t);
            c.Tick(0.02, t);
        }
        Assert.Single(s.Sent.Where(b => s.IsPanTiltMove(b)));
    }

    [Fact]
    public void SafetyStop_SendsStop_AndBlocksUntilNeutral()
    {
        var (c, s, _) = Make();
        c.SetAnalog(InputSource.Fisico, 1, 0, 0, T0);
        c.Tick(0.02, T0);
        s.Sent.Clear();
        c.SafetyStop("teste");
        Assert.True(s.Sent.Count(b => s.IsPanTiltStop(b)) >= 2);
        s.Sent.Clear();
        var t = T0.AddMilliseconds(20);
        c.SetAnalog(InputSource.Fisico, 1, 0, 0, t);   // analógico ainda inclinado
        c.Tick(0.02, t);
        Assert.DoesNotContain(s.Sent, b => s.IsPanTiltMove(b));
        t = t.AddMilliseconds(20);
        c.SetAnalog(InputSource.Fisico, 0, 0, 0, t);
        c.Tick(0.02, t);
        t = t.AddMilliseconds(20);
        c.SetAnalog(InputSource.Fisico, 1, 0, 0, t);   // liberado após voltar ao neutro
        c.Tick(0.02, t);
        Assert.Contains(s.Sent, b => s.IsPanTiltMove(b));
    }

    [Fact]
    public void Watchdog_StopsWhenInputGoesSilent()
    {
        var (c, s, _) = Make();
        c.SetAnalog(InputSource.Fisico, 1, 0, 0, T0);
        c.Tick(0.02, T0);
        s.Sent.Clear();
        c.Tick(0.02, T0.AddMilliseconds(PtzController.WatchdogMs + 50)); // nenhuma entrada nova
        Assert.Contains(s.Sent, b => s.IsPanTiltStop(b));
    }

    [Fact]
    public void InactiveSource_IsIgnored()
    {
        var (c, s, _) = Make();
        c.ActiveSource = InputSource.Fisico;
        c.SetAnalog(InputSource.Virtual, 1, 0, 0, T0);
        c.Tick(0.02, T0);
        Assert.DoesNotContain(s.Sent, b => s.IsPanTiltMove(b));
    }

    [Fact]
    public void SwitchingSource_StopsMovement()
    {
        var (c, s, _) = Make();
        c.SetAnalog(InputSource.Fisico, 1, 0, 0, T0);
        c.Tick(0.02, T0);
        s.Sent.Clear();
        c.ActiveSource = InputSource.Virtual;
        Assert.Contains(s.Sent, b => s.IsPanTiltStop(b));
    }

    [Fact]
    public void Preset_Recall_StopsFirst()
    {
        var (c, s, _) = Make();
        c.RecallPreset(3);
        Assert.True(s.IsPanTiltStop(s.Sent[0]));
        Assert.Equal(new byte[] { 0x81, 0x01, 0x04, 0x3F, 0x02, 0x03, 0xFF }, s.Sent.Last());
    }

    [Fact]
    public void Pulse_GoesThroughInversion()
    {
        var (c, s, p) = Make();
        p.Motion.Pan.Invert = true;
        c.Pulse(1, 0, 0, 500, T0);
        c.Tick(0.02, T0);
        var mv = s.Sent.First(b => s.IsPanTiltMove(b));
        Assert.Equal(0x01, mv[6]); // invertido: entrada "direita" vira "esquerda"
    }
}

public class InputTests
{
    private static InputSnapshot Snap(double x = 0.5, double y = 0.5, double rz = 0.5)
    {
        var s = new InputSnapshot { Buttons = new bool[16] };
        s.Axes[0] = x; s.Axes[1] = y; s.Axes[4] = rz;
        return s;
    }

    [Fact]
    public void StickUp_BecomesPositiveY()
    {
        var r = InputInterpreter.Interpret(Snap(y: 0.0), new AxisMap(), new double[8], Array.Empty<int>());
        Assert.True(r.Y > 0.9);
    }

    [Fact]
    public void StickRight_BecomesPositiveX()
    {
        var r = InputInterpreter.Interpret(Snap(x: 1.0), new AxisMap(), new double[8], Array.Empty<int>());
        Assert.True(r.X > 0.9);
    }

    [Fact]
    public void Dpad_And_Buttons_AreReported()
    {
        var s = Snap(); s.Buttons[3] = true; s.Pov[0] = 9000;
        var r = InputInterpreter.Interpret(s, new AxisMap(), new double[8], Array.Empty<int>());
        Assert.Contains(3, r.Pressed);
        Assert.Contains(VirtualButtons.DpadRight, r.Pressed);
        Assert.DoesNotContain(VirtualButtons.DpadUp, r.Pressed);
    }

    [Fact]
    public void AnalogTrigger_BecomesVirtualButton()
    {
        var s = Snap(); s.Axes[5] = 0.9;
        var r = InputInterpreter.Interpret(s, new AxisMap(), new double[8], new[] { VirtualButtons.AxisPlus(5) });
        Assert.Contains(VirtualButtons.AxisPlus(5), r.Pressed);
    }

    [Fact]
    public void Router_HoldAndPressActions_AndCombos()
    {
        var p = new Profile();
        p.Bindings.Add(new ButtonBinding { Button = 0, Action = ButtonAction.PanEsquerda });
        p.Bindings.Add(new ButtonBinding { Button = 1, Action = ButtonAction.AcionarPreset, Parameter = 7 });
        p.Bindings.Add(new ButtonBinding { Button = 2, Action = ButtonAction.Home });
        p.Bindings.Add(new ButtonBinding { Button = 2, Modifier = 4, Action = ButtonAction.Parar });
        var r = new ButtonRouter();
        var fired = new List<(ButtonAction, int)>();
        r.Triggered += (a, prm) => fired.Add((a, prm));

        r.Update(new[] { 0 }, p);
        Assert.Equal(-1, r.HeldPan);
        r.Update(new[] { 1 }, p); r.Update(new[] { 1 }, p);
        Assert.Single(fired.Where(f => f.Item1 == ButtonAction.AcionarPreset && f.Item2 == 7)); // dispara só na borda
        r.Update(Array.Empty<int>(), p);
        r.Update(new[] { 2, 4 }, p);
        Assert.Contains(fired, f => f.Item1 == ButtonAction.Parar);
        Assert.DoesNotContain(fired, f => f.Item1 == ButtonAction.Home); // combo vence o botão isolado
    }

    [Fact]
    public void ConflictDetection()
    {
        var p = new Profile();
        p.Bindings.Add(new ButtonBinding { Button = 3, Action = ButtonAction.Home });
        Assert.NotNull(BindingValidator.FindConflict(p, 3, -1));
        Assert.Null(BindingValidator.FindConflict(p, 3, 5));
        Assert.Null(BindingValidator.FindConflict(p, 4, -1));
    }
}
