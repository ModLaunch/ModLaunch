using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Views;

/// <summary>
/// Снимки 26.0 (MODLAUNCH_SHOTS=V26Features): функции для авторов — пустые «Мои моды» с «Путём автора» и примерами
/// для своих игр, «Статистика» (полная на демо-данных, в тёмной и светлой теме, и с историей в один день).
/// </summary>
public static class V26FeaturesShots
{
    static void Shot(Shots s, string name, Func<Page> page, int ms = 1300)
    {
        s.Window.Navigate(page);
        s.Pump(ms);
        s.Save(name);
    }

    [DemoShots]
    static void V26Features(Shots s)
    {
        // Уведомление об уровне при старте закрыло бы низ снимков — даём ему уйти.
        s.Pump(4500);

        // Пустые «Мои моды»: проектов в демо-папке ещё нет — общее пустое состояние, «Путь автора», примеры для игр.
        Shot(s, "v26f-01-mine-empty", () => new CreatorPage("home"));
        V23Shots.Scroll(s, 1);
        s.Save("v26f-02-mine-empty-bottom");

        // Статистика: месяц истории (с парой пропущенных дней), что нового с прошлого визита, комментарии и рынок.
        CreatorPage.DemoStatsFill(30);
        CreatorPage.DemoJourney = CreatorStats.Journey(true, true, true, false);
        Shot(s, "v26f-03-stats", () => new CreatorPage("stats"));
        V23Shots.Scroll(s, 0.62);
        s.Save("v26f-03b-stats-table");
        V23Shots.Scroll(s, 1);
        s.Save("v26f-04-stats-bottom");
        Shot(s, "v26f-05-stats-likes-14", () => CreatorPage.DemoStatsView("likes", 14));

        // История только началась: честная заглушка вместо выдуманной линии.
        CreatorPage.DemoStatsFill(1, fresh: false);
        Shot(s, "v26f-06-stats-new", () => CreatorPage.DemoStatsView("downloads", 30));

        // Колокольчик: раздел «Автору» — новые комментарии, продажи, скачивания и лайки с прошлого просмотра статистики.
        CreatorWatch.Demo([new("comments", 3, "demo_night", "Ночь с друзьями"), new("sales", 2), new("downloads", 412), new("likes", 37)]);
        s.Window.Navigate(() => new CreatorPage("home"));
        s.Pump(900);
        s.Window.DemoOpenBell();
        s.Pump(600);
        s.Save("v26f-09-bell");
        s.Window.DemoCloseBell();

        var was = Settings.Data.Str("theme") ?? "store";
        Look.SetTheme("light");
        try
        {
            CreatorPage.DemoStatsFill(30);
            Shot(s, "v26f-07-light-stats", () => CreatorPage.DemoStatsView("downloads", 30));
            CreatorPage.DemoJourney = null;
            Shot(s, "v26f-08-light-mine-empty", () => new CreatorPage("home"));
            s.Window.DemoOpenBell();
            s.Pump(600);
            s.Save("v26f-10-light-bell");
            s.Window.DemoCloseBell();
        }
        finally { Look.SetTheme(was); }
        CreatorPage.DemoStats = null;
        CreatorPage.DemoJourney = null;
        CreatorWatch.Demo([]);

        // Страница скрипта Hub: «Ремикс в ModScript»; после нажатия — проект с шапкой об оригинале в среде ModScript.
        // В конце: ремикс создаёт проект, и «Мои моды» перестают быть пустыми.
        var now = DateTime.UtcNow;
        var demo = new HubMod("kira_comfy_build", "u-kira", "Kira", "Удобная стройка", "Больше места для построек и быстрый ремонт",
            "## Что умеет\n- Больше места для построек\n- Быстрый ремонт\n\nСтавится в один клик через ModLaunch.", "valheim", "1.3.0", "script",
            Templates.ById("valheim-qol")!.Code, ["qol", "tweaks"], [], 0, "", 0, "", "Быстрее ремонт", 1210, 18400, 64, now.AddDays(-40), now.AddDays(-2));
        ModPage.DemoHubMod = demo;
        Shot(s, "v26f-11-modpage-remix", () => new ModPage("valheim", Hub.ToModInfo(demo)), 1800);
        var remix = Remix.Create(demo);
        Shot(s, "v26f-12-remix-studio", () => new XStudio(XProjects.Adopt(remix).Id, "files", "problems", "code"), 1800);
        ModPage.DemoHubMod = null;
    }
}

