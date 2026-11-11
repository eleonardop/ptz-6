using System.Windows;
using System.Windows.Controls;
using PtzStudio.App.Engine;
using PtzStudio.App.Ui;
using PtzStudio.Core.Input;
using PtzStudio.Core.Models;

namespace PtzStudio.App.Tabs;

internal sealed class AnalogTab : TabBase
{
    private readonly List<ProgressBar> _bars = new();
    private readonly TextBlock _live = Ux.Text("", 14);
    private readonly TextBlock _detectInfo = Ux.Text("", 14, false, Ux.Warn);
    private readonly TextBlock _wizard = Ux.Text("", 13, true, Ux.Warn);

    public AnalogTab(AppEngine e) : base(e) { Init(); }

    private static List<string> AxisItems()
    {
        var l = new List<string> { "(nenhum)" };
        l.AddRange(InputSnapshot.AxisNames);
        return l;
    }

    private UIElement AxisRow(string label, Func<int> get, Action<int> set)
    {
        ComboBox? combo = null;
        combo = Ux.Combo(AxisItems(), get() + 1, i => { set(i - 1); E.Save(); ResolveZoomConflict(); }, 160);
        var btn = Ux.Btn("Detectar eixo", () =>
        {
            if (!E.Pad.Connected) { MessageBox.Show("Nenhum controle detectado.", "Detectar eixo"); return; }
            _detectInfo.Text = $"Mova o analógico/gatilho de “{label}” até o fim…";
            E.BeginDetectAxis(a => { set(a); combo!.SelectedIndex = a + 1; E.Save(); _detectInfo.Text = $"Detectado: eixo {InputSnapshot.AxisNames[a]}."; ResolveZoomConflict(); });
        }, 130);
        return Ux.H(Ux.Text(label, 14).Also(t => t.Width = 230), combo, btn);
    }

    /// <summary>Evita que o mesmo eixo físico comande Pan/Tilt e Zoom ao mesmo tempo.</summary>
    private void ResolveZoomConflict()
    {
        var map = E.Profile.AxisMap;
        if (map.ZoomAxis >= 0 && (map.ZoomAxis == map.PanAxis || map.ZoomAxis == map.TiltAxis))
        {
            map.ZoomAxis = -1;
            E.Save();
            _wizard.Text = "Aviso: o eixo do zoom era o mesmo do Pan/Tilt. O zoom pelo analógico foi desativado para evitar conflito (use os gatilhos ou escolha outro eixo).";
            Dispatcher.BeginInvoke(new Action(Rebuild));
        }
    }

    /// <summary>Descobre sozinho os eixos (e a polaridade) do analógico que vai comandar Pan e Tilt.</summary>
    private void CalibrateStick()
    {
        if (!E.Pad.Connected) { MessageBox.Show("Nenhum controle detectado. Conecte o GameSir (USB ou Bluetooth) e tente de novo.", "Calibrar analógico"); return; }
        var map = E.Profile.AxisMap;

        void Step2()
        {
            _wizard.Text = "Passo 2 de 2: solte o analógico e depois empurre-o TODO PARA CIMA, segurando por um instante.";
            E.BeginDetectAxis(ay =>
            {
                if (ay == map.PanAxis) { Step2(); return; }      // era só o analógico voltando ao centro: espera de novo
                map.TiltAxis = ay;
                double v = E.LastSnapshot != null && ay < E.LastSnapshot.Axes.Length ? E.LastSnapshot.Axes[ay] : 0.5;
                map.TiltAxisReversed = v > 0.5;                  // em DirectInput o normal é "cima" = valor menor
                ResolveZoomConflict();
                E.Save();
                _wizard.Text = $"Pronto! Pan = eixo {InputSnapshot.AxisNames[map.PanAxis]}, Tilt = eixo {InputSnapshot.AxisNames[map.TiltAxis]}. Mova o analógico para conferir; depois teste a direção na câmera mais abaixo.";
                Dispatcher.BeginInvoke(new Action(Rebuild));
            });
        }

        _wizard.Text = "Passo 1 de 2: empurre o analógico escolhido TODO PARA A DIREITA e segure por um instante.";
        E.BeginDetectAxis(ax =>
        {
            map.PanAxis = ax;
            double v = E.LastSnapshot != null && ax < E.LastSnapshot.Axes.Length ? E.LastSnapshot.Axes[ax] : 0.5;
            map.PanAxisReversed = v < 0.5;                       // o normal é "direita" = valor maior
            Step2();
        });
    }

