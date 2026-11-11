using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PtzStudio.App.Engine;
using PtzStudio.App.Ui;
using PtzStudio.Core.Input;
using PtzStudio.Core.Models;

namespace PtzStudio.App.Tabs;

internal sealed class MappingTab : TabBase
{
    private ControllerView _view = new();
    private string? _sel;
    private readonly TextBlock _title = Ux.Text("Clique em um botão do desenho.", 17, true);
    private readonly TextBlock _idLbl = Ux.Text("", 14);
    private readonly TextBlock _funcLbl = Ux.Text("", 14);
    private readonly TextBlock _live = Ux.Text("", 14);
    private readonly TextBlock _events = Ux.Text("", 13, false, Ux.Muted);
    private ComboBox _func = new();
    private ComboBox _mod = new();
    private readonly TextBox _param = new() { Width = 80, FontSize = 15, MinHeight = 34, Margin = new Thickness(4), Text = "1" };
    private Button _detectBtn = new();
    private readonly List<string> _log = new();
    private HashSet<string> _prev = new();
    private List<string> _modParts = new();
    private bool _detecting;
    private RadioButton _rbFront = new(), _rbRear = new();

    // assistente "mapear todos os botões"
    private static readonly string[] WizardRequired = { "A", "B", "X", "Y", "LB", "RB", "VIEW", "MENU", "L3", "R3", "DPAD_UP", "DPAD_DOWN", "DPAD_LEFT", "DPAD_RIGHT" };
    private static readonly string[] WizardOptional = { "HOME", "SCREENSHOT", "M", "L4", "R4" };
    private bool _wizActive;
    private readonly Queue<string> _wizQueue = new();
    private readonly HashSet<string> _wizDone = new();
    private Dictionary<string, int> _wizNew = new();
    private int _wizTotal;
    private readonly TextBlock _wizText = Ux.Text("", 14, true, Ux.Warn);
    private StackPanel _wizPanel = new();

    public MappingTab(AppEngine e) : base(e)
    {
        Init();
        Unloaded += (_, _) => AbortCapture();   // ao sair da aba, nenhuma captura de botão fica armada
    }

    private ButtonBinding? Current()
    {
        if (_sel == null || !E.Settings.Layout.TryGetValue(_sel, out int idx) || idx < 0) return null;
        int mod = ModifierId();
        return E.Profile.Bindings.FirstOrDefault(b => b.Button == idx && b.Modifier == mod);
    }

    private int ModifierId()
    {
        if (_mod.SelectedIndex <= 0 || _mod.SelectedIndex - 1 >= _modParts.Count) return -1;
        return E.Settings.Layout[_modParts[_mod.SelectedIndex - 1]];
    }

