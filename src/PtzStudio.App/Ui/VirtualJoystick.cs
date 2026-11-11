using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PtzStudio.App.Ui;

/// <summary>Joystick virtual para o mouse: valores X (direita +) e Y (cima +) de -1 a 1, proporcionais ao deslocamento.</summary>
internal sealed class VirtualJoystick : Canvas
{
    private const double Size = 260, KnobR = 34;
    private readonly Ellipse _knob;
    public double X { get; private set; }
    public double Y { get; private set; }
    public event Action<double, double>? ValueChanged;

    public VirtualJoystick()
    {
        Width = Size; Height = Size; Background = Brushes.Transparent; Cursor = Cursors.Hand;
        var ring = new Ellipse
        {
            Width = Size - 6, Height = Size - 6, StrokeThickness = 3,
            Stroke = new SolidColorBrush(Color.FromRgb(0x3B, 0x43, 0x52)), Fill = new SolidColorBrush(Color.FromRgb(0x0F, 0x12, 0x17))
        };
        SetLeft(ring, 3); SetTop(ring, 3);
        Children.Add(ring);
        var inner = new Ellipse
        {
            Width = Size * 0.5, Height = Size * 0.5, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 4 },
            Stroke = new SolidColorBrush(Color.FromRgb(0x2A, 0x30, 0x3B))
        };
        SetLeft(inner, Size * 0.25); SetTop(inner, Size * 0.25);
        Children.Add(inner);
        var dash = new SolidColorBrush(Color.FromRgb(0x2A, 0x30, 0x3B));
        Children.Add(new Line { X1 = 20, X2 = Size - 20, Y1 = Size / 2, Y2 = Size / 2, Stroke = dash, StrokeThickness = 1 });
        Children.Add(new Line { Y1 = 20, Y2 = Size - 20, X1 = Size / 2, X2 = Size / 2, Stroke = dash, StrokeThickness = 1 });
        void Arrow(string t, double x, double y)
        {
            var tb = new TextBlock { Text = t, FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x74, 0x85)), IsHitTestVisible = false };
            SetLeft(tb, x); SetTop(tb, y);
            Children.Add(tb);
        }
        Arrow("▲", Size / 2 - 7, 8); Arrow("▼", Size / 2 - 7, Size - 26); Arrow("◀", 10, Size / 2 - 10); Arrow("▶", Size - 26, Size / 2 - 10);
        _knob = new Ellipse
        {
            Width = KnobR * 2, Height = KnobR * 2, Fill = Ux.Accent, Stroke = Ux.AccentLight, StrokeThickness = 4
        };
        Children.Add(_knob);
        Place(0, 0);

        MouseLeftButtonDown += (_, e) => { CaptureMouse(); Move(e.GetPosition(this)); };
        MouseMove += (_, e) => { if (IsMouseCaptured) Move(e.GetPosition(this)); };
        MouseLeftButtonUp += (_, _) => Release();
        LostMouseCapture += (_, _) => Release();
        Unloaded += (_, _) => Release();            // saiu da tela: nunca deixar o joystick "preso" fora do centro
    }

    private void Move(Point p)
    {
        double c = Size / 2, max = c - KnobR - 4;
        double dx = p.X - c, dy = p.Y - c;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len > max) { dx = dx / len * max; dy = dy / len * max; }
        Set(dx / max, -dy / max);
    }

    public void Release()
    {
        if (IsMouseCaptured) ReleaseMouseCapture();
        Set(0, 0);
    }

    private void Set(double x, double y)
    {
        if (x == X && y == Y) return;
        X = x; Y = y;
        Place(x, y);
        ValueChanged?.Invoke(x, y);
    }

    private void Place(double x, double y)
    {
        double c = Size / 2, max = c - KnobR - 4;
        SetLeft(_knob, c + x * max - KnobR);
        SetTop(_knob, c - y * max - KnobR);
    }
}
