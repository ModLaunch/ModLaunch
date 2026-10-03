using System.IO.Compression;
using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Sources;

namespace ModLaunch.Minecraft;

/// <summary>Minecraft без сети — для снимков экрана (--screenshot): папка .minecraft, три сборки и каталог.</summary>
public static class McDemo
{
    static readonly (string Id, string Title, string Author, string Desc, long Downloads, string File, string Version)[] Mods =
    [
        ("AANobbMI", "Sodium", "jellysquid3", "Современный движок отрисовки: заметно больше кадров в секунду.", 98_000_000, "sodium-fabric-0.6.13+mc1.21.1.jar", "0.6.13"),
        ("YL57xq9U", "Iris Shaders", "coderbot", "Шейдеры вместе с Sodium — красивый свет и тени.", 61_000_000, "iris-fabric-1.8.8+mc1.21.1.jar", "1.8.8"),
        ("P7dR8mSH", "Fabric API", "modmuss50", "Библиотека для модов на Fabric — нужна почти всем.", 120_000_000, "fabric-api-0.116.4+1.21.1.jar", "0.116.4"),
        ("gvQqBUqZ", "Lithium", "jellysquid3", "Оптимизация логики мира: мобы, блоки, физика.", 52_000_000, "lithium-fabric-0.15.0+mc1.21.1.jar", "0.15.0"),
        ("mOgUt4GM", "Mod Menu", "Prospector", "Список модов и их настройки прямо в игре.", 70_000_000, "modmenu-11.0.3.jar", "11.0.3"),
        ("1bokaNcj", "Xaero's Minimap", "xaero96", "Мини-карта с метками и путевыми точками.", 40_000_000, "Xaeros_Minimap_25.2.6_Fabric_1.21.jar", "25.2.6"),
        ("fQEb0iXm", "Krypton", "astei", "Быстрее сеть: меньше задержек на сервере.", 12_000_000, "krypton-0.2.8.jar", "0.2.8"),
        ("uXXizFIs", "FerriteCore", "malte0811", "Меньше памяти — больше модов без тормозов.", 45_000_000, "ferritecore-7.0.2-fabric.jar", "7.0.2"),
        ("NNAgCjsB", "Entity Culling", "tr7zw", "Не рисует то, что скрыто за стенами.", 30_000_000, "entityculling-fabric-1.7.4-mc1.21.1.jar", "1.7.4"),
        ("LQ3K71Q1", "Dynamic FPS", "juliand665", "Экономит видеокарту, когда игра свёрнута.", 22_000_000, "dynamic-fps-3.9.4+minecraft-1.21.0-fabric.jar", "3.9.4"),
    ];

