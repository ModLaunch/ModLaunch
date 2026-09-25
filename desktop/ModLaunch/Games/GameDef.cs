namespace ModLaunch.Games;

public enum LoaderKind { Bepinex, Smapi, HkApi, None }
public enum CatalogKind { Nexus, Thunderstore, ModLinks, None }

/// <summary>Раздел каталога и его фильтры для каждого источника.</summary>
public sealed record Section(string Id, string[]? Thunderstore = null, string[]? ModLinks = null, string? Special = null)
{
    public static readonly Dictionary<string, Section> All = new()
    {
        ["all"] = new("all"),
        ["picks"] = new("picks", Special: "picks"),
        ["best"] = new("best", Special: "best"),
        ["packs"] = new("packs", Special: "packs"),
        ["buildings"] = new("buildings", ["furniture", "building"]),
        ["vehicles"] = new("vehicles", ["vehicles", "transportation"]),
        ["items"] = new("items", ["items", "equipment", "gear", "crafting", "valuables", "upgrades", "weapons", "drones"]),
        ["gameplay"] = new("gameplay", ["tweaks-and-quality-of-life", "performance", "bug-fixes", "tweaks", "utility", "gamemodes", "artifacts", "quality-of-life"], ["Gameplay"]),
        ["content"] = new("content", ["moons", "interiors", "monsters", "weather", "hazards", "enemies", "npcs", "world-generation", "player-characters", "maps", "skills", "levels"], ["Expansion", "Boss"]),
        ["visuals"] = new("visuals", ["asset-replacements"]),
        ["cosmetics"] = new("cosmetics", ["cosmetics", "suits", "emotes", "skins"], ["Cosmetic"]),
        ["audio"] = new("audio", ["audio", "boombox"]),
        ["ui"] = new("ui"),
        ["tools"] = new("tools", ["libraries", "tools"], ["Library", "Utility"]),
        ["modpacks"] = new("modpacks", ["modpacks"]),
    };

    public static Section[] Pick(params string[] ids) => ids.Select(id => All[id]).ToArray();
}

public sealed record Kit(string Id, string[] Mods);

