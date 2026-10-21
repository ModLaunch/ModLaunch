using System.Diagnostics;
using System.Text.Json.Nodes;
using ModLaunch.Core;

namespace ModLaunch.Games;

/// <summary>Проверки справочника игр без сети: снимок, имена, слияние трёх списков, кеш и скорость поиска.</summary>
public static partial class GameDirectory
{
    /// <summary>Схема экосистемы в миниатюре: те же поля, что отдаёт Thunderstore.</summary>
    const string FixtureSchema = """
        {
          "schemaVersion": "0.3.0",
          "modloaderPackages": [
            { "packageId": "BepInEx-BepInExPack", "rootFolder": "BepInExPack", "loader": "bepinex" },
            { "packageId": "BepInEx-BepInExPack_IL2CPP", "rootFolder": "BepInExPack", "loader": "bepinex" },
            { "packageId": "BepInEx-BepInExPack_V_Rising", "rootFolder": "BepInExPack", "loader": "bepinex" },
            { "packageId": "BepInEx-BepInExPack_TABS", "rootFolder": "BepInExPack", "loader": "bepinex" },
            { "packageId": "LavaGang-MelonLoader", "rootFolder": "", "loader": "recursive-melonloader" },
            { "packageId": "Thunderstore-unreal_shimloader", "rootFolder": "", "loader": "shimloader" }
          ],
          "games": {
            "lethal-company": {
              "label": "lethal-company", "meta": { "displayName": "Lethal Company" },
              "r2modman": [{
                "meta": { "displayName": "Lethal Company" },
                "packageIndex": "https://thunderstore.io/c/lethal-company/api/v1/package-listing-index/",
                "distributions": [{ "platform": "steam", "identifier": "1966720" }, { "platform": "other", "identifier": null }],
                "steamFolderName": "Lethal Company", "exeNames": ["Lethal Company.exe", "linux.x86_64"], "packageLoader": "bepinex",
                "installRules": [{ "route": "BepInEx/plugins", "isDefaultLocation": true }, { "route": "BepInEx/config", "isDefaultLocation": false }]
              }],
              "thunderstore": { "displayName": "Lethal Company", "autolistPackageIds": ["BepInEx-BepInExPack"] }
            },
            "valheim": {
              "label": "valheim", "meta": { "displayName": "Valheim" },
              "r2modman": [
                { "meta": { "displayName": "Valheim Dedicated Server" }, "gameInstanceType": "server", "packageIndex": "https://thunderstore.io/c/valheim/api/v1/package-listing-index/",
                  "distributions": [{ "platform": "steam", "identifier": "896660" }], "steamFolderName": "Valheim dedicated server", "exeNames": ["valheim_server.exe"], "packageLoader": "bepinex", "installRules": [] },
                { "meta": { "displayName": "Valheim" }, "gameInstanceType": "game", "packageIndex": "https://thunderstore.io/c/valheim/api/v1/package-listing-index/",
                  "distributions": [{ "platform": "steam", "identifier": "892970" }], "steamFolderName": "Valheim", "exeNames": ["valheim.exe"], "packageLoader": "bepinex", "installRules": [] }
              ]
            },
            "v-rising": {
              "label": "v-rising", "meta": { "displayName": "V Rising" },
              "r2modman": [{ "meta": { "displayName": "V Rising" }, "packageIndex": "https://thunderstore.io/c/v-rising/api/v1/package-listing-index/",
                "distributions": [{ "platform": "steam", "identifier": "1604030" }], "steamFolderName": "VRising", "exeNames": ["VRising.exe"], "packageLoader": "bepinex", "installRules": [] }]
            },
            "totally-accurate-battle-simulator": {
              "label": "totally-accurate-battle-simulator", "meta": { "displayName": "TABS" },
              "r2modman": [{ "meta": { "displayName": "TABS" }, "packageIndex": "https://thunderstore.io/c/totally-accurate-battle-simulator/api/v1/package-listing-index/",
                "distributions": [{ "platform": "steam", "identifier": "508440" }], "steamFolderName": "Totally Accurate Battle Simulator", "exeNames": ["TotallyAccurateBattleSimulator.exe"], "packageLoader": "bepinex", "installRules": [] }]
            },
            "megabonk": {
              "label": "megabonk", "meta": { "displayName": "Megabonk" },
              "r2modman": [{ "meta": { "displayName": "Megabonk" }, "packageIndex": "https://thunderstore.io/c/megabonk/api/v1/package-listing-index/",
                "distributions": [{ "platform": "steam", "identifier": "3405340" }], "steamFolderName": "Megabonk", "exeNames": ["Megabonk.exe"], "packageLoader": "bepinex", "installRules": [] }],
              "thunderstore": { "displayName": "Megabonk", "autolistPackageIds": ["BepInEx-BepInExPack_IL2CPP"] }
            },
            "bonelab": {
              "label": "bonelab", "meta": { "displayName": "BONELAB" },
              "r2modman": [{ "meta": { "displayName": "BONELAB" }, "packageIndex": "https://thunderstore.io/c/bonelab/api/v1/package-listing-index/",
                "distributions": [{ "platform": "steam", "identifier": "1592190" }], "steamFolderName": "BONELAB", "exeNames": ["BONELAB_Steam_Windows64.exe"], "packageLoader": "recursive-melonloader", "installRules": [] }],
              "thunderstore": { "displayName": "BONELAB", "autolistPackageIds": ["LavaGang-MelonLoader"] }
            },
            "astroneer": {
              "label": "astroneer", "meta": { "displayName": "ASTRONEER" },
              "r2modman": [{ "meta": { "displayName": "ASTRONEER" }, "packageIndex": "https://thunderstore.io/c/astroneer/api/v1/package-listing-index/",
                "distributions": [{ "platform": "steam", "identifier": "361420" }], "steamFolderName": "ASTRONEER", "exeNames": ["Astro.exe"], "packageLoader": "shimloader",
                "installRules": [{ "route": "shimloader/mod", "isDefaultLocation": true }] }],
              "thunderstore": { "displayName": "ASTRONEER", "autolistPackageIds": ["Thunderstore-unreal_shimloader"] }
            },
            "dark-souls-remastered": {
              "label": "dark-souls-remastered", "meta": { "displayName": "DARK SOULS™: REMASTERED" },
              "thunderstore": { "displayName": "DARK SOULS™: REMASTERED" }
            },
            "nine-sols": {
              "label": "nine-sols", "meta": { "displayName": "Nine Sols" },
              "thunderstore": { "displayName": "Nine Sols", "autolistPackageIds": ["BepInEx-BepInExPack"] }
            },
            "phasmophobia": {
              "label": "phasmophobia", "meta": { "displayName": "Phasmophobia" },
              "thunderstore": { "displayName": "Phasmophobia", "listed": false }
            }
          }
        }
        """;

