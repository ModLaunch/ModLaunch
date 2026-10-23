using System.Diagnostics;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>
/// 17.0: запись анимаций кадрами (MODLAUNCH_SHOTS=Motion, MODLAUNCH_ANIM=1, MODLAUNCH_MOTION=1). Каждая анимация
/// снимается по реальным часам: кадр примерно раз в 30 мс, имя кадра — «motion-вид-NN-мс». Из кадров собираются
/// GIF «до/после» и проверяется, что ничего не обрезается и не дёргается. В обычном прогоне снимков не участвует.
/// </summary>
public static class MotionShots
{
    /// <summary>Часы анимаций, которые идут только по команде: кадр снимается ровно в свой момент, как бы долго ни рисовался.</summary>
    /// <summary>
    /// Часы анимаций по команде. Свои часы Avalonia не даёт подставить (IClock закрыт), поэтому тикаем
    /// таймер отрисовки сами — с нужным временем, а не с настоящим: кадр снимается ровно в свой момент,
    /// как бы долго он ни рисовался.
    /// </summary>
    static Action<TimeSpan>? FindTick()
    {
        const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
        var asm = typeof(Visual).Assembly;
        var type = asm.GetType("Avalonia.Rendering.IRenderTimer");
        var locator = asm.GetType("Avalonia.AvaloniaLocator")?.GetProperty("Current", Any)?.GetValue(null);
        var timer = type is null || locator is null ? null : locator.GetType().GetMethod("GetService", Any, [typeof(Type)])?.Invoke(locator, [type]);
        for (var t = timer?.GetType(); t is not null; t = t.BaseType)
            foreach (var f in t.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public))
                if (f.GetValue(timer) is Action<TimeSpan> tick) return tick;
        Console.WriteLine("motion: render timer not found (" + timer?.GetType().FullName + ")");
        return null;
    }

    static void Record(Shots s, string name, Action start, int ms, int every = 33)
    {
        var w = s.Window;
        var tick = FindTick();
        var pending = new List<(double At, Action Run)>();
        var now = 0.0;
        var t0 = Environment.TickCount64 + 50;
        void Step(double at)
        {
            foreach (var p in pending.Where(p => p.At <= at).ToList()) { pending.Remove(p); p.Run(); }
            tick?.Invoke(TimeSpan.FromMilliseconds(t0 + at));
            Dispatcher.UIThread.RunJobs();
        }
        Animate.Delay = (a, delay) => pending.Add((now + delay, a));
        var frames = 0;
        try
        {
            Step(0);
            start();
            for (now = 0; now <= ms; now += every)
            {
                Step(now);
                using (var f = w.CaptureRenderedFrame()) f?.Save(Path.Combine(s.OutDir, $"motion-{name}-{frames++:00}-{now:0000}.png"));
            }
        }
        finally
        {
            // Довести всё до конца и вернуть обычное время: иначе начатые переходы застынут на полпути.
            Animate.Delay = null;
            for (var i = 0; i < 60; i++) { now += 50; Step(now); }
            while (Environment.TickCount64 < t0 + now + 20) Thread.Sleep(10);
        }
        Console.WriteLine($"motion {name}: {frames} frames, {ms} ms");
    }

    static void Settle(Shots s, Func<Page> make, int ms = 900)
    {
        s.Window.CloseDialog();
        s.Window.Navigate(make);
        s.Pump(ms);
    }

    [DemoShots]
    static void Motion(Shots s)
    {
        if (Environment.GetEnvironmentVariable("MODLAUNCH_MOTION") != "1") return;
        var w = s.Window;

        // Переход между страницами: главная → библиотека.
        Settle(s, () => new HomePage());
        Record(s, "page", () => w.Navigate(() => new LibraryPage()), 520);

        // Открытие игры: арт, обложка и надписи.
        Settle(s, () => new LibraryPage());
        Record(s, "game", () => w.Navigate(() => new GamePage("subnautica")), 1500, 50);

        // Карточки профиля поднимаются по очереди.
        Settle(s, () => new HomePage());
        Record(s, "profile", () => w.Navigate(() => new ProfilePage("rewards")), 700);

        // Окно «Забрать награды».
        var (level, _, _) = RoadKit.Now();
        Settings.Data.Obj("profile")["roadSeen"] = Math.Max(1, level - 3);
        Settle(s, () => new HomePage());
        Record(s, "dialog", RoadKit.Claim, 420);
        w.CloseDialog();

        // Уведомление справа внизу.
        Settle(s, () => new HomePage());
        Record(s, "toast", () => w.Toast("ModLaunch · Subnautica — " + I18n.T("dl.done").ToLowerInvariant()), 520);

        // Смена игры на сцене главной.
        Settle(s, () => new HomePage(), 1200);
        Record(s, "stage", () => (w.CurrentPage as HomePage)?.StepStage(1), 420);

        // Боковая панель сведений.
        Settle(s, () => new GamePage("subnautica", "installed"));
        Record(s, "aside", w.ToggleAside, 480);
        w.ToggleAside();

        // Экран запуска игры.
        Settle(s, () => new HomePage());
        var g = AppState.Games.First(x => x.Status == Detect.Found && !x.Def.IsMinecraft);
        Record(s, "launch", () =>
        {
            var screen = w.ShowLaunch(g);
            screen.SetStep(0, LaunchScreen.State.Working, null);
            DispatcherTimer.RunOnce(() => { screen.SetStep(0, LaunchScreen.State.Done, null); screen.SetStep(1, LaunchScreen.State.Working, null); }, TimeSpan.FromMilliseconds(500));
        }, 1100, 50);
        w.HideLaunch();
        s.Pump(300);
    }
}
