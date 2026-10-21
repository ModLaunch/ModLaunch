using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>Снимки 12.0: главная (сцена со свечением, быстрые кнопки) и спокойный экран запуска.</summary>
public static class V12Shots
{
    [DemoShots]
    static void Twelve0Home(Shots s)
    {
        Settings.Data.Remove("widgets");
        s.Window.Navigate(() => new HomePage());
        s.Pump(1400);
        s.Save("v12-0-home");
    }

    [DemoShots]
    static void Twelve1Launch(Shots s)
    {
        var w = s.Window;
        if (AppState.Games.FirstOrDefault(x => x.Status == Detect.Found && x.Def.Id == "hollow-knight") is not { } g) return;
        var launch = w.ShowLaunch(g);
        s.Pump(500);
        launch.DemoProgress();
        s.Save("v12-1-launch");
        w.HideLaunch();
    }
}
