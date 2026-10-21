using Avalonia;
using Avalonia.Controls;
using ModLaunch.Core;
using ModLaunch.Games;

namespace ModLaunch.Views;

/// <summary>Снимки 12.0, справочник игр: «Добавить игру» (готовые к добавлению, поиск по справочнику) и ссылка в библиотеке.</summary>
public static class V12GamesIntegrationShots
{
    /// <summary>Игры «с диска» для снимков: три из справочника (если они в нём есть) и две без него.</summary>
    static List<FoundGame> DemoFound()
    {
        FoundGame? Dir(int appId, string name, string path, string exe, string engine) =>
            GameDirectory.BySteam(appId) is { } d && !GameDirectory.IsBuiltin(d) ? new FoundGame(name, path, exe, appId, "steam", engine, d) : null;
        return new List<FoundGame?>
        {
            Dir(548430, "Deep Rock Galactic", @"D:\SteamLibrary\steamapps\common\Deep Rock Galactic", "FSD.exe", "unreal"),
            Dir(1091500, "Cyberpunk 2077", @"D:\SteamLibrary\steamapps\common\Cyberpunk 2077", @"bin\x64\Cyberpunk2077.exe", "other"),
            Dir(1092790, "Inscryption", @"C:\Program Files (x86)\Steam\steamapps\common\Inscryption", "Inscryption.exe", "unity"),
            new FoundGame("Muck", @"D:\SteamLibrary\steamapps\common\Muck", "Muck.exe", 1625450, "steam", "unity"),
            new FoundGame("Terraria", @"C:\GOG Games\Terraria", "Terraria.exe", 0, "gog", "xna"),
        }.OfType<FoundGame>().ToList();
    }

    static ScrollViewer? Scroller(Page? page) => page?.Content as ScrollViewer;

    [DemoShots]
    static void GamesIntegrationAddPage(Shots s)
    {
        AddGamePage.Demo(DemoFound());
        s.Window.Navigate(() => new AddGamePage());
        s.Pump(1200);
        s.Save("v12g-1-add-game");

        // Весь справочник: прокрутка к списку, потом поиск в нём.
        var page = s.Window.CurrentPage;
        if (Scroller(page) is { } sv)
        {
            sv.Offset = new Vector(0, sv.Extent.Height);
            s.Pump(500);
            s.Save("v12g-2-directory");
        }
        page?.Search("sky");
        s.Pump(600);
        if (Scroller(page) is { } sv2) sv2.Offset = new Vector(0, sv2.Extent.Height);
        s.Pump(500);
        s.Save("v12g-3-directory-search");

        I18n.Set("en");
        s.Window.Navigate(() => new AddGamePage());
        s.Pump(900);
        s.Save("v12g-4-add-game-en");
        I18n.Set("ru");
    }

    [DemoShots]
    static void GamesIntegrationLibrary(Shots s)
    {
        s.Window.Navigate(() => new LibraryPage());
        s.Pump(1200);
        // Ссылка — под играми с компьютера: прокручиваем к ней.
        if (Scroller(s.Window.CurrentPage) is { } sv) sv.Offset = new Vector(0, 420);
        s.Pump(500);
        s.Save("v12g-5-library-link");
        s.Window.CurrentPage?.Search("Skyrim");
        s.Pump(700);
        s.Save("v12g-6-library-search-link");
    }
}
