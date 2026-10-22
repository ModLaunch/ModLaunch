using Avalonia.Controls;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>Снимки 15.1 (MODLAUNCH_SHOTS=V151): каталог в библиотеке, новая игра везде, смена игры на сцене.</summary>
public static class V151Shots
{
    /// <summary>Одна из найденных игр, в которую ещё не играли, — «только что появилась».</summary>
    static void MarkNew()
    {
        var g = AppState.Games.FirstOrDefault(x => x.Status == Detect.Found && Features.PlayTime.Get(x.Def.Id).LastPlayed is null && !x.Def.IsMinecraft);
        if (g is not null) Features.NewGames.Mark(g.Def.Id);
        AppState.Notify();
    }

    static void Scroll(Shots s, double y)
    {
        if (s.Window.CurrentPage?.Content is ScrollViewer sv) sv.Offset = new Avalonia.Vector(0, y);
        s.Pump(300);
    }

    [DemoShots]
    static void V151Library(Shots s)
    {
        MarkNew();
        s.Window.Navigate(() => new LibraryPage());
        s.Pump(2500);
        s.Save("v151-library-1");
        Scroll(s, 100_000);
        s.Save("v151-library-2-catalog");
        s.Window.CurrentPage?.Search("hollow");
        s.Pump(600);
        Scroll(s, 100_000);
        s.Save("v151-library-3-search");
    }

    [DemoShots]
    static void V151Home(Shots s)
    {
        MarkNew();
        s.Window.Navigate(() => new HomePage());
        s.Pump(1500);
        s.Save("v151-home-1-new");
        if (s.Window.CurrentPage is HomePage hp) hp.ShotSwitch();
        s.Pump(1500);
        s.Save("v151-home-2-switched");
    }
}
