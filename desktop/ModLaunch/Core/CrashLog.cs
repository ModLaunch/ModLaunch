using System.Text;
using Avalonia.Threading;

namespace ModLaunch.Core;

/// <summary>
/// Журнал ошибок и защита от вылетов.
///
/// Раньше любое необработанное исключение — в таймере, в обработчике кнопки,
/// после await, в фоновом потоке — молча закрывало программу. За несколько
/// часов работы что-нибудь да случалось (файл занят антивирусом, сеть
/// отвалилась посреди запроса), и ModLaunch просто исчезал без единого слова.
///
/// Теперь ошибка в потоке интерфейса записывается в logs\errors.log и
/// показывается всплывашкой, а программа работает дальше. Если процесс всё же
/// падает (ошибка в чужом потоке — её .NET перехватить не даёт), причина
/// остаётся в журнале, и при следующем запуске ModLaunch об этом скажет.
/// </summary>
public static class CrashLog
{
    const long MaxSize = 1024 * 1024;
    const int MaxPerMinute = 20;

    static readonly object Lock = new();
    static DateTime _windowStart = DateTime.MinValue;
    static int _inWindow;
    static DateTime _lastToast = DateTime.MinValue;
    static readonly string Version = typeof(CrashLog).Assembly.GetName().Version?.ToString(3) ?? "?";

    public static string Dir => Path.Combine(Paths.DataDir, "logs");
    public static string File => Path.Combine(Dir, "errors.log");
    static string Marker => Path.Combine(Dir, "crashed.flag");

    /// <summary>Ошибки вне потока интерфейса: записать причину до того, как процесс закроется.</summary>
    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is not Exception ex) return;
            Write("fatal", ex, force: true);
            if (e.IsTerminating)
            {
                try { System.IO.File.WriteAllText(Marker, DateTime.Now.ToString("o")); } catch { }
            }
        };
        // Забытая задача с ошибкой — не повод для вылета, но в журнал её стоит записать.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("task", e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>Ошибки в потоке интерфейса (таймеры, продолжения после await): в журнал, а программа живёт дальше.</summary>
    public static void InstallUi()
    {
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            e.Handled = true;
            Report("ui", e.Exception);
        };
    }

    /// <summary>Записать ошибку и коротко сказать о ней человеку (не чаще раза в 10 секунд).</summary>
    public static void Report(string where, Exception e)
    {
        Write(where, e);
        // Памяти не хватило — отпускаем картинки из сети: их всегда можно загрузить снова.
        if (e is OutOfMemoryException || e.InnerException is OutOfMemoryException) Images.Trim();
        if (DateTime.UtcNow - _lastToast < TimeSpan.FromSeconds(10)) return;
        _lastToast = DateTime.UtcNow;
        try
        {
            if (Dispatcher.UIThread.CheckAccess()) Toast(e);
            else Dispatcher.UIThread.Post(() => Toast(e));
        }
        catch { }
    }

    static void Toast(Exception e)
    {
        try { Views.MainWindow.Current?.Toast(I18n.T("err.unexpected", ("reason", Short(e))), bad: true); } catch { }
    }

    static string Short(Exception e)
    {
        var inner = e is AggregateException { InnerExceptions.Count: 1 } a ? a.InnerExceptions[0] : e;
        var text = inner.Message.ReplaceLineEndings(" ");
        return text.Length > 160 ? text[..160] + "…" : text;
    }

    /// <summary>Дописать ошибку в журнал. Журнал не растёт больше мегабайта, повторы не льются потоком.</summary>
    public static void Write(string where, Exception e, bool force = false)
    {
        try
        {
            lock (Lock)
            {
                var now = DateTime.UtcNow;
                if (now - _windowStart > TimeSpan.FromMinutes(1)) { _windowStart = now; _inWindow = 0; }
                if (++_inWindow > MaxPerMinute && !force) return;

                Directory.CreateDirectory(Dir);
                var info = new FileInfo(File);
                if (info.Exists && info.Length > MaxSize) System.IO.File.Move(File, Path.Combine(Dir, "errors.old.log"), true);
                var text = new StringBuilder()
                    .Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("] ")
                    .Append("ModLaunch ").Append(Version).Append(' ').Append(where)
                    .Append(", память ").Append(Environment.WorkingSet / (1024 * 1024)).Append(" МБ")
                    .AppendLine()
                    .AppendLine(e.ToString())
                    .AppendLine();
                System.IO.File.AppendAllText(File, text.ToString());
            }
        }
        catch { }
    }

    /// <summary>true — прошлый запуск закончился вылетом (отметка снимается).</summary>
    public static bool TakeCrashMarker()
    {
        try
        {
            if (!System.IO.File.Exists(Marker)) return false;
            System.IO.File.Delete(Marker);
            return true;
        }
        catch { return false; }
    }
}