public sealed partial class CreatorPage
{
    /// <summary>Для снимков: «Статистика» с выбранной метрикой и сроком графика.</summary>
    internal static CreatorPage DemoStatsView(string metric, int span)
    {
        _statsMetric = metric;
        _statsSpan = span;
        return new CreatorPage("stats");
    }

    /// <summary>
    /// Для снимков: статистика автора без сети — четыре мода Hub с историей за history дней (несколько дней пропущено,
    /// как бывает, когда программу не открывают), свежие комментарии, продажи на рынке и что нового с прошлого визита.
    /// </summary>
    internal static void DemoStatsFill(int history, bool fresh = true)
    {
        var today = DateTime.Now.Date;
        var mods = Demo.Hub().Where(m => m.Id is "demo_night" or "demo_slots" or "demo_seeds" or "demo_hud").Select(m => m with { Uid = "demo-me" }).ToList();
        var days = new JsonObject();
        var skip = new[] { 5, 11, 12, 20 };
        for (var i = history - 1; i >= 0; i--)
        {
            if (i > 0 && skip.Contains(i)) continue;
            var day = today.AddDays(-i);
            CreatorStats.Record(days, day, mods.Select((m, k) =>
            {
                // Рост к сегодняшнему итогу: быстрее в последние дни, у каждого мода — свой разброс.
                var t = i / 29.0;
                var f = 1 - (0.42 + 0.06 * k) * Math.Pow(t, 0.85) + 0.012 * Math.Sin(i * 1.7 + k);
                f = Math.Min(1, f);
                return KeyValuePair.Create(m.Id, new ModCounts((long)(m.Downloads * f), (long)(m.Likes * Math.Pow(f, 1.2)), (long)(m.Comments * Math.Pow(f, 1.5))));
            }));
        }
        var now = DateTime.UtcNow;
        var night = mods.First(m => m.Id == "demo_night");
        var slots = mods.First(m => m.Id == "demo_slots");
        var seeds = mods.First(m => m.Id == "demo_seeds");
        List<(HubMod, HubComment)> comments =
        [
            (night, new HubComment("d1", "u-kira", "Kira", "Играли вчетвером весь вечер — ни одного вылета. Добавьте, пожалуйста, костюм пингвина!", now.AddHours(-2))),
            (night, new HubComment("d2", "u-echo", "Echo", "С новой версией игры работает, проверил на двух ПК.", now.AddHours(-9))),
            (slots, new HubComment("d3", "u-vega", "Vega", "Раскладка удобная, но на 21:9 слоты уезжают вправо.", now.AddDays(-1))),
            (seeds, new HubComment("d4", "u-alpin", "Alpin", "Лучший мод для первого года на ферме.", now.AddDays(-2))),
            (night, new HubComment("d5", "u-mira", "Mira", "Можно ли сделать так, чтобы лобби было на 12 человек?", now.AddDays(-3))),
        ];
        IReadOnlyList<CreatorNote> news = fresh
            ? [new("comments", 3, night.Id, night.Name), new("sales", 2), new("downloads", 412), new("likes", 37)]
            : [];
        var listings = Demo.Listings().Take(3).Select(l => l with { Uid = "demo-me" }).ToList();
        DemoStats = new StatsData("demo-me", mods, listings, 14, 1_284_500, comments, days, news, null, now.AddMinutes(-3));
    }
}
