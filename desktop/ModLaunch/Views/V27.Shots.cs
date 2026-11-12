using ModLaunch.Core;
using ModLaunch.Games;

namespace ModLaunch.Views;

/// <summary>
/// Снимки 27.0 (MODLAUNCH_SHOTS=V27Library): единая библиотека «Все игры» на демо-данных — все магазины,
/// фильтр по одному магазину, выбранная игра не из Steam, ссылка из «Игр» и светлая тема.
/// </summary>
public static class V27Shots
{
    [DemoShots]
    static void V27Library(Shots s)
    {
        s.Pump(4500);
        Settings.Data["allGamesStore"] = "all";
        Settings.Data["allGamesSel"] = "steam:1245620";
        AllGames.Refresh();
        s.Window.Navigate(() => new AllGamesPage());
        s.Pump(2500);
        s.Save("v27-01-all-games");

        s.Window.Navigate(() => new AllGamesPage("epic:Hades"));
        s.Pump(1500);
        s.Save("v27-02-epic-game");

        Settings.Data["allGamesStore"] = "battlenet";
        s.Window.Navigate(() => new AllGamesPage("battlenet:prometheus"));
        s.Pump(1500);
        s.Save("v27-03-battlenet-filter");
        Settings.Data["allGamesStore"] = "all";

        s.Window.Navigate(() => new LibraryPage());
        s.Pump(1500);
        s.Save("v27-04-library-link");

        var was = Settings.Data.Str("theme") ?? "store";
        Look.SetTheme("light");
        try
        {
            s.Window.Navigate(() => new AllGamesPage("xbox:Microsoft.624F8B84B80_8wekyb3d8bbwe"));
            s.Pump(1500);
            s.Save("v27-05-light-xbox");
        }
        finally { Look.SetTheme(was); }
    }
}
