using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PtzStudio.App.Ui;

internal enum PartKind { Face, Pill, Dpad, Bumper, Trigger, Stick, StickPress, Plate }
internal enum ShapeKind { Ellipse, Rect, Poly }

/// <summary>
/// Uma peça clicável do controle. Elipse: centro (A,B) e raios (C,D). Retângulo: canto (A,B), largura C, altura D.
/// Polígono: lista de pontos. (ChipX, ChipY) é onde aparece a etiqueta com a função atribuída.
/// </summary>
internal sealed record PartDef(string Id, string Label, PartKind Kind, ShapeKind Shape, double A, double B, double C, double D,
    double ChipX, double ChipY, Point[]? Poly = null, string? Glyph = null);

/// <summary>
/// Desenho vetorial do GameSir Nova 2 Lite (frente e traseira), com o contorno traçado das fotos do modelo.
/// Cada peça é clicável, acende quando o botão físico é pressionado, mostra o que já tem função atribuída
/// e a posição dos analógicos em tempo real. A ligação peça → botão do Windows fica em Settings.Layout.
/// </summary>
internal sealed class ControllerView : Viewbox
{
    private readonly Canvas _c = new();
    private readonly Dictionary<string, Shape> _shapes = new();
    private readonly Dictionary<string, Brush> _idleFill = new();
    private readonly Dictionary<string, Brush> _idleStroke = new();
    private readonly Dictionary<string, double> _idleThickness = new();
    private readonly Dictionary<string, PartDef> _defs = new();
    private readonly Dictionary<string, Ellipse> _badges = new();
    private readonly Dictionary<string, TextBlock> _labels = new();
    private readonly Dictionary<string, Border> _chips = new();
    private readonly Dictionary<string, TextBlock> _chipText = new();
    private readonly HashSet<string> _pressed = new();
    private readonly HashSet<string> _assigned = new();
    private readonly Dictionary<string, double> _amount = new();
    private Ellipse? _dotL, _dotR;
    private readonly (double x, double y) _centerL = (224, 131), _centerR = (561, 232);

    public string? Selected { get; private set; }
    public bool Front { get; private set; } = true;
    public event Action<string>? PartClicked;

    private static SolidColorBrush Rgb(byte r, byte g, byte b)
    {
        var br = new SolidColorBrush(Color.FromRgb(r, g, b));
        br.Freeze();
        return br;
    }

    private static readonly Brush White = Rgb(0xFF, 0xFF, 0xFF);
    private static readonly Brush Orange = Rgb(0xE8, 0x54, 0x1E);
    private static readonly Brush Ink = Rgb(0x4B, 0x55, 0x63);
    private static readonly Brush EdgeLight = Rgb(0xC4, 0xC9, 0xD2);
    private static readonly Brush PillFill = Rgb(0xF6, 0xF7, 0xF9);
    private static readonly Brush DpadFill = Rgb(0x8A, 0x90, 0x99);
    private static readonly Brush DpadEdge = Rgb(0x6C, 0x72, 0x7C);
    private static readonly Brush DpadText = Rgb(0xD9, 0xDD, 0xE4);
    private static readonly Brush CapFill = Rgb(0x9D, 0xA3, 0xAD);
    private static readonly Brush WellFill = Rgb(0xE6, 0xE8, 0xEC);
    private static readonly Brush BumperFill = Rgb(0xE1, 0xE4, 0xEA);
    private static readonly Brush PlateFill = Rgb(0xB9, 0xBE, 0xC7);
    private static readonly Brush PlateEdge = Rgb(0x7F, 0x85, 0x90);
    private static readonly Brush PadFill = Rgb(0xC9, 0xCD, 0xD4);
    private static readonly Brush PressedFill = Rgb(0x22, 0xC5, 0x5E);
    private static readonly Brush SelectedEdge = Rgb(0x3B, 0x82, 0xF6);
    private static readonly Brush ChipFill = Rgb(0x1E, 0x3A, 0x6E);

