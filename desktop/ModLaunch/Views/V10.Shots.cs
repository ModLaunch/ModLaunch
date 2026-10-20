using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>Снимки 10.0: главная со сценой и виджетами, конфиги, автопилот, кейсы, запуск игры, Creator Hub.</summary>
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
    static void Ten2Cases(Shots s)
    {
        var w = s.Window;
        // Неделя игры — один кейс, чтобы на снимке всегда было что открыть.
        Cases.NoteWeek();
        w.Navigate(() => new CasesPage());
        s.Pump(1000);
        s.Save("v10-3-cases");
        if (w.CurrentPage is CasesPage page)
        {
            if (Animate.On)
            {
                // MODLAUNCH_ANIM=1: кадры открытия кейса.
                page.DemoOpen();
                // Save сам ждёт 600 мс: кадры примерно на 0,6 / 1,2 / 1,8 с.
                for (var i = 1; i <= 3; i++) s.Save($"v10-3-open-{i}");
            }
            page.DemoShow(new Loot("cfg", "synthwave", "legend"), "legend");
            s.Pump(700);
            s.Save("v10-4-case-loot");
        }
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
}
