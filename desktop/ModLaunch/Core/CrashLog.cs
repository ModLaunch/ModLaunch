namespace ModLaunch.Core;

/// <summary>
/// Журнал ошибок: %APPDATA%\ModHub\crash.log. Сюда попадает всё, что иначе закрыло бы программу
/// молча, — по этому файлу видно, где именно и почему.
/// </summary>
public static class CrashLog
{
    public static string Path => System.IO.Path.Combine(Paths.DataDir, "crash.log");

    public static void Write(string where, Exception? e)
    {
        try
        {
            Directory.CreateDirectory(Paths.DataDir);
            var file = new FileInfo(Path);
            if (file.Exists && file.Length > 512 * 1024) file.Delete(); // файл не должен расти бесконечно
            File.AppendAllText(Path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ModLaunch {Http.Version} ({Environment.OSVersion.VersionString}) — {where}{Environment.NewLine}{e}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { }
    }

    /// <summary>Ловит всё, что не поймано в обычных местах, и записывает в журнал.</summary>
    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write(e.IsTerminating ? "fatal" : "unhandled", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Write("task", e.Exception); e.SetObserved(); };
    }
}