    public static void Prepare(string root)
    {
        var mc = Directory.CreateDirectory(Path.Combine(root, "Games", ".minecraft")).FullName;
        Directory.CreateDirectory(Path.Combine(mc, "versions", "1.21.1"));
        File.WriteAllText(Path.Combine(mc, "launcher_profiles.json"), "{\"profiles\":{\"vanilla\":{\"name\":\"Latest release\",\"type\":\"latest-release\"}},\"settings\":{}}");
        Settings.Data.Obj("gamePaths")[Mc.Id] = mc;
        var state = AppState.Game(Mc.Id);
        state.Path = mc;
        state.Status = Detect.Found;

        var main = Mc.Create("Выживание с друзьями", "1.21.1", "fabric", "0.17.2", 6144);
        foreach (var m in Mods) Jar(main, m.File, m.Title, m.Version, m.Desc, m.Author, m.Id);
        // Один мод выключен — как в жизни.
        var off = Path.Combine(main.Folder("mod"), Mods[9].File);
        File.Move(off, off + ".disabled");
        Pack(main, "resourcepack", "Faithful 32x.zip", "Faithful 32x", "1.21.1-1", "z8nwb0ag");
        Pack(main, "shader", "ComplementaryReimagined_r5.5.zip", "Complementary Shaders - Reimagined", "r5.5", "HVnmMxH1");
        foreach (var (world, hours) in new[] { ("Остров у моря", 3), ("Хардкор", 30), ("Творческий", 200) })
        {
            var w = Directory.CreateDirectory(Path.Combine(main.Dir, "saves", world)).FullName;
            File.WriteAllBytes(Path.Combine(w, "level.dat"), []);
            File.SetLastWriteTimeUtc(Path.Combine(w, "level.dat"), DateTime.UtcNow.AddHours(-hours));
        }
        Directory.CreateDirectory(Path.Combine(main.Dir, "saves", "Хардкор", "datapacks", "terralith"));
        Directory.CreateDirectory(Path.Combine(main.Dir, "screenshots"));
        Directory.CreateDirectory(Path.Combine(main.Dir, "crash-reports"));
        File.WriteAllText(Path.Combine(main.Dir, "crash-reports", "crash-2026-09-29_18.22.10-client.txt"),
            "---- Minecraft Crash Report ----\n// Uh... Did I do that?\n\nTime: 2026-09-29 18:22:10\nDescription: Rendering overlay\n\njava.lang.NullPointerException: Cannot invoke \"net.minecraft.class_310.method_1551()\"\n");
        Directory.CreateDirectory(Path.Combine(main.Dir, "logs"));
        File.WriteAllText(Path.Combine(main.Dir, "logs", "latest.log"),
            "[12:00:01] [main/INFO]: Loading Minecraft 1.21.1 with Fabric Loader 0.17.2\n[12:00:04] [Render thread/INFO]: Sodium has been loaded\n" +
            "[12:00:09] [Render thread/ERROR] (Xaero's Minimap): Failed to load waypoints, using defaults\n[12:00:12] [Worker-Main-3/WARN]: Missing sound for event: minecraft:item.goat_horn.play\n");
        main.LastPlayed = DateTime.UtcNow.AddHours(-3);
        Mc.Save(main);

        var tech = Mc.Create("Техно и магия", "26.2", "neoforge", "26.2.0.7", 8192);
        Jar(tech, "create-26.2-6.1.0.jar", "Create", "6.1.0", "Механизмы, шестерёнки и конвейеры.", "simibubi", "LNytGWDc", toml: true);
        Jar(tech, "jei-26.2-neoforge-24.1.0.jar", "Just Enough Items", "24.1.0", "Рецепты всех предметов.", "mezz", "u6dRKJwZ", toml: true);
        tech.LastPlayed = DateTime.UtcNow.AddDays(-4);
        Mc.Save(tech);

        var vanilla = Mc.Create("Чистая игра", "26.2", "vanilla");
        vanilla.LastPlayed = DateTime.UtcNow.AddDays(-9);
        Mc.Save(vanilla);
        Mc.Active = main;
        Settings.Save();
    }

    /// <summary>Картинки миров и скриншоты — когда интерфейс уже запущен (картинки берём из ресурсов программы).</summary>
    public static void Pictures()
    {
        if (Mc.Active is not { } main) return;
        foreach (var world in Directory.EnumerateDirectories(Path.Combine(main.Dir, "saves")))
            Asset("art/minecraft-cover.jpg", Path.Combine(world, "icon.png"));
        var shots = Path.Combine(main.Dir, "screenshots");
        Asset("art/minecraft-hero.jpg", Path.Combine(shots, "2026-09-28_19.04.12.png"));
        Asset("art/minecraft-header.jpg", Path.Combine(shots, "2026-09-30_21.17.40.png"));
    }

    /// <summary>Встроенная картинка — файлом (значок мира, скриншот).</summary>
    static void Asset(string name, string target)
    {
        try
        {
            using var src = Avalonia.Platform.AssetLoader.Open(new Uri($"avares://ModLaunch/Assets/{name}"));
            using var dst = File.Create(target);
            src.CopyTo(dst);
        }
        catch { }
    }

