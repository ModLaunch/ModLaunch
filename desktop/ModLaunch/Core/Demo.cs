using System.Text.Json.Nodes;
using ModLaunch.Games;
using ModLaunch.Sources;

namespace ModLaunch.Core;

/// <summary>
/// Показ без сети (--screenshot): поддельные папки игр и каталог с правдоподобными
/// модами. Нужен, чтобы проверять вид программы там, где нет Steam и сайтов модов.
/// </summary>
public static class Demo
{
    static readonly Dictionary<string, string> Names = new() { ["1262"] = "Nautilus", ["1112"] = "Configuration Manager for BepInEx", ["859"] = "Vehicle Framework", ["1457"] = "ECC Library 2.0", ["207"] = "Radial tabs", ["984"] = "Quick Slots Plus", ["142"] = "Slot Extender", ["12"] = "Map", ["24"] = "EasyCraft", ["2800"] = "Decorations Mod (Continued)", ["1119"] = "Base Kits", ["3143"] = "Composite Buildables (continued)", ["2447"] = "Modularily Based", ["3816"] = "Builder Module", ["1180"] = "SleekBases", ["1121"] = "Building Tweaks", ["1504"] = "AutoSortLockers", ["141"] = "Alien Rifle", ["216"] = "Defabricator", ["398"] = "More Modified Items", ["220"] = "Pickupable Storage Enhanced", ["1116"] = "All Items 1x1", ["1206"] = "Inventory Size", ["640"] = "De-Extinction 2.0", ["1604"] = "Bloop and Blaza Leviathans", ["1820"] = "The Red Plague", ["1542"] = "Epic Weather Mod", ["871"] = "Odyssey Vehicle", ["1748"] = "Beluga Submarine", ["1912"] = "The Hydra Submarine", ["2153"] = "Echelon", ["2461"] = "The Prototype Expansion", ["365"] = "Seamoth Arms", ["136"] = "Laser Cannon", ["1135"] = "More Seamoth Depth Modules", ["235"] = "Better Scanner Room", ["1453"] = "CustomBatteries (Purple Edition)", ["722"] = "Tweaks and Fixes", ["237"] = "Subnautica Autosave", ["389"] = "Performance Booster", ["3419"] = "Inventory Stacking", ["517"] = "Free Look", ["1300"] = "Blueprint Search Bar", ["229"] = "Storage Info", ["125"] = "Accelerated Start",  };

    static readonly string[] Descriptions =
    [
        "Добавляет новые постройки и декорации в меню строителя.",
        "Библиотека для других модов: без неё многие моды не запустятся.",
        "Больше слотов, быстрый доступ и удобный инвентарь.",
        "Новые модули улучшений для транспорта и манипуляторы.",
        "Исправляет мелкие ошибки игры и добавляет полезные настройки.",
    ];

