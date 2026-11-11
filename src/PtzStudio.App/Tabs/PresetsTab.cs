using System.Windows;
using System.Windows.Controls;
using PtzStudio.App.Engine;
using PtzStudio.App.Ui;
using PtzStudio.Core.Input;
using PtzStudio.Core.Models;
using PtzStudio.Core.Visca;

namespace PtzStudio.App.Tabs;

internal sealed class PresetsTab : TabBase
{
    private ListView _list = new();
    private TextBox _slot = new(), _name = new();

    public PresetsTab(AppEngine e) : base(e) { Init(); }

    private sealed record Row(int Slot, string Nome, string Armazenamento, PresetEntry Entry);

    private IEnumerable<PresetEntry> Mine => E.Settings.Presets.Where(p => p.CameraId == E.Camera.Id);

    private static string Storage(PresetEntry p) => p.ConfirmedOnCamera
        ? $"Memória da câmera, slot {p.Slot} (confirmado). Nome só no PC."
        : p.SentAt != null ? $"Comando enviado ao slot {p.Slot}; SEM confirmação da câmera. Nome só no PC."
                           : $"Apenas registro local (nome) do slot {p.Slot}; posição não gravada por este programa.";

    private void Fill()
    {
        _list.Items.Clear();
        foreach (var p in Mine) _list.Items.Add(new Row(p.Slot, p.Name, Storage(p), p));
    }

    private PresetEntry? Selected => (_list.SelectedItem as Row)?.Entry;

