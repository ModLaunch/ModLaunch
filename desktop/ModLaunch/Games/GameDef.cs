namespace ModLaunch.Games;

public enum LoaderKind { Bepinex, Smapi, HkApi }
public enum CatalogKind { Nexus, Thunderstore, ModLinks }

/// <summary>Раздел каталога и его фильтры для каждого источника.</summary>
public sealed record Section(string Id, string[]? Thunderstore = null, string[]? ModLinks = null, string? Special = null)
{
    public static readonly Dictionary<string, Section> All = new()
    {
        ["all"] = new("all"),
        ["picks"] = new("picks", Special: "picks"),
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
    public string RecordId(string catalogId) => Catalog == CatalogKind.Nexus ? $"nexus:{NexusDomain}:{catalogId}" : catalogId;

    /// <summary>Обратно: номер в каталоге по записи (или null, если мод не из каталога этой игры).</summary>
    public string? CatalogId(string recordId)
    {
        if (Catalog != CatalogKind.Nexus) return recordId;
        var prefix = $"nexus:{NexusDomain}:";
        return recordId.StartsWith(prefix, StringComparison.Ordinal) ? recordId[prefix.Length..] : null;
    }

    public bool IsLegacy(DateTime? updated) => LegacyBefore is DateTime cut && updated is DateTime u && u < cut;
}
