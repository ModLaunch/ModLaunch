using Avalonia;
using Avalonia.Controls;
using ModLaunch.Core;
using ModLaunch.Minecraft;

namespace ModLaunch.Views;

/// <summary>Снимки 9.4: картинки сборок, серверы, экспорт, окно и Java, «поставить игру», настройки загрузок.</summary>
public static class V94Shots
{
    [DemoShots]
    static void Minecraft94Shots(Shots s)
    {
        var w = s.Window;
        McDemo.Pictures();
        // Одной сборке — своя встроенная картинка, остальным — по умолчанию.
        if (Mc.Instances().Skip(1).FirstOrDefault() is { } second) McIcons.SetBuiltin(second, "amethyst");

        w.Navigate(() => new MinecraftPage("builds"));
        s.Pump(1000);
        s.Save("v94-1-builds");

        if (Mc.Active is { } a)
        {
            MinecraftPage.ShotIconDialog(a);
            s.Pump(700);
            s.Save("v94-2-icon-dialog");
            w.CloseDialog();

            MinecraftPage.ShotExportDialog(a);
            s.Pump(700);
            s.Save("v94-3-export");
            w.CloseDialog();
        }

        w.Navigate(() => new MinecraftPage("servers"));
        s.Pump(900);
        s.Save("v94-4-servers");

        w.Navigate(() => new MinecraftPage("settings"));
        s.Pump(900);
        s.Save("v94-5-settings");
        if (MainWindowScroller(w) is { } sv) sv.Offset = new Vector(0, 900);
        s.Pump(500);
        s.Save("v94-5b-settings-down");

        w.Navigate(() => new MinecraftPage("log"));
        s.Pump(700);
        s.Save("v94-6-log");

        w.Navigate(() => new MinecraftPage("mods"));
        s.Pump(900);
        s.Save("v94-7-mods");
    }

    [DemoShots]
    static void Get94Shots(Shots s)
    {
        var w = s.Window;
        var hk = AppState.Games.FirstOrDefault(g => g.Status != Detect.Found && g.Def.SteamAppId > 0);
        if (hk is not null)
        {
            w.Navigate(() => new GamePage(hk.Def.Id));
            s.Pump(900);
            s.Save("v94-8-get-game");
            Features.GetGame.Watch(hk);
            w.Navigate(() => new GamePage(hk.Def.Id));
            s.Pump(700);
            s.Save("v94-8b-get-waiting");
            Features.GetGame.StopWatching(hk.Def.Id);
        }
        w.Navigate(() => new SettingsPage("downloads"));
        s.Pump(900);
        s.Save("v94-9-downloads-settings");
        WhatsNew.Show();
        s.Pump(900);
        s.Save("v94-10-whatsnew");
        w.CloseDialog();
    }

    static ScrollViewer? MainWindowScroller(MainWindow w) => w.CurrentPage is UserControl { Content: ScrollViewer sv } ? sv : null;
}
