using PtzStudio.Core.Models;

namespace PtzStudio.Core.Motion;

public static class AxisMath
{
    /// <summary>Remove a zona morta e reescala o restante para 0..1 (sem salto na saída da zona).</summary>
    public static double Deadzone(double v, double dz)
    {
        dz = Math.Clamp(dz, 0, 0.95);
        double a = Math.Abs(v);
        if (a <= dz) return 0;
        return Math.Sign(v) * Math.Min(1.0, (a - dz) / (1.0 - dz));
    }

    /// <summary>Curva de resposta: entrada 0..1 -> saída 0..1.</summary>
    public static double Curve(double a, ResponseCurve curve, double exponent)
    {
        a = Math.Clamp(a, 0, 1);
        exponent = Math.Clamp(exponent, 1.0, 6.0);
        return curve switch
        {
            ResponseCurve.Linear => a,
            ResponseCurve.Progressiva => Math.Pow(a, exponent),
            ResponseCurve.Suave => a * a * (3 - 2 * a),
            ResponseCurve.Agressiva => Math.Pow(a, 1.0 / exponent),
            _ => a
        };
    }

    /// <summary>Velocidade-alvo (com sinal) para uma entrada já sem zona morta.</summary>
    public static double TargetSpeed(double v, AxisSettings s)
    {
        double a = Math.Abs(v);
        if (a <= 0) return 0;
        double c = Curve(a, s.Curve, s.CurveExponent);
        int min = Math.Max(1, s.MinSpeed);
        int max = Math.Max(min, s.MaxSpeed);
        return Math.Sign(v) * (min + (max - min) * c);
    }

    /// <summary>Evita que um pequeno desvio no eixo secundário vire movimento diagonal.</summary>
    public static (double x, double y) ApplyDirectionRules(double x, double y, MotionSettings m)
    {
        double ax = Math.Abs(x), ay = Math.Abs(y);
        if (ax == 0 || ay == 0) return (x, y);
        if (m.LockSingleAxis) return ax >= ay ? (x, 0) : (0, y);

        double tol = Math.Clamp(m.AngularToleranceDeg, 0, 44);
        double tolH = Math.Min(44, tol * (m.Priority == AxisPriority.Horizontal ? 2 : 1));
        double tolV = Math.Min(44, tol * (m.Priority == AxisPriority.Vertical ? 2 : 1));
        double angle = Math.Atan2(ay, ax) * 180.0 / Math.PI; // 0° = horizontal puro, 90° = vertical puro
        if (angle < tolH) return (x, 0);
        if (90.0 - angle < tolV) return (0, y);
        return (x, y);
    }
}

/// <summary>Estado de um eixo: filtro de ruído/suavização e rampa de aceleração/desaceleração.</summary>
public sealed class AxisChannel
{
    private double _last;
    private double _cur;

    public double Current => _cur;
    public void Reset() { _last = 0; _cur = 0; }

    public double Filter(double v, AxisSettings s)
    {
        if (v == 0) { _last = 0; return 0; } // neutro nunca é segurado pelo filtro
        if (Math.Abs(v - _last) < s.NoiseThreshold) v = _last;
        else if (s.Smoothing > 0) v = _last + (v - _last) * (1 - Math.Clamp(s.Smoothing, 0, 0.95));
        _last = v;
        return v;
    }

    public double Step(double target, double dt, AxisSettings s)
    {
        if (target == 0 && !s.SoftStopOnRelease) { _cur = 0; return 0; }
        bool growing = _cur == 0 || (Math.Sign(target) == Math.Sign(_cur) && Math.Abs(target) > Math.Abs(_cur));
        double rate = growing ? s.AccelPerSecond : s.DecelPerSecond;
        if (rate <= 0) { _cur = target; return _cur; }
        double step = rate * dt;
        double diff = target - _cur;
        _cur = Math.Abs(diff) <= step ? target : _cur + Math.Sign(diff) * step;
        return _cur;
    }
}

public readonly record struct MotionOutput(double Pan, double Tilt, double Zoom);

public sealed class MotionProcessor
{
    private readonly AxisChannel _pan = new(), _tilt = new(), _zoom = new();

    public void Reset() { _pan.Reset(); _tilt.Reset(); _zoom.Reset(); }

    /// <summary>x: direita +, y: cima +, z: aproximar +. Saída em unidades de velocidade (com sinal).</summary>
    public MotionOutput Process(double x, double y, double z, double dt, MotionSettings m)
    {
        x = Math.Clamp(x * m.Pan.Sensitivity, -1, 1);
        y = Math.Clamp(y * m.Tilt.Sensitivity, -1, 1);
        z = Math.Clamp(z * m.Zoom.Sensitivity, -1, 1);
        if (m.Pan.Invert) x = -x;
        if (m.Tilt.Invert) y = -y;
        if (m.Zoom.Invert) z = -z;

        x = AxisMath.Deadzone(x, m.Pan.Deadzone);
        y = AxisMath.Deadzone(y, m.Tilt.Deadzone);
        z = AxisMath.Deadzone(z, m.Zoom.Deadzone);

        x = _pan.Filter(x, m.Pan);
        y = _tilt.Filter(y, m.Tilt);
        z = _zoom.Filter(z, m.Zoom);

        (x, y) = AxisMath.ApplyDirectionRules(x, y, m);

        double p = _pan.Step(AxisMath.TargetSpeed(x, m.Pan), dt, m.Pan);
        double t = _tilt.Step(AxisMath.TargetSpeed(y, m.Tilt), dt, m.Tilt);
        double zz = _zoom.Step(AxisMath.TargetSpeed(z, m.Zoom), dt, m.Zoom);
        return new MotionOutput(p, t, zz);
    }
}
