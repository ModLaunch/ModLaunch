using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Social;

namespace ModLaunch.Views;

/// <summary>
/// Снимки 13.0 (MODLAUNCH_SHOTS=V13): Creator Hub в четырёх разделах, консоль и справка ModScript,
/// группа разработчиков (своя и пустая) и виджеты с видом, новыми размерами и сеткой.
/// </summary>
public static class V13Shots
{
    static void SeedProjects()
    {
        if (Projects.List().Count >= 3) return;
        foreach (var id in new[] { "sdv-seeds", "sdv-fish", "valheim-builder" })
            Projects.Create(I18n.T($"cr.ex.{id}"), Templates.ById(id)!.Code);
    }

    static void DemoTeam()
    {
        var now = DateTime.UtcNow;
        var names = new Dictionary<string, string> { [Teams.DemoUid] = "Максим", ["u2"] = "Lera", ["u3"] = "Kostya_dev", ["u4"] = "Sasha", ["u5"] = "Dima" };
        var team = new Team("demo1", "Valley Makers", "VLM", "green", "Моды для Stardew Valley: фермы, рыбалка и новые NPC.", Teams.DemoUid,
            [Teams.DemoUid, "u2", "u3", "u4"], names, ["u5"], "K7MPQ2XA", now.AddDays(-12), now.AddHours(-2));
        var second = new Team("demo2", "Night Shift", "NS", "violet", "", "u3", ["u3", Teams.DemoUid], names, [], "", now.AddDays(-3), now.AddDays(-1));
        var invite = new Team("demo3", "Heim Builders", "HB", "orange", "Строительство в Valheim.", "u9", ["u9"], new() { ["u9"] = "Oleg" }, [Teams.DemoUid], "", now.AddDays(-1), now);
        Teams.DemoMine = [team, second];
        Teams.DemoInvites = [invite];
        Teams.DemoProjects = new()
        {
            ["demo1"] =
            [
                new("sdv-fish", "Рыбалка без нервов", "stardew-valley", "1.3.0", "u2", "Lera", Templates.ById("sdv-fish")!.Code, now.AddHours(-2)),
                new("sdv-seeds", "Семена круглый год", "stardew-valley", "2.0.1", Teams.DemoUid, "Максим", Templates.ById("sdv-seeds")!.Code, now.AddDays(-1)),
                new("npc-alex", "Новый NPC: Алекс", "stardew-valley", "0.4.0", "u3", "Kostya_dev", "mod \"Alex\"\ngame stardew-valley\n", now.AddDays(-4)),
            ],
        };
    }

    static void NoTeam()
    {
        Teams.DemoMine = null;
        Teams.DemoInvites = null;
        Teams.DemoProjects = null;
    }

    [DemoShots]
    static void V13Hub1Projects(Shots s)
    {
        SeedProjects();
        s.Window.Navigate(() => new CreatorPage("home"));
        s.Pump(900);
        s.Save("v13-1-projects");
    }

    [DemoShots]
    static void V13Hub2Console(Shots s)
    {
        s.Window.Navigate(() => new CreatorPage("console"));
        s.Pump(1200);
        s.Save("v13-2-console");
    }

    [DemoShots]
    static void V13Hub3Commands(Shots s)
    {
        s.Window.Navigate(() => new CreatorPage("commands"));
        s.Pump(900);
        s.Save("v13-3-commands");
    }

    [DemoShots]
    static void V13Hub4Team(Shots s)
    {
        DemoTeam();
        try
        {
            s.Window.Navigate(() => new CreatorPage("team"));
            s.Pump(1000);
            s.Save("v13-4-team");
        }
        finally { NoTeam(); }
    }

    [DemoShots]
    static void V13Hub5TeamEmpty(Shots s)
    {
        Teams.DemoMine = [];
        Teams.DemoInvites = [];
        try
        {
            s.Window.Navigate(() => new CreatorPage("team"));
            s.Pump(900);
            s.Save("v13-5-team-empty");
        }
        finally { NoTeam(); }
    }

    static void Layout()
    {
        Settings.Data["widgets"] = new System.Text.Json.Nodes.JsonArray(
            "game xl game=steam%3A1245620;corner=round",
            "clock s style=tinted;tint=violet",
            "level s style=glass;tint=orange;label=" + Uri.EscapeDataString(I18n.T("v13.shot.level")),
            "playtime w style=outline;tint=green",
            "streak t style=tinted;tint=red",
            "note s style=glass;tint=yellow;text=" + Uri.EscapeDataString(I18n.T("v12w.shot.note")),
            "builds s style=clear;caption=0",
            "continue m corner=square");
        Settings.Data["widgetGap"] = "wide";
    }

    static void Clean()
    {
        Settings.Data.Remove("widgets");
        Settings.Data.Remove("widgetGap");
        Settings.Data.Remove("widgetCell");
    }

    [DemoShots]
    static void V13Widgets1Home(Shots s)
    {
        Layout();
        try
        {
            s.Window.Navigate(() => new HomePage());
            s.Pump(900);
            if (s.Window.CurrentPage?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } scroll) scroll.Offset = new Vector(0, 520);
            s.Pump(500);
            s.Save("v13-6-widgets");
        }
        finally { Clean(); }
    }

    [DemoShots]
    static void V13Widgets2Edit(Shots s)
    {
        Layout();
        try
        {
            HomePage.EditWidgets();
            s.Window.Navigate(() => new HomePage());
            s.Pump(900);
            if (s.Window.CurrentPage?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } scroll) scroll.Offset = new Vector(0, 460);
            s.Pump(500);
            s.Save("v13-7-widgets-edit");
            (s.Window.CurrentPage as HomePage)?.OpenStudio(edit: 1);
            s.Pump(700);
            s.Save("v13-8-widget-look");
            s.Window.CloseDialog();
        }
        finally { HomePage.EditWidgets(false); Clean(); }
    }
}
