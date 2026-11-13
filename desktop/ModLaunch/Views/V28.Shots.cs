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
}