    protected override void Build()
    {
        var m = E.Profile.Motion;
        var map = E.Profile.AxisMap;

        // ---- Pan/Tilt completo (Analógico) ----
        bool onLeft = map.PanAxis == 0 && map.TiltAxis == 1;
        var stickCombo = Ux.Combo(new[] { "Analógico esquerdo (padrão)", "Analógico direito (calibrar)" }, onLeft ? 0 : 1, i =>
        {
            if (i == 0)
            {
                map.PanAxis = 0; map.TiltAxis = 1; map.PanAxisReversed = false; map.TiltAxisReversed = false;
                E.Save(); ResolveZoomConflict();
                _wizard.Text = "Analógico esquerdo selecionado (eixos X e Y). Se a direção estiver trocada, use “Calibrar automaticamente”.";
            }
            else CalibrateStick();
        }, 300);
        var full = Ux.Group("Pan/Tilt completo (Analógico)",
            Ux.V(
                Ux.Check("Ativar: um único analógico controla Pan e Tilt juntos (diagonais incluídas)", map.FullPanTilt, v =>
                {
                    map.FullPanTilt = v; E.Save();
                    if (v) ResolveZoomConflict();
                }),
                Ux.Text("Para cima = Tilt para cima · baixo = Tilt para baixo · esquerda/direita = Pan · diagonal = os dois ao mesmo tempo · centro = para. " +
                        "Quanto mais inclinado, mais rápido, sempre dentro dos limites abaixo.", 12, false, Ux.Muted),
                Ux.Labeled("Analógico", stickCombo, 170),
                Ux.Wrap(Ux.Btn("Calibrar automaticamente", CalibrateStick, 240, Ux.Accent)),
                _wizard,
                Ux.SliderRow("Zona morta (centro)", 0, 0.5, m.Pan.Deadzone, v => { m.Pan.Deadzone = v; m.Tilt.Deadzone = v; E.Save(); }),
                Ux.SliderRow("Sensibilidade", 0.3, 2.0, m.Pan.Sensitivity, v => { m.Pan.Sensitivity = v; m.Tilt.Sensitivity = v; E.Save(); }),
                Ux.SliderRow("Velocidade máxima do Pan", 1, 24, m.Pan.MaxSpeed, v => { m.Pan.MaxSpeed = (int)Math.Round(v); E.Save(); }, "0"),
                Ux.SliderRow("Velocidade máxima do Tilt", 1, 20, m.Tilt.MaxSpeed, v => { m.Tilt.MaxSpeed = (int)Math.Round(v); E.Save(); }, "0"),
                Ux.SliderRow("Aceleração (quanto menor, mais gradual)", 5, 120, m.Pan.AccelPerSecond, v => { m.Pan.AccelPerSecond = v; m.Tilt.AccelPerSecond = v; E.Save(); }, "0"),
                Ux.SliderRow("Desaceleração", 5, 240, m.Pan.DecelPerSecond, v => { m.Pan.DecelPerSecond = v; m.Tilt.DecelPerSecond = v; E.Save(); }, "0"),
                Ux.Wrap(
                    Ux.Check("Inverter eixo X (Pan)", m.Pan.Invert, v => { m.Pan.Invert = v; E.Save(); }),
                    Ux.Check("Inverter eixo Y (Tilt)", m.Tilt.Invert, v => { m.Tilt.Invert = v; E.Save(); })),
                Ux.Check("Limitar a diagonal (o vetor X/Y nunca passa do máximo)", map.LimitDiagonal, v => { map.LimitDiagonal = v; E.Save(); }),
                Ux.Text("Ao voltar ao centro (dentro da zona morta) o programa envia a parada na hora. Estes ajustes são os mesmos da aba Movimento, onde Pan, Tilt e Zoom também podem ser ajustados separadamente.", 12, false, Ux.Muted)));

        var axes = Ux.Group("Qual eixo do controle comanda cada movimento",
            Ux.V(
                AxisRow("Pan (esquerda/direita)", () => map.PanAxis, v => map.PanAxis = v),
                AxisRow("Tilt (cima/baixo)", () => map.TiltAxis, v => map.TiltAxis = v),
                AxisRow("Zoom (analógico)", () => map.ZoomAxis, v => map.ZoomAxis = v),
                AxisRow("Zoom aproximar (gatilho)", () => map.ZoomTeleTriggerAxis, v => map.ZoomTeleTriggerAxis = v),
                AxisRow("Zoom afastar (gatilho)", () => map.ZoomWideTriggerAxis, v => map.ZoomWideTriggerAxis = v),
                _detectInfo,
                Ux.Text("Os números dos eixos dependem do modo do controle (XInput/DInput). Use “Detectar eixo” e confira nas barras abaixo.", 13, false, Ux.Muted)));

        _bars.Clear();
        var barPanel = new StackPanel();
        for (int i = 0; i < 8; i++)
        {
            var pb = new ProgressBar { Minimum = 0, Maximum = 1, Height = 16, Width = 260, Margin = new Thickness(4) };
            _bars.Add(pb);
            barPanel.Children.Add(Ux.H(Ux.Text(InputSnapshot.AxisNames[i], 13).Also(t => t.Width = 70), pb));
        }
        var liveGroup = Ux.Group("Valores em tempo real", Ux.V(barPanel, _live));

        var dir = Ux.Group("Diagonais e tolerância",
            Ux.V(
                Ux.SliderRow("Tolerância angular (graus)", 0, 44, m.AngularToleranceDeg, v => { m.AngularToleranceDeg = v; E.Save(); }, "0"),
                Ux.Labeled("Prioridade de eixo", Ux.Combo(new[] { "Nenhuma", "Horizontal (favorece Pan)", "Vertical (favorece Tilt)" }, (int)m.Priority, i => { m.Priority = (AxisPriority)i; E.Save(); }, 260)),
                Ux.Check("Restringir a UM eixo por vez (sem diagonal)", m.LockSingleAxis, v => { m.LockSingleAxis = v; E.Save(); }),
                Ux.Text("Tolerância angular: se o analógico estiver quase na horizontal (ou vertical), o desvio pequeno no outro eixo é ignorado e o movimento não vira diagonal.", 13, false, Ux.Muted)));

        void Pulse(double x, double y, double z) => E.Controller.Pulse(x, y, z, 700, DateTime.UtcNow);
        var calib = Ux.Group("Calibração: teste a direção real na câmera",
            Ux.V(
                Ux.Text("Cada botão simula o analógico naquela direção por 0,7 s (já com as inversões ativas). Se a câmera for para o lado errado, marque “Inverter eixo X (Pan)” ou “Inverter eixo Y (Tilt)” no cartão do topo; para o zoom, use “Inverter direção” na aba Movimento.", 13, false, Ux.Muted),
                Ux.Wrap(
                    Ux.Btn("Analógico → DIREITA", () => Pulse(1, 0, 0), 190), Ux.Btn("Analógico ← ESQUERDA", () => Pulse(-1, 0, 0), 190),
                    Ux.Btn("Analógico ↑ CIMA", () => Pulse(0, 1, 0), 190), Ux.Btn("Analógico ↓ BAIXO", () => Pulse(0, -1, 0), 190),
                    Ux.Btn("Zoom: aproximar", () => Pulse(0, 0, 0.6), 190), Ux.Btn("Zoom: afastar", () => Pulse(0, 0, -0.6), 190))));

        var page = new StackPanel();
        page.Children.Add(Ux.Header("Analógicos", "Escolha qual analógico comanda a câmera, ajuste o centro e calibre a direção."));
        page.Children.Add(full); page.Children.Add(axes); page.Children.Add(liveGroup); page.Children.Add(dir); page.Children.Add(calib);
        Content = Ux.Scroll(page);
    }

    protected override void Refresh()
    {
        var s = E.LastSnapshot;
        if (s == null) { _live.Text = "Controle não detectado."; return; }
        for (int i = 0; i < _bars.Count && i < s.Axes.Length; i++) _bars[i].Value = s.Axes[i];
        string Dir(double v, string neg, string pos) => Math.Abs(v) < 0.05 ? "neutro" : (v > 0 ? pos : neg);
        _live.Text = $"Entrada processada → X {E.Last.X:+0.00;-0.00;0.00} ({Dir(E.Last.X, "esquerda", "direita")})   " +
                     $"Y {E.Last.Y:+0.00;-0.00;0.00} ({Dir(E.Last.Y, "baixo", "cima")})   Zoom {E.Last.Z:+0.00;-0.00;0.00} ({Dir(E.Last.Z, "afastar", "aproximar")})\n" +
                     $"Enviado à câmera → Pan {E.Controller.SentPan:+0;-0;0}  Tilt {E.Controller.SentTilt:+0;-0;0}  Zoom {E.Controller.SentZoom:+0;-0;0}";
    }
}

internal static class Ext
{
    public static T Also<T>(this T x, Action<T> a) { a(x); return x; }
}
