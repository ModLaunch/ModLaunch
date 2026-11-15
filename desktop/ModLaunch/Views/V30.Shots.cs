using Avalonia.VisualTree;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>Снимки 30.0 (MODLAUNCH_SHOTS=V30…) на демо-данных.</summary>
public static class V30Shots
{
    /// <summary>MODLAUNCH_SHOTS=V30Nexus: окно ожидания браузера Nexus в задаче на несколько модов — «Пропустить» и «Отменить всё».</summary>
    [DemoShots]
    static void V30Nexus(Shots s)
    {
        s.Pump(4500);
        var job = new Job { Title = "Обновления: 3", GameName = "Skyrim" };
        Actions.ShowNexusWait("SkyUI", "https://www.nexusmods.com/", job, new TaskCompletionSource<object>(), skippable: true);
        s.Pump(800);
        s.Save("v30-01-nexus-skip");
        s.Window.CloseDialog();
    }

    /// <summary>MODLAUNCH_SHOTS=V30Shot: настройки снимка экрана в игре (рядом с оверлеем).</summary>
    [DemoShots]
    static void V30Shot(Shots s)
    {
        s.Pump(4500);
        s.Window.Navigate(() => new SettingsPage("launch"));
        s.Pump(1500);
        if (s.Window.CurrentPage is SettingsPage p) p.DemoScroll(1080);
        s.Pump(800);
        s.Save("v30-02-shot-settings");
    }

    /// <summary>MODLAUNCH_SHOTS=V30Ach: достижения Steam на странице игры во «Всех играх».</summary>
    [DemoShots]
    static void V30Ach(Shots s)
    {
        s.Pump(4500);
        Games.AllGames.Refresh();
        s.Pump(1500);
        s.Window.Navigate(() => new AllGamesPage("steam:1245620"));
        s.Pump(2500);
        s.Save("v30-03-steam-achievements");
    }

    /// <summary>MODLAUNCH_SHOTS=V30LibOverlay: оверлей поверх игры из «Всех игр» (Epic): время, снимок, друзья.</summary>
    [DemoShots]
    static void V30LibOverlay(Shots s)
    {
        s.Pump(4500);
        Games.AllGames.Refresh();
        s.Pump(1500);
        if (Games.AllGames.Current?.FirstOrDefault(g => g.Key == "epic:Hades") is not { } lib) return;
        OverlayWindow.OnLibraryGameStarted(lib);
        OverlayWindow.Toggle();
        s.Pump(1500);
        if (OverlayWindow.Current is { } o) Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(o)?.Save(Path.Combine(s.OutDir, "v30-04-library-overlay.png"));
        Console.WriteLine("saved v30-04-library-overlay");
        OverlayWindow.OnLibraryGameExited(lib);
        s.Pump(300);
    }

    /// <summary>MODLAUNCH_SHOTS=V30Mfa: «Аккаунт → Безопасность» — карточка двухфакторной защиты и окно подключения.</summary>
    [DemoShots]
    static void V30Mfa(Shots s)
    {
        s.Pump(4500);
        Social.Account.DemoSignIn();
        s.Window.Navigate(() => new AccountPage("security"));
        s.Pump(1500);
        s.Save("v30-05-mfa-card");
        MfaUi.Enable(() => { });
        s.Pump(800);
        s.Save("v30-06-mfa-enable");
        s.Window.CloseDialog();
    }

    /// <summary>MODLAUNCH_SHOTS=V30Refund: «Аккаунт → Покупки» с возвратами и окно заявки.</summary>
    [DemoShots]
    static void V30Refund(Shots s)
    {
        s.Pump(4500);
        Social.Account.DemoSignIn();
        s.Window.Navigate(() => new AccountPage("purchases"));
        s.Pump(800);
        if (s.Window.CurrentPage is not AccountPage page) return;
        page.DemoPurchases();
        s.Pump(1200);
        s.Save("v30-07-refunds");
        if (page.FirstPurchase is { } first) page.AskRefund(first);
        s.Pump(800);
        s.Save("v30-08-refund-ask");
        s.Window.CloseDialog();
    }

    /// <summary>MODLAUNCH_SHOTS=V30Curators: популярные кураторы, страница куратора и «Кураторы об игре».</summary>
    [DemoShots]
    static void V30Curators(Shots s)
    {
        s.Pump(4500);
        s.Window.Navigate(() => new CuratorsPage());
        s.Pump(1500);
        s.Save("v30-09-curators");
        s.Window.Navigate(() => new CuratorsPage("demo-c2"));
        s.Pump(1500);
        s.Save("v30-10-curator-page");
        Games.AllGames.Refresh();
        s.Pump(1500);
        s.Window.Navigate(() => new AllGamesPage("steam:1245620"));
        s.Pump(2500);
        if (s.Window.CurrentPage is AllGamesPage) DemoScrollToEnd(s);
        s.Pump(600);
        s.Save("v30-11-curators-on-game");
    }

    /// <summary>Пролистать открытую страницу вниз до конца.</summary>
    static void DemoScrollToEnd(Shots s)
    {
        foreach (var sv in s.Window.CurrentPage!.GetVisualDescendants().OfType<Avalonia.Controls.ScrollViewer>())
            sv.Offset = new Avalonia.Vector(sv.Offset.X, sv.Extent.Height);
    }

    /// <summary>MODLAUNCH_SHOTS=V30Shelves: «Все игры» с избранным, коллекцией и меню полок.</summary>
    [DemoShots]
    static void V30Shelves(Shots s)
    {
        s.Pump(4500);
        Games.AllGames.Refresh();
        s.Pump(1500);
        var shelves = Games.Shelves.Current;
        foreach (var key in new[] { "steam:1245620", "epic:Hades" }) if (!shelves.IsFavorite(key)) shelves.ToggleFavorite(key);
        var coop = shelves.Create("Кооператив");
        foreach (var key in new[] { "steam:892970", "epic:Hades" }) if (!shelves.Contains(coop, key)) shelves.Toggle(coop, key);
        s.Window.Navigate(() => new AllGamesPage("steam:1245620"));
        s.Pump(2000);
        s.Save("v30-12-shelves");
    }

    /// <summary>MODLAUNCH_SHOTS=V30Disk: «Место на дисках» для игр из всех лаунчеров.</summary>
    [DemoShots]
    static void V30Disk(Shots s)
    {
        s.Pump(4500);
        Games.AllGames.Refresh();
        s.Pump(1500);
        s.Window.Navigate(() => new StoragePage());
        s.Pump(2000);
        s.Save("v30-13-disk");
    }
}
