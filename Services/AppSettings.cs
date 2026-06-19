using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoClicker.Services;

/// <summary>
/// Модель всех настроек приложения с JSON-сериализацией.
/// Автоматически сохраняется в %AppData%/AutoClicker/settings.json
/// </summary>
public class AppSettings
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AutoClicker");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    public int IntervalMs { get; set; } = 100;
    public bool UseMilliseconds { get; set; } = true;
    public string InputType { get; set; } = "Mouse";
    public string MouseButton { get; set; } = "Left";
    public int KeyboardVk { get; set; }
    public string ClickType { get; set; } = "Single";
    public string Mode { get; set; } = "Toggle";
    public string HotkeyStartStop { get; set; } = "F6";
    public string HotkeyReset { get; set; } = "None";
    public bool SoundEnabled { get; set; } = true;
    public bool AnimationsEnabled { get; set; } = true;
    public int ClickLimit { get; set; } = 0;
    public bool RandomInterval { get; set; }
    public int RandomPercent { get; set; } = 10;
    public string ComboKeys { get; set; } = "";
    public string MacrosJson { get; set; } = "[]";
    public string WindowTitle { get; set; } = "";
    public bool UseTheme { get; set; } = true;
    public bool Autostart { get; set; }
    public bool RealAutostart { get; set; }
    public bool MinimizeToTray { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(this, options);
            File.WriteAllText(SettingsPath, json);
        }
        catch { }
    }
}

/// <summary>
/// Модель пресета пользователя.
/// </summary>
public class UserPreset
{
    public string Name { get; set; } = "";
    public AppSettings Settings { get; set; } = new();
}

/// <summary>
/// Сервис управления пресетами.
/// Сохраняет/загружает пресеты из %AppData%/AutoClicker/presets.json
/// </summary>
public static class PresetsService
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AutoClicker");

    private static readonly string PresetsPath = Path.Combine(SettingsDir, "presets.json");

    public static List<UserPreset> Load()
    {
        try
        {
            if (File.Exists(PresetsPath))
            {
                string json = File.ReadAllText(PresetsPath);
                return JsonSerializer.Deserialize<List<UserPreset>>(json) ?? new List<UserPreset>();
            }
        }
        catch { }
        return new List<UserPreset>();
    }

    public static void Save(List<UserPreset> presets)
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(presets, options);
            File.WriteAllText(PresetsPath, json);
        }
        catch { }
    }
}