/// <summary>
/// Описание игры: всё, что программе нужно знать, чтобы найти игру, поставить
/// загрузчик, показать каталог и разложить моды. Поведение одинаковое для всех
/// игр и живёт в ядре — здесь только данные.
/// </summary>
public sealed class GameDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string ShortName { get; init; }
    public required int SteamAppId { get; init; }
    public required string[] FolderNames { get; init; }
    public required string Accent { get; init; }
    public string? Art { get; init; }
    /// <summary>Обложка из сети (у своих и новых игр — картинка из Steam).</summary>
    public string? ArtUrl { get; init; }
    /// <summary>Игра добавлена человеком кнопкой «+», а не встроена в программу.</summary>
    public bool Custom { get; init; }
    /// <summary>Движок своей игры: unity, unity-il2cpp, unreal, godot, …</summary>
    public string? Engine { get; init; }
    /// <summary>Папка модов для игр без загрузчика (относительно папки игры).</summary>
    public string ModsFolder { get; init; } = "Mods";

    public required LoaderKind Loader { get; init; }
    public required string LoaderName { get; init; }
    public required string LoaderSite { get; init; }
    public string? ThunderstorePackage { get; init; }
    /// <summary>Сообщество Thunderstore, откуда берётся BepInExPack (и каталог, если он там).</summary>
    public string? ThunderstoreCommunity { get; init; }

    public required CatalogKind Catalog { get; init; }
    public string? NexusDomain { get; init; }
    public int NexusGameId { get; init; }
    public string[] NexusHide { get; init; } = [];
    public DateTime? LegacyBefore { get; init; }
    public required string BrowseUrl { get; init; }
    public Dictionary<string, string[]> NexusCategories { get; init; } = new();

    public required Section[] Sections { get; init; }
    public string[] Picks { get; init; } = [];
    public Kit[] Kits { get; init; } = [];

    /// <summary>Признак папки игры: папка данных Unity или файлы рядом с exe.</summary>
    public string[] SignatureDirs { get; init; } = [];
    public string[] SignatureExes { get; init; } = [];
    /// <summary>Что должно лежать рядом с exe из SignatureExes (UnityPlayer.dll, Content…).</summary>
    public string SignatureWith { get; init; } = "UnityPlayer.dll";
    public string? GogId { get; init; }

    /// <summary>Какие exe запускать, по порядку.</summary>
    public required string[] Executables { get; init; }
    public Func<string, string>? SavesDir { get; init; }
    public required string ModMarker { get; init; } // "dll" или "manifest"
    /// <summary>Как ReShade встаёт в игру: dx11 (dxgi.dll) или opengl (opengl32.dll).</summary>
    public string ReShadeApi { get; init; } = "dx11";

    public string ModsDir(string gamePath) => Loader switch
    {
        LoaderKind.Smapi => Path.Combine(gamePath, "Mods"),
        LoaderKind.HkApi => Path.Combine(Loaders.HkApi.ManagedDir(gamePath), "Mods"),
        LoaderKind.None => Path.Combine(gamePath, ModsFolder),
        _ => Path.Combine(gamePath, "BepInEx", "plugins"),
    };

    public string LaunchExe(string gamePath)
    {
        foreach (var exe in Executables)
        {
            var p = Path.Combine(gamePath, exe);
            if (File.Exists(p)) return p;
        }
        return Path.Combine(gamePath, Executables[0]);
    }

    public bool MatchesSignature(string dir)
    {
        bool Has(string name) => File.Exists(Path.Combine(dir, name)) || Directory.Exists(Path.Combine(dir, name));
        if (SignatureDirs.Any(Has)) return true;
        if (SignatureExes.Any(Has) && Has(SignatureWith)) return true;
        if (GogId is not null)
        {
            try { if (Directory.EnumerateFiles(dir, $"goggame-{GogId}.*").Any()) return true; } catch { }
        }
        return false;
    }

    /// <summary>Номер мода в списке установленных: у Nexus с приставкой, как в версии 3.x.</summary>
    public string RecordId(string catalogId, string? source = null) =>
        (source ?? PrimarySource) == "nexus" ? $"nexus:{NexusDomain}:{catalogId}" : catalogId;

    /// <summary>Обратно: номер в каталоге по записи (или null, если это мод Nexus чужой игры).</summary>
    public string? CatalogId(string recordId)
    {
        var prefix = $"nexus:{NexusDomain}:";
        if (recordId.StartsWith(prefix, StringComparison.Ordinal)) return recordId[prefix.Length..];
        return recordId.StartsWith("nexus:", StringComparison.Ordinal) ? null : recordId;
    }

    /// <summary>Источник по записи: nexus — по приставке, иначе основной (или указанный в записи).</summary>
    public string SourceOf(string recordId, string? recorded = null) =>
        recordId.StartsWith("nexus:", StringComparison.Ordinal) ? "nexus" : recorded is "thunderstore" or "modlinks" ? recorded : PrimarySource == "nexus" ? "thunderstore" : PrimarySource;

    public string PrimarySource => Catalog switch { CatalogKind.Nexus => "nexus", CatalogKind.Thunderstore => "thunderstore", CatalogKind.ModLinks => "modlinks", _ => "none" };

    /// <summary>Есть ли у игры хоть один каталог модов.</summary>
    public bool HasCatalog => Sources.Length > 0;

    public static string SteamArt(int appId) => $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/header.jpg";

    /// <summary>
    /// Все каталоги игры: основной и дополнительные (как в Vortex — моды с разных
    /// сайтов в одном месте). Nexus — если у игры есть раздел на Nexus,
    /// Thunderstore — если там есть её сообщество.
    /// </summary>
    public string[] Sources
    {
        get
        {
            var list = new List<string>();
            if (PrimarySource != "none") list.Add(PrimarySource);
            if (NexusDomain is not null && !list.Contains("nexus")) list.Add("nexus");
            if (ThunderstoreCommunity is not null && ExtraThunderstore && !list.Contains("thunderstore")) list.Add("thunderstore");
            return list.ToArray();
        }
    }

    /// <summary>Показывать ли Thunderstore вторым каталогом (у Subnautica там сообщество загрузчика).</summary>
    public bool ExtraThunderstore { get; init; }

    public bool IsLegacy(DateTime? updated) => LegacyBefore is DateTime cut && updated is DateTime u && u < cut;
}
