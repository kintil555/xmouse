using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Win32;

namespace XMouse;

public static class ConfigManager
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "xmouse");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "xmouse";

    public static RemapConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var cfg = JsonSerializer.Deserialize<RemapConfig>(json);
                if (cfg != null) return cfg;
            }
        }
        catch
        {
            // config korup/tidak terbaca -> pakai default
        }
        return new RemapConfig();
    }

    public static void Save(RemapConfig config)
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }

    /// <summary>Mendaftarkan/menghapus xmouse dari Windows startup (HKCU Run key, tidak perlu admin).</summary>
    public static void SetRunOnStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key == null) return;

        if (enabled)
        {
            string exePath = Environment.ProcessPath ?? Application.ExecutablePath;
            key.SetValue(RunValueName, $"\"{exePath}\" --minimized");
        }
        else
        {
            if (key.GetValue(RunValueName) != null)
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
    }
}
