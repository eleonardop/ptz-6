using PtzStudio.Core.Models;

namespace PtzStudio.Core.Storage;

/// <summary>
/// Corrige apenas valores IMPOSSÍVEIS (NaN, infinito, fora da faixa que a própria tela permite) vindos de arquivos
/// corrompidos ou editados à mão. Nunca mexe em valores válidos: o ajuste do usuário (por exemplo, zona morta 0)
/// continua exatamente como ele salvou.
/// </summary>
public static class SettingsSanitizer
{
    private static double F(double v, double min, double max, double fallback) =>
        double.IsNaN(v) || double.IsInfinity(v) ? fallback : Math.Clamp(v, min, max);

    public static void Apply(AppSettings s)
    {
        foreach (var c in s.Cameras) Camera(c);
        foreach (var p in s.Profiles) Motion(p.Motion);
    }

    public static void Camera(CameraConfig c)
    {
        if (string.IsNullOrWhiteSpace(c.Name)) c.Name = "Câmera";
        c.IpAddress = (c.IpAddress ?? "").Trim();
        c.Port = Math.Clamp(c.Port, 1, 65535);
        c.ViscaAddress = Math.Clamp(c.ViscaAddress, 1, 7);
        c.MaxPanSpeed = Math.Clamp(c.MaxPanSpeed, 1, 24);
        c.MaxTiltSpeed = Math.Clamp(c.MaxTiltSpeed, 1, 20);
        c.MaxZoomSpeed = Math.Clamp(c.MaxZoomSpeed, 1, 8);
        c.TimeoutMs = Math.Clamp(c.TimeoutMs, 100, 10000);
        c.CommandIntervalMs = Math.Clamp(c.CommandIntervalMs, 10, 500);
        c.ReconnectAttempts = Math.Clamp(c.ReconnectAttempts, 1, 20);
    }

    public static void Motion(MotionSettings m)
    {
        Axis(m.Pan, 24); Axis(m.Tilt, 20); Axis(m.Zoom, 8);
        m.AngularToleranceDeg = F(m.AngularToleranceDeg, 0, 45, 18);
        m.PanTiltSpeedPercent = Math.Clamp(m.PanTiltSpeedPercent, 1, 100);
        m.VirtualZoomPercent = Math.Clamp(m.VirtualZoomPercent, 1, 100);
    }

    private static void Axis(AxisSettings a, int limit)
    {
        a.Sensitivity = F(a.Sensitivity, 0.2, 2.0, 1.0);
        a.Deadzone = F(a.Deadzone, 0.0, 0.5, 0.12);
        a.MaxSpeed = Math.Clamp(a.MaxSpeed, 1, limit);
        a.MinSpeed = Math.Clamp(a.MinSpeed, 1, a.MaxSpeed);
        a.CurveExponent = F(a.CurveExponent, 1.0, 4.0, 2.0);
        a.AccelPerSecond = F(a.AccelPerSecond, 0, 200, 40);
        a.DecelPerSecond = F(a.DecelPerSecond, 0, 400, 120);
        a.Smoothing = F(a.Smoothing, 0.0, 0.9, 0.0);
        a.NoiseThreshold = F(a.NoiseThreshold, 0.0, 0.1, 0.01);
        if (!Enum.IsDefined(a.Curve)) a.Curve = ResponseCurve.Progressiva;
    }
}
