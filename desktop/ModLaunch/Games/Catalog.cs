namespace ModLaunch.Games;

/// <summary>Все поддерживаемые игры — те же восемь, что и в версии 3.x.</summary>
public static class GameCatalog
{
    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    static string LocalLow(params string[] parts) => Path.Combine([Home, "AppData", "LocalLow", .. parts]);

    public static readonly GameDef[] All =
    [
        new()
        {
            Id = "stardew-valley", Name = "Stardew Valley", ShortName = "Stardew", SteamAppId = 413150,
            FolderNames = ["Stardew Valley", "StardewValley"], Accent = "#6BAA3C", Art = "game-stardew-valley.jpg",
            Loader = LoaderKind.Smapi, LoaderName = "SMAPI", LoaderSite = "https://smapi.io",
            Catalog = CatalogKind.Nexus, NexusDomain = "stardewvalley", NexusGameId = 1303, NexusHide = ["2400"],
            BrowseUrl = "https://www.nexusmods.com/stardewvalley/mods",
            Sections = Section.Pick("all", "picks", "buildings", "content", "gameplay", "items", "cosmetics", "ui", "tools", "visuals", "packs"),
            NexusCategories = new()
            {
                ["buildings"] = ["Buildings", "Furniture", "Interiors"],
                ["content"] = ["Expansions", "New Characters", "Maps", "Locations", "Events", "Dialogue"],
                ["gameplay"] = ["Gameplay Mechanics", "Cheats", "Fishing", "Crops", "Livestock and Animals", "Crafting"],
                ["items"] = ["Items", "Clothing"],
                ["cosmetics"] = ["Characters", "Portraits", "Pets / Horses", "Player", "!New Characters"],
                ["ui"] = ["User Interface"],
                ["tools"] = ["Modding Tools"],
                ["visuals"] = ["Visuals and Graphics"],
            },
            Picks = ["541", "518", "1063", "239", "5098", "1915", "3753", "7098", "1401"],
            Kits =
            [
                new("sdv-comfort", ["5098", "541", "518", "239", "7098"]),
                new("sdv-farm", ["1063", "1401", "518"]),
                new("sdv-expanded", ["1915", "3753"]),
            ],
            SignatureExes = ["Stardew Valley.exe", "Stardew Valley.dll", "StardewValley.exe", "Stardew Valley.deps.json"],
            SignatureWith = "Content", GogId = "1453375253",
            Executables = ["StardewModdingAPI.exe", "Stardew Valley.exe"],
            SavesDir = _ => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StardewValley", "Saves"),
            ModMarker = "manifest",
        },
        new()
        {
            Id = "hollow-knight", Name = "Hollow Knight", ShortName = "Hollow Knight", SteamAppId = 367520,
            FolderNames = ["Hollow Knight"], Accent = "#6F9BFF", Art = "game-hollow-knight.jpg",
            Loader = LoaderKind.HkApi, LoaderName = "Modding API", LoaderSite = "https://github.com/hk-modding/api",
            Catalog = CatalogKind.ModLinks, BrowseUrl = "https://github.com/hk-modding/modlinks",
            Sections = Section.Pick("all", "picks", "content", "gameplay", "cosmetics", "tools", "packs"),
            Picks = ["Custom Knight", "Benchwarp", "Pale Court", "Randomizer 4", "HKMP", "QoL", "DebugMod"],
            Kits = [new("hk-comfort", ["Benchwarp", "QoL"]), new("hk-coop", ["HKMP", "Custom Knight"])],
            SignatureDirs = ["hollow_knight_Data", "Hollow Knight_Data"], GogId = "1308320804",
            Executables = ["hollow_knight.exe", "Hollow Knight.exe"],
            SavesDir = _ => LocalLow("Team Cherry", "Hollow Knight"),
            ModMarker = "dll",
        },
        new()
        {
            Id = "lethal-company", Name = "Lethal Company", ShortName = "Lethal Company", SteamAppId = 1966720,
            FolderNames = ["Lethal Company"], Accent = "#C9A227", Art = "game-lethal-company.jpg",
            Loader = LoaderKind.Bepinex, LoaderName = "BepInEx",
            LoaderSite = "https://thunderstore.io/c/lethal-company/p/BepInEx/BepInExPack/",
            ThunderstorePackage = "BepInEx-BepInExPack", ThunderstoreCommunity = "lethal-company",
            Catalog = CatalogKind.Thunderstore, BrowseUrl = "https://thunderstore.io/c/lethal-company/",
            Sections = Section.Pick("all", "picks", "content", "items", "gameplay", "cosmetics", "audio", "tools", "visuals", "modpacks"),
            Picks = ["notnotnotswipez-MoreCompany", "tinyhoot-ShipLoot", "anormaltwig-LateCompany", "x753-More_Suits", "Evaisa-LethalThings", "malco-Lategame_Upgrades"],
            SignatureDirs = ["Lethal Company_Data"], SignatureExes = ["Lethal Company.exe"],
            Executables = ["Lethal Company.exe"],
            SavesDir = _ => LocalLow("ZeekerssRBLX", "Lethal Company"),
            ModMarker = "dll",
        },
        new()
        {
            Id = "subnautica", Name = "Subnautica", ShortName = "Subnautica", SteamAppId = 264710,
            FolderNames = ["Subnautica"], Accent = "#2BB3C0", Art = "game-subnautica.png",
            Loader = LoaderKind.Bepinex, LoaderName = "BepInEx",
            LoaderSite = "https://thunderstore.io/c/subnautica/p/Subnautica_Modding/BepInExPack/",
            ThunderstorePackage = "Subnautica_Modding-BepInExPack", ThunderstoreCommunity = "subnautica",
            Catalog = CatalogKind.Nexus, NexusDomain = "subnautica", NexusGameId = 1155,
            LegacyBefore = new DateTime(2022, 12, 1), BrowseUrl = "https://www.nexusmods.com/subnautica/mods",
            Sections = Section.Pick("all", "picks", "buildings", "vehicles", "items", "gameplay", "ui", "tools", "visuals", "packs"),
            NexusCategories = new()
            {
                ["buildings"] = ["Buildables"],
                ["vehicles"] = ["Vehicles and Upgrades"],
                ["items"] = ["Items", "Crafting"],
                ["gameplay"] = ["Gameplay", "Creatures", "Environment", "Adventure"],
                ["ui"] = ["User Interface"],
                ["tools"] = ["Libraries", "Utilities", "Modding Tools"],
                ["visuals"] = ["Visuals and Graphics"],
            },
            Picks =
            [
                "1262", "1112", "859", "1457", "207", "984", "142", "12", "24", "2800", "1119", "3143", "2447", "3816",
                "1180", "1121", "1504", "141", "216", "398", "220", "1116", "1206", "640", "1604", "1820", "1542", "871",
                "1748", "1912", "2153", "2461", "365", "136", "1135", "235", "1453", "722", "237", "389", "3419", "517",
                "1300", "229", "125",
            ],
            Kits =
            [
                new("sn-builder", ["1262", "2800", "1119", "3143", "2447", "1180", "1121"]),
                new("sn-vehicles", ["1262", "142", "365", "859", "1135", "3816"]),
                new("sn-comfort", ["1262", "1112", "207", "984", "722", "3419", "235", "1453", "237"]),
                new("sn-subs", ["1262", "859", "871", "1748", "1912", "2153", "2461"]),
                new("sn-story", ["1262", "1457", "640", "1604", "1820", "1542"]),
            ],
            SignatureDirs = ["Subnautica_Data"], SignatureExes = ["Subnautica.exe"],
            Executables = ["Subnautica.exe"],
            SavesDir = p => Path.Combine(p, "SNAppData", "SavedGames"),
            ModMarker = "dll",
        },
        new()
        {
            Id = "subnautica-below-zero", Name = "Subnautica: Below Zero", ShortName = "Below Zero", SteamAppId = 848450,
            FolderNames = ["SubnauticaZero", "Subnautica Below Zero", "Subnautica - Below Zero"], Accent = "#7FB4E6",
            Art = "game-subnautica-below-zero.png",
            Loader = LoaderKind.Bepinex, LoaderName = "BepInEx",
            LoaderSite = "https://thunderstore.io/c/subnautica-below-zero/p/Subnautica_Modding/BepInExPack/",
            ThunderstorePackage = "Subnautica_Modding-BepInExPack", ThunderstoreCommunity = "subnautica-below-zero",
            Catalog = CatalogKind.Nexus, NexusDomain = "subnauticabelowzero", NexusGameId = 2706,
            BrowseUrl = "https://www.nexusmods.com/subnauticabelowzero/mods",
            Sections = Section.Pick("all", "picks", "buildings", "vehicles", "items", "gameplay", "ui", "tools", "visuals", "packs"),
            NexusCategories = new()
            {
                ["buildings"] = ["Base Pieces"],
                ["vehicles"] = ["Vehicles and Upgrades"],
                ["items"] = ["Items"],
                ["gameplay"] = ["Gameplay Effects and Changes", "Adventure", "Environment", "Bug Fixes"],
                ["ui"] = ["User Interface"],
                ["tools"] = ["Library", "Utilities"],
                ["visuals"] = ["Shader Presets"],
            },
            Picks = ["373", "44", "287", "264", "599", "470", "52", "53", "54", "55", "137", "417", "444"],
            Kits =
            [
                new("bz-builder", ["373", "287", "264", "599", "470"]),
                new("bz-seatruck", ["373", "44", "417", "52", "53", "54", "55", "137"]),
            ],
            SignatureDirs = ["SubnauticaZero_Data"], SignatureExes = ["SubnauticaZero.exe"],
            Executables = ["SubnauticaZero.exe"],
            SavesDir = p => Path.Combine(p, "SNAppData", "SavedGames"),
            ModMarker = "dll",
        },
        new()
        {
            Id = "valheim", Name = "Valheim", ShortName = "Valheim", SteamAppId = 892970,
            FolderNames = ["Valheim"], Accent = "#E09F3E", Art = "game-valheim.jpg",
            Loader = LoaderKind.Bepinex, LoaderName = "BepInEx",
            LoaderSite = "https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/",
            ThunderstorePackage = "denikson-BepInExPack_Valheim", ThunderstoreCommunity = "valheim",
            Catalog = CatalogKind.Thunderstore, BrowseUrl = "https://thunderstore.io/c/valheim/",
            Sections = Section.Pick("all", "picks", "buildings", "items", "content", "gameplay", "vehicles", "cosmetics", "audio", "tools", "modpacks"),
            Picks =
            [
                "Advize-PlantEverything", "RandyKnapp-EquipmentAndQuickSlots", "shudnal-ExtraSlots", "Advize-PlantEasily",
                "OdinPlus-TeleportEverything", "MSchmoecker-MultiUserChest", "ishid4-BetterArchery",
                "Goldenrevolver-Quick_Stack_Store_Sort_Trash_Restock", "Tekla-AutoRepair", "BentoG-MissingPieces",
                "RustyMods-Seasonality", "Therzie-Warfare",
            ],
            SignatureDirs = ["valheim_Data"], SignatureExes = ["valheim.exe"],
            Executables = ["valheim.exe"],
            SavesDir = _ => LocalLow("IronGate", "Valheim"),
            ModMarker = "dll",
        },
        new()
        {
            Id = "risk-of-rain-2", Name = "Risk of Rain 2", ShortName = "Risk of Rain 2", SteamAppId = 632360,
            FolderNames = ["Risk of Rain 2"], Accent = "#4FB0C6", Art = "game-risk-of-rain-2.jpg",
            Loader = LoaderKind.Bepinex, LoaderName = "BepInEx",
            LoaderSite = "https://thunderstore.io/c/riskofrain2/p/bbepis/BepInExPack/",
            ThunderstorePackage = "bbepis-BepInExPack", ThunderstoreCommunity = "riskofrain2",
            Catalog = CatalogKind.Thunderstore, BrowseUrl = "https://thunderstore.io/c/riskofrain2/",
            Sections = Section.Pick("all", "picks", "content", "items", "gameplay", "cosmetics", "audio", "tools", "modpacks"),
            Picks =
            [
                "TeamMoonstorm-Starstorm2", "KingEnderBrine-ProperSave", "DropPod-LookingGlass", "KingEnderBrine-ScrollableLobbyUI",
                "EnforcerGang-Enforcer", "Paladin_Alliance-PaladinMod", "Zenithrium-VanillaVoid", "MagnusMagnuson-BiggerBazaar",
                "duckduckgreyduck-ArtificerExtended", "niwith-DropinMultiplayer",
            ],
            SignatureDirs = ["Risk of Rain 2_Data"], SignatureExes = ["Risk of Rain 2.exe"],
            Executables = ["Risk of Rain 2.exe"],
            SavesDir = SteamUserdataSaves(632360),
            ModMarker = "dll",
        },
        new()
        {
            Id = "repo", Name = "R.E.P.O.", ShortName = "R.E.P.O.", SteamAppId = 3241660,
            FolderNames = ["REPO", "R.E.P.O."], Accent = "#F2A93B",
            Loader = LoaderKind.Bepinex, LoaderName = "BepInEx",
            LoaderSite = "https://thunderstore.io/c/repo/p/BepInEx/BepInExPack/",
            ThunderstorePackage = "BepInEx-BepInExPack", ThunderstoreCommunity = "repo",
            Catalog = CatalogKind.Thunderstore, BrowseUrl = "https://thunderstore.io/c/repo/",
            Sections = Section.Pick("all", "picks", "items", "content", "gameplay", "cosmetics", "audio", "tools", "modpacks"),
            Picks =
            [
                "YMC_MHZ-MoreHead", "BULLETBOT-MoreUpgrades", "Zehs-ExtractionPointConfirmButton", "Magic_Wesley-Wesleys_Enemies",
                "flipf17-DeadTTS", "Cronchy-DeathHeadHopper", "Jettcodey-MoreShopItems_Updated",
                "XiaohaiMod-XH_DamageShow_EnemyHealthBar", "Lazarus-BetterTruckHeals", "Tidaleus-MoreReviveHP",
                "Magic_Wesley-Wesleys_Valuables", "Zehs-LethalCompanyValuables",
            ],
            SignatureDirs = ["REPO_Data"], SignatureExes = ["REPO.exe"],
            Executables = ["REPO.exe"],
            SavesDir = _ => LocalLow("semiwork", "Repo", "saves"),
            ModMarker = "dll",
        },
    ];

    public static GameDef? ById(string id) => All.FirstOrDefault(g => g.Id == id);

    /// <summary>Сохранения в Steam: userdata/&lt;id&gt;/&lt;appid&gt;/remote/UserProfiles.</summary>
    static Func<string, string> SteamUserdataSaves(int appId) => gamePath =>
    {
        var roots = new List<string>();
        var idx = gamePath.IndexOf(Path.Combine("steamapps", "common"), StringComparison.OrdinalIgnoreCase);
        if (idx > 0) roots.Add(gamePath[..(idx - 1)]);
        roots.AddRange(Locator.SteamRoots());
        foreach (var root in roots)
        {
            var userdata = Path.Combine(root, "userdata");
            if (!Directory.Exists(userdata)) continue;
            var found = Directory.EnumerateDirectories(userdata)
                .Select(u => Path.Combine(u, appId.ToString(), "remote", "UserProfiles"))
                .Where(Directory.Exists)
                .OrderByDescending(Directory.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (found is not null) return found;
        }
        return Path.Combine(roots.FirstOrDefault() ?? "", "userdata");
    };
}