    /// <summary>Игры Nexus (games.json): лишние поля — как в настоящем списке.</summary>
    const string FixtureNexus = """
        [
          { "id": 5848, "name": "Lethal Company", "domain_name": "lethalcompany", "genre": "Horror", "mods": 1500, "downloads": 1, "categories": [] },
          { "id": 1704, "name": "Skyrim Special Edition", "domain_name": "skyrimspecialedition", "mods": 100000 },
          { "id": 110, "name": "Skyrim", "domain_name": "skyrim", "mods": 70000 },
          { "id": 1303, "name": "Stardew Valley", "domain_name": "stardewvalley", "mods": 26000 },
          { "id": 3667, "name": "Valheim", "domain_name": "valheim", "mods": 6000 },
          { "id": 4234, "name": "V Rising", "domain_name": "vrising", "mods": 40 },
          { "id": 1166, "name": "Astroneer", "domain_name": "astroneer", "mods": "200" },
          { "id": 160, "name": "Dark Souls", "domain_name": "darksouls", "mods": 3000 },
          { "id": 2014, "name": "Dark Souls Remastered", "domain_name": "darksoulsremastered", "mods": 900 },
          { "id": 6553, "name": "Nine Sols GOTY", "domain_name": "ninesolsgoty", "mods": 30 },
          { "id": 952, "name": "The Witcher 3", "domain_name": "witcher3", "mods": 8000 },
          { "id": 2, "name": "Empty Game", "domain_name": "emptygame", "mods": 0 },
          { "id": 3, "name": "Modding Tools", "domain_name": "site", "mods": 900 },
          { "id": 4, "name": "Few Mods", "domain_name": "fewmods", "mods": 2 },
          { "id": 5, "name": "Pok&eacute;mon Fan Game", "domain_name": "pokemonfangame", "mods": 12 }
        ]
        """;

