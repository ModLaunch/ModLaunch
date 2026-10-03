using Microsoft.Win32;

namespace ModLaunch.Features;

/// <summary>Запуск вместе с Windows: строка в HKCU\…\Run (свёрнутым, если так настроено).</summary>
public static class Autostart
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "ModLaunch";

    public static bool Enabled
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            try { return Registry.CurrentUser.OpenSubKey(Key)?.GetValue(Name) is string; } catch { return false; }
        }
    }

    /// <summary>exe — какую копию запускать (установщик указывает установленную, а не себя).</summary>
    public static void Set(bool on, string? exe = null)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(Key);
            exe ??= Environment.ProcessPath;
            if (on && exe is not null) key.SetValue(Name, $"\"{exe}\" --autostart");
            else key.DeleteValue(Name, false);
        }
        catch { }
    }
}
