using Avalonia;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>Снимки 9.2 «Store»: главная, библиотека, страница игры, страница мода, три оформления, анимации открытия мода.</summary>
public static class V92Shots
{
    [DemoShots]
    static void Store92Shots(Shots s)
    {
        var w = s.Window;
        w.Navigate(() => new HomePage());
        s.Pump(1500);
        w.Navigate(() => new HomePage());
        s.Pump(900);
        s.Save("v92-1-home");

        w.Navigate(() => new LibraryPage());
        s.Pump(900);
        s.Save("v92-2-library");
        Settings.Data["libraryView"] = "list";
        w.Navigate(() => new LibraryPage());
        s.Pump(900);
        s.Save("v92-2-library-list");
        Settings.Data["libraryView"] = "grid";

        foreach (var (id, tab, name) in new[] { ("subnautica", "", "overview"), ("subnautica", "installed", "installed"), ("lethal-company", "catalog", "catalog"), ("valheim", "", "loader"), ("hollow-knight", "", "notfound") })
        {
            w.Navigate(() => new GamePage(id, tab));
            s.Pump(1400);
            s.Save("v92-3-game-" + name);
        }

        // Страница мода и три анимации её открытия (кадр в начале и в середине).
        var mod = Demo.Many(Games.GameCatalog.ById("subnautica")!, ["2800"])[0];
        w.Navigate(() => new ModPage("subnautica", mod));
        s.Pump(1200);
        s.Save("v92-4-mod");
        foreach (var kind in new[] { OpenKind.Zoom, OpenKind.Slide, OpenKind.Cascade })
        {
            w.Navigate(() => new GamePage("subnautica", "catalog"));
            s.Pump(900);
            w.Navigate(() => new ModPage("subnautica", mod));
            s.Pump(300);
            var run = ModOpenFx.Play(w.CurrentPage!, w.CurrentPage!, new Point(380, 420), kind)!;
            foreach (var ms in new[] { 120, 300 })
            {
                run.Seek(ms);
                s.Pump(80);
                s.Save($"v92-5-open-{kind.ToString().ToLowerInvariant()}-{ms}");
            }
            run.Finish();
            s.Pump(200);
        }

        // Настройки → Внешний вид и окно выбора оформления.
        w.Navigate(() => new SettingsPage("look"));
        s.Pump(900);
        s.Save("v92-6-settings-look");
        StylePicker.Show();
        s.Pump(700);
        s.Save("v92-6-picker");
        w.CloseDialog();

        // Три оформления: главная и страница игры в каждом.
        foreach (var style in Look.Styles)
        {
            Look.SetStyle(style.Id);
            w.Navigate(() => new HomePage());
            s.Pump(900);
            s.Save($"v92-7-{style.Id}-home");
            w.Navigate(() => new GamePage("subnautica"));
            s.Pump(1200);
            s.Save($"v92-7-{style.Id}-game");
            w.Navigate(() => new LibraryPage());
            s.Pump(900);
            s.Save($"v92-7-{style.Id}-library");
            w.Navigate(() => new GamePage("lethal-company", "catalog"));
            s.Pump(1200);
            s.Save($"v92-7-{style.Id}-catalog");
            w.Navigate(() => new ModPage("subnautica", mod));
            s.Pump(1000);
            s.Save($"v92-7-{style.Id}-mod");
        }
        Look.SetStyle("dark");
    }
}
