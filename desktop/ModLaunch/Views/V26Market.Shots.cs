using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Views;

/// <summary>
/// Снимки 26.0 (MODLAUNCH_SHOTS=V26Market): рынок без рекламной колонки и с фильтрами в два ряда, «Моды сообщества»
/// с обновлениями отслеживаемых, метками, поиском и «Показать ещё», страница лота в рамке хаба, мастер «Продать работу»
/// и пустое «Желаемое»; рынок, моды сообщества и лот — ещё и в светлой теме.
/// </summary>
public static class V26MarketShots
{
    /// <summary>Моды сообщества для снимков: демо-набор и ещё два десятка, чтобы были метки, игры и «Показать ещё».</summary>
    static List<HubMod> Community()
    {
        var list = Demo.Hub();
        var extra = new (string Name, string Summary, string Game, string Kind, string[] Tags)[]
        {
            ("Быстрый сундук", "Сундуки сортируют добычу сами", "valheim", "package", ["qol", "items"]),
            ("Компас викинга", "Компас и метки на краю экрана", "valheim", "script", ["ui"]),
            ("Тёплые ночи", "Мягкий свет костров и факелов", "valheim", "package", ["visuals"]),
            ("Рация на всех", "Голосовая рация без ограничений", "lethal-company", "package", ["multiplayer", "audio"]),
            ("Цены на луны", "Новые цены и награды за вылазки", "lethal-company", "script", ["gameplay", "tweaks"]),
            ("Больше костюмов", "40 костюмов для экипажа", "lethal-company", "package", ["cosmetics"]),
            ("Счётчик лута", "Сколько добычи на корабле — прямо в углу", "lethal-company", "script", ["ui", "qol"]),
            ("Глубокий сканер", "Сканер видит дальше и подсвечивает ресурсы", "subnautica", "script", ["qol", "gameplay"]),
            ("Чистая вода", "Прозрачная вода и новые цвета рифа", "subnautica", "package", ["visuals"]),
            ("Склад на базе", "Большие шкафчики и подписи", "subnautica", "package", ["items", "qol"]),
            ("Поющие киты", "Новые звуки морских гигантов", "subnautica", "package", ["audio"]),
            ("Ферма без хлопот", "Поливалки с первого дня", "stardew-valley", "script", ["qol", "tweaks"]),
            ("Рыбалка проще", "Шкала рыбалки шире и медленнее", "stardew-valley", "script", ["gameplay", "cheats"]),
            ("Уютный дом", "60 предметов мебели", "stardew-valley", "package", ["content", "items"]),
            ("Живая деревня", "Жители чаще гуляют и болтают", "stardew-valley", "package", ["content"]),
            ("Крепкие тросы", "Тросы не рвутся на морозе", "peak", "script", ["tweaks"]),
            ("Горный HUD", "Высота и выносливость понятными шкалами", "peak", "package", ["ui"]),
            ("Компания из восьми", "До восьми игроков в одном походе", "peak", "package", ["multiplayer"]),
            ("Тихие шаги", "Шаги тише, монстры слышат хуже", "repo", "script", ["gameplay", "audio"]),
            ("Цены на хлам", "Добыча стоит честных денег", "repo", "script", ["tweaks"]),
            ("Фикс прыжка", "Прыжок не застревает на краях", "repo", "package", ["fixes"]),
            ("Библиотека меню", "Общее меню настроек для модов", "repo", "package", ["library"]),
        };
        string[] authors = ["Kira", "Nox", "Vega", "Echo", "Alpin", "Mira", "Farmer", "Lumen"];
        for (var i = 0; i < extra.Length; i++)
        {
            var (name, summary, game, kind, tags) = extra[i];
            long dl = 400 + (i * 7919 % 9000);
            list.Add(new HubMod("demo_x" + i, "demo" + authors[i % authors.Length], authors[i % authors.Length], name, summary, summary + ".", game, "1." + (dl % 7) + ".0",
                kind, "", [.. tags], [], kind == "package" ? 1_200_000 : 0, "", kind == "package" ? 2 : 0, "mod.zip", "", dl / 9, dl, i % 5, DateTime.UtcNow.AddDays(-40 - i), DateTime.UtcNow.AddDays(-i - 1)));
        }
        return list;
    }