    static readonly string[] FixtureCommunities =
    [
        """
        { "count": 4, "next": "https://thunderstore.io/api/cyberstorm/community/?page=2", "results": [
          { "identifier": "lethal-company", "name": "Lethal Company", "total_package_count": 40000 },
          { "identifier": "v-rising", "name": "V Rising", "total_package_count": 1200 },
          { "identifier": "nine-sols", "name": "Nine Sols", "total_package_count": 0 } ] }
        """,
        """
        { "count": 4, "next": null, "results": [
          { "identifier": "some-new-game", "name": "Some New Game", "total_package_count": 12 },
          { "identifier": "dead-community", "name": "Dead", "total_package_count": 0 } ] }
        """,
    ];

    [SelfTest]
    static string GameDirSnapshotParses()
    {
        string raw;
        using (var stream = typeof(GameDirectory).Assembly.GetManifestResourceStream(SnapshotName) ?? throw new Exception("snapshot is not embedded"))
        using (var reader = new StreamReader(stream)) raw = reader.ReadToEnd();
        var list = Parse(Resources.Json(SnapshotName)) ?? throw new Exception("snapshot did not parse (or has < 250 games)");
        if (list.Count < MinValid) throw new Exception($"only {list.Count} games");
        // Снимок пишет только Serialize: тот же текст — значит, порядок и формат устойчивы (git не «дрожит»).
        if (Serialize(list) != raw.Replace("\r\n", "\n")) throw new Exception("snapshot is not in canonical form — regenerate it with --gen-directory");
        for (var i = 1; i < list.Count; i++)
            if (string.CompareOrdinal(list[i - 1].Key, list[i].Key) >= 0) throw new Exception("keys not sorted/unique at " + list[i].Key);
        var index = new Index(list);
        var lc = index.BySteam(1966720) ?? throw new Exception("no Lethal Company by steam id");
        if (lc.Key != "lethal-company" || lc.Loader != "bepinex" || lc.Pack != "BepInEx-BepInExPack" || lc.ModsRoute != "BepInEx/plugins" || !lc.Exes.Contains("Lethal Company.exe")) throw new Exception("lethal company: " + lc);
        var builtin = list.Count(IsBuiltin);
        if (builtin < 10) throw new Exception($"only {builtin} built-in games recognised");
        if (list.Any(g => g.Loader is not (null or "bepinex" or "bepinex-il2cpp" or "melonloader"))) throw new Exception("unknown loader value");
        return $"{list.Count} games ({list.Count(g => g.Community is not null)} thunderstore, {list.Count(g => g.NexusDomain is not null)} nexus, {list.Count(g => g.SteamAppId > 0)} steam ids, {builtin} built-in)";
    }

    [SelfTest]
    static string GameDirNorm()
    {
        (string In, string Out)[] cases =
        [
            ("The Witcher 3: Wild Hunt", "witcher3wildhunt"),
            ("The Witcher 3 - Wild Hunt GOTY Edition", "witcher3wildhunt"),
            ("Baldur's Gate 3", "baldursgate3"),
            ("DOOM™", "doom"),
            ("Pokémon® Legends", "pokemonlegends"),
            ("Hot Dogs, Horseshoes & Hand Grenades", "hotdogshorseshoesandhandgrenades"),
            ("DARK SOULS™: REMASTERED", "darksouls"),
            ("Age of Empires II: Definitive Edition", "ageofempiresii"),
            ("Mass Effect™ Legendary Edition", "masseffect"),
            ("Death Stranding Director's Cut", "deathstranding"),
            ("R.E.P.O.", "repo"),
            ("Theme Hospital", "themehospital"),
            ("The", "the"),
            ("Remastered", "remastered"),
            ("  ", ""),
        ];
        foreach (var (input, expected) in cases)
            if (Norm(input) != expected) throw new Exception($"Norm(\"{input}\") = \"{Norm(input)}\", expected \"{expected}\"");
        // Строгое имя издание оставляет: на Nexus Skyrim и Skyrim Special Edition — разные игры.
        if (Strict("Skyrim Special Edition") == Strict("Skyrim") || Strict("The Elder Scrolls") != "elderscrolls") throw new Exception("strict");
        return $"{cases.Length} names";
    }

