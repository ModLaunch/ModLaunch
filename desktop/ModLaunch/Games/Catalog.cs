namespace ModLaunch.Games;

/// <summary>Встроенные игры и свои игры, добавленные кнопкой «+».</summary>
public static class GameCatalog
{
    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    static string LocalLow(params string[] parts) => Path.Combine([Home, "AppData", "LocalLow", .. parts]);

    public static readonly GameDef[] Builtin =
    [
        new()
        {
            Id = "stardew-valley", Name = "Stardew Valley", ShortName = "Stardew", SteamAppId = 413150,
            FolderNames = ["Stardew Valley", "StardewValley"], Accent = "#6BAA3C", Art = "game-stardew-valley.jpg",
            Loader = LoaderKind.Smapi, LoaderName = "SMAPI", LoaderSite = "https://smapi.io",
            Catalog = CatalogKind.Nexus, NexusDomain = "stardewvalley", NexusGameId = 1303, NexusHide = ["2400"],
            BrowseUrl = "https://www.nexusmods.com/stardewvalley/mods",
            Sections = Section.Pick("all", "picks", "best", "buildings", "content", "gameplay", "items", "cosmetics", "ui", "tools", "visuals", "packs"),
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
            ReShadeApi = "opengl",
        },
        new()
        {
            Id = "hollow-knight", Name = "Hollow Knight", ShortName = "Hollow Knight", SteamAppId = 367520,
            FolderNames = ["Hollow Knight"], Accent = "#6F9BFF", Art = "game-hollow-knight.jpg",
            Loader = LoaderKind.HkApi, LoaderName = "Modding API", LoaderSite = "https://github.com/hk-modding/api",
            Catalog = CatalogKind.ModLinks, BrowseUrl = "https://github.com/hk-modding/modlinks",
            NexusDomain = "hollowknight", NexusGameId = 2698, NexusHide = ["44"],
            Sections = Section.Pick("all", "picks", "best", "content", "gameplay", "cosmetics", "tools", "packs"),
            Picks = ["Custom Knight", "Benchwarp", "Pale Court", "Randomizer 4", "HKMP", "QoL", "DebugMod",
                "Transcendence", "Enemy HP Bar", "MapChanger", "GodSeekerPlus", "Charm Changer", "Lightbringer",
                "Fyrenest", "The Glimmering Realm", "Pale Prince", "Mantis Gods", "HKTimer", "Toggleable Bindings",
                "AdditionalMaps", "HealthShare"],
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
            NexusDomain = "lethalcompany", NexusGameId = 5848, NexusHide = ["42"],
            Sections = Section.Pick("all", "picks", "best", "content", "items", "gameplay", "cosmetics", "audio", "tools", "visuals", "modpacks"),
            Picks =
            [
                "notnotnotswipez-MoreCompany", "tinyhoot-ShipLoot", "anormaltwig-LateCompany", "x753-More_Suits", "Evaisa-LethalThings",
                "malco-Lategame_Upgrades", "x753-Mimics", "FlipMods-ReservedFlashlightSlot", "FlipMods-TooManyEmotes", "sunnobunno-YippeeMod",
                "FlipMods-ReservedWalkieSlot", "EliteMasterEric-Coroner", "mrgrm7-LethalCasino", "Magic_Wesley-Wesleys_Moons",
                "LethalResonance-LETHALRESONANCE", "Jordo-NeedyCats", "TwinDimensionalProductions-CoilHeadStare",
            ],
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
            // Tobey's BepInEx Pack — это загрузчик: его ставит кнопка загрузчика, в каталоге и требованиях он лишний.
            // QModManager и SMLHelper — загрузчики старой игры (до 2.0), с BepInEx не работают.
            NexusHide = ["1108", "201", "113"],
            ExtraThunderstore = true,
            LegacyBefore = new DateTime(2022, 12, 1), BrowseUrl = "https://www.nexusmods.com/subnautica/mods",
            Sections = Section.Pick("all", "picks", "best", "buildings", "vehicles", "items", "gameplay", "ui", "tools", "visuals", "packs"),
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
                "1300", "229", "125", "554", "51", "427", "42", "1104",
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
            NexusHide = ["344", "1", "34"],
            ExtraThunderstore = true,
            BrowseUrl = "https://www.nexusmods.com/subnauticabelowzero/mods",
            Sections = Section.Pick("all", "picks", "best", "buildings", "vehicles", "items", "gameplay", "ui", "tools", "visuals", "packs"),
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
            Picks = ["373", "44", "287", "264", "599", "470", "52", "53", "54", "55", "137", "417", "444", "15", "10", "57", "84", "106", "128", "171", "56", "235"],
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
            NexusDomain = "valheim", NexusGameId = 3667, NexusHide = ["15", "505"],
            Sections = Section.Pick("all", "picks", "best", "buildings", "items", "content", "gameplay", "vehicles", "cosmetics", "audio", "tools", "modpacks"),
            Picks =
            [
                "Advize-PlantEverything", "RandyKnapp-EquipmentAndQuickSlots", "shudnal-ExtraSlots", "Advize-PlantEasily",
                "OdinPlus-TeleportEverything", "MSchmoecker-MultiUserChest", "ishid4-BetterArchery",
                "Goldenrevolver-Quick_Stack_Store_Sort_Trash_Restock", "Tekla-AutoRepair", "BentoG-MissingPieces",
                "RustyMods-Seasonality", "Therzie-Warfare", "RandyKnapp-EpicLoot", "Vapok-AdventureBackpacks", "OdinPlus-OdinArchitect",
                "Therzie-Monstrum", "Therzie-Armory", "Smoothbrain-Sailing", "Smoothbrain-Jewelcrafting", "MathiasDecrock-PlanBuild",
                "OdinPlus-OdinHorse", "Smoothbrain-PassivePowers", "JereKuusela-Server_devcommands",
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
            Sections = Section.Pick("all", "picks", "best", "content", "items", "gameplay", "cosmetics", "audio", "tools", "modpacks"),
            Picks =
            [
                "TeamMoonstorm-Starstorm2", "KingEnderBrine-ProperSave", "DropPod-LookingGlass", "KingEnderBrine-ScrollableLobbyUI",
                "EnforcerGang-Enforcer", "Paladin_Alliance-PaladinMod", "Zenithrium-VanillaVoid", "MagnusMagnuson-BiggerBazaar",
                "duckduckgreyduck-ArtificerExtended", "niwith-DropinMultiplayer", "EnforcerGang-Rocket", "FunkFrog-and-Sipondo-ShareSuite",
                "TheRealElysium-EmptyChestsBeGone", "KomradeSpectre-Aetherium", "Bog-Deputy", "William758-ZetAspects",
                "EnforcerGang-HAND_OVERCLOCKED", "JavAngle-TheHouse", "Rune580-Risk_Of_Options",
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
            NexusDomain = "repo", NexusGameId = 7398,
            Sections = Section.Pick("all", "picks", "best", "items", "content", "gameplay", "cosmetics", "audio", "tools", "modpacks"),
            Picks =
            [
                "YMC_MHZ-MoreHead", "BULLETBOT-MoreUpgrades", "Zehs-ExtractionPointConfirmButton", "Magic_Wesley-Wesleys_Enemies",
                "flipf17-DeadTTS", "Cronchy-DeathHeadHopper", "Jettcodey-MoreShopItems_Updated",
                "XiaohaiMod-XH_DamageShow_EnemyHealthBar", "Lazarus-BetterTruckHeals", "Tidaleus-MoreReviveHP",
                "Magic_Wesley-Wesleys_Valuables", "Zehs-LethalCompanyValuables", "nickklmao-REPOConfig", "RESET-MoreHeadPlus", "eth9n-Mimic",
                "itsUndefined-Shop_Items_Spawn_in_Level", "AriIcedT-MinecraftStrongholdLevel", "Tansinator-Map_Value_Tracker",
                "DiFFoZ-BepInEx_Faster_Load_AssetBundles_Patcher", "OrtonLongGaming-FNAFLevel",
            ],
            SignatureDirs = ["REPO_Data"], SignatureExes = ["REPO.exe"],
            Executables = ["REPO.exe"],
            SavesDir = _ => LocalLow("semiwork", "Repo", "saves"),
            ModMarker = "dll",
        },
        new()
        {
            Id = "peak", Name = "PEAK", ShortName = "PEAK", SteamAppId = 3527290,
            FolderNames = ["PEAK"], Accent = "#E8743B", ArtUrl = GameDef.SteamArt(3527290),
            Loader = LoaderKind.Bepinex, LoaderName = "BepInEx",
            LoaderSite = "https://thunderstore.io/c/peak/p/BepInEx/BepInExPack_PEAK/",
            ThunderstorePackage = "BepInEx-BepInExPack_PEAK", ThunderstoreCommunity = "peak",
            Catalog = CatalogKind.Thunderstore, BrowseUrl = "https://thunderstore.io/c/peak/",
            NexusDomain = "peak", NexusGameId = 7867,
            Sections = Section.Pick("all", "picks", "best", "items", "content", "gameplay", "cosmetics", "audio", "tools", "modpacks"),
            Picks = ["glarmer-PEAK_Unlimited", "Roose-Piggyback", "cretapark-More_Customizations", "nickklmao-EasyBackpack", "TeddyBRB-Too_Many_Hats", "figgies-SmoreSkinColors", "Steven-Everest", "MonAmiral-MoreCustomHats", "PEAKModding-PEAKLib_Items", "loaforc-loaforcsSoundAPI"],
            SignatureDirs = ["PEAK_Data"], SignatureExes = ["PEAK.exe"],
            Executables = ["PEAK.exe"],
            ModMarker = "dll",
        },
        new()
        {
            Id = "h3vr", Name = "Hot Dogs, Horseshoes & Hand Grenades", ShortName = "H3VR", SteamAppId = 450540,
            FolderNames = ["H3VR"], Accent = "#D9A441", ArtUrl = GameDef.SteamArt(450540),
            Loader = LoaderKind.Bepinex, LoaderName = "BepInEx",
            LoaderSite = "https://thunderstore.io/c/h3vr/p/BepInEx/BepInExPack_H3VR/",
            ThunderstorePackage = "BepInEx-BepInExPack_H3VR", ThunderstoreCommunity = "h3vr",
            Catalog = CatalogKind.Thunderstore, BrowseUrl = "https://thunderstore.io/c/h3vr/",
            Sections = Section.Pick("all", "picks", "best", "items", "content", "gameplay", "cosmetics", "audio", "tools", "modpacks"),
            Picks = ["cityrobo-OpenScripts", "Andrew_FTW-FTW_Arms_AFCL", "nrgill28-Sodalite", "cityrobo-OpenScripts2", "WFIOST-H3VRUtilities", "devyndamonster-OtherLoader", "VIP-H3MP", "cityrobo-ModularWorkshop", "nrgill28-Atlas", "Meat_banono-Meats_ModulAR"],
            SignatureDirs = ["h3vr_Data"], SignatureExes = ["h3vr.exe"],
            Executables = ["h3vr.exe"],
            ModMarker = "dll",
        },
        new()
        {
            Id = "content-warning", Name = "Content Warning", ShortName = "Content Warning", SteamAppId = 2881650,
            FolderNames = ["Content Warning"], Accent = "#F5D90A", ArtUrl = GameDef.SteamArt(2881650),
            Loader = LoaderKind.Bepinex, LoaderName = "BepInEx",
            LoaderSite = "https://thunderstore.io/c/content-warning/p/BepInEx/BepInExPack/",
            ThunderstorePackage = "BepInEx-BepInExPack", ThunderstoreCommunity = "content-warning",
            Catalog = CatalogKind.Thunderstore, BrowseUrl = "https://thunderstore.io/c/content-warning/",
            NexusDomain = "contentwarning", NexusGameId = 6301,
            Sections = Section.Pick("all", "picks", "best", "items", "content", "gameplay", "cosmetics", "audio", "tools", "modpacks"),
            Picks = ["MaxWasUnavailable-Virality", "ViViKo-MoreColors", "CommanderCat101-ContentSettings", "RamuneNeptune-MakeMeRagdoll", "cw_qwbarch-Mirage", "loaforc-Flashcard", "hyydsz-ShopUtils", "hyydsz-Boombox", "GamingFrame-More_Comments", "Clementinise-DeathStatus"],
            SignatureDirs = ["Content Warning_Data"], SignatureExes = ["Content Warning.exe"],
            Executables = ["Content Warning.exe"],
            ModMarker = "dll",
        },
        new()
        {
            Id = "ultrakill", Name = "ULTRAKILL", ShortName = "ULTRAKILL", SteamAppId = 1229490,
            FolderNames = ["ULTRAKILL"], Accent = "#E03C31", ArtUrl = GameDef.SteamArt(1229490),
            Loader = LoaderKind.Bepinex, LoaderName = "BepInEx",
            LoaderSite = "https://thunderstore.io/c/ultrakill/p/BepInEx/BepInExPack/",
            ThunderstorePackage = "BepInEx-BepInExPack", ThunderstoreCommunity = "ultrakill",
            Catalog = CatalogKind.Thunderstore, BrowseUrl = "https://thunderstore.io/c/ultrakill/",
            NexusDomain = "ultrakill", NexusGameId = 3515,
            Sections = Section.Pick("all", "picks", "best", "items", "content", "gameplay", "cosmetics", "audio", "tools", "modpacks"),
            Picks = ["EternalsTeam-PluginConfigurator", "EternalsTeam-AngryLevelLoader", "Hydraxous-Configgy", "xzxADIxzx-Jaket", "Hydraxous-UltraFunGuns", "ZedDev-USTManager", "Waff1e-UltraTweaker", "GalvinVoltag-The_Timestopper", "bobthecorn-ULTRASKINS_GC", "Flazhik-CybergrindMusicExplorer"],
            SignatureDirs = ["ULTRAKILL_Data"], SignatureExes = ["ULTRAKILL.exe"],
            Executables = ["ULTRAKILL.exe"],
            ModMarker = "dll",
        },
        new()
        {
            Id = "rounds", Name = "ROUNDS", ShortName = "ROUNDS", SteamAppId = 1557740,
            FolderNames = ["ROUNDS"], Accent = "#F2C14E", ArtUrl = GameDef.SteamArt(1557740),
            Loader = LoaderKind.Bepinex, LoaderName = "BepInEx",
            LoaderSite = "https://thunderstore.io/c/rounds/p/BepInEx/BepInExPack_ROUNDS/",
            ThunderstorePackage = "BepInEx-BepInExPack_ROUNDS", ThunderstoreCommunity = "rounds",
            Catalog = CatalogKind.Thunderstore, BrowseUrl = "https://thunderstore.io/c/rounds/",
            Sections = Section.Pick("all", "picks", "best", "items", "content", "gameplay", "cosmetics", "audio", "tools", "modpacks"),
            Picks = ["olavim-RoundsWithFriends", "willis81808-UnboundLib", "Root-RarityLib", "Pykess-ModdingUtils", "XAngelMoonX-CR", "Root-Classes_Manager_Reborn", "olavim-MapsExtended", "willuwontu-WillsWackyManagers", "CrazyCoders-RarityBundle", "Root-CardThemeLib"],
            SignatureDirs = ["Rounds_Data"], SignatureExes = ["Rounds.exe"],
            Executables = ["Rounds.exe"],
            ModMarker = "dll",
        },
        new()
        {
            Id = "dyson-sphere-program", Name = "Dyson Sphere Program", ShortName = "Dyson Sphere", SteamAppId = 1366540,
            FolderNames = ["Dyson Sphere Program"], Accent = "#3FA7F5", ArtUrl = GameDef.SteamArt(1366540),
            Loader = LoaderKind.Bepinex, LoaderName = "BepInEx",
            LoaderSite = "https://thunderstore.io/c/dyson-sphere-program/p/xiaoye97/BepInEx/",
            ThunderstorePackage = "xiaoye97-BepInEx", ThunderstoreCommunity = "dyson-sphere-program",
            Catalog = CatalogKind.Thunderstore, BrowseUrl = "https://thunderstore.io/c/dyson-sphere-program/",
            NexusDomain = "dysonsphereprogram", NexusGameId = 3641,
            Sections = Section.Pick("all", "picks", "best", "items", "content", "gameplay", "cosmetics", "audio", "tools", "modpacks"),
            Picks = ["CommonAPI-CommonAPI", "xiaoye97-LDBTool", "nebula-NebulaMultiplayerMod", "blacksnipebiu-Auxilaryfunction", "starfi5h-BulletTime", "Galactic_Scale-GalacticScale", "soarqin-UXAssist", "kremnev8-BlueprintTweaks", "jinxOAO-MoreMegaStructure", "hetima-SplitterOverBelt"],
            SignatureDirs = ["DSPGAME_Data"], SignatureExes = ["DSPGAME.exe"],
            Executables = ["DSPGAME.exe"],
            ModMarker = "dll",
        },
    ];

    /// <summary>Все игры: встроенные и свои (свои живут в AppState.Games).</summary>
    public static GameDef[] All => Core.AppState.Games.Select(g => g.Def).ToArray();

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
