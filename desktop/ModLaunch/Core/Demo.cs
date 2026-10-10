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
            "{\"games\":{\"subnautica\":{\"totalMs\":45300000,\"sessions\":14,\"lastPlayed\":\"" + DateTime.UtcNow.AddDays(-1).ToString("o") + "\"}," +
            "\"minecraft\":{\"totalMs\":312000000,\"sessions\":61,\"lastPlayed\":\"" + DateTime.UtcNow.AddHours(-3).ToString("o") + "\"}}}");
        Minecraft.McDemo.Prepare(root);

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

    /// <summary>Лоты рынка креаторов для скриншотов (без сети).</summary>
    public static List<Creator.Listing> Listings()
    {
        Creator.Listing L(string id, string author, string title, string summary, string kind, string game, long price, long sales, int days, string license, string[] tags, string preview = "") =>
            new(id, "demo" + author, author, title, summary,
                summary + ".\n\nЧто внутри:\n• файлы в понятных папках\n• инструкция по подключению на русском и английском\n• бесплатные обновления\n\nВопросы — в отзывах, отвечаю быстро.",
                kind, game, price, license, "1." + (sales % 5) + "." + (days % 4), preview, [.. tags], [], 180_000 + sales * 911, "", 1, "item.zip", "active", sales,
                DateTime.UtcNow.AddDays(-days - 40), DateTime.UtcNow.AddDays(-days));
        return
        [
            L("seamoth", "Mira", "Low-poly Seamoth", "Готовая модель подлодки: 3 LOD, текстуры 2K", "model", "subnautica", 34900, 128, 2, "commercial", ["visuals", "content"]),
            L("sort", "Kira", "Сортировка инвентаря", "Часть кода на C#: сортировка по типу, весу и имени", "code", "subnautica", 9900, 341, 5, "personal", ["qol", "library"],
                "public static void Sort(Inventory inv)\n{\n    inv.Items.Sort((a, b) => a.Kind != b.Kind\n        ? a.Kind.CompareTo(b.Kind)\n        : a.Name.CompareTo(b.Name));\n}"),
            L("daynight", "Nox", "Day/Night tweaks", "Скрипт ModScript: длинные ночи и яркие рассветы", "script", "valheim", 0, 1230, 1, "open", ["gameplay", "visuals"]),
            L("icons", "Vega", "Alien UI icons", "180 иконок интерфейса в едином стиле, SVG и PNG", "asset", "subnautica-below-zero", 14900, 77, 8, "commercial", ["ui", "cosmetics"]),
            L("survival", "Alpin", "Survival pack", "Сборка: 24 мода на выживание, проверены вместе", "pack", "valheim", 49900, 63, 3, "personal", ["modpack", "gameplay"]),
            L("crew", "Nox", "Crew Radio FX", "Звуки рации и шумы для экипажа", "asset", "lethal-company", 7900, 212, 4, "personal", ["audio", "multiplayer"]),
            L("monster", "Mira", "Monster Kit", "Три модели монстров с анимациями и звуками", "model", "lethal-company", 59900, 41, 6, "commercial", ["content", "visuals"]),
            L("lootbox", "Echo", "Генератор лута", "Часть кода: таблицы редкости и выпадения, как в RPG", "code", "repo", 12900, 96, 9, "commercial", ["gameplay", "library"],
                "var roll = rng.NextDouble();\nvar tier = roll < 0.01 ? Tier.Legendary\n         : roll < 0.08 ? Tier.Epic\n         : roll < 0.25 ? Tier.Rare : Tier.Common;"),
            L("vikinghud", "Kira", "Viking HUD", "Интерфейс в стиле рун: полоски, компас, карта", "asset", "valheim", 19900, 154, 11, "personal", ["ui", "visuals"]),
            L("starter", "Alpin", "Стартовый набор", "Скрипт: полезные вещи в начале игры", "script", "subnautica", 0, 2210, 2, "open", ["qol", "tweaks"]),
            L("blocks", "Vega", "Пак блоков «Неон»", "64 светящихся блока для построек", "pack", "minecraft", 24900, 187, 7, "personal", ["content", "visuals"]),
            L("netcode", "Echo", "Лобби на 8 игроков", "Часть кода: лобби, приглашения и голос", "code", "lethal-company", 29900, 58, 12, "commercial", ["multiplayer", "library"]),
            L("shaders", "Mira", "Подводные шейдеры", "Свет под водой и каустика, настройка яркости", "asset", "subnautica", 0, 940, 13, "open", ["visuals"]),
            L("truck", "Nox", "Грузовик экспедиции", "Модель с интерьером и физикой колёс", "model", "repo", 39900, 22, 1, "commercial", ["content"]),
        ];
    }

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
}
