using System.Text.Json;
using Fireworm.Core.Models;

namespace Fireworm.Core.Settings;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
    };

    public string BaseDirectory { get; }
    public string SettingsPath { get; }
    public string BackupPath { get; }
    public string? LastWarning { get; private set; }

    public SettingsStore(string? baseDirectory = null)
    {
        BaseDirectory = baseDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fireworm");
        SettingsPath = Path.Combine(BaseDirectory, "settings.json");
        BackupPath = Path.Combine(BaseDirectory, "device-backup.json");
    }

    public AppSettings Load()
    {
        LastWarning = null;
        try
        {
            Directory.CreateDirectory(BaseDirectory);
            if (!File.Exists(SettingsPath))
            {
                var defaults = AppSettings.CreateDefault();
                Save(defaults);
                return defaults;
            }

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (settings == null)
                throw new JsonException("settings.json deserialized to null");

            settings.EnsureAllBindings();
            return settings;
        }
        catch (Exception ex)
        {
            LastWarning = $"설정을 불러오지 못했습니다: {ex.Message}. 기본값으로 시작합니다.";
            TryQuarantine(SettingsPath);
            var defaults = AppSettings.CreateDefault();
            try { Save(defaults); } catch { /* read-only environment */ }
            return defaults;
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(BaseDirectory);
        var temp = SettingsPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temp, SettingsPath, overwrite: true);
    }

    public DeviceBackup LoadOrCreateBackup()
    {
        try
        {
            Directory.CreateDirectory(BaseDirectory);
            if (File.Exists(BackupPath))
            {
                var json = File.ReadAllText(BackupPath);
                var backup = JsonSerializer.Deserialize<DeviceBackup>(json, JsonOptions);
                if (backup?.Keys.Count > 0)
                    return backup;
            }
        }
        catch
        {
            TryQuarantine(BackupPath);
        }

        var created = DeviceBackup.CreateObservedDefault();
        SaveBackup(created);
        return created;
    }

    public void SaveBackup(DeviceBackup backup)
    {
        Directory.CreateDirectory(BaseDirectory);
        backup.CreatedAt = DateTime.Now;
        var temp = BackupPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(backup, JsonOptions));
        File.Move(temp, BackupPath, overwrite: true);
    }

    private static void TryQuarantine(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            var target = path + ".bad-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            File.Move(path, target, overwrite: true);
        }
        catch
        {
            // Best effort only; startup must not fail because of a corrupt file.
        }
    }
}
