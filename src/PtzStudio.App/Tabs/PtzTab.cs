using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using PtzStudio.App.Engine;
using PtzStudio.App.Ui;
using PtzStudio.Core.Models;
using PtzStudio.Core.Visca;

namespace PtzStudio.App.Tabs;

/// <summary>Controle PTZ: joystick virtual, velocidade Pan/Tilt, zoom, imagem (brilho/exposição), entrada ativa e presets rápidos.</summary>
internal sealed class PtzTab : TabBase
{
    private VirtualJoystick _joy = new();
    private readonly TextBlock _info = Ux.Text("", 13, false, Ux.Muted);
    private readonly TextBlock _sent = Ux.Text("", 14, true);
    private readonly TextBlock _ptEffect = Ux.Text("", 12, false, Ux.Muted);
    private readonly TextBlock _zoomEffect = Ux.Text("", 12, false, Ux.Muted);
    private readonly List<RadioButton> _srcBtns = new();
    private readonly StackPanel _imageHost = new();
    private readonly Dictionary<ImageParam, (Slider slider, TextBlock value)> _imgRows = new();
    private Window? _win;
    private string _imgSig = "";
    private bool _imgUpdating;

    public PtzTab(AppEngine e) : base(e)
    {
        Init();
        // Janela desativada (Alt+Tab, clique fora) ou aba fechada: o joystick e o zoom da tela voltam ao centro e a câmera para.
        Loaded += (_, _) => { _win = Window.GetWindow(this); if (_win != null) _win.Deactivated += OnDeactivated; };
        Unloaded += (_, _) => { if (_win != null) _win.Deactivated -= OnDeactivated; _win = null; ReleaseAll(); };
    }

    private void OnDeactivated(object? s, EventArgs a) => ReleaseAll();
    private void ReleaseAll() { _joy.Release(); E.VirtualX = E.VirtualY = E.VirtualZ = 0; }

