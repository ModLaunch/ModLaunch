using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Views;

/// <summary>Снимки 9.1: заставка, три раскладки главной, три анимации «Скачать», Creator Hub и полоска у края.</summary>
public static class V91Shots
{
    [DemoShots]
    static void Design91Shots(Shots s)
    {
        var w = s.Window;

        // Заставка при запуске.
        var splash = new Splash(demo: true);
        splash.DemoAt(0.62, I18n.T("v91.splash.games"));
        w.Cover(splash);
        s.Pump(500);
        s.Save("v91-0-splash");
        w.CloseDialog();

        // Главная: три раскладки плиток.
        foreach (var layout in new[] { "a", "b", "c" })
        {
            HomePage.DemoLayout(layout);
            w.Navigate(() => new HomePage());
            s.Pump(1600);
            w.Navigate(() => new HomePage());
            s.Pump(900);
            s.Save("v91-1-home-" + layout);
        }
        HomePage.DemoLayout("a");
        w.Navigate(() => new HomePage());
        s.Pump(900);

        // Три анимации «Скачать»: начало, середина и карточка с прогрессом.
        var sub = AppState.Game("subnautica");
        var card = new FxCard("Nautilus", "SubnauticaModding · Subnautica", null, sub.Def);
        foreach (var (kind, name) in new[] { (FxKind.Frame, "frame"), (FxKind.Shatter, "shatter"), (FxKind.Wave, "wave") })
        {
            w.Navigate(() => new GamePage("subnautica", "catalog"));
            s.Pump(1200);
            var run = InstallFx.Demo(w, kind, card);
            if (run is null) { System.Console.WriteLine("::warning title=screenshots::fx " + name + " not prepared"); continue; }
            foreach (var ms in new[] { 220, 480 })
            {
                run.Seek(ms);
                s.Pump(120);
                s.Save($"v91-2-fx-{name}-{ms}");
            }
            run.Seek(1400);
            s.Pump(120);
            s.Save($"v91-2-fx-{name}-card");
            if (kind == FxKind.Wave)
            {
                run.SeekReturn(0.55);
                s.Pump(120);
                s.Save("v91-2-fx-return");
            }
            run.Close();
            s.Pump(200);
        }

        // Путь «Главная › игра › раздел» в шапке.
        w.Navigate(() => new GamePage("subnautica", "catalog"));
        s.Pump(900);
        s.Save("v91-3-crumbs");

        // Creator Hub: главная, «Коды», «Модели» и выдвижная полоска.
        CreatorPage.DemoHub(Core.Demo.Hub());
        Listing L(string id, string title, string kind, long price, long sales) => new(
            id, "demo", "Mira", title, "Готово к использованию: подключение в два шага.", "", kind, "subnautica", price, "personal", "1.2.0",
            "", [], [], 120_000, "", 1, "item.zip", "active", sales, DateTime.UtcNow.AddDays(-3), DateTime.UtcNow.AddHours(-sales));
        CreatorPage.DemoMarket(
        [
            L("a", "Low-poly Seamoth", "model", 34900, 12),
            L("b", "Inventory sort (C#)", "code", 9900, 41),
            L("c", "Day/Night tweaks", "script", 0, 230),
            L("d", "Alien UI icons", "asset", 14900, 7),
            L("e", "Survival pack", "pack", 49900, 3),
        ]);
        w.Navigate(() => new CreatorPage());
        s.Pump(1200);
        s.Save("v91-4-creator-home");
        w.Navigate(() => new CreatorPage("codes"));
        s.Pump(900);
        s.Save("v91-5-creator-codes");
        w.Navigate(() => new CreatorPage("models"));
        s.Pump(900);
        s.Save("v91-6-creator-models");
    }
}
