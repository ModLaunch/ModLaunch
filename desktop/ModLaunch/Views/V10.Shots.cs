using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>Снимки 10.0: главная со сценой и виджетами, конфиги, автопилот, запуск игры, Creator Hub, Minecraft.</summary>
public static class V10Shots
{
    [DemoShots]
    static void Ten0Home(Shots s)
    {
        var w = s.Window;
        Settings.Data.Remove("widgets");
        w.Navigate(() => new HomePage());
        s.Pump(1400);
        s.Save("v10-0-home");
        if (Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(w).OfType<HomePage>().FirstOrDefault()?.Content is Avalonia.Controls.ScrollViewer sv)
        {
            sv.Offset = new Avalonia.Vector(0, 520);
            s.Pump(500);
            s.Save("v10-1-home-widgets");
        }
    }

    [DemoShots]
    static void Ten1Pilot(Shots s)
    {
        var w = s.Window;
        w.Navigate(() => new AutopilotPage());
        s.Pump(1000);
        s.Save("v10-2-autopilot");
    }

    [DemoShots]
    static void Ten3Creator(Shots s)
    {
        var w = s.Window;
        w.Navigate(() => new CreatorPage());
        s.Pump(1200);
        s.Save("v10-5-creator");
    }

    [DemoShots]
    static void Ten4Configs(Shots s)
    {
        var w = s.Window;
        w.Navigate(() => new SettingsPage("configs"));
        s.Pump(1000);
        s.Save("v10-6-configs");
        // Конфиг «Ретро» — вся программа в другом стиле.
        Configs.Apply(Configs.BuiltIn().First(c => c.Id == "retro"));
        w.Navigate(() => new HomePage());
        s.Pump(1200);
        s.Save("v10-7-retro");
        Configs.Apply(Configs.BuiltIn().First(c => c.Id == "modlaunch"));
        s.Pump(300);
    }

    [DemoShots]
    static void Ten5Launch(Shots s)
    {
        var w = s.Window;
        var g = AppState.Games.FirstOrDefault(x => x.Status == Detect.Found && !x.Def.IsMinecraft);
        if (g is not null)
        {
            w.Navigate(() => new HomePage());
            s.Pump(600);
            var launch = w.ShowLaunch(g);
            s.Pump(600);
            launch.DemoProgress();
            s.Save("v10-8-launch");
            w.HideLaunch();
        }
        CloudFlow.DemoOffer();
        s.Save("v10-9-cloud");
        w.CloseDialog();
    }

    [DemoShots]
    static void Ten6GamePages(Shots s)
    {
        var w = s.Window;
        var g = AppState.Games.FirstOrDefault(x => x.Status == Detect.Found && !x.Def.IsMinecraft);
        if (g is not null)
        {
            w.Navigate(() => new GamePage(g.Def.Id));
            s.Pump(1000);
            s.Save("v10-a-game");
        }
        w.Navigate(() => new MinecraftPage());
        s.Pump(1000);
        s.Save("v10-b-minecraft");
        if (w.CurrentPage?.Content is Avalonia.Controls.ScrollViewer mc)
        {
            mc.Offset = new Avalonia.Vector(0, 560);
            s.Pump(500);
            s.Save("v10-b2-minecraft-lower");
        }
        w.Navigate(() => new MinecraftPage("mods"));
        s.Pump(800);
        s.Save("v10-c-minecraft-mods");
    }
}