    /// <summary>Botão que repete enquanto está apertado (primeiro passo na hora, depois em intervalos curtos).</summary>
    private static void Repeat(Button b, Action step)
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        void Stop() { t.Stop(); }
        t.Tick += (_, _) => { t.Interval = TimeSpan.FromMilliseconds(140); step(); };
        b.PreviewMouseLeftButtonDown += (_, _) => { b.CaptureMouse(); step(); t.Interval = TimeSpan.FromMilliseconds(450); t.Start(); };
        b.PreviewMouseLeftButtonUp += (_, _) => { b.ReleaseMouseCapture(); Stop(); };
        b.LostMouseCapture += (_, _) => Stop();
        b.Unloaded += (_, _) => Stop();
    }

    private static Button Hold(string glyph, string text, Action down, Action up)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(new TextBlock { Text = glyph, FontSize = 30, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock { Text = text, FontSize = 17, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var b = new Button { Content = content, MinHeight = 76, MinWidth = 220, Margin = new Thickness(0, 0, 0, 10), HorizontalAlignment = HorizontalAlignment.Stretch };
        b.PreviewMouseLeftButtonDown += (_, _) => { b.CaptureMouse(); down(); };
        b.PreviewMouseLeftButtonUp += (_, _) => { b.ReleaseMouseCapture(); up(); };
        b.LostMouseCapture += (_, _) => up();
        return b;
    }

    protected override void Build()
    {
        E.VirtualX = E.VirtualY = E.VirtualZ = 0;     // nada pode ficar "apertado" ao reconstruir a tela
        _srcBtns.Clear(); _imgRows.Clear(); _imgSig = "";
        var m = E.Profile.Motion;

        // ---------- Pan / Tilt: joystick virtual (volta sozinho ao centro ao soltar) ----------
        _joy = new VirtualJoystick { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 10) };
        _joy.ValueChanged += (x, y) => { E.VirtualX = x; E.VirtualY = y; };
        var panTilt = Ux.Card(Ux.V(
            _joy,
            Ux.Text("Arraste para mover; ao soltar, o joystick volta ao centro e a câmera para. Usa a curva e a zona morta do perfil ativo.", 12, false, Ux.Muted)),
            "PAN / TILT  ·  JOYSTICK VIRTUAL");

        // ---------- Velocidade Pan/Tilt (por perfil) ----------
        var speed = Ux.Card(Ux.V(
            Ux.SliderRow("Velocidade Pan/Tilt", 1, 100, m.PanTiltSpeedPercent, v =>
            {
                m.PanTiltSpeedPercent = (int)Math.Round(v); E.Save(); UpdateEffects();
            }, "0\\%", true),
            _ptEffect,
            _sent),
            $"VELOCIDADE · PERFIL {E.Settings.ActiveProfile.ToUpperInvariant()}");

        // ---------- Zoom (independente da velocidade Pan/Tilt) ----------
        var zoomIn = Hold("+", "Aproximar", () => E.VirtualZ = 1, () => E.VirtualZ = 0);
        var zoomOut = Hold("−", "Afastar", () => E.VirtualZ = -1, () => E.VirtualZ = 0);
        var zoom = Ux.Card(Ux.V(
            zoomIn, zoomOut,
            Ux.SliderRow("Intensidade do zoom", 1, 100, m.VirtualZoomPercent, v =>
            {
                m.VirtualZoomPercent = (int)Math.Round(v); E.Save(); UpdateEffects();
            }, "0\\%", true),
            _zoomEffect,
            Ux.Text("Segure o botão: ao soltar, o zoom para na hora. Não depende da velocidade Pan/Tilt.", 12, false, Ux.Muted)),
            "ZOOM");

        // ---------- Imagem: brilho e exposição (só o que a câmera confirmar) ----------
        BuildImageCard();
        var image = Ux.Card(_imageHost, "IMAGEM DA CÂMERA");

        // ---------- Entrada ativa ----------
        var phys = new RadioButton { Content = "Joystick físico (GameSir)", GroupName = "entrada", FontSize = 14 };
        var virt = new RadioButton { Content = "Joystick virtual (mouse)", GroupName = "entrada", FontSize = 14 };
        _srcBtns.Add(phys); _srcBtns.Add(virt);
        phys.IsChecked = E.Controller.ActiveSource == InputSource.Fisico;
        virt.IsChecked = E.Controller.ActiveSource == InputSource.Virtual;
        phys.Checked += (_, _) => { if (E.Controller.ActiveSource != InputSource.Fisico) E.SetSource(InputSource.Fisico); };
        virt.Checked += (_, _) => { if (E.Controller.ActiveSource != InputSource.Virtual) E.SetSource(InputSource.Virtual); };
        var source = Ux.Card(Ux.V(Ux.Wrap(phys, virt),
            Ux.Text("Só uma entrada controla a câmera por vez. Trocar de entrada para o movimento na hora.", 12, false, Ux.Muted), _info), "ENTRADA ATIVA");

        var leftCol = new StackPanel();
        leftCol.Children.Add(panTilt); leftCol.Children.Add(speed);
        var midCol = new StackPanel();
        midCol.Children.Add(zoom); midCol.Children.Add(image);

        // ---------- Presets rápidos (mesma lógica de sempre: acionar o slot da câmera) ----------
        var grid = new UniformGrid { Columns = 2 };
        foreach (var p in E.Settings.Presets.Where(x => x.CameraId == E.Camera.Id))
        {
            int slot = p.Slot;
            var face = new StackPanel();
            face.Children.Add(new TextBlock { Text = p.Slot.ToString(), FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Ux.AccentLight });
            face.Children.Add(new TextBlock { Text = p.Name, FontSize = 12, Foreground = Ux.Muted, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 120 });
            var btn = new Button { Content = face, MinHeight = 66, Margin = new Thickness(3), HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(14, 8, 10, 8) };
            btn.Click += (_, _) => E.Controller.RecallPreset(slot);
            btn.ToolTip = p.ConfirmedOnCamera ? "Gravado na memória da câmera" : "Gravação ainda sem confirmação da câmera";
            grid.Children.Add(btn);
        }
        UIElement presetBody = grid.Children.Count > 0
            ? grid
            : Ux.Text("Nenhum preset cadastrado. Grave posições na aba Presets.", 13, false, Ux.Muted);
        var presets = Ux.Card(Ux.V(presetBody, Ux.Btn("Posição inicial (Home)", () => E.Controller.Home(), 220)), "PRESETS RÁPIDOS");

        var rightCol = new StackPanel();
        rightCol.Children.Add(source); rightCol.Children.Add(presets);

        // Três colunas de largura fixa dentro de um WrapPanel: em janelas estreitas a última coluna desce para a linha de baixo.
        leftCol.Width = 340; midCol.Width = 290; rightCol.Width = 360;
        var g = new WrapPanel { Margin = new Thickness(22, 18, 10, 6) };
        g.Children.Add(leftCol); g.Children.Add(midCol); g.Children.Add(rightCol);

        var page = new StackPanel();
        page.Children.Add(Ux.Header("Controle PTZ", "Opere a câmera com o joystick virtual, o zoom e os presets."));
        page.Children.Add(g);
        Content = Ux.Scroll(page);
        UpdateEffects();
        if (E.Image.Detected) E.Image.Refresh();      // lê os valores atuais da câmera ao abrir a tela
    }

    // ---------------- imagem ----------------
    private string ImageSignature()
    {
        var i = E.Image;
        return $"{i.Detecting}|{i.Detected}|{i.Brilho.Support}|{i.Exposicao.Support}|{i.Brilho.Note}|{i.Exposicao.Note}|{E.Client.State}";
    }

    private void BuildImageCard()
    {
        _imageHost.Children.Clear(); _imgRows.Clear();
        _imgSig = ImageSignature();
        var img = E.Image;

        if (img.Detecting) { _imageHost.Children.Add(Ux.Text("Consultando a câmera…", 13, false, Ux.Warn)); return; }

        bool any = false;
        foreach (var (param, title) in new[] { (ImageParam.Brilho, "Brilho"), (ImageParam.Exposicao, "Exposição") })
        {
            var st = img.Get(param);
            if (st.Support != ImageSupport.Suportado) continue;
            any = true;
            var val = Ux.Text("", 14, true); val.HorizontalAlignment = HorizontalAlignment.Right; val.Margin = new Thickness(0);
            var head = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var lbl = Ux.Text(title, 14); lbl.Margin = new Thickness(0);
            Grid.SetColumn(val, 1);
            head.Children.Add(lbl); head.Children.Add(val);

            var slider = new Slider { Minimum = st.Min, Maximum = Math.Max(st.Max, st.Min + 1), Value = Math.Clamp(st.Value, st.Min, Math.Max(st.Max, st.Min + 1)), Height = 28, VerticalAlignment = VerticalAlignment.Center };
            var debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            debounce.Tick += (_, _) => { debounce.Stop(); E.Image.SetValue(param, (int)Math.Round(slider.Value)); };
            slider.ValueChanged += (_, _) => { if (_imgUpdating) return; debounce.Stop(); debounce.Start(); };

            var minus = new Button { Content = "−", Width = 40, MinHeight = 36, FontSize = 18, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 0) };
            var plus = new Button { Content = "+", Width = 40, MinHeight = 36, FontSize = 18, Padding = new Thickness(0), Margin = new Thickness(6, 0, 0, 0) };
            Repeat(minus, () => E.Image.Step(param, -1));
            Repeat(plus, () => E.Image.Step(param, +1));
            var reset = new Button { Content = "↺", Width = 36, MinHeight = 36, Padding = new Thickness(0), Margin = new Thickness(6, 0, 0, 0), ToolTip = "Voltar ao valor de quando a câmera foi detectada" };
            reset.Click += (_, _) => E.Image.RestoreInitial(param);

            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 8) };
            DockPanel.SetDock(minus, Dock.Left); DockPanel.SetDock(reset, Dock.Right); DockPanel.SetDock(plus, Dock.Right);
            row.Children.Add(minus); row.Children.Add(reset); row.Children.Add(plus); row.Children.Add(slider);
            _imageHost.Children.Add(head); _imageHost.Children.Add(row);
            _imgRows[param] = (slider, val);
        }

        if (!any)
        {
            string msg = img.Detected
                ? "A câmera não confirmou ajuste de brilho nem de exposição por este protocolo. Nada foi enviado."
                : E.Client.State == ConnectionState.Conectado ? "Ajustes de imagem ainda não detectados." : "Conecte a câmera para detectar os ajustes de imagem.";
            _imageHost.Children.Add(Ux.Text(msg, 13, false, Ux.Muted));
            foreach (var n in new[] { img.Brilho.Note, img.Exposicao.Note }.Where(x => !string.IsNullOrEmpty(x)).Distinct())
                _imageHost.Children.Add(Ux.Text(n, 12, false, Ux.Muted));
        }
        else _imageHost.Children.Add(Ux.Text("Os botões do controle (aba Mapeamento) ajustam os mesmos valores.", 12, false, Ux.Muted));

        var detect = Ux.Btn("Detectar ajustes de imagem", () => { _ = E.Image.DetectAsync(); }, 200);
        detect.Margin = new Thickness(0, 4, 0, 0);
        _imageHost.Children.Add(detect);
    }

    private void SyncImageValues()
    {
        _imgUpdating = true;
        try
        {
            foreach (var (param, row) in _imgRows)
            {
                var st = E.Image.Get(param);
                if (!row.slider.IsMouseCaptureWithin)
                {
                    row.slider.Maximum = Math.Max(st.Max, st.Min + 1);
                    row.slider.Value = Math.Clamp(st.Value, st.Min, row.slider.Maximum);
                }
                row.value.Text = param == ImageParam.Exposicao
                    ? (st.Value - 7 == 0 ? "0 EV" : $"{st.Value - 7:+0;-0} passos")      // 7 = sem compensação
                    : st.Value.ToString();
            }
        }
        finally { _imgUpdating = false; }
    }

    // ---------------- textos que mostram o efeito real dos percentuais ----------------
    private void UpdateEffects()
    {
        var m = E.Profile.Motion; var cam = E.Camera;
        int Eff(int axisMax, int pct, int camMax) => Math.Clamp((int)Math.Round(axisMax * pct / 100.0), 1, camMax);
        int pan = Eff(m.Pan.MaxSpeed, m.PanTiltSpeedPercent, cam.MaxPanSpeed);
        int tilt = Eff(m.Tilt.MaxSpeed, m.PanTiltSpeedPercent, cam.MaxTiltSpeed);
        int zoom = Eff(m.Zoom.MaxSpeed, m.VirtualZoomPercent, cam.MaxZoomSpeed);
        _ptEffect.Text = $"No máximo do analógico: Pan {pan} de {cam.MaxPanSpeed} · Tilt {tilt} de {cam.MaxTiltSpeed} (velocidade VISCA). Cada perfil guarda a sua.";
        _zoomEffect.Text = $"Zoom da tela: nível {zoom} de {cam.MaxZoomSpeed} (a câmera tem só {cam.MaxZoomSpeed} níveis de velocidade de zoom).";
    }

    protected override void Refresh()
    {
        var c = E.Controller;
        _sent.Text = $"Enviado agora →  Pan {c.SentPan:+0;-0;0}    Tilt {c.SentTilt:+0;-0;0}    Zoom {c.SentZoom:+0;-0;0}";
        _info.Text = (c.SafetyLatched ? "Parada de segurança: leve os controles ao neutro para continuar.\n" : "") +
                     (c.ActiveSource != InputSource.Virtual ? "Joystick e zoom da TELA estão desativados: escolha “Joystick virtual (mouse)”." : "");
        _info.Visibility = _info.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_srcBtns.Count == 2)
        {
            bool virt = c.ActiveSource == InputSource.Virtual;
            if (_srcBtns[0].IsChecked == virt) _srcBtns[0].IsChecked = !virt;
            if (_srcBtns[1].IsChecked != virt) _srcBtns[1].IsChecked = virt;
        }
        if (ImageSignature() != _imgSig) BuildImageCard();
        SyncImageValues();
    }
}
