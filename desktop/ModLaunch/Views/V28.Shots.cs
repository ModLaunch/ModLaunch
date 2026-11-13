using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>Снимки 28.0 (MODLAUNCH_SHOTS=V28…) на демо-данных.</summary>
public static class V28Shots
{
    /// <summary>MODLAUNCH_SHOTS=V28Wish: «Желаемое» со скидками, поиск, окно цены-цели, колокольчик, светлая тема.</summary>
    [DemoShots]
    static void V28Wish(Shots s)
    {
        s.Pump(4500);
        s.Window.Navigate(() => new WishlistPage());
        s.Pump(2500);
        s.Save("v28-01-wishlist");

        if (s.Window.CurrentPage is WishlistPage page)
        {
            page.Search("hollow");
            s.Pump(2500);
            s.Save("v28-02-wishlist-search");
        }

        s.Window.Navigate(() => new WishlistPage());
        s.Pump(1200);
        s.Window.RefreshBell();
        s.Save("v28-03-wishlist-again");

        var was = Settings.Data.Str("theme") ?? "store";
        Look.SetTheme("light");
        try
        {
            s.Window.Navigate(() => new WishlistPage());
            s.Pump(1500);
            s.Save("v28-04-wishlist-light");
        }
        finally { Look.SetTheme(was); }
    }

    /// <summary>MODLAUNCH_SHOTS=V28Reviews: отзывы игроков на странице игры и замок для тех, кто ещё не наиграл час.</summary>
    [DemoShots]
    static void V28Reviews(Shots s)
    {
        s.Pump(4500);
        Games.AllGames.Refresh();
        s.Pump(1500);
        Social.Reviews.DemoGame("steam:1245620", "ELDEN RING");
        s.Window.Navigate(() => new AllGamesPage("steam:1245620"));
        s.Pump(2500);
        if (s.Window.CurrentPage is AllGamesPage page) page.ScrollDetailToEnd();
        s.Pump(800);
        s.Save("v28-05-game-reviews");

        s.Window.Navigate(() => new AllGamesPage("epic:Hades"));
        s.Pump(2000);
        if (s.Window.CurrentPage is AllGamesPage locked) locked.ScrollDetailToEnd();
        s.Pump(800);
        s.Save("v28-06-game-reviews-locked");
    }

    /// <summary>MODLAUNCH_SHOTS=V28Chat: непрочитанные в панели друзей, разговор с приглашением в игру, чат без правил на сервере.</summary>
    [DemoShots]
    static void V28Chat(Shots s)
    {
        s.Pump(4500);
        var now = DateTime.UtcNow;
        Social.Friends.DemoView = new Social.FriendsView(true, true, "ML-4K2Q-9", "online",
        [
            new("u1", "Lena", "playing", "lethal-company", "Lethal Company", now.AddMinutes(-40), now),
            new("u2", "Max_K", "online", "", "", null, now),
            new("u4", "Sasha", "offline", "", "", null, now.AddHours(-6)),
        ], [], [], null);
        Social.ChatMessage M(int minutesAgo, bool mine, string text, string kind = "text", string game = "", string gameName = "") =>
            new($"m{minutesAgo}", mine ? "me" : "u1", text, kind, game, gameName, now.AddMinutes(-minutesAgo), mine, false);
        Social.Friends.Chat.Demo = new()
        {
            ["u1"] =
            [
                M(1500, false, "Привет! Ты вчера поставил ту сборку на Lethal Company?"),
                M(1490, true, "Да, всё работает. Вечером зайдёшь?"),
                M(1488, false, "Конечно 🙂"),
                M(12, false, "", "invite", "steam:1966720", "Lethal Company"),
                M(11, false, "Мы уже на корабле, ждём тебя"),
            ],
        };
        try
        {
            s.Window.Navigate(() => new AllGamesPage());
            s.Pump(1500);
            s.Window.RefreshFriendsDock();
            s.Pump(600);
            s.Save("v28-07-friends-unread");
            s.Window.OpenChat(Social.Friends.DemoView.Friends[0]);
            s.Pump(1500);
            s.Save("v28-08-chat");
        }
        finally
        {
            Social.Friends.Chat.Demo = null;
            Social.Friends.DemoView = null;
        }
    }

    /// <summary>MODLAUNCH_SHOTS=V28BigPicture: Big Picture с играми из всех лаунчеров (выбрана игра не из ModLaunch).</summary>
    [DemoShots]
    static void V28BigPicture(Shots s)
    {
        s.Pump(4500);
        Games.AllGames.Refresh();
        s.Pump(2000);
        var big = new BigPictureWindow(windowed: true);
        big.Show();
        s.Pump(1500);
        big.DemoSelectLibrary();
        s.Pump(2500);
        Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(big)?.Save(Path.Combine(s.OutDir, "v28-11-bigpicture-library.png"));
        Console.WriteLine("saved v28-11-bigpicture-library");
        big.Close();
        s.Pump(500);
    }

    /// <summary>MODLAUNCH_SHOTS=V28Achievements: новые достижения 28.0 в профиле.</summary>
    [DemoShots]
    static void V28Achievements(Shots s)
    {
        s.Pump(4500);
        Games.AllGames.Refresh();
        s.Pump(1500);
        s.Window.Navigate(() => new ProfilePage("achievements", Features.Achievements.Main));
        s.Pump(2000);
        if (s.Window.CurrentPage is ProfilePage p) p.DemoScrollToEnd();
        s.Pump(800);
        s.Save("v28-12-achievements");
    }

    /// <summary>MODLAUNCH_SHOTS=V28Cloud: облачные сохранения на вкладке «Сохранения» и спор «какую копию оставить».</summary>
    [DemoShots]
    static void V28Cloud(Shots s)
    {
        s.Pump(4500);
        var root = Path.Combine(Path.GetTempPath(), "modlaunch-shots-cloud");
        var onedrive = Directory.CreateDirectory(Path.Combine(root, "OneDrive", "ModLaunch Backups")).FullName;
        Features.OneClickBackup.DemoTargets =
        [
            new("local", "local", "Документы", true, Path.Combine(root, "local")),
            new("onedrive", "service", Core.I18n.T("v11.bk.dest.onedrive"), true, onedrive),
            new("gdrive", "service", Core.I18n.T("v11.bk.dest.gdrive"), false, null),
        ];
        try
        {
            Features.CloudSaves.PlaceId = "onedrive";
            Features.CloudSaves.SetEnabled("subnautica", true);
            s.Window.Navigate(() => new GamePage("subnautica", "saves"));
            s.Pump(2000);
            s.Save("v28-09-cloud-saves");

            if (AppState.Games.FirstOrDefault(g => g.Def.Id == "subnautica") is { } game)
            {
                var now = DateTime.UtcNow;
                Actions.CloudConflict(game, new Features.SaveCheck("conflict",
                    new Features.SaveSide("a", now.AddMinutes(-35), 18_400_000, 42, "DESKTOP-HOME"),
                    new Features.SaveSide("b", now.AddHours(-20), 17_900_000, 41, "LAPTOP-MAX")), () => { });
                s.Pump(1000);
                s.Save("v28-10-cloud-conflict");
                s.Window.CloseDialog();
            }
        }
        finally
        {
            Features.CloudSaves.SetEnabled("subnautica", false);
            Features.OneClickBackup.DemoTargets = null;
        }
    }
}