    protected override void Build()
    {
        bool front = _view.Front;
        _view = new ControllerView();
        _view.Build(front);
        _view.PartClicked += id => { _sel = id; ShowSelection(); };
        RefreshTooltips();

        var front_ = _rbFront = new RadioButton { Content = "Frente", IsChecked = front, FontSize = 14, GroupName = "vista" };
        var rear = _rbRear = new RadioButton { Content = "Traseira (L4 / R4)", IsChecked = !front, FontSize = 14, GroupName = "vista" };
        front_.Checked += (_, _) => { _view.Build(true); _sel = null; ShowSelection(); RefreshTooltips(); };
        rear.Checked += (_, _) => { _view.Build(false); _sel = null; ShowSelection(); RefreshTooltips(); };

        _modParts = E.Settings.Layout.Where(kv => kv.Value >= 0).Select(kv => kv.Key).OrderBy(x => x).ToList();
        var modItems = new List<string> { "(nenhum — botão isolado)" };
        modItems.AddRange(_modParts.Select(ControllerView.Describe));
        _mod = Ux.Combo(modItems, 0, _ => ShowSelection(), 340);

        var names = ActionCatalog.Todas.Select(t => t.Nome).ToList();
        _func = Ux.Combo(names, 0, _ => { }, 340);
        _detectBtn = Ux.Btn("DETECTAR BOTÃO", ToggleDetect, 340, Ux.Accent);
        var wizBtn = Ux.Btn("ASSISTENTE: MAPEAR BOTÕES", StartWizard, 340);
        _wizPanel = Ux.V(_wizText, Ux.Wrap(Ux.Btn("Pular este botão", WizardSkip, 150), Ux.Btn("Cancelar assistente", WizardCancel, 170)));
        _wizPanel.Visibility = _wizActive ? Visibility.Visible : Visibility.Collapsed;

        // ---- coluna esquerda: o controle ----
        var legend = Ux.Text("● azul = já tem função     ● verde = pressionado agora     borda azul = selecionado", 12, false, Ux.Muted);
        legend.Margin = new Thickness(4, 8, 0, 0);
        var stage = new Border
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0B, 0x0D, 0x11)), BorderBrush = Ux.Edge,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(12), Child = _view
        };
        var leftSide = new DockPanel { Margin = new Thickness(0, 0, 14, 0) };
        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        top.Children.Add(Ux.Header("Mapeamento do controle", "Clique no botão do desenho para ver e configurar a função dele."));
        top.Children.Add(Ux.H(front_, rear));
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(legend, Dock.Bottom);
        leftSide.Children.Add(top);
        leftSide.Children.Add(legend);
        leftSide.Children.Add(stage);

        // ---- coluna direita: cartões ----
        var selCard = Ux.Card(Ux.V(_title, _idLbl, _funcLbl), "BOTÃO SELECIONADO");
        var funcCard = Ux.Card(Ux.V(
            Ux.Text("Função", 13, true, Ux.Muted), _func,
            Ux.Labeled("Parâmetro (nº do preset)", _param, 200),
            Ux.Text("Combinação (segurar este botão + o selecionado)", 13, true, Ux.Muted), _mod,
            Ux.Wrap(Ux.Btn("Aplicar", Apply, 104, Ux.Accent), Ux.Btn("Remover", Remove, 104), Ux.Btn("Testar", TestFunction, 104))), "FUNÇÃO");
        var detectCard = Ux.Card(Ux.V(
            _detectBtn, wizBtn, _wizPanel, _live,
            Ux.Text("Botões sem número ainda (M, Screenshot, Home, L4, R4): selecione o botão no desenho, clique em DETECTAR BOTÃO e aperte-o no controle. Se o Windows não enxergar o botão, ele não aparecerá aqui — veja a aba Diagnóstico.", 12, false, Ux.Muted),
            Ux.Text("Eventos recentes", 13, true, Ux.Muted), _events), "DETECÇÃO AUTOMÁTICA");
        var panel = new StackPanel { Margin = new Thickness(0, 0, 4, 0) };
        panel.Children.Add(selCard); panel.Children.Add(funcCard); panel.Children.Add(detectCard);

        var g = new Grid { Margin = new Thickness(22, 18, 10, 6) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 340 });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(370) });
        Grid.SetColumn(leftSide, 0); g.Children.Add(leftSide);
        var sc = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(sc, 1); g.Children.Add(sc);
        Content = g;
        ShowSelection();
    }

    /// <summary>Texto curto da função de cada botão (para as etiquetas azuis do desenho).</summary>
    private Dictionary<string, string> AssignedLabels()
    {
        var d = new Dictionary<string, string>();
        foreach (var kv in E.Settings.Layout)
        {
            if (kv.Value < 0) continue;
            var list = E.Profile.Bindings.Where(x => x.Button == kv.Value && x.Action != ButtonAction.Nenhuma).ToList();
            if (list.Count == 0) continue;
            var plain = list.FirstOrDefault(x => x.Modifier == -1);
            var first = plain ?? list[0];
            string t = ActionLabels.Short(first.Action, first.Parameter);
            if (plain == null) t = "Combo: " + t;
            else if (list.Count > 1) t += $" +{list.Count - 1}";
            d[kv.Key] = t;
        }
        return d;
    }

    private void RefreshTooltips()
    {
        foreach (var kv in E.Settings.Layout)
        {
            var b = E.Profile.Bindings.FirstOrDefault(x => x.Button == kv.Value && x.Modifier == -1 && x.Action != ButtonAction.Nenhuma);
            _view.SetToolTip(kv.Key, ControllerView.Describe(kv.Key) + "\n" + (b == null ? "Sem função" : ActionCatalog.Nome(b.Action)));
        }
        _view.SetAssigned(AssignedLabels());
    }

    private void ShowSelection()
    {
        if (_sel == null) { _title.Text = "Clique em um botão do desenho."; _idLbl.Text = ""; _funcLbl.Text = ""; return; }
        _title.Text = ControllerView.Describe(_sel);
        if (_sel.StartsWith("STICK"))
        {
            _idLbl.Text = "Os analógicos são configurados na aba “Analógicos”. O ponto azul mostra a posição em tempo real.";
            _funcLbl.Text = ""; return;
        }
        int idx = E.Settings.Layout.TryGetValue(_sel, out var v) ? v : -1;
        _idLbl.Text = "Identificador detectado: " + VirtualButtons.Describe(idx);
        var cur = Current();
        _funcLbl.Text = "Função atual: " + (cur == null ? "(nenhuma)" : ActionCatalog.Nome(cur.Action) + (ActionCatalog.UsesParameter(cur.Action) ? $" — preset {cur.Parameter}" : ""));
        if (cur != null)
        {
            _func.SelectedIndex = ActionCatalog.Todas.ToList().FindIndex(t => t.Action == cur.Action);
            _param.Text = cur.Parameter.ToString();
        }
        else _func.SelectedIndex = 0;
    }

    private void Apply()
    {
        if (_sel == null || _sel.StartsWith("STICK")) return;
        int idx = E.Settings.Layout[_sel];
        if (idx < 0) { MessageBox.Show("Este botão ainda não foi detectado. Use DETECTAR BOTÃO e pressione o botão físico.", "Mapeamento"); return; }
        var action = ActionCatalog.Todas[Math.Max(0, _func.SelectedIndex)].Action;
        int mod = ModifierId();
        int.TryParse(_param.Text, out int prm);
        var existing = BindingValidator.FindConflict(E.Profile, idx, mod);
        if (action == ButtonAction.Nenhuma) { Remove(); return; }
        if (existing != null && existing.Action != action)
        {
            var r = MessageBox.Show($"Este botão já tem a função “{ActionCatalog.Nome(existing.Action)}”.\n\nSim = substituir pela nova função\nNão = manter as duas (duplicar de propósito)\nCancelar = não alterar",
                "Atribuição já existente", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) return;
            if (r == MessageBoxResult.Yes) { existing.Action = action; existing.Parameter = prm; }
            else E.Profile.Bindings.Add(new ButtonBinding { Button = idx, Modifier = mod, Action = action, Parameter = prm });
        }
        else if (existing != null) existing.Parameter = prm;
        else E.Profile.Bindings.Add(new ButtonBinding { Button = idx, Modifier = mod, Action = action, Parameter = prm });
        E.Save(); RefreshTooltips(); ShowSelection();
    }

    private void Remove()
    {
        if (_sel == null || !E.Settings.Layout.TryGetValue(_sel, out int idx) || idx < 0) return;
        int mod = ModifierId();
        E.Profile.Bindings.RemoveAll(b => b.Button == idx && b.Modifier == mod);
        E.Save(); RefreshTooltips(); ShowSelection();
    }

    private void TestFunction()
    {
        var action = ActionCatalog.Todas[Math.Max(0, _func.SelectedIndex)].Action;
        int.TryParse(_param.Text, out int prm);
        var now = DateTime.UtcNow;
        switch (action)
        {
            case ButtonAction.PanEsquerda: E.Controller.Pulse(-1, 0, 0, 800, now); break;
            case ButtonAction.PanDireita: E.Controller.Pulse(1, 0, 0, 800, now); break;
            case ButtonAction.TiltCima: E.Controller.Pulse(0, 1, 0, 800, now); break;
            case ButtonAction.TiltBaixo: E.Controller.Pulse(0, -1, 0, 800, now); break;
            case ButtonAction.ZoomAproximar: E.Controller.Pulse(0, 0, 0.6, 800, now); break;
            case ButtonAction.ZoomAfastar: E.Controller.Pulse(0, 0, -0.6, 800, now); break;
            case ButtonAction.FocoProximo:
            case ButtonAction.FocoDistante:
                E.Controller.PulseFocus(action == ButtonAction.FocoDistante ? 1 : -1, 700, now);
                break;
            case ButtonAction.Nenhuma: break;
            default: E.Execute(action, prm); break;
        }
    }

    private void ToggleDetect()
    {
        if (_sel == null || _sel.StartsWith("STICK")) { MessageBox.Show("Selecione um botão no desenho primeiro.", "Detectar botão"); return; }
        if (!E.Pad.Connected) { MessageBox.Show("Nenhum controle detectado. Conecte o GameSir (USB ou Bluetooth) e tente de novo.", "Detectar botão"); return; }
        if (_wizActive) { MessageBox.Show("Termine ou cancele o assistente de mapeamento primeiro.", "Detectar botão"); return; }
        if (_detecting) { E.CancelDetect(); _detecting = false; _detectBtn.Content = "DETECTAR BOTÃO"; _live.Text = "Detecção cancelada."; return; }
        _detecting = true;
        _detectBtn.Content = "Pressione o botão físico agora… (clique para cancelar)";
        _live.Text = $"Aguardando: pressione o botão que corresponde a “{ControllerView.Describe(_sel)}”.";
        string part = _sel;
        E.BeginDetectButton(id =>
        {
            _detecting = false; _detectBtn.Content = "DETECTAR BOTÃO";
            // o diálogo fica fora do ciclo de controle (um MessageBox aqui abriria um laço aninhado dentro do Tick)
            Dispatcher.BeginInvoke(new Action(() => AssignDetected(part, id)));
        });
    }

    private void AssignDetected(string part, int id)
    {
        var other = E.Settings.Layout.FirstOrDefault(kv => kv.Value == id && kv.Key != part);
        if (other.Key != null)
        {
            var r = MessageBox.Show($"{VirtualButtons.Describe(id)} já estava ligado a “{ControllerView.Describe(other.Key)}”. Mover para “{ControllerView.Describe(part)}”?",
                "Botão já usado", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) { _live.Text = "Mantido como estava."; return; }
            E.Settings.Layout[other.Key] = -1;
        }
        E.Settings.Layout[part] = id;
        E.Save(); RefreshTooltips(); ShowSelection();
        _live.Text = $"Detectado: {VirtualButtons.Describe(id)} → {ControllerView.Describe(part)}. Escolha a função e clique em Aplicar.";
    }

    // ---------------- assistente: mapear todos os botões ----------------
    private void ShowView(bool front)
    {
        if (front) _rbFront.IsChecked = true; else _rbRear.IsChecked = true;
    }

    private void AbortCapture()
    {
        bool had = _wizActive || _detecting;
        _wizActive = false; _wizQueue.Clear(); _wizDone.Clear(); _detecting = false;
        if (had) E.CancelDetect();
    }

    private void StartWizard()
    {
        if (_detecting) { E.CancelDetect(); _detecting = false; _detectBtn.Content = "DETECTAR BOTÃO"; }
        if (!E.Pad.Connected) { MessageBox.Show("Nenhum controle detectado. Conecte o GameSir (USB ou Bluetooth) e tente de novo.", "Assistente de mapeamento"); return; }
        var r = MessageBox.Show(
            "O assistente vai pedir, um por vez, para você apertar cada botão do controle.\n\n" +
            "As funções já atribuídas continuam nos mesmos botões do desenho, mesmo que o número do botão mude. " +
            "Nada é gravado até o fim, e dá para pular ou cancelar. Os gatilhos LT/RT não entram aqui (use DETECTAR BOTÃO neles).\n\nComeçar?",
            "Assistente de mapeamento", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;
        _wizQueue.Clear(); _wizDone.Clear();
        foreach (var p in WizardRequired.Concat(WizardOptional)) _wizQueue.Enqueue(p);
        _wizTotal = _wizQueue.Count;
        _wizNew = new Dictionary<string, int>(E.Settings.Layout);
        _wizActive = true;
        _wizPanel.Visibility = Visibility.Visible;
        WizardNext();
    }

    private void WizardNext()
    {
        if (_wizQueue.Count == 0) { FinishWizard(); return; }
        string part = _wizQueue.Peek();
        bool optional = WizardOptional.Contains(part);
        ShowView(part != "L4" && part != "R4");
        _view.Select(part);
        _wizText.Text = $"Passo {_wizTotal - _wizQueue.Count + 1} de {_wizTotal}: aperte “{ControllerView.Describe(part)}” no controle." +
                        (optional ? " (Opcional: se o controle não tem esse botão ou o Windows não o enxerga, clique em Pular.)" : "");
        ArmWizardCapture(part);
    }

    private void ArmWizardCapture(string part)
    {
        // roda dentro do ciclo de controle: sem diálogos aqui e já rearma a captura, para o botão recém-apertado não disparar sua função
        E.BeginDetectButton(id => WizardGot(part, id));
    }

    private void WizardGot(string part, int id)
    {
        if (!_wizActive || _wizQueue.Count == 0 || _wizQueue.Peek() != part) return;
        var usedByDone = _wizNew.FirstOrDefault(kv => kv.Value == id && kv.Key != part && _wizDone.Contains(kv.Key));
        if (usedByDone.Key != null)
        {
            _wizText.Text = $"{VirtualButtons.Describe(id)} já foi usado para “{ControllerView.Describe(usedByDone.Key)}”. Aperte outro botão para “{ControllerView.Describe(part)}”.";
            ArmWizardCapture(part);
            return;
        }
        var usedByOld = _wizNew.FirstOrDefault(kv => kv.Value == id && kv.Key != part);
        if (usedByOld.Key != null) _wizNew[usedByOld.Key] = -1;     // número antigo de outra peça: ela será perguntada na sua vez
        _wizNew[part] = id; _wizDone.Add(part); _wizQueue.Dequeue();
        WizardNext();
    }

    private void WizardSkip()
    {
        if (!_wizActive || _wizQueue.Count == 0) return;
        E.CancelDetect();
        _wizQueue.Dequeue();
        WizardNext();
    }

    private void WizardCancel()
    {
        AbortCapture();
        _wizPanel.Visibility = Visibility.Collapsed;
        _live.Text = "Assistente cancelado. Nada foi alterado.";
    }

    private void FinishWizard()
    {
        _wizActive = false;
        _wizPanel.Visibility = Visibility.Collapsed;
        E.ApplyLayout(_wizNew);
        _sel = null; RefreshTooltips(); ShowSelection();
        _live.Text = $"Mapeamento concluído: {_wizDone.Count} botão(ões) detectado(s). As funções já atribuídas ficaram nos mesmos botões do desenho.";
    }

    protected override void Refresh()
    {
        var pressedIds = E.Last.Pressed;
        var parts = new HashSet<string>();
        var trig = new Dictionary<string, double>();
        foreach (var kv in E.Settings.Layout)
        {
            if (kv.Value < 0) continue;
            if (pressedIds.Contains(kv.Value)) parts.Add(kv.Key);
            if (kv.Value >= 2000 && E.LastSnapshot != null)
            {
                int axis = kv.Value % 1000;
                if (axis < E.LastSnapshot.Axes.Length)
                {
                    double d = E.LastSnapshot.Axes[axis] - E.Rest[axis];
                    trig[kv.Key] = kv.Value < 3000 ? d : -d;
                }
            }
        }
        _view.SetPressed(parts, trig);
        var map = E.Profile.AxisMap;
        if (map.PanAxis == 0 && map.TiltAxis == 1) _view.SetSticks(E.Last.X, E.Last.Y, 0, E.Last.Z);   // Pan/Tilt no analógico esquerdo
        else _view.SetSticks(0, E.Last.Z, E.Last.X, E.Last.Y);                                          // Pan/Tilt no direito

        foreach (var n in parts.Except(_prev)) AddEvent($"▼ {ControllerView.Describe(n)} ({VirtualButtons.Describe(E.Settings.Layout[n])}) pressionado");
        foreach (var n in _prev.Except(parts)) AddEvent($"▲ {ControllerView.Describe(n)} liberado");
        _prev = parts;

        var known = new HashSet<int>(E.Settings.Layout.Values);
        var unknown = pressedIds.Where(i => !known.Contains(i)).ToList();
        string status = E.Pad.Connected ? $"Controle: {E.Pad.Name}" : "Controle não detectado";
        if (!_detecting)
            _live.Text = status + (unknown.Count > 0 ? "\nSem peça no desenho: " + string.Join(", ", unknown.Select(VirtualButtons.Describe)) : "");
    }

    private void AddEvent(string s)
    {
        _log.Add($"{DateTime.Now:HH:mm:ss} {s}");
        while (_log.Count > 6) _log.RemoveAt(0);
        _events.Text = string.Join("\n", _log);
    }
}
