using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>Снимки 30.0 (MODLAUNCH_SHOTS=V30…) на демо-данных.</summary>
public static class V30Shots
{
    /// <summary>MODLAUNCH_SHOTS=V30Nexus: окно ожидания браузера Nexus в задаче на несколько модов — «Пропустить» и «Отменить всё».</summary>
    [DemoShots]
    static void V30Nexus(Shots s)
    {
        s.Pump(4500);
        var job = new Job { Title = "Обновления: 3", GameName = "Skyrim" };
        Actions.ShowNexusWait("SkyUI", "https://www.nexusmods.com/", job, new TaskCompletionSource<object>(), skippable: true);
        s.Pump(800);
        s.Save("v30-01-nexus-skip");
        s.Window.CloseDialog();
    }

    /// <summary>MODLAUNCH_SHOTS=V30Shot: настройки снимка экрана в игре (рядом с оверлеем).</summary>
    [DemoShots]
    static void V30Shot(Shots s)
    {
        s.Pump(4500);
        s.Window.Navigate(() => new SettingsPage("launch"));
        s.Pump(1500);
        if (s.Window.CurrentPage is SettingsPage p) p.DemoScroll(1080);
        s.Pump(800);
        s.Save("v30-02-shot-settings");
    }

    /// <summary>MODLAUNCH_SHOTS=V30Ach: достижения Steam на странице игры во «Всех играх».</summary>
    [DemoShots]
    static void V30Ach(Shots s)
    {
        s.Pump(4500);
        Games.AllGames.Refresh();
        s.Pump(1500);
        s.Window.Navigate(() => new AllGamesPage("steam:1245620"));
        s.Pump(2500);
        s.Save("v30-03-steam-achievements");
    }

    /// <summary>MODLAUNCH_SHOTS=V30LibOverlay: оверлей поверх игры из «Всех игр» (Epic): время, снимок, друзья.</summary>
    [DemoShots]
    static void V30LibOverlay(Shots s)
    {
        s.Pump(4500);
        Games.AllGames.Refresh();
        s.Pump(1500);
        if (Games.AllGames.Current?.FirstOrDefault(g => g.Key == "epic:Hades") is not { } lib) return;
        OverlayWindow.OnLibraryGameStarted(lib);
        OverlayWindow.Toggle();
        s.Pump(1500);
        if (OverlayWindow.Current is { } o) Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(o)?.Save(Path.Combine(s.OutDir, "v30-04-library-overlay.png"));
        Console.WriteLine("saved v30-04-library-overlay");
        OverlayWindow.OnLibraryGameExited(lib);
        s.Pump(300);
    }
}