    // ---------------- peças (coordenadas em pixels das fotos) ----------------
    private static Point[] Pts(params double[] xy)
    {
        var l = new List<Point>();
        for (int i = 0; i + 1 < xy.Length; i += 2) l.Add(new Point(xy[i], xy[i + 1]));
        return l.ToArray();
    }

    public static readonly PartDef[] FrontParts =
    {
        new("STICK_L", "", PartKind.Stick, ShapeKind.Ellipse, 218, 148, 48, 56, 0, 0),
        new("STICK_R", "", PartKind.Stick, ShapeKind.Ellipse, 556, 248, 48, 56, 0, 0),
        new("L3", "L3", PartKind.StickPress, ShapeKind.Ellipse, 224, 131, 30, 30, 118, 131),
        new("R3", "R3", PartKind.StickPress, ShapeKind.Ellipse, 561, 232, 30, 30, 556, 322),

        new("DPAD_UP", "▲", PartKind.Dpad, ShapeKind.Rect, 316, 194, 40, 44, 336, 178),
        new("DPAD_DOWN", "▼", PartKind.Dpad, ShapeKind.Rect, 316, 264, 40, 44, 336, 326),
        new("DPAD_LEFT", "◀", PartKind.Dpad, ShapeKind.Rect, 278, 231, 44, 40, 226, 251),
        new("DPAD_RIGHT", "▶", PartKind.Dpad, ShapeKind.Rect, 350, 231, 44, 40, 446, 251),

        new("Y", "Y", PartKind.Face, ShapeKind.Ellipse, 682, 103, 27, 27, 682, 60),
        new("X", "X", PartKind.Face, ShapeKind.Ellipse, 627, 153, 27, 27, 560, 153),
        new("B", "B", PartKind.Face, ShapeKind.Ellipse, 738, 152, 27, 27, 812, 152),
        new("A", "A", PartKind.Face, ShapeKind.Ellipse, 683, 203, 27, 27, 683, 246),

        new("HOME", "", PartKind.Pill, ShapeKind.Ellipse, 462, 70, 27, 27, 462, 34, null, "M 1,8 L 8,1 L 15,8 M 3,7 V 15 H 13 V 7"),
        new("VIEW", "", PartKind.Pill, ShapeKind.Rect, 356, 72, 36, 40, 372, 54, null, "M 1,5 H 10 V 14 H 1 Z M 5,1 H 14 V 10"),
        new("MENU", "", PartKind.Pill, ShapeKind.Rect, 527, 72, 36, 40, 548, 54, null, "M 1,3 H 15 M 1,8 H 15 M 1,13 H 15"),
        new("SCREENSHOT", "", PartKind.Pill, ShapeKind.Rect, 432, 113, 52, 24, 458, 148, null, "M 3,8 A 5,5 0 1 1 13,8 A 5,5 0 1 1 3,8 Z"),
        new("M", "M", PartKind.Pill, ShapeKind.Rect, 432, 292, 52, 24, 458, 334),

        new("LB", "LB", PartKind.Bumper, ShapeKind.Poly, 0, 0, 0, 0, 236, 66, Pts(188, 42, 198, 26, 270, 22, 282, 34, 268, 48, 196, 50)),
        new("RB", "RB", PartKind.Bumper, ShapeKind.Poly, 0, 0, 0, 0, 800, 60, Pts(734, 42, 724, 26, 652, 22, 640, 34, 654, 48, 726, 50)),
        new("LT", "LT", PartKind.Trigger, ShapeKind.Rect, 190, 0, 92, 22, 140, 12),
        new("RT", "RT", PartKind.Trigger, ShapeKind.Rect, 640, 0, 94, 22, 780, 12),
    };

