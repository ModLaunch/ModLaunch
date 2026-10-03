using Avalonia.Threading;

namespace ModLaunch.Core;

/// <summary>
/// Страховка от вылетов (8.4.1). Любая ошибка в обработчике щелчка, пункте меню или
/// «async void» больше не закрывает программу: пишем её в logs\errors.log и показываем
/// короткое уведомление, а программа работает дальше.
/// </summary>
public static class Guard
{
    static bool _installed;
    static readonly object Gate = new();

    public static string LogPath => Path.Combine(Paths.DataDir, "logs", "errors.log");

    /// <summary>Подключить один раз при старте окна.</summary>
    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Report(e.Exception);
            e.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log(e.Exception);
            e.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { if (e.ExceptionObject is Exception ex) Log(ex, fatal: true); };
    }

    /// <summary>Записать и показать пользователю понятным текстом.</summary>
    public static void Report(Exception e)
    {
        Log(e);
        try { Views.MainWindow.Current?.Toast(Jobs.Explain(e), bad: true); } catch { }
    }

    public static void Log(Exception e, bool fatal = false)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                var info = new FileInfo(LogPath);
                if (info.Exists && info.Length > 512 * 1024) File.Move(LogPath, LogPath + ".old", true);
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {(fatal ? "FATAL " : "")}{Http.Version}\n{e}\n\n");
            }
        }
        catch { }
    }

    /// <summary>
    /// Выполнить действие пункта меню, когда меню уже закрылось. Действие может перестроить
    /// экран и убрать элемент, к которому привязано меню, — если сделать это прямо внутри
    /// щелчка, Avalonia падает при закрытии меню. Ошибки самого действия — в уведомление.
    /// </summary>
    public static void Later(Action run) => Dispatcher.UIThread.Post(() =>
    {
        try { run(); }
        catch (Exception e) { Report(e); }
    }, DispatcherPriority.Background);
}
