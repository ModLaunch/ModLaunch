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

        var sn = AppState.Game("subnautica");
        sn.Path = Settings.GamePath("subnautica");
        sn.Status = Detect.Found;
        var registry = sn.Registry!;
        foreach (var (id, name, days, on) in new[] { ("1262", "Nautilus", 24, true), ("12", "Map Mod", 23, true), ("142", "Slot Extender", 22, false) })
        {
            Directory.CreateDirectory(Path.Combine(on ? registry.ModsDir : Path.Combine(registry.StorageDir, "disabled"), name));
            registry.Add(new JsonObject
            {
                ["id"] = id, ["name"] = name, ["version"] = "2.1." + days, ["author"] = "Subnautica Modding", ["source"] = "nexus",
                ["folder"] = name, ["enabled"] = on, ["missing"] = false,
                ["installedAt"] = DateTime.UtcNow.AddDays(-days).ToString("o"),
                ["url"] = $"https://www.nexusmods.com/subnautica/mods/{id}",
            });
        }
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

    public static List<ModInfo> Many(GameDef game, IEnumerable<string> ids) => ids.Distinct().Select((id, i) => Mod(game, id, i)).ToList();
}