    public static readonly PartDef[] RearParts =
    {
        new("RB", "RB", PartKind.Bumper, ShapeKind.Poly, 0, 0, 0, 0, 96, 40, Pts(170, 34, 186, 17, 248, 15, 266, 30, 266, 62, 172, 62)),
        new("RT", "RT", PartKind.Trigger, ShapeKind.Poly, 0, 0, 0, 0, 96, 100, Pts(172, 66, 266, 66, 262, 118, 236, 142, 196, 137, 176, 112)),
        new("LB", "LB", PartKind.Bumper, ShapeKind.Poly, 0, 0, 0, 0, 720, 40, Pts(552, 30, 568, 15, 632, 17, 650, 34, 650, 62, 552, 62)),
        new("LT", "LT", PartKind.Trigger, ShapeKind.Poly, 0, 0, 0, 0, 720, 100, Pts(552, 66, 650, 66, 646, 112, 626, 138, 584, 142, 556, 118)),
        new("R4", "R4", PartKind.Plate, ShapeKind.Poly, 0, 0, 0, 0, 300, 380, Pts(190, 303, 215, 288, 300, 283, 410, 283, 410, 340, 228, 349, 200, 334)),
        new("L4", "L4", PartKind.Plate, ShapeKind.Poly, 0, 0, 0, 0, 520, 380, Pts(430, 283, 470, 275, 600, 290, 622, 306, 606, 340, 590, 348, 430, 342)),
    };

    public static string Describe(string id) => id switch
    {
        "LT" => "Gatilho esquerdo (LT)", "RT" => "Gatilho direito (RT)",
        "LB" => "Botão superior esquerdo (LB)", "RB" => "Botão superior direito (RB)",
        "VIEW" => "Botão View", "MENU" => "Botão Menu", "HOME" => "Botão Home", "SCREENSHOT" => "Botão Screenshot (captura)",
        "M" => "Botão M",
        "L4" => "Botão traseiro L4", "R4" => "Botão traseiro R4",
        "L3" => "Clique do analógico esquerdo (L3)", "R3" => "Clique do analógico direito (R3)",
        "DPAD_UP" => "Direcional ↑", "DPAD_DOWN" => "Direcional ↓", "DPAD_LEFT" => "Direcional ←", "DPAD_RIGHT" => "Direcional →",
        "STICK_L" => "Analógico esquerdo (movimento)", "STICK_R" => "Analógico direito (movimento)",
        "A" or "B" or "X" or "Y" => "Botão " + id,
        _ when id.StartsWith("EXTRA") => "Botão extra " + id[^1],
        _ => "Botão " + id
    };

    public ControllerView()
    {
        Child = _c; Stretch = Stretch.Uniform; MinHeight = 300;
        Build(true);
    }

    // ---------------- construção ----------------
    public void Build(bool front)
    {
        Front = front;
        _assigned.Clear(); _pressed.Clear(); _amount.Clear(); Selected = null;
        _c.Children.Clear();
        _shapes.Clear(); _idleFill.Clear(); _idleStroke.Clear(); _idleThickness.Clear(); _defs.Clear();
        _badges.Clear(); _labels.Clear(); _chips.Clear(); _chipText.Clear();
        _dotL = _dotR = null;
        _c.Width = front ? ControllerArt.FrontW : ControllerArt.RearW;
        _c.Height = front ? ControllerArt.FrontH : ControllerArt.RearH;

        if (front) DrawFrontArt(); else DrawRearArt();

        var parts = front ? FrontParts : RearParts;
        foreach (var p in parts) AddPart(p);
        if (front)
        {
            _dotL = AddDot(_centerL.x, _centerL.y);
            _dotR = AddDot(_centerR.x, _centerR.y);
        }
        foreach (var p in parts) AddChip(p);
        Refresh();
    }

