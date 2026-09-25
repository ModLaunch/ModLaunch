using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using ModLaunch.Core;

namespace ModLaunch.Games;

/// <summary>Игра, найденная на диске, но не встроенная в программу.</summary>
public sealed record FoundGame(string Name, string Path, string Exe, int AppId, string Store, string Engine);

/// <summary>
/// Свои игры (кнопка «+»): поиск всех установленных игр в Steam, Epic и GOG,
/// опознание движка и подбор каталога — сообщество Thunderstore или раздел
/// Nexus с тем же именем. Хранятся в settings.json → customGames.
/// </summary>
public static partial class CustomGames
{
    static JsonArray Store
    {
        get
        {
            if (Settings.Data["customGames"] is JsonArray a) return a;
            var created = new JsonArray();
            Settings.Data["customGames"] = created;
            return created;
        }
    }

    public static IEnumerable<GameDef> Defs => Store.OfType<JsonObject>().Select(Make).OfType<GameDef>();

    /// <summary>Не игры: инструменты Steam, среды Proton и т. п.</summary>
    [GeneratedRegex(@"(Redistributable|Steamworks|Proton|Steam Linux Runtime|SteamVR|Dedicated Server|\bSDK\b|Soundtrack|Wallpaper Engine|Benchmark|Demo$)", RegexOptions.IgnoreCase)]
    private static partial Regex NotGame();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonAlnum();

    public static string Slug(string name) => NonAlnum().Replace(Translit(name).ToLowerInvariant(), "-").Trim('-');

    static readonly string[] Cyr = ["a", "b", "v", "g", "d", "e", "zh", "z", "i", "y", "k", "l", "m", "n", "o", "p", "r", "s", "t", "u", "f", "kh", "ts", "ch", "sh", "shch", "", "y", "", "e", "yu", "ya"];

    /// <summary>Кириллица → латиница (имена пакетов и папок должны быть латиницей).</summary>
    public static string Translit(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
        {
            var lower = char.ToLowerInvariant(c);
            string? r = lower == 'ё' ? "yo" : lower is >= 'а' and <= 'я' ? Cyr[lower - 'а'] : null;
            if (r is null) { sb.Append(c); continue; }
            sb.Append(char.IsUpper(c) && r.Length > 0 ? char.ToUpperInvariant(r[0]) + r[1..] : r);
        }
        return sb.ToString();
    }

    // ---------------------------------------------------------------- движок

    /// <summary>Движок по содержимому папки игры.</summary>
    public static string DetectEngine(string dir)
    {
        bool Has(string rel) => File.Exists(Path.Combine(dir, rel)) || Directory.Exists(Path.Combine(dir, rel));
        IEnumerable<string> Files(string pattern) { try { return Directory.EnumerateFiles(dir, pattern); } catch { return []; } }
        IEnumerable<string> Dirs(string pattern) { try { return Directory.EnumerateDirectories(dir, pattern); } catch { return []; } }

        if (Has("UnityPlayer.dll") || Dirs("*_Data").Any(d => Directory.Exists(Path.Combine(d, "Managed")) || File.Exists(Path.Combine(d, "globalgamemanagers"))))
            return Has("GameAssembly.dll") ? "unity-il2cpp" : "unity";
        if (Has("Engine") && Dirs("*").Any(d => Directory.Exists(Path.Combine(d, "Content", "Paks")))) return "unreal";
        if (Files("*.pck").Any()) return "godot";
        if (Has("renpy")) return "renpy";
        if (Has(Path.Combine("www", "js", "rpg_core.js")) || Has(Path.Combine("js", "rmmz_core.js")) || Has(Path.Combine("js", "rpg_core.js"))) return "rpgmaker";
        if (Has("data.win")) return "gamemaker";
        if (Has("MonoGame.Framework.dll") || Has("FNA.dll") || Has("Microsoft.Xna.Framework.dll")) return "xna";
        if (Files("*.vpk").Any() || Dirs("*").Any(d => Files("*").Any() && File.Exists(Path.Combine(d, "gameinfo.txt")))) return "source";
        return "other";
    }

    /// <summary>Куда класть моды, если своего загрузчика у движка нет.</summary>
    public static string ModsFolderFor(string dir, string engine)
    {
        switch (engine)
        {
            case "unreal":
                try
                {
                    var paks = Directory.EnumerateDirectories(dir).Select(d => Path.Combine(d, "Content", "Paks")).FirstOrDefault(Directory.Exists);
                    if (paks is not null) return Path.GetRelativePath(dir, Path.Combine(paks, "~mods"));
                }
                catch { }
                return "Mods";
            case "rpgmaker": return Directory.Exists(Path.Combine(dir, "www")) ? Path.Combine("www", "js", "plugins") : Path.Combine("js", "plugins");
            case "renpy": return "game";
            case "godot": return "mods";
            default: return "Mods";
        }
    }