    [SelfTest]
    static string GameDirBuildMerges()
    {
        var list = Build(FixtureSchema, FixtureNexus, FixtureCommunities);
        var index = new Index(list);
        DirGame Get(string key) => list.FirstOrDefault(g => g.Key == key) ?? throw new Exception($"no {key} in: " + string.Join(", ", list.Select(g => g.Key)));

        // Thunderstore + Nexus с тем же именем — одна запись с обоими каталогами; моды складываются.
        var lc = Get("lethal-company");
        if (lc.Community != "lethal-company" || lc.NexusDomain != "lethalcompany" || lc.SteamAppId != 1966720 || lc.Mods != 41500) throw new Exception("lc merge: " + lc);
        if (lc.Loader != "bepinex" || lc.Pack != "BepInEx-BepInExPack" || lc.Folder != "Lethal Company" || lc.ModsRoute != "BepInEx/plugins" || lc.Exes is not ["Lethal Company.exe"]) throw new Exception("lc data: " + lc);
        // Выделенный сервер не подменяет игру; пакет встроенной игры — тот же, что ставит программа.
        var vh = Get("valheim");
        if (vh.SteamAppId != 892970 || vh.Exes is not ["valheim.exe"] || vh.NexusDomain != "valheim" || vh.Pack != "denikson-BepInExPack_Valheim") throw new Exception("valheim: " + vh);
        // Пакет угадан по имени (BepInExPack_V_Rising) и по первым буквам (TABS).
        if (Get("v-rising") is not { Pack: "BepInEx-BepInExPack_V_Rising", NexusDomain: "vrising", Mods: 1240 }) throw new Exception("v rising: " + Get("v-rising"));
        if (Get("totally-accurate-battle-simulator").Pack != "BepInEx-BepInExPack_TABS") throw new Exception("tabs pack");
        if (Get("megabonk") is not { Loader: "bepinex-il2cpp", Pack: "BepInEx-BepInExPack_IL2CPP" }) throw new Exception("il2cpp: " + Get("megabonk"));
        if (Get("bonelab") is not { Loader: "melonloader", Pack: "LavaGang-MelonLoader", ModsRoute: "Mods" }) throw new Exception("melon: " + Get("bonelab"));
        // Загрузчик, который программа не ставит (Shimloader), — null, и папки модов нет.
        if (Get("astroneer") is not { Loader: null, Pack: null, ModsRoute: null, NexusDomain: "astroneer", Mods: 200 }) throw new Exception("astroneer: " + Get("astroneer"));
        // Имя с изданием: Remastered — к своему разделу, а не к старой Dark Souls.
        if (Get("dark-souls-remastered").NexusDomain != "darksoulsremastered" || Get("darksouls").Community is not null) throw new Exception("dark souls");
        // Только без «издания» совпало (GOTY) — и пара единственная: склеены. Пустое сообщество с Nexus — остаётся.
        if (Get("nine-sols") is not { NexusDomain: "ninesolsgoty", Mods: 30 }) throw new Exception("nine sols: " + Get("nine-sols"));
        // Встроенная игра без Thunderstore получает Steam id из GameCatalog.
        var sdv = Get("stardewvalley");
        if (sdv.SteamAppId != 413150 || !IsBuiltin(sdv) || IsBuiltin(Get("skyrim"))) throw new Exception("stardew: " + sdv);
        if (Get("skyrimspecialedition").Mods != 100000 || Get("pokemonfangame").Name != "Pokémon Fan Game") throw new Exception("nexus-only entries");
        if (Get("some-new-game").Community != "some-new-game") throw new Exception("community from cyberstorm");
        // Ни модов, ни каталога: Empty Game, раздел «site», пустое и скрытое сообщества.
        foreach (var gone in new[] { "emptygame", "site", "dead-community", "phasmophobia" })
            if (list.Any(g => g.Key == gone)) throw new Exception(gone + " must be dropped");
        // Список короткий — порог Nexus опускается до одного мода.
        Get("fewmods");
        // Тот же вход — тот же текст.
        if (Serialize(Build(FixtureSchema, FixtureNexus, FixtureCommunities)) != Serialize(list)) throw new Exception("build is not deterministic");
        var back = Parse(JsonNode.Parse(Serialize(list)), min: 0) ?? throw new Exception("round trip");
        if (Serialize(back) != Serialize(list)) throw new Exception("round trip differs");

        if (index.ByKey("lethalcompany")?.Key != "lethal-company" || index.ByKey("LETHAL-COMPANY")?.Key != "lethal-company") throw new Exception("ByKey aliases");
        if (index.ByName("Skyrim")?.Key != "skyrim" || index.ByName("Skyrim Special Edition")?.Key != "skyrimspecialedition") throw new Exception("ByName strict");
        if (index.ByName("The Witcher 3: Wild Hunt — Game of the Year Edition") is not null) throw new Exception("ByName: no such name");
        if (index.ByName("Witcher 3 GOTY")?.Key != "witcher3" || index.ByName("DARK SOULS™: REMASTERED")?.Key != "dark-souls-remastered") throw new Exception("ByName loose");
        if (index.BySteam(1604030)?.Key != "v-rising" || index.BySteam(896660) is not null) throw new Exception("BySteam");
        var sky = index.Search("skyrim");
        if (sky.Count != 2 || sky[0].Key != "skyrimspecialedition") throw new Exception("search skyrim: " + string.Join(",", sky.Select(g => g.Key)));
        if (index.Search("souls").Select(g => g.Key).FirstOrDefault() != "darksouls" || index.Search("", 3).Count != 3 || index.Search("zzzz").Count != 0) throw new Exception("search");
        // Начало имени важнее середины, даже если модов меньше (witcher3 — 8000, stardewvalley — 26000).
        if (index.Search("w").FirstOrDefault()?.Key != "witcher3") throw new Exception("prefix first: " + string.Join(",", index.Search("w").Select(g => g.Key)));
        return $"{list.Count} games from 3 fixtures, lookups ok";
    }