    public static void Prepare(string root)
    {
        Environment.SetEnvironmentVariable("MODLAUNCH_DATA", Path.Combine(root, "data"));
        var games = Path.Combine(root, "Games");
        void Make(string id, string folder, string dataDir, bool loader)
        {
            var dir = Path.Combine(games, folder);
            Directory.CreateDirectory(Path.Combine(dir, dataDir));
            if (loader)
            {
                Directory.CreateDirectory(Path.Combine(dir, "BepInEx", "core"));
                Directory.CreateDirectory(Path.Combine(dir, "BepInEx", "plugins"));
                File.WriteAllText(Path.Combine(dir, "winhttp.dll"), "");
            }
            Settings.Data.Obj("gamePaths")[id] = dir;
        }
        Make("subnautica", "Subnautica", "Subnautica_Data", true);
        Make("subnautica-below-zero", "SubnauticaZero", "SubnauticaZero_Data", true);
        Make("lethal-company", "Lethal Company", "Lethal Company_Data", true);
        Make("valheim", "Valheim", "valheim_Data", false);
        Make("repo", "REPO", "REPO_Data", true);
        Settings.Save();

        // Игровое время — прямо в файл, до первого обращения к PlayTime.
        File.WriteAllText(Path.Combine(root, "data", "playtime.json"),
            "{\"games\":{\"subnautica\":{\"totalMs\":45300000,\"sessions\":14,\"lastPlayed\":\"" + DateTime.UtcNow.AddDays(-1).ToString("o") + "\"}}}");

        var sn = AppState.Game("subnautica");
        sn.Path = Settings.GamePath("subnautica");
        sn.Status = Detect.Found;
        var registry = sn.Registry!;
        foreach (var (id, name, days, on) in new[] { ("1262", "Nautilus", 24, true), ("12", "Map Mod", 23, true), ("142", "Slot Extender", 22, false) })
        {
            Directory.CreateDirectory(Path.Combine(on ? registry.ModsDir : Path.Combine(registry.StorageDir, "disabled"), name));
            registry.Add(new JsonObject
            {
                ["id"] = "nexus:subnautica:" + id, ["name"] = name, ["version"] = "2.1." + days, ["author"] = "Subnautica Modding", ["source"] = "nexus",
                ["folder"] = name, ["enabled"] = on, ["missing"] = false,
                ["installedAt"] = DateTime.UtcNow.AddDays(-days).ToString("o"),
                ["url"] = $"https://www.nexusmods.com/subnautica/mods/{id}",
            });
        }
        // Лог с ошибкой мода, сохранения и копии, профили.
        File.WriteAllText(Path.Combine(sn.Path!, "BepInEx", "LogOutput.log"),
            "[Info   :   BepInEx] Loading [Nautilus 1.0]\n[Error  : Map Mod] NullReferenceException: Object reference not set to an instance of an object\n  at MapMod.Plugin.Awake ()\n");
        var saves = Path.Combine(sn.Path!, "SNAppData", "SavedGames", "slot0000");
        Directory.CreateDirectory(saves);
        File.WriteAllText(Path.Combine(saves, "gameinfo.json"), "{}");
        Features.Backups.Create("subnautica", Path.Combine(sn.Path!, "SNAppData", "SavedGames"), "launch");
        Features.Backups.Create("subnautica", Path.Combine(sn.Path!, "SNAppData", "SavedGames"), "manual");
        Features.Profiles.Save("subnautica", "С друзьями", registry);
        Features.Notes.Set("subnautica", "nexus:subnautica:12", "Карта с метками баз — не выключать");
        Features.Tools.Add("subnautica", Path.Combine(sn.Path!, "Subnautica.exe"));
        Features.Profiles.Save("subnautica", "Хардкор", registry);

        foreach (var g in AppState.Games)
        {
            if (Settings.GamePath(g.Def.Id) is not string p) { g.Status = Detect.NotFound; continue; }
            g.Path = p;
            g.Status = Detect.Found;
            g.Refresh();
        }
    }

    static ModInfo Mod(GameDef game, string id, int i) => new()
    {
        Source = game.Catalog == CatalogKind.Nexus ? "nexus" : "thunderstore",
        Id = id,
        Name = Names.GetValueOrDefault(id) ?? (id.Contains('-') ? id[(id.IndexOf('-') + 1)..].Replace('_', ' ') : $"Mod {id}"),
        Author = id.Contains('-') ? id[..id.IndexOf('-')] : "Modder" + (i % 7),
        Version = $"1.{i % 9}.{i % 4}",
        Description = Descriptions[i % Descriptions.Length],
        Downloads = 900_000 / (i + 1),
        UpdatedAt = DateTime.UtcNow.AddDays(-(i * 37 % 1400)),
        Categories = [i % 3 == 0 ? "Gameplay" : i % 3 == 1 ? "Buildables" : "Vehicles and Upgrades"],
        Url = "https://example.invalid/",
    };

    public static Page Catalog(GameDef game, Query q)
    {
        var ids = game.Picks.Length > 0 ? game.Picks : Enumerable.Range(1, 30).Select(n => n.ToString()).ToArray();
        var mods = ids.Select((id, i) => Mod(game, id, i))
            .Where(m => q.Text == "" || m.Name.Contains(q.Text, StringComparison.OrdinalIgnoreCase))
            .Skip((q.Page - 1) * 12).Take(12).ToList();
        return new Page(mods, 1234, q.Page < 3, q.Page);
    }

    public static (List<CollectionInfo>, long, bool) Collections(int page) =>
        (Enumerable.Range(0, 6).Select(i => new CollectionInfo($"demo{i}", new[] { "Строитель баз", "Полный ремастер", "Выживание+", "Удобства", "Подлодки", "Хардкор" }[i],
            "Подборка модов от игроков.", null, "Player" + i, 900 - i * 120, 40000 - i * 5000, 12 + i * 5, "https://www.nexusmods.com/")).ToList(), 6, false);

    public static (CollectionInfo, List<CollectionMod>) Collection(GameDef game) =>
        (new CollectionInfo("demo0", "Строитель баз", "Всё для красивых баз: декорации, новые постройки и удобства.", null, "Player0", 900, 40000, 7, "https://www.nexusmods.com/"),
         game.Picks.Take(7).Select((id, i) => new CollectionMod(id, 1000 + i, Names.GetValueOrDefault(id) ?? id, "1.0", null, null, i == 6)).ToList());

