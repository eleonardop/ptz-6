using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PtzStudio.App.Engine;
using PtzStudio.App.Ui;
using PtzStudio.Core.Models;

namespace PtzStudio.App.Tabs;

internal sealed class ProfilesTab : TabBase
{
    private ListBox _list = new();
    public ProfilesTab(AppEngine e) : base(e) { Init(); }

    private Profile? Sel => _list.SelectedIndex >= 0 ? E.Settings.Profiles[_list.SelectedIndex] : null;
    private Window Owner => Window.GetWindow(this)!;

    private void Fill()
    {
        _list.Items.Clear();
        foreach (var p in E.Settings.Profiles) _list.Items.Add(p.Name + (p.Name == E.Settings.ActiveProfile ? "   ← ativo" : ""));
    }

    private string UniqueName(string baseName)
    {
        string n = baseName; int i = 2;
        while (E.Settings.Profiles.Any(p => p.Name.Equals(n, StringComparison.OrdinalIgnoreCase))) n = $"{baseName} ({i++})";
        return n;
    }

    protected override void Build()
    {
        _list = new ListBox { FontSize = 16, MinHeight = 220, MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left };
        Fill();
        _list.SelectedIndex = E.Settings.Profiles.FindIndex(p => p.Name == E.Settings.ActiveProfile);

        Content = Ux.Scroll(Ux.V(
            Ux.Text("Perfis de configuração", 17, true),
            Ux.Text("Cada perfil guarda: mapeamento de botões, atalhos de presets, sensibilidade, velocidades, curvas, zonas mortas, inversões e eixos do controle.", 13, false, Ux.Muted),
            _list,
            Ux.Wrap(
                Ux.Btn("Ativar", () => { if (Sel is { } p) { E.SetProfile(p.Name); } }, 120, Ux.Accent),
                Ux.Btn("Novo (padrão)", () =>
                {
                    var n = Ux.Prompt(Owner, "Novo perfil", "Nome do perfil:", "Personalizado"); if (string.IsNullOrWhiteSpace(n)) return;
                    var p = AppSettings.CreateDefault().Profiles[0]; p.Name = UniqueName(n.Trim());
                    E.Settings.Profiles.Add(p); E.Save(); Fill();
                }, 150),
                Ux.Btn("Duplicar", () =>
                {
                    if (Sel is not { } p) return;
                    var c = p.Clone(); c.Name = UniqueName(p.Name + " (cópia)"); E.Settings.Profiles.Add(c); E.Save(); Fill();
                }, 120),
                Ux.Btn("Renomear", () =>
                {
                    if (Sel is not { } p) return;
                    var n = Ux.Prompt(Owner, "Renomear perfil", "Novo nome:", p.Name);
                    if (string.IsNullOrWhiteSpace(n) || n.Trim() == p.Name) return;
                    bool wasActive = p.Name == E.Settings.ActiveProfile;
                    p.Name = UniqueName(n.Trim());
                    if (wasActive) E.Settings.ActiveProfile = p.Name;
                    E.Save(); Fill();
                }, 120),
                Ux.Btn("Excluir", () =>
                {
                    if (Sel is not { } p) return;
                    if (E.Settings.Profiles.Count <= 1) { MessageBox.Show("É preciso manter pelo menos um perfil.", "Perfis"); return; }
                    if (MessageBox.Show($"Excluir o perfil “{p.Name}”?", "Perfis", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                    bool wasActive = p.Name == E.Settings.ActiveProfile;
                    E.Settings.Profiles.Remove(p);
                    if (wasActive) E.SetProfile(E.Settings.Profiles[0].Name); else E.Save();
                    Fill();
                }, 120)),
            Ux.Wrap(
                Ux.Btn("Exportar perfil…", () =>
                {
                    if (Sel is not { } p) return;
                    var d = new SaveFileDialog { Filter = "Perfil PTZ Studio (*.json)|*.json", FileName = p.Name + ".json" };
                    if (d.ShowDialog() == true) E.Store.ExportProfile(p, d.FileName);
                }, 170),
                Ux.Btn("Importar perfil…", () =>
                {
                    var d = new OpenFileDialog { Filter = "Perfil PTZ Studio (*.json)|*.json" };
                    if (d.ShowDialog() != true) return;
                    try { E.Store.ImportProfile(d.FileName, E.Settings); E.Save(); Fill(); }
                    catch (Exception ex) { MessageBox.Show("Não foi possível importar: " + ex.Message, "Perfis"); }
                }, 170))));
        Ux.WithHeader((ScrollViewer)Content, "Perfis", "Conjuntos de configurações para cada situação (louvor, pregação, operação...).");
    }
}