    /// <summary>Главный exe игры: не установщики, не отчёты о сбоях; крупнейший из оставшихся.</summary>
    public static string? MainExe(string dir, string name)
    {
        try
        {
            var skip = new Regex(@"(unins|setup|crash|report|launcher|redist|vc_?redist|dxsetup|ue4prereq|helper|update)", RegexOptions.IgnoreCase);
            var exes = Directory.EnumerateFiles(dir, "*.exe").Where(e => !skip.IsMatch(Path.GetFileName(e))).ToList();
            if (exes.Count == 0)
            {
                // Unreal: Binaries/Win64/<Game>-Win64-Shipping.exe рядом с короткой «заглушкой» в корне.
                exes = Directory.EnumerateFiles(dir, "*-Shipping.exe", SearchOption.AllDirectories).Take(3).ToList();
            }
            var slug = Slug(name).Replace("-", "");
            return exes
                .OrderByDescending(e => Slug(Path.GetFileNameWithoutExtension(e)).Replace("-", "") == slug)
                .ThenByDescending(e => new FileInfo(e).Length)
                .Select(e => Path.GetRelativePath(dir, e))
                .FirstOrDefault();
        }
        catch { return null; }
    }

    // ---------------------------------------------------------------- поиск на диске

    /// <summary>Все игры в Steam, Epic и GOG, кроме встроенных и уже добавленных.</summary>
    public static Task<List<FoundGame>> Scan(CancellationToken ct = default) => Task.Run(() =>
    {
        var result = new List<FoundGame>();
        var known = GameCatalog.All.Select(g => g.SteamAppId).Where(id => id > 0).ToHashSet();
        var knownPaths = GameCatalog.All.Select(g => Settings.GamePath(g.Id)).OfType<string>().Select(p => p.TrimEnd('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);

        void Add(string name, string path, int appId, string store)
        {
            if (ct.IsCancellationRequested || NotGame().IsMatch(name) || !Directory.Exists(path)) return;
            if (knownPaths.Contains(path.TrimEnd('\\', '/')) || result.Any(r => r.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) return;
            if (appId > 0 && known.Contains(appId)) return;
            if (GameCatalog.All.Any(g => !g.Custom && g.MatchesSignature(path))) return;
            var exe = MainExe(path, name);
            if (exe is null) return;
            result.Add(new FoundGame(name, path, exe, appId, store, DetectEngine(path)));
        }

        foreach (var library in Locator.SteamLibraries())
        {
            IEnumerable<string> manifests;
            try { manifests = Directory.EnumerateFiles(Path.Combine(library, "steamapps"), "appmanifest_*.acf").ToList(); } catch { continue; }
            foreach (var file in manifests)
            {
                try
                {
                    var acf = Vdf.Parse(File.ReadAllText(file));
                    if (Vdf.Get(acf, "AppState", "installdir") is not string dir || dir == "") continue;
                    var name = Vdf.Get(acf, "AppState", "name") as string ?? dir;
                    int.TryParse(Vdf.Get(acf, "AppState", "appid") as string, out var appId);
                    Add(name, Path.Combine(library, "steamapps", "common", dir), appId, "steam");
                }
                catch { }
            }
        }

        if (OperatingSystem.IsWindows())
        {
            var epic = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
            if (Directory.Exists(epic))
            {
                foreach (var file in Directory.EnumerateFiles(epic, "*.item"))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(file));
                        var root = doc.RootElement;
                        var name = root.TryGetProperty("DisplayName", out var n) ? n.GetString() : null;
                        var location = root.TryGetProperty("InstallLocation", out var l) ? l.GetString() : null;
                        if (name is not null && location is not null) Add(name, location, 0, "epic");
                    }
                    catch { }
                }
            }
            foreach (var hive in new[] { @"SOFTWARE\WOW6432Node\GOG.com\Games", @"SOFTWARE\GOG.com\Games" })
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(hive);
                    if (key is null) continue;
                    foreach (var sub in key.GetSubKeyNames())
                    {
                        using var g = key.OpenSubKey(sub);
                        if (g?.GetValue("gameName") is string name && g.GetValue("path") is string p) Add(name, p, 0, "gog");
                    }
                }
                catch { }
            }
        }
        return result.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }, ct);

    /// <summary>Игра по выбранному exe (репаки, игры без лаунчера).</summary>
    public static FoundGame FromExe(string exePath)
    {
        var dir = Path.GetDirectoryName(exePath)!;
        // Unreal: exe лежит в Binaries/Win64 — папка игры тремя уровнями выше.
        var up = Path.GetFullPath(Path.Combine(dir, "..", "..", ".."));
        if (dir.EndsWith(Path.Combine("Binaries", "Win64"), StringComparison.OrdinalIgnoreCase) && Directory.Exists(Path.Combine(up, "Engine"))) dir = up;
        var name = Path.GetFileNameWithoutExtension(exePath).Replace("-Win64-Shipping", "").Replace('_', ' ');
        if (name.Length <= 3) name = Path.GetFileName(dir);
        return new FoundGame(name, dir, Path.GetRelativePath(dir, exePath), 0, "manual", DetectEngine(dir));
    }

    // ---------------------------------------------------------------- каталоги

    /// <summary>Каталоги по имени игры: сообщество Thunderstore и раздел Nexus (их адреса почти всегда — имя без пробелов).</summary>
    public static async Task<(string? Community, string? Package, string? Domain, int NexusId)> FindSources(string name, CancellationToken ct = default)
    {
        string? community = null, package = null, domain = null;
        var nexusId = 0;
        var slug = Slug(name);
        foreach (var candidate in new[] { slug, slug.Replace("-", "") }.Distinct())
        {
            try
            {
                var page = await Sources.Thunderstore.Search(candidate, new Sources.Query(), [], ct);
                if (page.Total <= 0) continue;
                community = candidate;
                var packs = await Sources.Thunderstore.Search(candidate, new Sources.Query(Text: "BepInExPack"), [], ct);
                package = packs.Mods.FirstOrDefault(m => m.Id.Contains("BepInExPack", StringComparison.OrdinalIgnoreCase) && !m.Id.Contains("IL2CPP", StringComparison.OrdinalIgnoreCase))?.Id;
                break;
            }
            catch { }
        }
        try
        {
            var d = slug.Replace("-", "");
            var q = await Sources.Nexus.Query("query($d: String!) { game(domainName: $d) { id modCount } }", new JsonObject { ["d"] = d }, ct);
            if (q["game"] is JsonObject g && g.Long("id") > 0) { domain = d; nexusId = (int)g.Long("id"); }
        }
        catch { }
        return (community, package, domain, nexusId);
    }

    // ---------------------------------------------------------------- добавить и убрать

    public static async Task<GameState> Add(FoundGame found, CancellationToken ct = default)
    {
        var (community, package, domain, nexusId) = await FindSources(found.Name, ct);
        var id = "custom-" + Slug(found.Name);
        if (id == "custom-") id = "custom-" + Guid.NewGuid().ToString("N")[..8];
        while (AppState.Games.Any(g => g.Def.Id == id)) id += "-2";
        var record = new JsonObject
        {
            ["id"] = id,
            ["name"] = found.Name,
            ["exe"] = found.Exe,
            ["appId"] = found.AppId,
            ["engine"] = found.Engine,
            ["store"] = found.Store,
            ["community"] = community,
            ["package"] = package,
            ["domain"] = domain,
            ["nexusId"] = nexusId,
            ["modsFolder"] = ModsFolderFor(found.Path, found.Engine),
            ["added"] = DateTime.UtcNow.ToString("o"),
        };
        Store.Add(record);
        Settings.SetGamePath(id, found.Path);
        var def = Make(record)!;
        var state = new GameState { Def = def };
        AppState.Games.Add(state);
        AppState.SetPath(state, found.Path);
        return state;
    }

    public static void Remove(string id)
    {
        var entry = Store.OfType<JsonObject>().FirstOrDefault(o => o.Str("id") == id);
        if (entry is not null) Store.Remove(entry);
        Settings.SetGamePath(id, null);
        AppState.Games.RemoveAll(g => g.Def.Id == id);
        AppState.Notify();
    }

    static readonly string[] Accents = ["#7C5CFF", "#2BB3C0", "#E09F3E", "#6BAA3C", "#E5484D", "#4FB0C6", "#C9A227", "#D16BA5"];

    static GameDef? Make(JsonObject o)
    {
        var id = o.Str("id");
        var name = o.Str("name");
        var exe = o.Str("exe");
        if (id is null || name is null || exe is null) return null;
        var engine = o.Str("engine") ?? "other";
        var community = o.Str("community");
        var domain = o.Str("domain");
        var bepinex = engine == "unity";
        var appId = (int)o.Long("appId");
        var catalog = community is not null ? CatalogKind.Thunderstore : domain is not null ? CatalogKind.Nexus : CatalogKind.None;
        return new GameDef
        {
            Id = id, Name = name, ShortName = name.Length > 22 ? name[..22] : name, SteamAppId = appId,
            FolderNames = [], Accent = Accents[Math.Abs(StableHash(id)) % Accents.Length],
            ArtUrl = appId > 0 ? GameDef.SteamArt(appId) : null,
            Custom = true, Engine = engine, ModsFolder = o.Str("modsFolder") ?? "Mods",
            Loader = bepinex ? LoaderKind.Bepinex : LoaderKind.None,
            LoaderName = bepinex ? "BepInEx" : "—",
            LoaderSite = bepinex ? "https://github.com/BepInEx/BepInEx" : "",
            ThunderstoreCommunity = community,
            ThunderstorePackage = bepinex ? o.Str("package") : null,
            Catalog = catalog, NexusDomain = domain, NexusGameId = (int)o.Long("nexusId"),
            BrowseUrl = community is not null ? $"https://thunderstore.io/c/{community}/" : domain is not null ? $"https://www.nexusmods.com/{domain}/mods" : "",
            Sections = community is not null ? Section.Pick("all", "best", "modpacks") : Section.Pick("all", "best"),
            SignatureExes = [exe], SignatureWith = exe,
            Executables = [exe],
            ModMarker = bepinex ? "dll" : "any",
        };
    }

    static int StableHash(string s) { unchecked { var h = 17; foreach (var c in s) h = h * 31 + c; return h; } }
}