    protected override void Build()
    {
        _list = new ListView { MinHeight = 240, FontSize = 14 };
        var gv = new GridView();
        gv.Columns.Add(new GridViewColumn { Header = "Slot", Width = 60, DisplayMemberBinding = new System.Windows.Data.Binding("Slot") });
        gv.Columns.Add(new GridViewColumn { Header = "Nome", Width = 220, DisplayMemberBinding = new System.Windows.Data.Binding("Nome") });
        gv.Columns.Add(new GridViewColumn { Header = "Onde está armazenado", Width = 520, DisplayMemberBinding = new System.Windows.Data.Binding("Armazenamento") });
        _list.View = gv;
        _list.SelectionChanged += (_, _) => { if (Selected is { } p) { _slot.Text = p.Slot.ToString(); _name.Text = p.Name; } };
        Fill();

        _slot = Ux.Input("1", _ => { }, 80);
        _name = Ux.Input("", _ => { }, 260);

        int Slot() => int.TryParse(_slot.Text, out int s) ? Math.Clamp(s, 0, 254) : -1;

        var form = Ux.Group("Preset",
            Ux.V(
                Ux.H(Ux.Text("Slot (nº na câmera):", 14), _slot, Ux.Text("Nome:", 14), _name),
                Ux.Wrap(
                    Ux.Btn("Salvar posição ATUAL neste slot", () =>
                    {
                        int s = Slot(); if (s < 0) return;
                        var ex = Mine.FirstOrDefault(p => p.Slot == s);
                        if (ex != null && MessageBox.Show($"O slot {s} já existe (“{ex.Name}”). Sobrescrever a posição na câmera?", "Presets", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                        E.SavePresetSlot(s, string.IsNullOrWhiteSpace(_name.Text) ? null : _name.Text.Trim());
                        Fill();
                    }, 260, Ux.Accent),
                    Ux.Btn("Acionar", () => { if (Slot() >= 0) E.Controller.RecallPreset(Slot()); }, 110),
                    Ux.Btn("Renomear (só nome)", () =>
                    {
                        if (Selected is not { } p) return;
                        var n = Ux.Prompt(Window.GetWindow(this)!, "Renomear preset", "Novo nome:", p.Name);
                        if (!string.IsNullOrWhiteSpace(n)) { p.Name = n.Trim(); E.Save(); Fill(); }
                    }, 170),
                    Ux.Btn("Registrar slot existente (só nome)", () =>
                    {
                        int s = Slot(); if (s < 0 || Mine.Any(p => p.Slot == s)) return;
                        E.Settings.Presets.Add(new PresetEntry { CameraId = E.Camera.Id, Slot = s, Name = string.IsNullOrWhiteSpace(_name.Text) ? $"Preset {s}" : _name.Text.Trim() });
                        E.Save(); Fill();
                    }, 260)),
                Ux.Wrap(
                    Ux.Btn("Subir", () => Move(-1), 90), Ux.Btn("Descer", () => Move(1), 90),
                    Ux.Btn("Excluir", Delete, 110),
                    Ux.Btn("Atribuir a um botão físico…", AssignButton, 250)),
                Ux.Text("Ao gravar, a posição fica na MEMÓRIA DA CÂMERA; o computador guarda só o nome e a ordem. Só aparece “confirmado” quando a câmera responde que concluiu o comando.", 13, false, Ux.Muted)));

        var page = new StackPanel();
        page.Children.Add(Ux.Header("Presets", "Câmera: " + E.Camera.Name));
        page.Children.Add(Ux.Card(_list, "POSIÇÕES SALVAS"));
        page.Children.Add(form);
        Content = Ux.Scroll(page);
    }

    private string _lastSig = "";

    protected override void Refresh()
    {
        string sig = string.Join("|", Mine.Select(p => p.Slot + p.Name + Storage(p)));
        if (sig == _lastSig) return;
        _lastSig = sig;
        int? slot = Selected?.Slot;
        Fill();
        if (slot != null)
            foreach (var it in _list.Items)
                if (it is Row r && r.Slot == slot) { _list.SelectedItem = it; break; }
    }

    private void Move(int dir)
    {
        if (Selected is not { } p) return;
        int i = E.Settings.Presets.IndexOf(p);
        // troca com o próximo preset da MESMA câmera
        int j = i + dir;
        while (j >= 0 && j < E.Settings.Presets.Count && E.Settings.Presets[j].CameraId != p.CameraId) j += dir;
        if (j < 0 || j >= E.Settings.Presets.Count) return;
        (E.Settings.Presets[i], E.Settings.Presets[j]) = (E.Settings.Presets[j], E.Settings.Presets[i]);
        E.Save(); Fill();
    }

    private void Delete()
    {
        if (Selected is not { } p) return;
        var r = MessageBox.Show($"Excluir “{p.Name}” da lista?\n\nSim = excluir também da memória da câmera (slot {p.Slot})\nNão = excluir só da lista do programa\nCancelar = nada",
            "Excluir preset", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (r == MessageBoxResult.Cancel) return;
        if (r == MessageBoxResult.Yes) E.Client.Send(ViscaCommands.PresetReset(E.Camera.ViscaAddress, p.Slot));
        E.Settings.Presets.Remove(p); E.Save(); Fill();
    }

    private void AssignButton()
    {
        if (Selected is not { } p) { MessageBox.Show("Selecione um preset na lista.", "Presets"); return; }
        if (!E.Pad.Connected) { MessageBox.Show("Nenhum controle detectado.", "Presets"); return; }
        MessageBox.Show("Depois de clicar em OK, pressione o botão físico que deve acionar este preset.", "Atribuir botão");
        E.BeginDetectButton(id => Dispatcher.BeginInvoke(new Action(() =>      // diálogos ficam fora do ciclo de controle
        {
            var conflict = BindingValidator.FindConflict(E.Profile, id, -1);
            if (conflict != null)
            {
                var r = MessageBox.Show($"{VirtualButtons.Describe(id)} já tem a função “{ActionCatalog.Nome(conflict.Action)}”. Substituir?", "Atribuição existente", MessageBoxButton.YesNo);
                if (r != MessageBoxResult.Yes) return;
                conflict.Action = ButtonAction.AcionarPreset; conflict.Parameter = p.Slot;
            }
            else E.Profile.Bindings.Add(new ButtonBinding { Button = id, Action = ButtonAction.AcionarPreset, Parameter = p.Slot });
            E.Save();
            MessageBox.Show($"{VirtualButtons.Describe(id)} agora aciona o preset {p.Slot} no perfil “{E.Settings.ActiveProfile}”.", "Atribuído");
        })));
    }
}