    public static ModDetails Details(GameDef game, ModInfo mod) => new(mod,
        [
            new Block("h", "Возможности"),
            new Block("p", mod.Description + " Работает с последней версией игры и не ломает старые сохранения."),
            new Block("li", "Новые постройки в меню строителя"),
            new Block("li", "Настройки в Configuration Manager"),
            new Block("h", "Установка"),
            new Block("p", "Нажмите «Установить» — ModLaunch сам поставит загрузчик и зависимости."),
        ], [], game.Catalog == CatalogKind.Nexus ? [new Requirement("1262", "Nautilus", true)] : []);

    public static List<ModInfo> Many(GameDef game, IEnumerable<string> ids) => ids.Distinct().Select((id, i) => Mod(game, id, i)).ToList();

    /// <summary>Моды ModLaunch Hub для скриншотов (без сети).</summary>
    public static List<Creator.HubMod> Hub()
    {
        Creator.HubMod M(string id, string author, string name, string summary, string game, string kind, long dl, long likes, long comments, int daysAgo, params string[] tags) =>
            new(id, "demo" + author, author, name, summary, "## Что умеет\n- " + summary + "\n- Работает с последней версией игры\n\nСтавится в один клик через ModLaunch.", game, "1." + (dl % 7) + ".0",
                kind, "", [.. tags], [], kind == "package" ? 2_400_000 : 0, "", kind == "package" ? 3 : 0, "mod.zip", "Исправления и новые настройки",
                likes, dl, comments, DateTime.UtcNow.AddDays(-daysAgo - 30), DateTime.UtcNow.AddDays(-daysAgo));
        return
        [
            M("demo_slots", "Kira", "Больше слотов", "Ещё 8 быстрых слотов и удобная раскладка", "valheim", "package", 18400, 2210, 64, 1, "qol", "ui"),
            M("demo_seeds", "Farmer", "Дешёвые семена", "Весенние семена вдвое дешевле", "stardew-valley", "script", 9310, 1402, 31, 3, "gameplay", "tweaks"),
            M("demo_night", "Nox", "Ночь с друзьями", "Сборка: 8 игроков, костюмы и заход в игру", "lethal-company", "script", 25120, 3120, 102, 0, "modpack", "multiplayer"),
            M("demo_hud", "Vega", "Чистый интерфейс", "Минималистичный HUD без лишних надписей", "subnautica", "package", 7120, 980, 18, 6, "ui", "visuals"),
            M("demo_peak", "Alpin", "Большая экспедиция", "PEAK на большую компанию", "peak", "script", 4210, 611, 12, 2, "modpack", "multiplayer"),
            M("demo_audio", "Echo", "Живой звук", "Новые звуки шагов и воды", "repo", "package", 3050, 402, 9, 9, "audio"),
        ];
    }

