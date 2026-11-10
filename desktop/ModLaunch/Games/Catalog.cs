namespace ModLaunch.Games;

/// <summary>Встроенные игры и свои игры, добавленные кнопкой «+».</summary>
public static class GameCatalog
{
    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    static string LocalLow(params string[] parts) => Path.Combine([Home, "AppData", "LocalLow", .. parts]);

    public static readonly GameDef[] Builtin =
    [
        // Minecraft: Java Edition — сборки, Fabric/Quilt/Forge/NeoForge и каталог Modrinth (8.5).
        new()
        {
            Id = "minecraft", Name = "Minecraft", ShortName = "Minecraft", SteamAppId = 0, ArtKey = "minecraft",
            FolderNames = [".minecraft"], Accent = "#3FB950",
            Loader = LoaderKind.Minecraft, LoaderName = "Fabric · Quilt · Forge · NeoForge", LoaderSite = "https://fabricmc.net",
            Catalog = CatalogKind.Modrinth, BrowseUrl = "https://modrinth.com/mods",
            Sections = [],
            SignatureDirs = ["versions"],
            Executables = ["MinecraftLauncher.exe"],
            SavesDir = root => Minecraft.Mc.SavesDir(root) ?? Path.Combine(root, "saves"),
            ModMarker = "jar",
            ReShadeApi = "opengl",
        },
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
                // 26.0: устаревшие (deprecated) ShipLoot, LateCompany и LethalThings заменены живыми модами.
                "notnotnotswipez-MoreCompany", "PXC-ShipLootPlus", "LTGVE-VeryLateCompany", "x753-More_Suits", "qwbarch-Mirage",
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
            // 26.0: 3605 — BepInExPack с Nexus, второй загрузчик рядом с нашим.
            NexusDomain = "valheim", NexusGameId = 3667, NexusHide = ["15", "505", "3605"],
            Sections = Section.Pick("all", "picks", "best", "buildings", "items", "content", "gameplay", "vehicles", "cosmetics", "audio", "tools", "modpacks"),
            Picks =
            [
                "Advize-PlantEverything", "RandyKnapp-EquipmentAndQuickSlots", "shudnal-ExtraSlots", "Advize-PlantEasily",
                "OdinPlus-TeleportEverything", "MSchmoecker-MultiUserChest", "ishid4-BetterArchery",
                // 26.0: устаревшие AutoRepair, Sailing, Jewelcrafting и PassivePowers заменены живыми модами.
                "Goldenrevolver-Quick_Stack_Store_Sort_Trash_Restock", "TastyChickenLegs-AutomaticFuel", "BentoG-MissingPieces",
                "RustyMods-Seasonality", "RandyKnapp-EpicLoot", "Vapok-AdventureBackpacks", "OdinPlus-OdinArchitect",
                "Therzie-Monstrum", "Therzie-Armory", "zolantris-ValheimRAFT", "Therzie-Warfare", "MathiasDecrock-PlanBuild",
                "OdinPlus-OdinHorse", "WackyMole-WackyEpicMMOSystem", "JereKuusela-Server_devcommands",
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
                "Magic_Wesley-Wesleys_Valuables", "Zehs-LethalCompanyValuables", "REPOModding-REPOConfig", "RESET-MoreHeadPlus", "eth9n-Mimic",
                "itsUndefined-Shop_Items_Spawn_in_Level", "AriIcedT-MinecraftStrongholdLevel", "Tansinator-Map_Value_Tracker",
                "DiFFoZ-BepInEx_Faster_Load_AssetBundles_Patcher",
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
            Picks = ["glarmer-PEAK_Unlimited", "cretapark-More_Customizations", "nickklmao-EasyBackpack", "TeddyBRB-Too_Many_Hats", "figgies-SmoreSkinColors", "Steven-Everest", "MonAmiral-MoreCustomHats", "PEAKModding-PEAKLib_Items", "loaforc-loaforcsSoundAPI"],
            SignatureDirs = ["PEAK_Data"], SignatureExes = ["PEAK.exe"],
            Executables = ["PEAK.exe"],
            ModMarker = "dll",
        },
        // 26.0: ATLYSS — одно из самых больших сообществ Thunderstore (600+ модов), плюс раздел на Nexus.
        new()
        {
            Id = "atlyss", Name = "ATLYSS", ShortName = "ATLYSS", SteamAppId = 2768430,
            FolderNames = ["ATLYSS"], Accent = "#8E6CF0", ArtUrl = GameDef.SteamArt(2768430),
            Loader = LoaderKind.Bepinex, LoaderName = "BepInEx",
            LoaderSite = "https://thunderstore.io/c/atlyss/p/BepInEx/BepInExPack/",
            ThunderstorePackage = "BepInEx-BepInExPack", ThunderstoreCommunity = "atlyss",
            Catalog = CatalogKind.Thunderstore, BrowseUrl = "https://thunderstore.io/c/atlyss/",
            NexusDomain = "atlyss", NexusGameId = 7077,
            Sections = Section.Pick("all", "picks", "best"),
            Picks =
            [
                "Catman232-Homebrewery", "Nessie-EasySettings", "The_Queue_Of_Em-QM_Customization_Options", "WolfKann-WolfKann_Skins", "Xesy-Storefront",
                "SnivyXXY-Atlyss_Emote_Wheel", "Marioalexsan-ModAudio", "WawaTofu-Haversack_Of_Everything", "WatchingPotatoes-EasyVanity",
            ],
            SignatureDirs = ["ATLYSS_Data"], SignatureExes = ["ATLYSS.exe"],
            Executables = ["ATLYSS.exe"],
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
            // 26.0: раздел на Nexus (домен — полное имя игры, «h3vr» там нет).
            NexusDomain = "hotdogshorseshoesandhandgrenades", NexusGameId = 3326,
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
            NexusDomain = "ultrakill", NexusGameId = 3515, NexusHide = ["200"],
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
        new()
        {
            Id = "hollow-knight-silksong", Name = "Hollow Knight: Silksong", ShortName = "Silksong", SteamAppId = 1030300,
            FolderNames = ["Hollow Knight Silksong", "Hollow Knight - Silksong"], Accent = "#C8394B", ArtUrl = GameDef.SteamArt(1030300),
            Loader = LoaderKind.Bepinex, LoaderName = "BepInEx",
            LoaderSite = "https://thunderstore.io/c/hollow-knight-silksong/p/silksong_modding/BepInExPack_Silksong/",
            ThunderstorePackage = "silksong_modding-BepInExPack_Silksong", ThunderstoreCommunity = "hollow-knight-silksong",
            // 26.0: 26 и 44 — сборки BepInEx на Nexus (самая популярная строка каталога), а загрузчик мы ставим сами.
            Catalog = CatalogKind.Nexus, NexusDomain = "hollowknightsilksong", NexusGameId = 8136, ExtraThunderstore = true, NexusHide = ["26", "44"],
            BrowseUrl = "https://www.nexusmods.com/hollowknightsilksong/mods",
            Sections = Section.Pick("all", "picks", "best", "packs"),
            // 26.0: рандомайзеры (врагов, Archipelago), DebugMod и пантеон — есть только на Thunderstore.
            Picks =
            [
                "cometcake575-Architect", "silksong_modding-ModMenu", "XiaohaiMod-ShowDamage_HealthBar", "cometcake575-SimpleEnemyRando",
                "hk_speedrunning-DebugMod", "momochi003-Pantheon_Of_Pharloom", "BatAtVideoGames-SilksongArchipelagoRandomizer",
            ],
            SignatureDirs = ["Hollow Knight Silksong_Data"], SignatureExes = ["Hollow Knight Silksong.exe"],
            Executables = ["Hollow Knight Silksong.exe"],
            SavesDir = _ => LocalLow("Team Cherry", "Hollow Knight Silksong"),
            ModMarker = "dll",
        },
        new()
        {
            Id = "gtfo", Name = "GTFO", ShortName = "GTFO", SteamAppId = 493520,
            FolderNames = ["GTFO"], Accent = "#9FB4C7", ArtUrl = GameDef.SteamArt(493520),
            Loader = LoaderKind.Bepinex, LoaderName = "BepInEx",
            LoaderSite = "https://thunderstore.io/c/gtfo/p/BepInEx/BepInExPack_GTFO/",
            ThunderstorePackage = "BepInEx-BepInExPack_GTFO", ThunderstoreCommunity = "gtfo",
            Catalog = CatalogKind.Thunderstore, BrowseUrl = "https://thunderstore.io/c/gtfo/",
            NexusDomain = "gtfo", NexusGameId = 3657,
            Sections = Section.Pick("all", "picks", "best", "content", "items", "gameplay", "cosmetics", "audio", "tools", "modpacks"),
            Picks = ["Dinorush-ExtraWeaponCustomization", "Localia-PingEverything", "easternunit100-BetterBots", "randomuserhi-KillIndicatorFix",
                "easternunit100-ChatterReborn", "Dex-Tweaker", "Localia-ResourceHelper", "randomuserhi-DamageSync", "Localia-PacksHelper", "dakkhuza-MTFO"],
            SignatureDirs = ["GTFO_Data"], SignatureExes = ["GTFO.exe"],
            Executables = ["GTFO.exe"],
            ModMarker = "dll",
        },
        new()
        {
            Id = "outward", Name = "Outward", ShortName = "Outward", SteamAppId = 794260,
            FolderNames = ["Outward", "Outward Definitive Edition"], Accent = "#B98B4E", ArtUrl = GameDef.SteamArt(794260),
            Loader = LoaderKind.Bepinex, LoaderName = "BepInEx",
            LoaderSite = "https://thunderstore.io/c/outward/p/BepInEx/BepInExPack_Outward/",
            ThunderstorePackage = "BepInEx-BepInExPack_Outward", ThunderstoreCommunity = "outward",
            Catalog = CatalogKind.Thunderstore, BrowseUrl = "https://thunderstore.io/c/outward/",
            NexusDomain = "outward", NexusGameId = 2775,
            Sections = Section.Pick("all", "picks", "best", "content", "items", "gameplay", "ui", "tools", "modpacks"),
            Picks = ["Mefino-Outward_Config_Manager", "SpicerXD-Raid_Mode", "sinai-dev-CombatHUD", "sinai-dev-More_Map_Details",
                "sinai-dev-Shared_Quest_Rewards", "stormcancer-Classfixes_Part_1", "stormcancer-Weaponry_and_Enchantments_Additions", "Faeryn-BetterInventory",
                "sinai-dev-Minimap", "lasyan3-SoroboreanTravelAgency"],
            SignatureDirs = ["Outward_Data", "Outward Definitive Edition_Data"],
            SignatureExes = ["Outward.exe", "Outward Definitive Edition.exe"], SignatureWith = "UnityPlayer.dll",
            Executables = ["Outward Definitive Edition.exe", "Outward.exe"],
            ModMarker = "dll",
        },
        // 23.2: Cyberpunk 2077 (REDengine 4) — моды с Nexus; режим разработчика — Cyber Engine Tweaks и Entity Spawner
        // (Features/DevBackends.cs). Узнаётся по bin/x64/Cyberpunk2077.exe в любой папке (Steam, GOG, Epic, свои папки).
        new()
        {
            Id = "cyberpunk-2077", Name = "Cyberpunk 2077", ShortName = "Cyberpunk 2077", SteamAppId = 1091500,
            FolderNames = ["Cyberpunk 2077", "Cyberpunk2077"], Accent = "#F3E600", ArtUrl = GameDef.SteamArt(1091500),
            Engine = "redengine", ModsFolder = Path.Combine("archive", "pc", "mod"),
            Loader = LoaderKind.None, LoaderName = "Cyber Engine Tweaks", LoaderSite = "https://github.com/maximegmd/CyberEngineTweaks",
            Catalog = CatalogKind.Nexus, BrowseUrl = "https://www.nexusmods.com/cyberpunk2077/mods",
            NexusDomain = "cyberpunk2077", NexusGameId = 3333,
            Sections = Section.Pick("all", "best"),
            SignatureDirs = [Path.Combine("bin", "x64", "Cyberpunk2077.exe")], SignatureExes = ["Cyberpunk2077.exe"], SignatureWith = "REDprelauncher.exe",
            GogId = "1423049311",
            Executables = [Path.Combine("bin", "x64", "Cyberpunk2077.exe")],
            SavesDir = _ => Path.Combine(Home, "Saved Games", "CD Projekt Red", "Cyberpunk 2077"),
            ModMarker = "any",
        },

        // 23.3: большие игры (AAA). Моды — с Nexus (номера игр сверены с Nexus и Steam). Узнаются по exe в любой папке.
        // Режим разработчика — по движку (Features/DevEngines.cs): Unreal → UE4SS, RE Engine → REFramework,
        // Creation Engine (Bethesda) → своя консоль игры. У игр с античитом он не включается.
        Aaa("elden-ring", "Elden Ring", "Elden Ring", 1245620, ["ELDEN RING"], "#C9A44C", "fromsoft", P("Game", "eldenring.exe"), P("Game", "mods"), "eldenring", 4333, Roaming("EldenRing")),
        Aaa("witcher-3", "The Witcher 3: Wild Hunt", "Witcher 3", 292030, ["The Witcher 3", "The Witcher 3 Wild Hunt", "The Witcher 3 Wild Hunt GOTY"], "#B32D2D", "redengine", P("bin", "x64", "witcher3.exe"), "Mods", "witcher3", 952, Docs("The Witcher 3", "gamesaves")),
        Aaa("skyrim-se", "The Elder Scrolls V: Skyrim Special Edition", "Skyrim SE", 489830, ["Skyrim Special Edition"], "#8FA3B5", "creation", "SkyrimSE.exe", "Data", "skyrimspecialedition", 1704, Docs("My Games", "Skyrim Special Edition", "Saves")),
        Aaa("fallout-4", "Fallout 4", "Fallout 4", 377160, ["Fallout 4"], "#4FA34A", "creation", "Fallout4.exe", "Data", "fallout4", 1151, Docs("My Games", "Fallout4", "Saves")),
        Aaa("starfield", "Starfield", "Starfield", 1716740, ["Starfield"], "#D9DCE3", "creation", "Starfield.exe", "Data", "starfield", 4187, Docs("My Games", "Starfield", "Saves")),
        // 26.0: классика Bethesda — самые большие разделы Nexus после Skyrim SE и Fallout 4 (73 тыс., 42 тыс., 34 тыс., 17 тыс. модов).
        Aaa("skyrim-le", "The Elder Scrolls V: Skyrim", "Skyrim LE", 72850, ["Skyrim"], "#7E8FA1", "creation", "TESV.exe", "Data", "skyrim", 110, Docs("My Games", "Skyrim", "Saves")),
        Aaa("fallout-new-vegas", "Fallout: New Vegas", "New Vegas", 22380, ["Fallout New Vegas", "Fallout New Vegas PCR"], "#C98B2E", "creation", "FalloutNV.exe", "Data", "newvegas", 130, Docs("My Games", "FalloutNV", "Saves")),
        Aaa("oblivion", "The Elder Scrolls IV: Oblivion", "Oblivion", 22330, ["Oblivion"], "#9C8A5B", "creation", "Oblivion.exe", "Data", "oblivion", 101, Docs("My Games", "Oblivion", "Saves")),
        Aaa("fallout-3", "Fallout 3", "Fallout 3", 22370, ["Fallout 3 goty", "Fallout 3"], "#7C8F3A", "creation", "Fallout3.exe", "Data", "fallout3", 120, Docs("My Games", "Fallout3", "Saves")),
        Aaa("oblivion-remastered", "The Elder Scrolls IV: Oblivion Remastered", "Oblivion Remastered", 2623190, ["Oblivion Remastered"], "#B59A5B", "unreal", P("OblivionRemastered", "Binaries", "Win64", "OblivionRemastered-Win64-Shipping.exe"), P("OblivionRemastered", "Content", "Paks", "~mods"), "oblivionremastered", 7587),
        Aaa("baldurs-gate-3", "Baldur's Gate 3", "Baldur's Gate 3", 1086940, ["Baldurs Gate 3", "Baldur's Gate 3"], "#C0392B", "larian", P("bin", "bg3.exe"), "Data", "baldursgate3", 3474, Local("Larian Studios", "Baldur's Gate 3", "PlayerProfiles")),
        Aaa("resident-evil-4", "Resident Evil 4", "Resident Evil 4", 2050650, ["RESIDENT EVIL 4  BIOHAZARD RE4", "RESIDENT EVIL 4 BIOHAZARD RE4", "Resident Evil 4"], "#7A1F1F", "re", "re4.exe", "natives", "residentevil42023", 5195),
        Aaa("resident-evil-village", "Resident Evil Village", "RE Village", 1196590, ["Resident Evil Village BIOHAZARD VILLAGE", "Resident Evil Village"], "#5E6B73", "re", "re8.exe", "natives", "residentevilvillage", 3669),
        Aaa("resident-evil-2", "Resident Evil 2", "Resident Evil 2", 883710, ["RESIDENT EVIL 2  BIOHAZARD RE2", "RESIDENT EVIL 2 BIOHAZARD RE2", "Resident Evil 2"], "#8E2A2A", "re", "re2.exe", "natives", "residentevil22019", 2702),
        Aaa("resident-evil-3", "Resident Evil 3", "Resident Evil 3", 952060, ["RE3", "Resident Evil 3"], "#9C3A2E", "re", "re3.exe", "natives", "residentevil32020", 3191),
        Aaa("devil-may-cry-5", "Devil May Cry 5", "Devil May Cry 5", 601150, ["Devil May Cry 5"], "#B0263C", "re", "DevilMayCry5.exe", "natives", "devilmaycry5", 2751),
        Aaa("monster-hunter-wilds", "Monster Hunter Wilds", "MH Wilds", 2246340, ["MonsterHunterWilds", "Monster Hunter Wilds"], "#C47F2C", "re", "MonsterHunterWilds.exe", "natives", "monsterhunterwilds", 6993),
        Aaa("monster-hunter-rise", "Monster Hunter Rise", "MH Rise", 1446780, ["MonsterHunterRise", "Monster Hunter Rise"], "#D1A33A", "re", "MonsterHunterRise.exe", "natives", "monsterhunterrise", 4095),
        // 26.0: у World модов больше, чем у Wilds и Rise вместе (6,6 тыс.); движок MT Framework, моды — в nativePC.
        Aaa("monster-hunter-world", "Monster Hunter: World", "MH World", 582010, ["Monster Hunter World"], "#B8823A", "mt", "MonsterHunterWorld.exe", "nativePC", "monsterhunterworld", 2531),
        Aaa("dragons-dogma-2", "Dragon's Dogma 2", "Dragon's Dogma 2", 2054970, ["Dragons Dogma 2", "Dragon's Dogma 2"], "#8C6A3F", "re", "DD2.exe", "natives", "dragonsdogma2", 6234),
        Aaa("hogwarts-legacy", "Hogwarts Legacy", "Hogwarts Legacy", 990080, ["Hogwarts Legacy"], "#C9A44C", "unreal", P("Phoenix", "Binaries", "Win64", "HogwartsLegacy.exe"), P("Phoenix", "Content", "Paks", "~mods"), "hogwartslegacy", 5113, Local("Hogwarts Legacy", "Saved", "SaveGames")),
        Aaa("palworld", "Palworld", "Palworld", 1623730, ["Palworld"], "#3FA7D6", "unreal", P("Pal", "Binaries", "Win64", "Palworld-Win64-Shipping.exe"), P("Pal", "Content", "Paks", "~mods"), "palworld", 6063, Local("Pal", "Saved", "SaveGames")),
        Aaa("black-myth-wukong", "Black Myth: Wukong", "Black Myth: Wukong", 2358720, ["BlackMythWukong", "Black Myth Wukong"], "#B8862E", "unreal", P("b1", "Binaries", "Win64", "b1-Win64-Shipping.exe"), P("b1", "Content", "Paks", "~mods"), "blackmythwukong", 6713),
        Aaa("stalker-2", "S.T.A.L.K.E.R. 2: Heart of Chornobyl", "S.T.A.L.K.E.R. 2", 1643320, ["S.T.A.L.K.E.R. 2 Heart of Chornobyl", "STALKER 2"], "#6B7F3A", "unreal", P("Stalker2", "Binaries", "Win64", "Stalker2-Win64-Shipping.exe"), P("Stalker2", "Content", "Paks", "~mods"), "stalker2heartofchornobyl", 6944),
        Aaa("silent-hill-2", "Silent Hill 2", "Silent Hill 2", 2124490, ["SILENT HILL 2", "Silent Hill 2"], "#7D8B8F", "unreal", P("SHProto", "Binaries", "Win64", "SHProto-Win64-Shipping.exe"), P("SHProto", "Content", "Paks", "~mods"), "silenthill2", 6880),
        Aaa("final-fantasy-7-remake", "Final Fantasy VII Remake Intergrade", "FF VII Remake", 1462040, ["FINAL FANTASY VII REMAKE", "FINAL FANTASY VII REMAKE INTERGRADE"], "#3D7FB8", "unreal", P("End", "Binaries", "Win64", "ff7remake_.exe"), P("End", "Content", "Paks", "~mods"), "finalfantasy7remake", 4202),
        Aaa("satisfactory", "Satisfactory", "Satisfactory", 526870, ["Satisfactory"], "#F2A33A", "unreal", P("FactoryGame", "Binaries", "Win64", "FactoryGameSteam-Win64-Shipping.exe"), P("FactoryGame", "Mods"), "satisfactory", 2904, Local("FactoryGame", "Saved", "SaveGames")),
        Aaa("deep-rock-galactic", "Deep Rock Galactic", "Deep Rock Galactic", 548430, ["Deep Rock Galactic"], "#E0A82E", "unreal", P("FSD", "Binaries", "Win64", "FSD-Win64-Shipping.exe"), P("FSD", "Content", "Paks"), "deeprockgalactic", 3671),
        Aaa("ready-or-not", "Ready or Not", "Ready or Not", 1144200, ["Ready Or Not", "Ready or Not"], "#4E6A8A", "unreal", P("ReadyOrNot", "Binaries", "Win64", "ReadyOrNot-Win64-Shipping.exe"), P("ReadyOrNot", "Content", "Paks", "~mods"), "readyornot", 4205),
        Aaa("red-dead-redemption-2", "Red Dead Redemption 2", "RDR2", 1174180, ["Red Dead Redemption 2"], "#A8321E", "rage", "RDR2.exe", "lml", "reddeadredemption2", 3024),
        Aaa("kingdom-come-deliverance-2", "Kingdom Come: Deliverance II", "KCD II", 1771300, ["KingdomComeDeliverance2", "Kingdom Come Deliverance II"], "#9B7B45", "cryengine", P("Bin", "Win64MasterMasterSteamPGO", "KingdomCome.exe"), "Mods", "kingdomcomedeliverance2", 7286),
        Aaa("sekiro", "Sekiro: Shadows Die Twice", "Sekiro", 814380, ["Sekiro"], "#A83A2A", "fromsoft", "sekiro.exe", "mods", "sekiro", 2763, Roaming("Sekiro")),
        Aaa("dark-souls-3", "Dark Souls III", "Dark Souls III", 374320, ["DARK SOULS III", "Dark Souls III"], "#8A7A5A", "fromsoft", P("Game", "DarkSoulsIII.exe"), P("Game", "mods"), "darksouls3", 1392, Roaming("DarkSoulsIII")),
        Aaa("bannerlord", "Mount & Blade II: Bannerlord", "Bannerlord", 261550, ["Mount & Blade II Bannerlord"], "#A6822F", "taleworlds", P("bin", "Win64_Shipping_Client", "Bannerlord.exe"), "Modules", "mountandblade2bannerlord", 3174),
        Aaa("mass-effect-le", "Mass Effect Legendary Edition", "Mass Effect LE", 1328670, ["Mass Effect Legendary Edition"], "#4A78B0", "unreal3", P("Game", "Launcher", "MassEffectLauncher.exe"), "Game", "masseffectlegendaryedition", 3712),
        Aaa("hitman-3", "HITMAN World of Assassination", "Hitman WoA", 1659040, ["HITMAN 3", "HITMAN World of Assassination"], "#C8102E", "glacier", P("Retail", "HITMAN3.exe"), "Runtime", "hitman3", 3617),
        Aaa("no-mans-sky", "No Man's Sky", "No Man's Sky", 275850, ["No Man's Sky"], "#E35D3A", "nms", P("Binaries", "NMS.exe"), P("GAMEDATA", "MODS"), "nomanssky", 1634, Roaming("HelloGames", "NMS")),
    ];

    static string P(params string[] parts) => Path.Combine(parts);
    static Func<string, string> Docs(params string[] parts) => _ => Path.Combine([Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), .. parts]);
    static Func<string, string> Roaming(params string[] parts) => _ => Path.Combine([Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), .. parts]);
    static Func<string, string> Local(params string[] parts) => _ => Path.Combine([Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), .. parts]);

    /// <summary>Большая игра с модами на Nexus: узнаётся по своему exe (путь от папки игры), моды кладутся в mods.</summary>
    static GameDef Aaa(string id, string name, string shortName, int steam, string[] folders, string accent, string engine, string exe, string mods,
        string nexus, int nexusId, Func<string, string>? saves = null) => new()
    {
        Id = id, Name = name, ShortName = shortName, SteamAppId = steam, FolderNames = folders, Accent = accent, ArtUrl = GameDef.SteamArt(steam),
        Engine = engine, ModsFolder = mods,
        Loader = LoaderKind.None, LoaderName = engine switch { "unreal" => "UE4SS", "re" => "REFramework", "creation" => "Script Extender", _ => "—" },
        LoaderSite = $"https://www.nexusmods.com/{nexus}",
        Catalog = CatalogKind.Nexus, BrowseUrl = $"https://www.nexusmods.com/{nexus}/mods", NexusDomain = nexus, NexusGameId = nexusId,
        Sections = Section.Pick("all", "best"),
        SignatureDirs = [exe], SignatureExes = [Path.GetFileName(exe)], SignatureWith = exe,
        Executables = [exe], SavesDir = saves, ModMarker = "any",
    };

    /// <summary>Все игры: встроенные и свои (свои живут в AppState.Games). Массив общий — не менять.</summary>
    public static GameDef[] All => Current.Defs;

    public static GameDef? ById(string id) => Current.Map.GetValueOrDefault(id);

    /// <summary>Снимок списка игр и словарь по id.</summary>
    sealed record Snapshot(int Count, GameDef[] Defs, Dictionary<string, GameDef> Map);

    static Snapshot? _index;
    static readonly object IndexLock = new();

    /// <summary>
    /// 12.0: своих игр из справочника может быть сотни, а ById и All зовут на каждой перерисовке —
    /// поэтому снимок и словарь, а не новый массив и перебор на каждый вызов. Список меняют только
    /// CustomGames.Add/Remove (они зовут Invalidate); сверка числа игр — страховка на случай других правок.
    /// </summary>
    static Snapshot Current
    {
        get
        {
            var games = Core.AppState.Games;
            var index = _index;
            if (index is not null && index.Count == games.Count) return index;
            lock (IndexLock)
            {
                index = _index;
                if (index is not null && index.Count == games.Count) return index;
                // ToArray у List не проверяет версию: из фонового потока не упадёт посреди добавления.
                var defs = Array.ConvertAll(games.ToArray(), g => g.Def);
                var map = new Dictionary<string, GameDef>(defs.Length);
                foreach (var d in defs) map.TryAdd(d.Id, d);
                return _index = new Snapshot(defs.Length, defs, map);
            }
        }
    }

    /// <summary>Список игр поменялся — следующий All/ById соберёт снимок заново.</summary>
    public static void Invalidate() { lock (IndexLock) _index = null; }

    /// <summary>ById через словарь: те же игры, что и перебором, и новая своя игра видна сразу после Invalidate.</summary>
    [SelfTest]
    static string CatalogIndexById()
    {
        foreach (var g in Builtin)
            if (!ReferenceEquals(ById(g.Id), Core.AppState.Games.First(s => s.Def.Id == g.Id).Def)) throw new Exception("wrong def for " + g.Id);
        if (ById("no-such-game") is not null) throw new Exception("phantom game");
        if (!ReferenceEquals(All, All)) throw new Exception("All is rebuilt on every call");
        var probe = new Core.GameState { Def = new GameDef
        {
            Id = "custom-index-probe", Name = "Probe", ShortName = "Probe", SteamAppId = 0, FolderNames = [], Accent = "#7C5CFF",
            Loader = LoaderKind.None, LoaderName = "—", LoaderSite = "", Catalog = CatalogKind.None, BrowseUrl = "", Sections = [], Executables = ["probe.exe"], ModMarker = "any",
        } };
        Core.AppState.Games.Add(probe);
        try
        {
            if (ById("custom-index-probe") != probe.Def || !All.Contains(probe.Def)) throw new Exception("added game not indexed");
        }
        finally { Core.AppState.Games.Remove(probe); Invalidate(); }
        if (ById("custom-index-probe") is not null) throw new Exception("removed game still indexed");
        return $"{All.Length} games indexed, add/remove seen";
    }

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