    [SelfTest]
    static string GameDirCacheAndThrottle()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Paths.DataDir, "gamedir-test")).FullName;
        var path = Path.Combine(dir, "gamedir.json");
        var big = Enumerable.Range(0, MinValid).Select(i => new DirGame($"g{i:D4}", $"Game {i}", i + 1, null, $"g{i}", null, null, null, [], null, i)).ToList();
        SaveAtomic(path, Serialize(big));
        if (ReadFile(path)?.Count != MinValid || File.Exists(path + ".tmp")) throw new Exception("valid cache not read");
        foreach (var (bad, why) in new[] { ("{ broken", "broken json"), ("{\"v\":99,\"games\":[]}", "other format"), (Serialize(big.Take(10)), "too few games") })
        {
            File.WriteAllText(path, bad);
            if (ReadFile(path) is not null) throw new Exception("accepted " + why);
        }
        var now = new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
        if (!Stale(null, now) || Stale(now.AddDays(-6), now) || !Stale(now.AddDays(-8), now) || !Stale(now.AddDays(3), now)) throw new Exception("refresh period");
        Directory.Delete(dir, true);
        return "atomic save, broken/foreign/short cache ignored, refresh every 7 days";
    }

    [SelfTest]
    static string GameDirLookupsFast()
    {
        var all = All;
        if (all.Count < MinValid) throw new Exception($"directory has {all.Count} games");
        var queries = all.Take(200).SelectMany(g => new[] { g.Name, g.Name[..Math.Min(3, g.Name.Length)], g.Name.Length > 6 ? g.Name[2..6] : g.Name }).ToArray();
        var watch = Stopwatch.StartNew();
        var hits = 0;
        for (var i = 0; i < 5000; i++)
        {
            hits += Search(queries[i % queries.Length], 20).Count;
            if (ByName(all[i % all.Count].Name) is null || BySteam(all[i % all.Count].SteamAppId) is null && all[i % all.Count].SteamAppId > 0) throw new Exception("lookup miss: " + all[i % all.Count].Name);
        }
        watch.Stop();
        if (watch.ElapsedMilliseconds > 1500) throw new Exception($"5000 searches took {watch.ElapsedMilliseconds} ms");
        return $"5000 searches + lookups in {watch.ElapsedMilliseconds} ms over {all.Count} games ({hits} hits)";
    }
}