    /// <summary>Рынок Creator Hub для скриншотов: кошелёк, ассеты, заказы со ставками, инвентарь и обмены.</summary>
    public static void Market()
    {
        var now = DateTime.UtcNow;
        Creator.Asset A(string id, string author, string name, string summary, string kind, long price, long supply, long sold, long owners, int days, string engine, params string[] games) =>
            new(id, "demo" + author, author, name, summary, "Аккуратная сетка, лёгкие текстуры, LOD для дальних планов.", kind, [.. games], engine, [], [],
                kind == "code" ? "helpers.mls" : "asset.bundle", kind == "code" ? 2400 : 1_840_000, "", 1,
                kind == "code" ? "fn price base mult {\n  return $base * $mult\n}\n\nfn shop item cost {\n  edit \"Data/Shops\" $item Price = $cost\n}\n" : "",
                price, supply, sold, owners, true, "1.2.0", now.AddDays(-days - 20), now.AddDays(-days));
        Creator.Market.DemoAssets =
        [
            A("kira_sword", "Kira", "Рунический меч", "Модель меча с рунами и свечением для игр на Unity", "model", 40, 25, 22, 22, 1, "unity"),
            A("vega_ui", "Vega", "Тёмные иконки интерфейса", "120 иконок в одном стиле: еда, броня, ресурсы", "texture", 0, 0, 312, 312, 3, "any"),
            A("echo_rain", "Echo", "Звук дождя и грозы", "Петли дождя, раскаты грома, капли по крыше", "sound", 15, 0, 87, 87, 5, "any"),
            A("farmer_shop", "Farmer", "Функции для магазинов", "fn shop, fn price — меняйте цены одной строкой", "code", 0, 0, 640, 640, 2, "stardew", "stardew-valley"),
            A("nox_crown", "Nox", "Корона основателя", "Тираж 10 копий — для первых игроков сервера", "model", 120, 10, 10, 10, 9, "unity", "valheim"),
            A("alpin_rope", "Alpin", "Верёвка и карабины", "Модели снаряжения для восхождений", "model", 25, 0, 41, 41, 4, "unity", "peak"),
            A("kira_ai", "Kira", "Умные враги: поведение", "Куски кода: патруль, погоня, отступление", "code", 30, 0, 58, 58, 6, "unity"),
            A("vega_sky", "Vega", "Небо в 4K", "Скайбоксы рассвет / день / закат / ночь", "texture", 20, 50, 49, 49, 0, "unity"),
        ];
        Creator.Market.DemoWallet = new Creator.Wallet(1240, now.AddHours(-23), "");
        Creator.Market.DemoOwned =
        [
            new("demoMe", "nox_crown", 7, "resale", 150, "demoX", now.AddDays(-2)),
            new("demoMe", "vega_ui", 0, "free", 0, "", now.AddDays(-5)),
        ];
        Creator.Order O(string id, string author, string title, string text, string game, long budget, double hoursLeft, int days, string status, long bids, string winner = "", long price = 0) =>
            new(id, "demo" + author, author, title, text, game, budget, now.AddHours(hoursLeft), days, status, bids, winner == "" ? "" : "demo" + winner, winner, price,
                now.AddDays(days), "", "", null, 0, now.AddHours(-30), now.AddMinutes(-hoursLeft));
        Creator.Market.DemoOrders =
        [
            O("o1", "Max", "Гроза с молниями для Valheim", "Хочу настоящую грозу: молнии бьют в высокие объекты, гром с задержкой по расстоянию, мокрые поверхности.", "valheim", 300, 2.4, 7, "open", 5),
            O("o2", "Lina", "Перевод диалогов Stardew на казахский", "Все диалоги жителей и письма. Есть словарь терминов.", "stardew-valley", 500, 26, 14, "open", 3),
            O("o3", "Dan", "Сборка «хоррор» для 8 игроков", "Lethal Company: страшные монстры, темнее, 8 игроков, без читов.", "lethal-company", 120, 0.6, 3, "open", 8),
            O("o4", "Oleg", "Скин костюма космонавта", "Белый костюм с нашивками для REPO.", "repo", 80, 50, 5, "open", 1),
            O("o5", "Max", "Быстрая переноска брёвен", "", "valheim", 60, -20, 4, "assigned", 4, "Kira", 45),
        ];
        Creator.Market.DemoBids = new()
        {
            ["o1"] =
            [
                new("demoKira", "Kira", 220, 5, "Делала погоду для двух модов, покажу видео в процессе.", now.AddMinutes(-3)),
                new("demoEcho", "Echo", 240, 4, "Возьму звук на себя, гром — пространственный.", now.AddMinutes(-12)),
                new("demoNox", "Nox", 260, 6, "", now.AddMinutes(-40)),
                new("demoVega", "Vega", 280, 3, "Шейдер мокрых поверхностей уже есть.", now.AddHours(-1)),
                new("demoAlpin", "Alpin", 300, 7, "", now.AddHours(-2)),
            ],
        };
        Creator.Market.DemoOrders.Add(O("o9", "Ira", "Новые лица жителей", "", "stardew-valley", 200, -100, 6, "done", 6, "Kira", 180) with { Rating = 5 });
        Creator.Market.DemoOrders.Add(O("o8", "Pavel", "Мост через реку", "", "valheim", 90, -200, 3, "done", 2, "Kira", 70) with { Rating = 4 });
        Creator.Market.DemoTrades =
        [
            new("t1", "demoLina", "Lina", "demoMe", "Вы", "", 0, "nox_crown", 7, 200, "Очень нужна для сервера!", "open", now.AddHours(-1)),
        ];
        Creator.Market.DemoOps =
        [
            new("op1", "bonus", "bonus", "demoMe", 25, "", "", now.AddHours(-23)),
            new("op2", "release", "order:o9", "demoMe", 180, "", "o9", now.AddDays(-1)),
            new("op3", "resale", "demoMe", "demoX", 150, "nox_crown", "", now.AddDays(-2)),
        ];
        Creator.Inventory.DemoItems =
        [
            new("i1", "Корона основателя", "model", "hub", "nox_crown", "Nox", ["valheim"], "unity", null, now.AddDays(-2), 7, ""),
            new("i2", "Тёмные иконки интерфейса", "texture", "hub", "vega_ui", "Vega", [], "any", null, now.AddDays(-5), 0, ""),
            new("i3", "Мои функции цен", "code", "local", null, "", [], "any", null, now.AddDays(-1), 0, ""),
            new("i4", "Фонарь (из мода)", "model", "mod", null, "", ["lethal-company"], "unity", null, now.AddDays(-3), 0, "Из мода «Better Lights»"),
            new("i5", "Шаги по снегу", "sound", "local", null, "", [], "any", null, now.AddDays(-6), 0, ""),
        ];
    }
}