    [DemoShots]
    static void V26Market(Shots s)
    {
        var w = s.Window;
        var listings = Demo.Listings();
        var mods = Community();
        MarketData.Demo(listings, mods);
        foreach (var (id, _) in MarketLocal.Wishlist().ToList()) MarketLocal.ToggleWish(id);

        // Рынок: без рекламы справа, фильтры в два ряда, «Выбор недели» без дыры.
        w.Navigate(() => new MarketPage());
        s.Pump(1500);
        s.Save("v26-m1-store-top");
        V23Shots.Scroll(s, 0.55);
        s.Save("v26-m2-store-grid");

        // Моды сообщества: обновления отслеживаемых (одно — по «Следить» на странице мода, одно — из подписок Hub).
        var slots = mods.First(m => m.Id == "demo_slots");
        var old = new ModLaunch.Sources.ModInfo { Source = "hub", Id = slots.Id, Name = slots.Name, Version = "1.2.0" };
        var wasTracked = Features.Tracking.Has(slots.Game, old);
        if (!wasTracked) Features.Tracking.Toggle(slots.Game, old);
        Hub.Updates.Clear();
        Hub.Updates.Add(mods.First(m => m.Id == "demo_night"));
        w.Navigate(() => new MarketPage("workshop"));
        s.Pump(1400);
        var page = w.CurrentPage as MarketPage;
        s.Save("v26-m3-community");
        V23Shots.Scroll(s, 1);
        s.Save("v26-m4-community-more");

        // Умный поиск: русское слово находит и название, и метку, и описание.
        page?.Search("интерфейс");
        s.Pump(900);
        s.Save("v26-m5-community-search");
        page?.Search("Kira");
        s.Pump(900);
        s.Save("v26-m6-community-author");
        page?.Search("");
        s.Pump(300);

        // Лот: внутри рамки хаба, янтарные акценты, метки — ярлыками.
        var lot = listings.First(l => l.Id == "sort");
        w.Navigate(() => new ListingPage(lot));
        s.Pump(1400);
        s.Save("v26-m7-listing");
        V23Shots.Scroll(s, 0.6);
        s.Save("v26-m8-listing-lower");

        // Окно лота (с вкладки рынка) — ярлыки хаба вместо синей «пилюли» цены.
        w.Navigate(() => new MarketPage());
        s.Pump(900);
        MarketViews.Open(lot);
        s.Pump(700);
        s.Save("v26-m9-listing-dialog");
        w.CloseDialog();

        // Мастер «Продать работу»: шаги, значки и выбранная плитка — янтарём.
        for (var step = 0; step < 3; step++)
        {
            ListingWizard.ShotStep = step;
            ListingWizard.Show(step == 0 ? null : lot);
            s.Pump(900);
            s.Save($"v26-m10-wizard-{step + 1}");
            w.CloseDialog();
        }
        ListingWizard.ShotStep = -1;

        // «Желаемое»: строки с нарисованными обложками и ценой ярлыком; затем пустое — общим пустым состоянием хаба.
        foreach (var id in new[] { "seamoth", "daynight", "netcode" }) MarketLocal.ToggleWish(id);
        w.Navigate(() => new MarketPage("wishlist"));
        s.Pump(1000);
        s.Save("v26-m11-wishlist");
        foreach (var (id, _) in MarketLocal.Wishlist().ToList()) MarketLocal.ToggleWish(id);
        w.Navigate(() => new MarketPage("wishlist"));
        s.Pump(1000);
        s.Save("v26-m11-wishlist-empty");

        // Светлая тема: те же экраны.
        Look.SetStyle("redwhite");
        try
        {
            w.Navigate(() => new MarketPage());
            s.Pump(1300);
            s.Save("v26-m12-light-store");
            ListingWizard.ShotStep = 0;
            ListingWizard.Show(null);
            s.Pump(1600);
            s.Save("v26-m13-light-wizard");
            w.CloseDialog();
            ListingWizard.ShotStep = -1;
            w.Navigate(() => new MarketPage("workshop"));
            s.Pump(1300);
            s.Save("v26-m14-light-community");
            w.Navigate(() => new ListingPage(lot));
            s.Pump(1300);
            s.Save("v26-m15-light-listing");
        }
        finally { Look.SetStyle("dark"); }

        if (!wasTracked && Features.Tracking.Has(slots.Game, old)) Features.Tracking.Toggle(slots.Game, old);
        Hub.Updates.Clear();
        MarketData.Demo(Demo.Listings(), Demo.Hub());
    }
}
