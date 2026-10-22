using System.Diagnostics;
using Avalonia.Threading;

namespace ModLaunch.Core;

/// <summary>
/// 15.0: сторож отклика. Отдельный поток раз в 250 мс спрашивает поток интерфейса «ты тут?»;
/// если ответ шёл дольше 400 мс, в logs/ui-stalls.log пишется строка: сколько ждали и что в этот
/// момент делала программа (Doing — страница или действие). Так задержку кнопок окна и страниц можно
/// найти на любом компьютере, а не гадать. Файл не растёт больше 256 КБ.
/// </summary>
public static class UiWatch
{
    /// <summary>Что сейчас делает интерфейс (ставят переходы по страницам и долгие действия).</summary>
    public static volatile string Doing = "start";

    public static int Stalls { get; private set; }
    public static long Worst { get; private set; }

    static string LogFile => Path.Combine(Directory.CreateDirectory(Path.Combine(Paths.DataDir, "logs")).FullName, "ui-stalls.log");

    static Thread? _thread;

    public static void Start()
    {
        if (_thread is not null || Program.Screenshot) return;
        _thread = new Thread(Loop) { IsBackground = true, Name = "ui-watch", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    static void Loop()
    {
        var sw = new Stopwatch();
        while (true)
        {
            Thread.Sleep(250);
            var doing = Doing;
            sw.Restart();
            try
            {
                // Самый высокий приоритет: меряем, свободен ли поток, а не длину очереди перерисовок.
                Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Send).GetTask().Wait(TimeSpan.FromSeconds(20));
            }
            catch { return; }
            var ms = sw.ElapsedMilliseconds;
            if (ms < 400) continue;
            Stalls++;
            Worst = Math.Max(Worst, ms);
            Write($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {ms,6} ms  {doing} → {Doing}");
        }
    }

    static void Write(string line)
    {
        try
        {
            var file = LogFile;
            if (File.Exists(file) && new FileInfo(file).Length > 256 * 1024) File.Move(file, file + ".old", true);
            File.AppendAllText(file, line + Environment.NewLine);
        }
        catch { }
    }

    /// <summary>Пометить долгое действие: using (UiWatch.Mark("…")) { … }.</summary>
    public static IDisposable Mark(string what)
    {
        var before = Doing;
        Doing = what;
        return new Restore(before);
    }

    sealed class Restore(string before) : IDisposable
    {
        public void Dispose() => Doing = before;
    }
}
