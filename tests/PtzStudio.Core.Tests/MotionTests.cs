using PtzStudio.Core.Models;
using PtzStudio.Core.Motion;
using Xunit;

namespace PtzStudio.Core.Tests;

public class MotionTests
{
    private static MotionSettings Instant()
    {
        var m = new MotionSettings();
        foreach (var a in new[] { m.Pan, m.Tilt, m.Zoom }) { a.AccelPerSecond = 0; a.DecelPerSecond = 0; a.NoiseThreshold = 0; a.Curve = ResponseCurve.Linear; a.MinSpeed = 1; a.MaxSpeed = 20; }
        return m;
    }

    [Fact]
    public void Deadzone_ZeroInside_FullAtOne_NoJumpAtEdge()
    {
        Assert.Equal(0, AxisMath.Deadzone(0.10, 0.12));
        Assert.Equal(1.0, AxisMath.Deadzone(1.0, 0.12), 6);
        Assert.True(AxisMath.Deadzone(0.13, 0.12) < 0.05);
        Assert.Equal(-1.0, AxisMath.Deadzone(-1.0, 0.12), 6);
    }

    [Theory]
    [InlineData(ResponseCurve.Linear)]
    [InlineData(ResponseCurve.Progressiva)]
    [InlineData(ResponseCurve.Suave)]
    [InlineData(ResponseCurve.Agressiva)]
    public void Curves_AreMonotonic_AndBounded(ResponseCurve c)
    {
        double prev = -1;
        for (double a = 0; a <= 1.0001; a += 0.05)
        {
            double v = AxisMath.Curve(a, c, 2.0);
            Assert.InRange(v, 0, 1);
            Assert.True(v >= prev);
            prev = v;
        }
        Assert.Equal(1.0, AxisMath.Curve(1.0, c, 2.0), 6);
    }

    [Fact]
    public void Speed_IsProportional_ToTilt()
    {
        var m = Instant();
        double Speed(double x) => new MotionProcessor().Process(x, 0, 0, 0.02, m).Pan;
        double s20 = Speed(0.2), s40 = Speed(0.4), s60 = Speed(0.6), s80 = Speed(0.8), s100 = Speed(1.0);
        Assert.True(s20 < s40 && s40 < s60 && s60 < s80 && s80 < s100);
        Assert.Equal(m.Pan.MaxSpeed, s100, 6);
    }

    [Fact]
    public void Progressive_IsSlowerThanLinear_AtMidRange()
    {
        var lin = Instant(); var pro = Instant(); pro.Pan.Curve = ResponseCurve.Progressiva;
        double L = new MotionProcessor().Process(0.5, 0, 0, 0.02, lin).Pan;
        double P = new MotionProcessor().Process(0.5, 0, 0, 0.02, pro).Pan;
        Assert.True(P < L);
    }

    [Fact]
    public void Horizontal_WithSmallVerticalDrift_IsNotDiagonal()
    {
        var m = Instant();
        var o = new MotionProcessor().Process(0.9, 0.22, 0, 0.02, m); // ~14° acima da horizontal, tolerância 18°
        Assert.True(o.Pan > 0);
        Assert.Equal(0, o.Tilt);
    }

    [Fact]
    public void RealDiagonal_IsPreserved()
    {
        var m = Instant();
        var o = new MotionProcessor().Process(0.7, 0.7, 0, 0.02, m);
        Assert.True(o.Pan > 0 && o.Tilt > 0);
    }

    [Fact]
    public void LockSingleAxis_KeepsOnlyDominant()
    {
        var m = Instant(); m.LockSingleAxis = true;
        var o = new MotionProcessor().Process(0.7, 0.6, 0, 0.02, m);
        Assert.True(o.Pan > 0);
        Assert.Equal(0, o.Tilt);
    }

    [Fact]
    public void Invert_FlipsSign_PerAxis()
    {
        var m = Instant(); m.Tilt.Invert = true;
        var o = new MotionProcessor().Process(0.5, 0.5, 0, 0.02, m);
        Assert.True(o.Pan > 0);
        Assert.True(o.Tilt < 0);
    }

    [Fact]
    public void Neutral_StopsImmediately_EvenWithLongDecel()
    {
        var m = new MotionSettings(); // rampas padrão ligadas
        var p = new MotionProcessor();
        for (int i = 0; i < 50; i++) p.Process(1, 0, 0, 0.02, m);
        var o = p.Process(0, 0, 0, 0.02, m);
        Assert.Equal(0, o.Pan);
    }

    [Fact]
    public void Acceleration_RampsUpGradually()
    {
        var m = new MotionSettings();
        m.Pan.AccelPerSecond = 10; m.Pan.Curve = ResponseCurve.Linear; m.Pan.MaxSpeed = 20;
        var p = new MotionProcessor();
        double a = p.Process(1, 0, 0, 0.1, m).Pan;
        double b = p.Process(1, 0, 0, 0.1, m).Pan;
        Assert.True(a < b && b <= 20);
        Assert.True(a < 5);
    }

    [Fact]
    public void SoftStop_DeceleratesInsteadOfCutting_WhenEnabled()
    {
        var m = Instant(); m.Pan.SoftStopOnRelease = true; m.Pan.DecelPerSecond = 10;
        var p = new MotionProcessor();
        p.Process(1, 0, 0, 0.02, m);
        var o = p.Process(0, 0, 0, 0.1, m);
        Assert.True(o.Pan > 0);
    }

    [Fact]
    public void Zoom_IsGradual_AndIndependent()
    {
        var m = Instant(); m.Zoom.MaxSpeed = 7;
        double z30 = new MotionProcessor().Process(0, 0, 0.3, 0.02, m).Zoom;
        double z90 = new MotionProcessor().Process(0, 0, 0.9, 0.02, m).Zoom;
        Assert.True(z30 > 0 && z90 > z30);
    }
}
