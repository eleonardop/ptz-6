using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using PtzStudio.Core.Models;

namespace PtzStudio.Core.Storage;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string Directory { get; }
    public string FilePath => Path.Combine(Directory, "settings.json");
    public string BackupDirectory => Path.Combine(Directory, "backups");

    public SettingsStore(string directory)
    {
        Directory = directory;
        System.IO.Directory.CreateDirectory(directory);
    }

    public static string DefaultDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PtzStudio");

    public AppSettings Load()
    {
        foreach (var path in new[] { FilePath, FilePath + ".bak" })
        {
            try
            {
                if (!File.Exists(path)) continue;
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options);
                if (s != null) return Normalize(s);
            }
            catch { /* arquivo corrompido: tenta a cópia .bak e por fim os padrões */ }
        }
        return AppSettings.CreateDefault();
    }

    public void Save(AppSettings s)
    {
        Normalize(s);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(s, Options));
        if (File.Exists(FilePath)) File.Copy(FilePath, FilePath + ".bak", true);
        File.Move(tmp, FilePath, true);
    }

    public static AppSettings Normalize(AppSettings s)
    {
        if (s.Cameras.Count == 0) s.Cameras.Add(new CameraConfig());
        if (!s.Cameras.Any(c => c.Id == s.ActiveCameraId)) s.ActiveCameraId = s.Cameras[0].Id;
        if (s.Profiles.Count == 0) s.Profiles.AddRange(AppSettings.CreateDefault().Profiles);
        if (!s.Profiles.Any(p => p.Name == s.ActiveProfile)) s.ActiveProfile = s.Profiles[0].Name;
        foreach (var kv in AppSettings.DefaultLayout())
            if (!s.Layout.ContainsKey(kv.Key)) s.Layout[kv.Key] = kv.Value;
        foreach (var p in s.Profiles) MigrateProfile(p);
        SettingsSanitizer.Apply(s);   // só corrige valores impossíveis (NaN, fora da faixa); ajustes válidos do usuário ficam como estão
        return s;
    }

    /// <summary>
    /// Perfis de versões antigas: o nível de velocidade padrão (35/65/100%) vira a porcentagem do novo controle contínuo, e as
    /// atribuições de botões que não existem mais (parada de emergência, alternar nível) são removidas. Nada mais é alterado.
    /// </summary>
    public static void MigrateProfile(Profile p)
    {
        var m = p.Motion;
        if (m.SpeedLevels is { Length: > 0 } lv)
        {
            int idx = Math.Clamp(m.DefaultSpeedLevel ?? 1, 0, lv.Length - 1);
            m.PanTiltSpeedPercent = (int)Math.Round(lv[idx] * 100);
        }
        m.SpeedLevels = null; m.DefaultSpeedLevel = null;
        p.Bindings.RemoveAll(b => b.Action == ButtonAction.ParadaEmergencia || b.Action == ButtonAction.AlternarVelocidade);
    }

    // ---- Perfis: exportação e importação ----
    public void ExportProfile(Profile p, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(p, Options));

    public Profile ImportProfile(string path, AppSettings into)
    {
        var p = JsonSerializer.Deserialize<Profile>(File.ReadAllText(path), Options)
                ?? throw new InvalidDataException("Arquivo de perfil inválido.");
        string baseName = string.IsNullOrWhiteSpace(p.Name) ? "Importado" : p.Name;
        string name = baseName; int n = 2;
        while (into.Profiles.Any(x => x.Name == name)) name = $"{baseName} ({n++})";
        p.Name = name;
        MigrateProfile(p);
        SettingsSanitizer.Motion(p.Motion);
        into.Profiles.Add(p);
        return p;
    }

    // ---- Backup e restauração ----
    public string CreateBackup(string? zipPath = null)
    {
        System.IO.Directory.CreateDirectory(BackupDirectory);
        zipPath ??= Path.Combine(BackupDirectory, $"backup-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        if (File.Exists(zipPath)) File.Delete(zipPath);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        if (File.Exists(FilePath)) zip.CreateEntryFromFile(FilePath, "settings.json");
        return zipPath;
    }

    /// <summary>Backup automático ao iniciar; mantém apenas os 10 mais recentes.</summary>
    public void AutoBackup()
    {
        if (!File.Exists(FilePath)) return;
        CreateBackup();
        var old = new DirectoryInfo(BackupDirectory).GetFiles("backup-*.zip").OrderByDescending(f => f.CreationTimeUtc).Skip(10);
        foreach (var f in old) { try { f.Delete(); } catch { } }
    }

    public AppSettings Restore(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.GetEntry("settings.json") ?? throw new InvalidDataException("O backup não contém settings.json.");
        using var rd = new StreamReader(entry.Open());
        var s = JsonSerializer.Deserialize<AppSettings>(rd.ReadToEnd(), Options)
                ?? throw new InvalidDataException("Backup inválido.");
        Normalize(s);
        if (File.Exists(FilePath)) File.Copy(FilePath, Path.Combine(Directory, "settings.antes-da-restauracao.json"), true);
        Save(s);
        return s;
    }

    public void ExportAll(AppSettings s, string path) => File.WriteAllText(path, JsonSerializer.Serialize(s, Options));

    public AppSettings ImportAll(string path)
    {
        var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options)
                ?? throw new InvalidDataException("Arquivo inválido.");
        return Normalize(s);
    }
}
