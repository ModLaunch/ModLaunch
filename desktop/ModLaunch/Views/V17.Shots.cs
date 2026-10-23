using Avalonia.Controls;
using Avalonia.VisualTree;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>
/// Снимки 17.0 (MODLAUNCH_SHOTS=V17): новые темы и акценты, сборки настроек от создателей — в профиле,
/// на пути уровней, в окне «Забрать» и в настройках, — и главная в одной из сборок.
/// </summary>
public static class V17Shots
{
    static void Shot(Shots s, string name, Func<Page> make, int ms = 1200)
    {
        s.Window.Navigate(make);
        s.Pump(ms);
        s.Save(name);
    }

    /// <summary>Прокрутить страницу до конца (полка сборок — последняя).</summary>
    static void Bottom(Shots s)
    {
        if (s.Window.CurrentPage?.GetVisualDescendants().OfType<ScrollViewer>().OrderByDescending(x => x.Extent.Height).FirstOrDefault() is { } sv)
            sv.Offset = new Avalonia.Vector(0, 100_000);
        s.Pump(600);
    }

    [DemoShots]
    static void V17Themes(Shots s)
    {
        var was = Settings.Data.Str("theme") ?? "store";
        foreach (var t in new[] { "ocean", "crimson", "cyber", "olive", "rose", "mint", "sky", "blossom" })
        {
            Look.SetTheme(t);
            Shot(s, "v17-theme-" + t, () => new HomePage(), 1300);
        }
        Look.SetTheme(was);
        Shot(s, "v17-look-accents", () => new SettingsPage("look"));
    }

    [DemoShots]
    static void V17Builds(Shots s)
    {
        var w = s.Window;
        Shot(s, "v17-cb-1-settings", () => new SettingsPage("look"));
        Shot(s, "v17-cb-2-profile", () => new ProfilePage("rewards"));
        Bottom(s);
        s.Save("v17-cb-3-shelf");

        // Окно «Забрать» с уровнем, за который дают сборку.
        var (level, _, _) = RoadKit.Now();
        if (CreatorBuilds.All.LastOrDefault(b => b.Level <= level) is { } got)
        {
            Settings.Data.Obj("profile")["roadSeen"] = got.Level - 1;
            Shot(s, "v17-cb-4-home", () => new HomePage(), 1500);
            RoadKit.Claim();
            s.Pump(900);
            s.Save("v17-cb-5-claim");
            w.CloseDialog();
        }

        // Вся программа в сборке создателя.
        foreach (var id in new[] { "legend", "morning" })
        {
            CreatorBuilds.Apply(CreatorBuilds.Find(id)!);
            Shot(s, "v17-cb-6-" + id, () => new HomePage(), 1400);
        }
        Configs.Apply(Configs.BuiltIn().First(c => c.Id == "modlaunch"));
        s.Pump(300);
    }
}