    private void DrawFrontArt()
    {
        _c.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(ControllerArt.FrontBody),
            Fill = new LinearGradientBrush(Color.FromRgb(0xF7, 0xF8, 0xFA), Color.FromRgb(0xD5, 0xD9, 0xE0), 90),
            Stroke = EdgeLight, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false
        });
        _c.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(ControllerArt.FrontShell), Fill = Rgb(0xA9, 0xAE, 0xB8), Opacity = 0.9, IsHitTestVisible = false
        });
        var disc = new Ellipse { Width = 122, Height = 122, Fill = Rgb(0x7F, 0x85, 0x8F), IsHitTestVisible = false };
        Canvas.SetLeft(disc, 336 - 61); Canvas.SetTop(disc, 251 - 61);
        _c.Children.Add(disc);
    }

    private void DrawRearArt()
    {
        _c.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(ControllerArt.RearBody),
            Fill = new LinearGradientBrush(Color.FromRgb(0xD8, 0xDB, 0xE1), Color.FromRgb(0xA7, 0xAC, 0xB5), 90),
            Stroke = EdgeLight, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false
        });
        void Pill(double x, double y, double w, double h, Brush fill)
        {
            var r = new Rectangle { Width = w, Height = h, RadiusX = h / 2, RadiusY = h / 2, Fill = fill, IsHitTestVisible = false };
            Canvas.SetLeft(r, x); Canvas.SetTop(r, y);
            _c.Children.Add(r);
        }
        Pill(368, 45, 42, 10, Rgb(0x6B, 0x72, 0x80));   // porta USB-C
        Pill(292, 122, 28, 13, Orange);                  // chave de trava do gatilho direito
        Pill(498, 124, 28, 13, Orange);                  // chave de trava do gatilho esquerdo
        for (int i = 0; i < 4; i++)
        {
            var d = new Ellipse { Width = 11, Height = 11, Fill = Rgb(0xB4, 0x83, 0x2E), IsHitTestVisible = false };
            Canvas.SetLeft(d, 372 + i * 21); Canvas.SetTop(d, 312);
            _c.Children.Add(d);
        }
        var tip = new TextBlock
        {
            Text = "Vista de trás: o lado direito do controle fica à esquerda da imagem.",
            Foreground = Rgb(0x5B, 0x61, 0x6B), FontSize = 13, IsHitTestVisible = false
        };
        Canvas.SetLeft(tip, 250); Canvas.SetTop(tip, 222);
        _c.Children.Add(tip);
    }

    private static (Brush fill, Brush stroke, double thick) PartStyle(PartKind k) => k switch
    {
        PartKind.Face => (White, EdgeLight, 2),
        PartKind.Pill => (PillFill, EdgeLight, 2),
        PartKind.Dpad => (DpadFill, DpadEdge, 2),
        PartKind.Bumper => (BumperFill, EdgeLight, 2),
        PartKind.Trigger => (BumperFill, EdgeLight, 2),
        PartKind.Stick => (WellFill, Orange, 8),
        PartKind.StickPress => (CapFill, DpadEdge, 2),
        _ => (PlateFill, PlateEdge, 2),
    };

    private static Rect Bounds(PartDef p)
    {
        if (p.Shape == ShapeKind.Ellipse) return new Rect(p.A - p.C, p.B - p.D, p.C * 2, p.D * 2);
        if (p.Shape == ShapeKind.Rect) return new Rect(p.A, p.B, p.C, p.D);
        double x0 = p.Poly!.Min(q => q.X), y0 = p.Poly!.Min(q => q.Y), x1 = p.Poly!.Max(q => q.X), y1 = p.Poly!.Max(q => q.Y);
        return new Rect(x0, y0, x1 - x0, y1 - y0);
    }

    private void AddPart(PartDef p)
    {
        var (fill, stroke, thick) = PartStyle(p.Kind);
        var bb = Bounds(p);
        Shape s;
        switch (p.Shape)
        {
            case ShapeKind.Ellipse:
                s = new Ellipse { Width = bb.Width, Height = bb.Height };
                break;
            case ShapeKind.Rect:
                double rad = Math.Min(bb.Width, bb.Height) / 3.2;
                s = new Rectangle { Width = bb.Width, Height = bb.Height, RadiusX = rad, RadiusY = rad };
                break;
            default:
                s = new Polygon { Points = new PointCollection(p.Poly!), StrokeLineJoin = PenLineJoin.Round };
                break;
        }
        if (p.Shape != ShapeKind.Poly) { Canvas.SetLeft(s, bb.X); Canvas.SetTop(s, bb.Y); }
        if (p.Kind == PartKind.Plate) fill = PlateFill;
        else if (!Front && (p.Kind == PartKind.Bumper || p.Kind == PartKind.Trigger)) fill = PadFill;
        s.Fill = fill; s.Stroke = stroke; s.StrokeThickness = thick; s.Cursor = System.Windows.Input.Cursors.Hand;
        string id = p.Id;
        s.MouseLeftButtonDown += (_, _) => Select(id);
        s.MouseEnter += (_, _) => s.Opacity = 0.82;
        s.MouseLeave += (_, _) => s.Opacity = 1.0;
        _c.Children.Add(s);
        _shapes[id] = s; _defs[id] = p;
        _idleFill[id] = fill; _idleStroke[id] = stroke; _idleThickness[id] = thick;

        if (p.Glyph != null)
        {
            var g = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(p.Glyph), Stroke = Ink, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false
            };
            Canvas.SetLeft(g, bb.X + bb.Width / 2 - 8); Canvas.SetTop(g, bb.Y + bb.Height / 2 - 8);
            _c.Children.Add(g);
        }
        if (!string.IsNullOrEmpty(p.Label))
        {
            bool face = p.Kind == PartKind.Face;
            var t = new TextBlock
            {
                Text = p.Label, FontWeight = FontWeights.Bold, IsHitTestVisible = false, TextAlignment = TextAlignment.Center, Width = bb.Width,
                FontSize = face ? 30 : p.Kind == PartKind.Dpad ? 18 : p.Kind == PartKind.Trigger && Front ? 12 : 15,
                Foreground = LabelBrush(p.Kind, false)
            };
            Canvas.SetLeft(t, bb.X);
            Canvas.SetTop(t, bb.Y + bb.Height / 2 - t.FontSize * 1.3 / 2);
            _c.Children.Add(t);
            _labels[id] = t;
        }
        var badge = new Ellipse { Width = 16, Height = 16, Fill = SelectedEdge, Stroke = White, StrokeThickness = 2, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        Canvas.SetLeft(badge, bb.Right - 11); Canvas.SetTop(badge, bb.Top - 5);
        _c.Children.Add(badge);
        _badges[id] = badge;
    }

    private static Brush LabelBrush(PartKind k, bool pressed) =>
        k == PartKind.Face ? Orange
        : pressed || k == PartKind.StickPress ? White
        : k == PartKind.Dpad ? DpadText
        : Ink;

    private void AddChip(PartDef p)
    {
        if (p.ChipX <= 0 && p.ChipY <= 0) return;
        var text = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = White, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 118 };
        var chip = new Border
        {
            Background = ChipFill, BorderBrush = SelectedEdge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9),
            Padding = new Thickness(9, 3, 9, 3), Child = text, Visibility = Visibility.Collapsed, IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        var host = new Grid { Width = 140, Height = 28, IsHitTestVisible = false };
        host.Children.Add(chip);
        Canvas.SetLeft(host, p.ChipX - 70); Canvas.SetTop(host, p.ChipY - 14);
        _c.Children.Add(host);
        _chips[p.Id] = chip; _chipText[p.Id] = text;
    }

    private Ellipse AddDot(double cx, double cy)
    {
        var e = new Ellipse
        {
            Width = 18, Height = 18, Fill = SelectedEdge, Stroke = White, StrokeThickness = 3, IsHitTestVisible = false
        };
        Canvas.SetLeft(e, cx - 8); Canvas.SetTop(e, cy - 8);
        _c.Children.Add(e);
        return e;
    }

    // ---------------- estado ----------------
    public void Select(string id)
    {
        Selected = id;
        Refresh();
        PartClicked?.Invoke(id);
    }

    /// <summary>Atualiza as cores: pressionado (verde), selecionado (borda azul) e atribuído (selo + etiqueta).</summary>
    private void Refresh()
    {
        foreach (var kv in _shapes)
        {
            string id = kv.Key; var s = kv.Value; var def = _defs[id];
            bool pressed = _pressed.Contains(id);
            Brush fill = _idleFill[id];
            if (def.Kind != PartKind.Stick)
            {
                if (pressed) fill = PressedFill;
                else if (def.Kind == PartKind.Trigger && _amount.TryGetValue(id, out var amt) && amt > 0.04)
                    fill = Blend(_idleFill[id], PressedFill, amt);
            }
            s.Fill = fill;
            if (id == Selected) { s.Stroke = SelectedEdge; s.StrokeThickness = def.Kind == PartKind.Stick ? 8 : 5; }
            else { s.Stroke = _idleStroke[id]; s.StrokeThickness = _idleThickness[id]; }
            if (_labels.TryGetValue(id, out var lbl) && def.Kind != PartKind.Face) lbl.Foreground = LabelBrush(def.Kind, pressed);
        }
        foreach (var kv in _badges) kv.Value.Visibility = _assigned.Contains(kv.Key) ? Visibility.Visible : Visibility.Collapsed;
        foreach (var kv in _chips) kv.Value.Visibility = _assigned.Contains(kv.Key) ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Brush Blend(Brush a, Brush b, double t)
    {
        var ca = ((SolidColorBrush)a).Color; var cb = ((SolidColorBrush)b).Color;
        t = Math.Clamp(t, 0, 1);
        byte L(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
        return new SolidColorBrush(Color.FromRgb(L(ca.R, cb.R), L(ca.G, cb.G), L(ca.B, cb.B)));
    }

    /// <summary>Peças pressionadas agora e, para os gatilhos, o quanto (0..1) estão apertados.</summary>
    public void SetPressed(ISet<string> parts, IDictionary<string, double> triggerAmount)
    {
        bool changed = !_pressed.SetEquals(parts);
        bool amountChanged = false;
        foreach (var kv in _defs)
        {
            if (kv.Value.Kind != PartKind.Trigger) continue;
            double v = triggerAmount.TryGetValue(kv.Key, out var x) ? Math.Clamp(x, 0, 1) : 0;
            _amount.TryGetValue(kv.Key, out var old);
            if (Math.Abs(old - v) > 0.02) { _amount[kv.Key] = v; amountChanged = true; }
        }
        if (!changed && !amountChanged) return;
        _pressed.Clear();
        foreach (var p in parts) _pressed.Add(p);
        Refresh();
    }

    /// <summary>Informa quais peças já têm função (id → texto curto da função). As demais ficam sem selo.</summary>
    public void SetAssigned(IDictionary<string, string> labels)
    {
        var next = new HashSet<string>();
        foreach (var kv in labels)
        {
            if (!_badges.ContainsKey(kv.Key)) continue;
            next.Add(kv.Key);
            if (_chipText.TryGetValue(kv.Key, out var t) && t.Text != kv.Value) t.Text = kv.Value;
        }
        if (next.SetEquals(_assigned) && labels.Count == _assigned.Count) return;
        _assigned.Clear();
        foreach (var k in next) _assigned.Add(k);
        Refresh();
    }

    /// <summary>Posição dos analógicos (-1..1). O ponto azul mostra X/Y em tempo real.</summary>
    public void SetSticks(double lx, double ly, double rx, double ry)
    {
        const double r = 17;
        if (_dotL != null) { Canvas.SetLeft(_dotL, _centerL.x - 8 + lx * r); Canvas.SetTop(_dotL, _centerL.y - 8 - ly * r); }
        if (_dotR != null) { Canvas.SetLeft(_dotR, _centerR.x - 8 + rx * r); Canvas.SetTop(_dotR, _centerR.y - 8 - ry * r); }
    }

    public void SetToolTip(string id, string text)
    {
        if (_shapes.TryGetValue(id, out var s)) s.ToolTip = text;
    }
}
