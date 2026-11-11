using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using PtzStudio.Core.Models;
using PtzStudio.Core.Motion;

namespace PtzStudio.App.Ui;

/// <summary>Todos os ajustes de um eixo (Pan, Tilt ou Zoom) com pré-visualização da curva entrada -> velocidade.</summary>
internal sealed class AxisSettingsPanel : StackPanel
{
    private readonly AxisSettings _s;
    private readonly int _limit;
    private readonly Polyline _line = new() { Stroke = Ux.Accent, StrokeThickness = 3 };
    private readonly TextBlock _warn = Ux.Text("", 13, true, Ux.Warn);
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(700) };

    public AxisSettingsPanel(AxisSettings s, int speedLimit, Action save)
    {
        _s = s; _limit = speedLimit;
        _debounce.Tick += (_, _) => { _debounce.Stop(); save(); };
        void Changed() { Redraw(); _debounce.Stop(); _debounce.Start(); }

        Children.Add(_warn);
        Children.Add(Ux.Check("Inverter direção", s.Invert, v => { s.Invert = v; Changed(); }));
        Children.Add(Ux.SliderRow("Sensibilidade (multiplicador da entrada)", 0.2, 2.0, s.Sensitivity, v => { s.Sensitivity = v; Changed(); }));
        Children.Add(Ux.SliderRow("Zona morta (deadzone)", 0.0, 0.5, s.Deadzone, v => { s.Deadzone = v; Changed(); }));
        Children.Add(Ux.SliderRow("Velocidade mínima", 1, speedLimit, s.MinSpeed, v => { s.MinSpeed = (int)Math.Round(v); Changed(); }, "0"));
        Children.Add(Ux.SliderRow("Velocidade máxima", 1, speedLimit, s.MaxSpeed, v => { s.MaxSpeed = (int)Math.Round(v); Changed(); }, "0"));
        var curveRow = Ux.Labeled("Curva de resposta", Ux.Combo(new[] { "Linear", "Progressiva (suave no início)", "Suave (S)", "Agressiva" }, (int)s.Curve, i => { s.Curve = (ResponseCurve)i; Changed(); }, 260));
        Children.Add(curveRow);
        Children.Add(Ux.SliderRow("Intensidade da curva (expoente)", 1.0, 4.0, s.CurveExponent, v => { s.CurveExponent = v; Changed(); }));
        Children.Add(Ux.SliderRow("Aceleração (unid./s; 0 = instantânea)", 0, 200, s.AccelPerSecond, v => { s.AccelPerSecond = v; Changed(); }, "0"));
        Children.Add(Ux.SliderRow("Desaceleração (unid./s; 0 = instantânea)", 0, 400, s.DecelPerSecond, v => { s.DecelPerSecond = v; Changed(); }, "0"));
        Children.Add(Ux.SliderRow("Suavização", 0.0, 0.9, s.Smoothing, v => { s.Smoothing = v; Changed(); }));
        Children.Add(Ux.SliderRow("Filtro de ruído", 0.0, 0.1, s.NoiseThreshold, v => { s.NoiseThreshold = v; Changed(); }, "0.000"));
        Children.Add(Ux.Check("Parada suave ao soltar o analógico (por padrão a câmera para na hora)", s.SoftStopOnRelease, v => { s.SoftStopOnRelease = v; Changed(); }));

        var box = new Border
        {
            BorderBrush = Ux.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Width = 260, Height = 150,
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0), Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x12, 0x17))
        };
        var cv = new Canvas { Width = 258, Height = 148, ClipToBounds = true };
        for (int g = 1; g < 4; g++)      // linhas de grade discretas
        {
            cv.Children.Add(new Line { X1 = 0, X2 = 258, Y1 = g * 37, Y2 = g * 37, Stroke = Ux.Edge, StrokeThickness = 1 });
            cv.Children.Add(new Line { Y1 = 0, Y2 = 148, X1 = g * 64.5, X2 = g * 64.5, Stroke = Ux.Edge, StrokeThickness = 1 });
        }
        cv.Children.Add(_line);
        box.Child = cv;
        Children.Add(Ux.Text("Prévia: inclinação do analógico (→) × velocidade da câmera (↑)", 13, false, Ux.Muted));
        Children.Add(box);
        Redraw();
    }

    private void Redraw()
    {
        var pts = new PointCollection();
        for (int i = 0; i <= 50; i++)
        {
            double input = i / 50.0;
            double v = AxisMath.Deadzone(Math.Clamp(input * _s.Sensitivity, 0, 1), _s.Deadzone);
            double speed = Math.Abs(AxisMath.TargetSpeed(v, _s));
            pts.Add(new Point(input * 256 + 1, 146 - Math.Min(1.0, speed / Math.Max(1, _limit)) * 142));
        }
        _line.Points = pts;

        // Avisos (não alteram nada: só avisam quando um ajuste pode atrapalhar a operação)
        var w = new List<string>();
        if (_s.Deadzone < 0.03) w.Add("Zona morta muito baixa: o analógico em repouso pode mover a câmera sozinho.");
        if (_s.NoiseThreshold >= 0.05) w.Add("Filtro de ruído alto: o movimento passa a andar em degraus e perde a gradualidade.");
        if (w.Count > 0) w.Add("Se não foi intencional, use “Restaurar padrões deste perfil” (no topo desta aba).");
        _warn.Text = string.Join("\n", w);
        _warn.Visibility = w.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
