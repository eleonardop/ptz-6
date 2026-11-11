using PtzStudio.Core.Models;
using PtzStudio.Core.Storage;
using Xunit;

namespace PtzStudio.Core.Tests;

public class StorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ptz-test-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Roundtrip_PreservesSettings()
    {
        var st = new SettingsStore(_dir);
        var s = st.Load();
        s.Profiles[0].Motion.Pan.Invert = true;
        s.Profiles[0].Motion.Tilt.Curve = ResponseCurve.Suave;
        s.Presets.Add(new PresetEntry { CameraId = s.ActiveCameraId, Slot = 3, Name = "Altar" });
        st.Save(s);
        var r = new SettingsStore(_dir).Load();
        Assert.True(r.Profiles[0].Motion.Pan.Invert);
        Assert.Equal(ResponseCurve.Suave, r.Profiles[0].Motion.Tilt.Curve);
        Assert.Equal("Altar", r.Presets.Single().Name);
        Assert.Equal(4, r.Profiles.Count);
    }

    [Fact]
    public void CorruptedFile_FallsBackToBackupOrDefaults()
    {
        var st = new SettingsStore(_dir);
        st.Save(st.Load()); st.Save(st.Load());           // cria .bak
        File.WriteAllText(st.FilePath, "{ isto não é json");
        var r = st.Load();
        Assert.NotEmpty(r.Profiles);
        File.WriteAllText(st.FilePath + ".bak", "lixo");
        Assert.NotEmpty(st.Load().Cameras);
    }

    [Fact]
    public void Backup_And_Restore()
    {
        var st = new SettingsStore(_dir);
        var s = st.Load(); s.Cameras[0].Name = "Original"; st.Save(s);
        var zip = st.CreateBackup();
        s.Cameras[0].Name = "Alterada"; st.Save(s);
        var restored = st.Restore(zip);
        Assert.Equal("Original", restored.Cameras[0].Name);
        Assert.Equal("Original", new SettingsStore(_dir).Load().Cameras[0].Name);
    }

    [Fact]
    public void Profile_Export_Import_AvoidsNameClash()
    {
        var st = new SettingsStore(_dir);
        var s = st.Load();
        var path = Path.Combine(_dir, "p.json");
        st.ExportProfile(s.Profiles[0], path);
        var imp = st.ImportProfile(path, s);
        Assert.NotEqual(s.Profiles[0].Name, imp.Name);
    }

    [Fact]
    public void Normalize_RepairsMissingActiveProfile()
    {
        var s = AppSettings.CreateDefault(); s.ActiveProfile = "inexistente";
        SettingsStore.Normalize(s);
        Assert.Contains(s.Profiles, p => p.Name == s.ActiveProfile);
    }
}