    static void Jar(McInstance i, string file, string title, string version, string desc, string author, string project, bool toml = false)
    {
        var path = Path.Combine(Directory.CreateDirectory(i.Folder("mod")).FullName, file);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var e = zip.CreateEntry(toml ? "META-INF/neoforge.mods.toml" : "fabric.mod.json");
            using var w = new StreamWriter(e.Open());
            w.Write(toml
                ? $"[[mods]]\nmodId=\"{title.ToLowerInvariant().Replace(' ', '_')}\"\nversion=\"{version}\"\ndisplayName=\"{title}\"\ndescription='''{desc}'''\n"
                : new JsonObject { ["id"] = title.ToLowerInvariant().Replace(' ', '_'), ["name"] = title, ["version"] = version, ["description"] = desc }.ToJsonString());
        }
        McContent.Remember(i, "mod", file, new MrProject(project, title.ToLowerInvariant(), title, desc, author, null, 0, 0, "mod", [], [i.Loader], [i.GameVersion], null, null),
            new MrVersion("v-" + project, project, version, version, "release", [i.Loader], [i.GameVersion], [], [], DateTime.UtcNow.AddDays(-20), 0, ""), null);
    }

    static void Pack(McInstance i, string kind, string file, string title, string version, string project)
    {
        var path = Path.Combine(Directory.CreateDirectory(i.Folder(kind)).FullName, file);
        using (ZipFile.Open(path, ZipArchiveMode.Create)) { }
        McContent.Remember(i, kind, file, new MrProject(project, title, title, "", "", null, 0, 0, kind, [], [], [i.GameVersion], null, null),
            new MrVersion("v-" + project, project, version, version, "release", [], [i.GameVersion], [], [], DateTime.UtcNow, 0, ""), null);
    }

    public static (List<MrProject>, long) Hits(string type)
    {
        var list = type switch
        {
            "modpack" => new List<MrProject>
            {
                P("1KVo5zza", "Fabulously Optimized", "robotkoer", "Быстрая и красивая игра без лишнего: оптимизация, шейдеры, удобства.", 14_200_000, "modpack", ["optimization", "lightweight"]),
                P("jkUMlr6n", "Better MC [FABRIC] - BMC4", "SharkieFPS", "Сотни модов: новые биомы, данжи, мобы и предметы — Minecraft «на максималках».", 6_100_000, "modpack", ["adventure", "multiplayer"]),
                P("KmiWHzQ4", "Cobblemon Official Modpack", "Cobblemon", "Ловите и тренируйте покемонов в мире Minecraft.", 3_400_000, "modpack", ["adventure"]),
                P("RSFKqfQq", "Create: Above and Beyond", "simibubi", "Техно-сборка вокруг механизмов Create.", 2_800_000, "modpack", ["technology", "quests"]),
            },
            "shader" => new List<MrProject>
            {
                P("HVnmMxH1", "Complementary Shaders - Reimagined", "EminGT", "Красивые шейдеры с мягким светом — работают даже на слабых видеокартах.", 18_000_000, "shader", ["iris", "optifine"]),
                P("R6NEzAwj", "BSL Shaders", "CaptTatsu", "Классические яркие шейдеры с объёмными облаками.", 11_000_000, "shader", ["iris", "optifine"]),
                P("tRuqrNQb", "Solas Shader", "Septonious", "Мягкий свет и живая атмосфера.", 4_200_000, "shader", ["iris"]),
            },
            "resourcepack" => new List<MrProject>
            {
                P("z8nwb0ag", "Faithful 32x", "Faithful Team", "Знакомые текстуры в два раза чётче.", 9_000_000, "resourcepack", ["32x"]),
                P("9TU6pTtE", "Fresh Animations", "FreshLX", "Живые анимации мобов без модов.", 7_300_000, "resourcepack", ["entities"]),
            },
            "datapack" => new List<MrProject>
            {
                P("lWDHr9jE", "Terralith", "Stardust Labs", "Почти сто новых биомов без новых блоков.", 6_600_000, "datapack", ["worldgen"]),
                P("HbmHDjE0", "Incendium", "Stardust Labs", "Переделанный Незер с новыми мобами и сокровищами.", 2_100_000, "datapack", ["worldgen"]),
            },
            _ => Mods.Select(m => P(m.Id, m.Title, m.Author, m.Desc, m.Downloads, "mod", ["optimization"])).Concat(
            [
                P("LNytGWDc", "Create", "simibubi", "Механизмы, шестерёнки и конвейеры для автоматизации.", 26_000_000, "mod", ["technology"]),
                P("u6dRKJwZ", "Just Enough Items", "mezz", "Рецепты всех предметов в одном окне.", 70_000_000, "mod", ["utility"]),
                P("ccKDOlHs", "Waystones", "BlayTheNinth", "Путевые камни для быстрых перемещений.", 25_000_000, "mod", ["transportation"]),
            ]).ToList(),
        };
        return (list, list.Count * 1240);
    }

    static MrProject P(string id, string title, string author, string desc, long downloads, string type, List<string> cats) =>
        new(id, title.ToLowerInvariant().Replace(' ', '-'), title, desc, author, null, downloads, downloads / 40, type, cats, ["fabric"], ["1.21.1"], DateTime.UtcNow.AddDays(-(downloads % 30) - 1), null);

    public static void Install(McInstance i, MrProject p)
    {
        var kind = p.Type is "resourcepack" or "shader" or "datapack" ? p.Type : "mod";
        if (kind == "mod") Jar(i, p.Slug + ".jar", p.Title, "1.0.0", p.Description, p.Author, p.Id);
        else Pack(i, kind, p.Title + ".zip", p.Title, "1.0", p.Id);
    }

    public static McInstance PackInstance(MrProject p) => Mc.Create(p.Title, "1.21.1", "fabric", "0.17.2");
}
