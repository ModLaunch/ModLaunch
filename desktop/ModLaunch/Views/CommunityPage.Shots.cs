using ModLaunch.Core;
using ModLaunch.Minecraft;
using ModLaunch.Social;

namespace ModLaunch.Views;

public sealed partial class CommunityPage
{
    /// <summary>Снимки 11.0: «Сообщество» — друзья, доска серверов, новый сервер, вход на сервер друга, свой сервер и без входа.</summary>
    [DemoShots]
    static void CommunityShots(Shots s)
    {
        var w = s.Window;
        var now = DateTime.UtcNow;
        Friends.DemoView = new FriendsView(true, true, "ML-4K2Q-9", "online",
        [
            new("u1", "Lena", "playing", "lethal-company", "Lethal Company", now.AddMinutes(-40), now),
            new("u2", "Max_K", "online", "", "", null, now),
            new("u3", "Dima", "playing", "minecraft", "Minecraft", now.AddMinutes(-5), now),
            new("u4", "Sasha", "offline", "", "", null, now.AddHours(-6)),
            new("u5", "Kirill", "offline", "", "", null, now.AddDays(-2)),
        ],
        [new("u9", "NightOwl", now.AddMinutes(-12))], [new("u7", "Artem", now.AddHours(-3))], null);
        Servers.DemoList =
        [
            new("demo1", Servers.DemoUid, "Я", "Выживание с друзьями", Mc.Id, "192.168.1.5:25565", "", 14, "friends", 2, 10, "1.21.1 · Fabric", now.AddDays(-3), now.AddMinutes(-2)),
            new("demo2", "u3", "Dima", "Дима SMP", Mc.Id, "mc.dima-smp.ru", "", 0, "friends", 3, 12, "1.21.1", now.AddDays(-20), now.AddMinutes(-1)),
            new("demo3", "u1", "Lena", "Ночные смены", "lethal-company", "482915", "018f2c3a-6b1e-4f1b-9c55-2d4e8a1f3b77", 23, "friends", 3, 4, "v69", now.AddHours(-2), now.AddMinutes(-4)),
            new("demo4", "u5", "Kirill", "Викинги по выходным", "valheim", "77.88.21.4:2456", "", 6, "friends", 0, 10, "", now.AddDays(-12), now.AddDays(-2)),
            new("demo5", "x1", "Tihon", "Тихая деревня", Mc.Id, "play.quietvillage.net", "", 0, "public", 17, 50, "1.20.4", now.AddDays(-90), now.AddMinutes(-3)),
            new("demo6", "x2", "Gleb", "Хардкор без правил", Mc.Id, "hc.example.org:25570", "", 0, "public", 0, 20, "1.21.1", now.AddDays(-30), now.AddHours(-5)),
            new("demo7", "x3", "Stranger", "Только для своих", Mc.Id, "10.0.0.7", "", 0, "friends", 1, 5, "1.21.1", now.AddDays(-1), now),
        ];

        w.Navigate(() => new CommunityPage());
        s.Pump(1200);
        s.Save("community-1-friends");

        w.Navigate(() => new CommunityPage("servers"));
        s.Pump(1000);
        s.Save("community-2-servers");

        ServerDialog(null);
        s.Pump(800);
        s.Save("community-3-create");
        w.CloseDialog();

        Join(Servers.DemoList[1]);
        s.Pump(800);
        s.Save("community-3b-join");
        w.CloseDialog();

        // Свой сервер: один запущен (с консолью и двумя игроками), второй ждёт согласия с EULA.
        var hosted = McHost.Create("Выживание с друзьями", "1.21.1", "fabric", McHost.DefaultPort, 10, "Свои люди, без гриферов", "normal", true, 4096);
        McHost.Create("Творческий для стройки", "1.20.4", "vanilla", McHost.DefaultPort + 1, 6, "", "peaceful", false, 2048);
        McHost.AcceptEula(hosted);
        var run = McHost.Demo(hosted,
        [
            "[18:02:11] [ServerMain/INFO]: Loading Minecraft 1.21.1 with Fabric Loader 0.17.2",
            "[18:02:14] [Server thread/INFO]: Starting minecraft server version 1.21.1",
            "[18:02:14] [Server thread/INFO]: Loading properties",
            "[18:02:14] [Server thread/INFO]: Default game type: SURVIVAL",
            "[18:02:15] [Server thread/INFO]: Starting Minecraft server on *:25565",
            "[18:02:19] [Server thread/INFO]: Preparing level \"world\"",
            "[18:02:27] [Server thread/INFO]: Done (11.904s)! For help, type \"help\"",
            "[18:05:40] [Server thread/INFO]: Dima joined the game",
            "[18:07:02] [Server thread/INFO]: Lena joined the game",
            "[18:07:15] [Server thread/INFO]: <Lena> привет! где база?",
            "> say База у озера, идите по дороге на восток",
            "[18:07:31] [Server thread/INFO]: [Server] База у озера, идите по дороге на восток",
        ], 2);
        McHost.SetBoard(hosted, "demo1");
        SelectHost(hosted.Id);
        CommunityPage? page = null;
        w.Navigate(() => page = new CommunityPage("host"));
        s.Pump(1200);
        s.Save("community-4-host");

        page?.HostDialog(null);
        s.Pump(900);
        s.Save("community-5-host-new");
        w.CloseDialog();

        I18n.Set("en");
        w.Navigate(() => new CommunityPage("servers"));
        s.Pump(1000);
        s.Save("community-6-servers-en");
        I18n.Set("ru");

        // Без входа: спокойное приглашение вместо списка друзей.
        Friends.DemoView = Friends.DemoView with { SignedIn = false, Code = null, Friends = [], Incoming = [], Outgoing = [] };
        w.Navigate(() => new CommunityPage());
        s.Pump(900);
        s.Save("community-7-signedout");

        McHost.Forget(run);
        foreach (var h in McHost.List()) McHost.Delete(h);
        Friends.DemoView = null;
        Servers.DemoList = null;
        _hostId = null;
        w.Navigate(() => new HomePage());
    }
}
