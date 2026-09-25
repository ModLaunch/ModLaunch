namespace ModLaunch.Core;

/// <summary>
/// Где ModLaunch хранит свои данные. Та же папка, что у версии на Electron
/// (%APPDATA%\ModHub), — поэтому настройки, пути к играм и списки
/// установленных модов переходят в новую версию сами.
/// </summary>
public static class Paths
{
    public static string DataDir { get; } = Resolve();

    static string Resolve()
    {
        var custom = Environment.GetEnvironmentVariable("MODLAUNCH_DATA");
        var dir = !string.IsNullOrWhiteSpace(custom)
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ModHub");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string GamesDir => Path.Combine(DataDir, "games");
    public static string CacheDir => Directory.CreateDirectory(Path.Combine(DataDir, "cache")).FullName;
    public static string DownloadsTemp => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "modhub-downloads")).FullName;

    /// <summary>Папка «Загрузки» пользователя — туда браузер кладёт файлы с Nexus.</summary>
    public static string UserDownloads
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Downloads");
        }
    }
}
