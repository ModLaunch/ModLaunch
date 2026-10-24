using Avalonia.Controls;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>
/// Снимки 20.0 (MODLAUNCH_SHOTS=V20): рамка окна и экраны по эскизам — главная, библиотека, страница игры,
/// каталог, страница мода, Creator Hub, свёрнутая панель и три темы. Окно 1440×900, как на эскизах.
/// </summary>
public static class V20Shots
{
    /// <summary>Окно 1440×900 и вид 20.0 по умолчанию: при полном прогоне снимки до этого меняют тему, конфиг и строку состояния.</summary>
    static void Size(Shots s, double w = 1440, double h = 900)
    {
        var d = Settings.Data;
        d["design"] = "store";
        d["theme"] = "lilac";
        d["accent"] = "#E2A8FF";
        d["font"] = "Onest";
        d["headings"] = "wide";
        d["statusBar"] = false;
        d.Remove("sideLabels");
        foreach (var key in new[] { "config", "customPalette", "customBase", "ownerStats", "uiScale", "radius", "fxScanlines", "fxVignette" }) d.Remove(key);
        Look.Apply();
        s.Window.Width = w;
        s.Window.Height = h;
        s.Pump(300);
    }

    static void Scroll(Shots s, double y)
    {
        foreach (var sv in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(s.Window.CurrentPage!).OfType<ScrollViewer>().Take(1))
            sv.Offset = new Avalonia.Vector(0, y);
        s.Pump(400);
    }

    [DemoShots]
    static void V20Home(Shots s)
    {
        Size(s);
        s.Window.Navigate(() => new HomePage());
        s.Pump(2500);
        s.Save("v20-home-1");
        Scroll(s, 520);
        s.Save("v20-home-2-picks");
    }

    [DemoShots]
    static void V20Themes(Shots s)
    {
        Size(s);
        try
        {
            foreach (var style in new[] { "terminal", "redwhite" })
            {
                Look.SetStyle(style);
                s.Window.Navigate(() => new HomePage());
                s.Pump(1800);
                s.Save("v20-theme-" + style);
            }
        }
        finally { Look.SetStyle("lilac"); }
    }

    [DemoShots]
    static void V20Collapsed(Shots s)
    {
        Size(s);
        Settings.Data["sideLabels"] = true;
        try
        {
            s.Window.Navigate(() => new HomePage());
            s.Pump(1500);
            s.Save("v20-side-labels");
        }
        finally { Settings.Data.Remove("sideLabels"); }
    }

    [DemoShots]
    static void V20Library(Shots s)
    {
        Size(s);
        s.Window.Navigate(() => new LibraryPage());
        s.Pump(2500);
        s.Save("v20-library-1");
        Scroll(s, 600);
        s.Save("v20-library-2");
    }

    /// <summary>Игра с модами (или первая найденная): «Обзор», затем вкладка «Моды».</summary>
    static GameState? ShotGame() =>
        AppState.Games.Where(g => g.Status == Detect.Found && g.Def.Loader != Games.LoaderKind.Minecraft).OrderByDescending(g => g.ModCount).FirstOrDefault();

    [DemoShots]
    static void V20Game(Shots s)
    {
        Size(s);
        if (ShotGame() is not { } g) return;
        s.Window.Navigate(() => new GamePage(g.Def.Id));
        s.Pump(2500);
        s.Save("v20-game-1");
        Scroll(s, 560);
        s.Save("v20-game-2");
        s.Window.Navigate(() => new GamePage(g.Def.Id, "installed"));
        s.Pump(1500);
        s.Save("v20-game-3-mods");
    }

    [DemoShots]
    static void V20Catalog(Shots s)
    {
        Size(s);
        s.Window.Navigate(() => new GamePage("subnautica", "catalog"));
        s.Pump(2500);
        s.Save("v20-catalog-1");
        Scroll(s, 700);
        s.Save("v20-catalog-2");
    }

    [DemoShots]
    static void V20Mod(Shots s)
    {
        Size(s);
        var mod = Demo.Many(Games.GameCatalog.ById("subnautica")!, ["2800"])[0];
        s.Window.Navigate(() => new ModPage("subnautica", mod));
        s.Pump(2500);
        s.Save("v20-mod-1");
        Scroll(s, 640);
        s.Save("v20-mod-2");

        // Установленный мод с картинками: картинки — арт игр из ресурсов, подложенный в кэш картинок.
        var urls = new List<string>();
        foreach (var name in new[] { "banner-hero.jpg", "game-subnautica.png", "game-subnautica-below-zero.png", "banner-deps.jpg", "game-valheim.jpg", "game-hollow-knight.jpg" })
        {
            var url = "https://shots.modlaunch.local/" + name;
            var file = System.IO.Path.Combine(Paths.CacheDir, "img", Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(url))) + ".bin");
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
                using var src = Avalonia.Platform.AssetLoader.Open(new Uri("avares://ModLaunch/Assets/" + name));
                using var dst = System.IO.File.Create(file);
                src.CopyTo(dst);
                urls.Add(url);
            }
            catch { }
        }
        var nautilus = Demo.Many(Games.GameCatalog.ById("subnautica")!, ["1262"])[0];
        var page = new ModPage("subnautica", nautilus);
        s.Window.Navigate(() => page);
        s.Pump(800);
        page.ShotMedia(urls, []);
        s.Pump(2500);
        s.Save("v20-mod-3-installed");
    }

    [DemoShots]
    static void V20Creator(Shots s)
    {
        Size(s);
        var id = XStudio.DemoProject();
        s.Window.Navigate(() => new XStudio(id, "files", "problems"));
        s.Pump(2000);
        s.Save("v20-creator-1-editor");
        s.Window.Navigate(() => new CreatorPage());
        s.Pump(1500);
        s.Save("v20-creator-2-projects");
    }

    [DemoShots]
    static void V20Settings(Shots s)
    {
        Size(s);
        s.Window.Navigate(() => new SettingsPage("look"));
        s.Pump(1800);
        s.Save("v20-settings-look");
    }

    /// <summary>Боковая панель на разных размерах окна: 1280×720, 1536×816 и 1920×1040.</summary>
    [DemoShots]
    static void V20Sizes(Shots s)
    {
        foreach (var (w, h) in new[] { (1280.0, 720.0), (1536.0, 816.0), (1920.0, 1040.0) })
        {
            Size(s, w, h);
            s.Window.Navigate(() => new HomePage());
            s.Pump(1800);
            s.Save($"v20-size-{w}x{h}");
        }
    }

    /// <summary>Нажатие по моду: установленный из каталога — его страница, из файла — окно «О моде».</summary>
    [DemoShots]
    static void V20Click(Shots s)
    {
        Size(s);
        var g = AppState.Game("subnautica");
        s.Window.Navigate(() => new GamePage("subnautica", "installed"));
        s.Pump(1200);
        if (g.Registry?.List().FirstOrDefault() is { } first)
        {
            ModView.Open(g, first);
            s.Pump(1800);
            s.Save("v20-click-1-installed");
        }
        ModView.Open(g, new System.Text.Json.Nodes.JsonObject { ["id"] = "local-shot", ["name"] = "Better Lights (local)", ["source"] = "file", ["version"] = "2.1", ["author"] = "Me" });
        s.Pump(900);
        s.Save("v20-click-2-local");
        s.Window.CloseDialog();
    }
}
