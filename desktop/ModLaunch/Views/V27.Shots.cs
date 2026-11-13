using ModLaunch.Core;
using ModLaunch.Features;
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

    /// <summary>MODLAUNCH_SHOTS=V27Safety: журнал обновлений (плашка и окно) и сейф модов в «Как было».</summary>
    [DemoShots]
    static void V27Safety(Shots s)
    {
        s.Pump(4500);
        Features.UpdateLog.DemoFill();
        s.Window.Navigate(() => new GamePage("subnautica", "installed"));
        s.Pump(1500);
        s.Save("v27-06-updates-notice");
        UpdateLogUi.Show("subnautica");
        s.Pump(800);
        s.Save("v27-07-update-log");
        s.Window.CloseDialog();

        // Сейф: старая версия мода и мод, поставленный руками, вместо удаления — в ModHub/safe.
        var sn = AppState.Game("subnautica");
        if (sn.Registry is { } registry)
        {
            var old = Directory.CreateDirectory(Path.Combine(registry.ModsDir, "OldMapMod")).FullName;
            File.WriteAllBytes(Path.Combine(old, "MapMod.dll"), new byte[180_000]);
            Features.ModSafe.Remove(registry, old, new System.Text.Json.Nodes.JsonObject { ["id"] = "MapMod", ["name"] = "Map", ["version"] = "1.4.3", ["folder"] = "OldMapMod" }, "update");
        }
        Snapshots.Take(sn, "update");
        s.Window.Navigate(() => new GamePage("subnautica", "profiles"));
        s.Pump(1500);
        V23Shots.Scroll(s, 1);
        s.Save("v27-08-mod-safe");
    }

    /// <summary>MODLAUNCH_SHOTS=V27Support: «Поддержать ModLaunch» (страница и окно) и «Поддержать автора».</summary>
    [DemoShots]
    static void V27Support(Shots s)
    {
        s.Pump(4500);
        s.Window.Navigate(() => new DonatePage());
        s.Pump(1200);
        s.Save("v27-09-donate-page");
        TipUi.Show(Creator.Tips.Platform, "ModLaunch");
        s.Pump(800);
        s.Save("v27-10-support-modlaunch");
        s.Window.CloseDialog();
        TipUi.Show("demo_author", "NightOwl");
        s.Pump(800);
        s.Save("v27-11-support-author");
        s.Window.CloseDialog();
    }
}
