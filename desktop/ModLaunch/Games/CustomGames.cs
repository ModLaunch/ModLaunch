using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using ModLaunch.Core;

namespace ModLaunch.Games;

/// <summary>Игра, найденная на диске, но не встроенная в программу. Dir — её запись в справочнике игр (12.0), если есть.</summary>
public sealed record FoundGame(string Name, string Path, string Exe, int AppId, string Store, string Engine, DirGame? Dir = null);

/// <summary>
/// 12.0: сопоставление установленных игр со справочником — по номеру Steam, по имени (GameDirectory.Norm)
/// и по папке установки. Три словаря на весь поиск: тысяча игр справочника не замедляет обход.
/// </summary>
public sealed class DirMatcher
{
    readonly Dictionary<int, DirGame> _steam = new();
    readonly Dictionary<string, DirGame> _strict = new(StringComparer.Ordinal);
    readonly Dictionary<string, DirGame> _name = new(StringComparer.Ordinal);
    readonly Dictionary<string, DirGame> _folder = new(StringComparer.OrdinalIgnoreCase);
    // Записи по первому и по последнему слову имени — для «имя из справочника стоит в начале или в конце».
    readonly Dictionary<string, List<(string[] Words, DirGame Game)>> _first = new(StringComparer.Ordinal), _last = new(StringComparer.Ordinal);

    public DirMatcher(IEnumerable<DirGame> games)
    {
        // При совпадении остаётся первая запись: справочник упорядочен, дубли — его забота.
        foreach (var g in games)
        {
            if (g.SteamAppId > 0) _steam.TryAdd(g.SteamAppId, g);
            if (GameDirectory.StrictName(g.Name) is { Length: > 0 } s) _strict.TryAdd(s, g);
            if (GameDirectory.Norm(g.Name) is { Length: > 0 } n) _name.TryAdd(n, g);
            if (!string.IsNullOrWhiteSpace(g.Folder)) _folder.TryAdd(g.Folder.Trim(), g);
            var w = Words(g.Name);
            // Одно короткое слово («Doom», «Rust») внутри чужого имени — скорее совпадение, чем та же игра.
            if (w.Length >= 2 || w is [{ Length: >= 6 }])
            {
                Bucket(_first, w[0]).Add((w, g));
                Bucket(_last, w[^1]).Add((w, g));
            }
        }
    }

    static List<(string[], DirGame)> Bucket(Dictionary<string, List<(string[] Words, DirGame Game)>> map, string key) =>
        map.TryGetValue(key, out var list) ? list : map[key] = [];

    static readonly string[] Roman = ["", "i", "ii", "iii", "iv", "v", "vi", "vii", "viii", "ix", "x"];

    /// <summary>Слова имени; римские цифры — арабскими (DARK SOULS™ III ↔ Dark Souls 3).</summary>
    static string[] Words(string name) =>
        Array.ConvertAll(GameDirectory.NameWords(name), w => Array.IndexOf(Roman, w) is var r and > 1 ? r.ToString() : w);

    /// <summary>
    /// Сначала номер Steam, потом имя из манифеста (строго, затем без слов издания), потом папка установки,
    /// и в конце — имя из справочника в начале или в конце имени из Steam (The Elder Scrolls V: Skyrim Special Edition
    /// ↔ Skyrim Special Edition). Имя и папка не спорят с номером: у записи с другим номером Steam это другая игра
    /// (демо, плейтест, VR-версия).
    /// </summary>
    public DirGame? Match(int appId, string name, string? folder)
    {
        if (appId > 0 && _steam.TryGetValue(appId, out var bySteam)) return bySteam;
        bool Fits(DirGame g) => appId <= 0 || g.SteamAppId <= 0 || g.SteamAppId == appId;
        if (_strict.TryGetValue(GameDirectory.StrictName(name), out var byStrict) && Fits(byStrict)) return byStrict;
        if (_name.TryGetValue(GameDirectory.Norm(name), out var byName) && Fits(byName)) return byName;
        if (!string.IsNullOrWhiteSpace(folder) && _folder.TryGetValue(folder.Trim(), out var byFolder) && Fits(byFolder)) return byFolder;
        return Affix(Words(name), Fits);
    }

    /// <summary>Самое длинное имя из справочника в начале или в конце; два разных одной длины — не угадываем.</summary>
    DirGame? Affix(string[] words, Func<DirGame, bool> fits)
    {
        if (words.Length < 2) return null;
        DirGame? best = null;
        int bestLength = 0;
        var tie = false;
        void Consider(string[] w, DirGame g)
        {
            if (!fits(g)) return;
            var length = w.Sum(x => x.Length + 1);
            if (length > bestLength) { best = g; bestLength = length; tie = false; }
            else if (length == bestLength && !ReferenceEquals(best, g)) tie = true;
        }
        if (_first.TryGetValue(words[0], out var heads))
            foreach (var (w, g) in heads)
                // Dark Souls 3 — не Dark Souls: число сразу после имени — это другая часть серии.
                if (w.Length < words.Length && words.AsSpan(0, w.Length).SequenceEqual(w) && !char.IsDigit(words[w.Length][0])) Consider(w, g);
        if (_last.TryGetValue(words[^1], out var tails))
            foreach (var (w, g) in tails)
                if (w.Length < words.Length && words.AsSpan(words.Length - w.Length).SequenceEqual(w)) Consider(w, g);
        return tie ? null : best;
    }
}

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

    /// <summary>Не игра (инструмент Steam, Proton, саундтрек, демо) — по имени из манифеста.</summary>
    public static bool IsTool(string name) => NotGame().IsMatch(name);

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

    /// <summary>
    /// Все игры в Steam, Epic и GOG, кроме встроенных и уже добавленных. 12.0: манифесты Steam читаются
    /// один раз, и каждая игра сразу сопоставляется со справочником (FoundGame.Dir).
    /// </summary>
    public static Task<List<FoundGame>> Scan(CancellationToken ct = default) => Task.Run(() =>
        Collect(Locator.SteamApps(), OtherStores(), Matcher(), ct), ct);

    sealed record MatcherFor(IReadOnlyList<DirGame> Source, DirMatcher Matcher);
    static MatcherFor? _matcher;

    /// <summary>Сопоставитель для текущего справочника: собирается заново, только когда справочник обновился.</summary>
    static DirMatcher Matcher()
    {
        var all = GameDirectory.All;
        var m = _matcher;
        if (m is null || !ReferenceEquals(m.Source, all)) _matcher = m = new MatcherFor(all, new DirMatcher(all));
        return m.Matcher;
    }

    /// <summary>Установленные игры → найденные: без встроенных и уже добавленных, каждая — с записью справочника, если она есть.</summary>
    internal static List<FoundGame> Collect(IEnumerable<SteamApp> steam, IEnumerable<(string Name, string Path, string Store)> other, DirMatcher dir, CancellationToken ct = default)
    {
        var result = new List<FoundGame>();
        var games = GameCatalog.All;
        var known = games.Select(g => g.SteamAppId).Where(id => id > 0).ToHashSet();
        var knownPaths = games.Select(g => Settings.GamePath(g.Id)).OfType<string>().Select(p => p.TrimEnd('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var addedKeys = Store.OfType<JsonObject>().Select(o => o.Str("dir")).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var builtin = games.Where(g => !g.Custom).ToList();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string name, string path, int appId, string store, string? installDir)
        {
            if (ct.IsCancellationRequested || NotGame().IsMatch(name) || !Directory.Exists(path)) return;
            var key = path.TrimEnd('\\', '/');
            if (knownPaths.Contains(key) || seen.Contains(key)) return;
            if (appId > 0 && known.Contains(appId)) return;
            var match = dir.Match(appId, name, installDir ?? Path.GetFileName(key));
            // Встроенная игра под другим номером или игра справочника, уже добавленная из другого магазина, — второй раз не предлагаем.
            if (match is not null && (GameDirectory.IsBuiltin(match) || addedKeys.Contains(match.Key) || (match.SteamAppId > 0 && known.Contains(match.SteamAppId)))) return;
            if (builtin.Any(g => g.MatchesSignature(path))) return;
            var exe = ExeFor(path, name, match);
            if (exe is null) return;
            seen.Add(key);
            result.Add(new FoundGame(name, path, exe, appId, store, DetectEngine(path), match));
        }

        foreach (var app in steam) Add(app.Name, app.Path, app.AppId, "steam", app.InstallDir);
        foreach (var (name, path, store) in other) Add(name, path, 0, store, null);
        return result.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Игры Epic (манифесты лаунчера) и GOG (реестр) — только Windows.</summary>
    static IEnumerable<(string Name, string Path, string Store)> OtherStores()
    {
        var list = new List<(string, string, string)>();
        if (!OperatingSystem.IsWindows()) return list;
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
                    if (name is not null && location is not null) list.Add((name, location, "epic"));
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
                    if (g?.GetValue("gameName") is string name && g.GetValue("path") is string p) list.Add((name, p, "gog"));
                }
            }
            catch { }
        }
        return list;
    }

    /// <summary>Exe игры: первый из справочника, что есть в папке, иначе — догадка по файлам (MainExe).</summary>
    static string? ExeFor(string dir, string name, DirGame? match)
    {
        foreach (var exe in match?.Exes ?? [])
        {
            var rel = exe.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            try { if (File.Exists(Path.Combine(dir, rel))) return rel; } catch { }
        }
        return MainExe(dir, name);
    }

    /// <summary>Игра справочника в выбранной папке (кнопка «Указать папку»). null — в папке нет exe.</summary>
    public static FoundGame? FromFolder(DirGame d, string dir)
    {
        if (!Directory.Exists(dir) || ExeFor(dir, d.Name, d) is not string exe) return null;
        return new FoundGame(d.Name, dir, exe, d.SteamAppId, "manual", DetectEngine(dir), d);
    }

    /// <summary>Игра по выбранному exe (репаки, игры без лаунчера).</summary>
    public static FoundGame FromExe(string exePath)
    {
        var dir = Path.GetDirectoryName(exePath)!;
        // Unreal: exe лежит в Binaries/Win64 — папка игры тремя уровнями выше.
        var up = Path.GetFullPath(Path.Combine(dir, "..", "..", ".."));
        if (dir.EndsWith(Path.Combine("Binaries", "Win64"), StringComparison.OrdinalIgnoreCase) && Directory.Exists(Path.Combine(up, "Engine"))) dir = up;
        var name = Path.GetFileNameWithoutExtension(exePath).Replace("-Win64-Shipping", "").Replace('_', ' ');
        if (name.Length <= 3) name = Path.GetFileName(dir);
        // 12.0: репак тоже может быть в справочнике — по имени exe или папки; встроенную игру справочник не подменяет.
        var match = Matcher().Match(0, name, Path.GetFileName(dir.TrimEnd('\\', '/')));
        if (match is not null && GameDirectory.IsBuiltin(match)) match = null;
        return new FoundGame(match?.Name ?? name, dir, Path.GetRelativePath(dir, exePath), match?.SteamAppId ?? 0, "manual", DetectEngine(dir), match);
    }

    // ---------------------------------------------------------------- справочник в интерфейсе

    sealed record CountFor(IReadOnlyList<DirGame> Source, int Steam, int Count);
    static CountFor? _supported;

    /// <summary>
    /// Сколько игр поддерживается: встроенные, справочник без повторов встроенных и (15.0) все игры Steam,
    /// которых нет ни там, ни там.
    /// </summary>
    public static int SupportedCount
    {
        get
        {
            var all = GameDirectory.All;
            var c = _supported;
            if (c is null || !ReferenceEquals(c.Source, all) || c.Steam != SteamCatalog.Count)
            {
                var known = all.Select(g => g.SteamAppId).Concat(GameCatalog.Builtin.Select(g => g.SteamAppId)).Where(id => id > 0);
                _supported = c = new CountFor(all, SteamCatalog.Count, all.Count(g => !GameDirectory.IsBuiltin(g)) + GameCatalog.Builtin.Length + SteamCatalog.ExtraOver(known));
            }
            return c.Count;
        }
    }

    /// <summary>Сколько игр справочника ещё не в программе: «+ N поддерживаемых игр» в библиотеке.</summary>
    public static int MoreSupported => Math.Max(0, SupportedCount - GameCatalog.Builtin.Length - Store.OfType<JsonObject>().Count(o => o.Str("dir") is not null));

    /// <summary>
    /// Какие игры справочника уже есть в программе (встроенные и добавленные): по ключу справочника,
    /// номеру Steam, сообществу Thunderstore и разделу Nexus. Словари собираются один раз на перерисовку.
    /// </summary>
    public static Func<DirGame, GameState?> ExistingLookup()
    {
        var bySteam = new Dictionary<int, GameState>();
        var byKey = new Dictionary<string, GameState>(StringComparer.OrdinalIgnoreCase);
        var dirOf = new Dictionary<string, string>();
        foreach (var o in Store.OfType<JsonObject>())
            if (o.Str("id") is { } id && o.Str("dir") is { } key) dirOf.TryAdd(id, key);
        foreach (var g in AppState.Games)
        {
            if (g.Def.SteamAppId > 0) bySteam.TryAdd(g.Def.SteamAppId, g);
            if (dirOf.TryGetValue(g.Def.Id, out var key)) byKey.TryAdd("dir:" + key, g);
            if (g.Def.ThunderstoreCommunity is { } community) byKey.TryAdd("ts:" + community, g);
            if (g.Def.NexusDomain is { } domain) byKey.TryAdd("nx:" + domain, g);
        }
        return d => (d.SteamAppId > 0 ? bySteam.GetValueOrDefault(d.SteamAppId) : null)
            ?? byKey.GetValueOrDefault("dir:" + d.Key)
            ?? (d.Community is { } c ? byKey.GetValueOrDefault("ts:" + c) : null)
            ?? (d.NexusDomain is { } n ? byKey.GetValueOrDefault("nx:" + n) : null);
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
        var d = slug.Replace("-", "");
        if (await NexusId(d, ct) is var id and > 0) { domain = d; nexusId = id; }
        return (community, package, domain, nexusId);
    }

    /// <summary>Номер игры на Nexus по её разделу (нужен для страниц модов). 0 — раздела нет или сети нет.</summary>
    static async Task<int> NexusId(string domain, CancellationToken ct)
    {
        try
        {
            // Без сети добавление не должно висеть: номер не так важен, как сама игра в списке.
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(TimeSpan.FromSeconds(8));
            var q = await Sources.Nexus.Query("query($d: String!) { game(domainName: $d) { id modCount } }", new JsonObject { ["d"] = domain }, limit.Token);
            return q["game"] is JsonObject g ? (int)g.Long("id") : 0;
        }
        catch { return 0; }
    }

    // ---------------------------------------------------------------- добавить и убрать

    public static async Task<GameState> Add(FoundGame found, CancellationToken ct = default)
    {
        // 12.0: игра из справочника — каталоги и загрузчик уже известны, угадывать по имени не нужно.
        // По сети — только номер игры на Nexus, если его нет в справочнике (без него не открываются страницы модов).
        var sources = found.Dir is { } d
            ? (d.Community, d.Pack, d.NexusDomain, d.NexusId > 0 ? d.NexusId : d.NexusDomain is { } domain ? await NexusId(domain, ct) : 0)
            : await FindSources(found.Name, ct);
        var id = "custom-" + Slug(found.Name);
        if (id == "custom-") id = "custom-" + Guid.NewGuid().ToString("N")[..8];
        while (GameCatalog.ById(id) is not null) id += "-2";
        var record = Record(id, found, sources);
        Store.Add(record);
        Settings.SetGamePath(id, found.Path);
        var def = Make(record)!;
        var state = new GameState { Def = def };
        AppState.Games.Add(state);
        GameCatalog.Invalidate();
        AppState.SetPath(state, found.Path);
        return state;
    }

    /// <summary>
    /// Запись своей игры для settings.json → customGames. Поля справочника (dir, loader, folder) — новые в 12.0:
    /// у старых записей их нет, и Make ведёт себя по-старому.
    /// </summary>
    internal static JsonObject Record(string id, FoundGame found, (string? Community, string? Package, string? Domain, int NexusId) sources)
    {
        var d = found.Dir;
        var record = new JsonObject
        {
            ["id"] = id,
            ["name"] = found.Name,
            ["exe"] = found.Exe,
            // Номер Steam из справочника — и у игры из Epic или GOG: по нему обложки и картинки Steam.
            ["appId"] = found.AppId > 0 ? found.AppId : d?.SteamAppId ?? 0,
            ["engine"] = found.Engine,
            ["store"] = found.Store,
            ["community"] = sources.Community,
            ["package"] = sources.Package,
            ["domain"] = sources.Domain,
            ["nexusId"] = sources.NexusId,
            ["modsFolder"] = d?.ModsRoute is { Length: > 0 } route
                ? route.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar).Trim(Path.DirectorySeparatorChar)
                : ModsFolderFor(found.Path, found.Engine),
            ["added"] = DateTime.UtcNow.ToString("o"),
        };
        if (d is not null)
        {
            record["dir"] = d.Key;
            record["loader"] = d.Loader;
            if (!string.IsNullOrWhiteSpace(d.Folder)) record["folder"] = d.Folder;
        }
        return record;
    }

    public static void Remove(string id)
    {
        var entry = Store.OfType<JsonObject>().FirstOrDefault(o => o.Str("id") == id);
        if (entry is not null) Store.Remove(entry);
        Settings.SetGamePath(id, null);
        AppState.Games.RemoveAll(g => g.Def.Id == id);
        GameCatalog.Invalidate();
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
        // 12.0: загрузчик из справочника (BepInEx — и для IL2CPP, своим набором). У записей до 12.0
        // поля нет — тогда как раньше, по движку. MelonLoader и «без загрузчика» — моды в папку.
        var bepinex = o.ContainsKey("loader") ? o.Str("loader") is "bepinex" or "bepinex-il2cpp" : engine == "unity";
        var package = bepinex ? o.Str("package") : null;
        var appId = (int)o.Long("appId");
        var catalog = community is not null ? CatalogKind.Thunderstore : domain is not null ? CatalogKind.Nexus : CatalogKind.None;
        var packSite = community is not null && package?.Split('-', 2) is [var team, var pack] ? $"https://thunderstore.io/c/{community}/p/{team}/{pack}/" : null;
        return new GameDef
        {
            Id = id, Name = name, ShortName = name.Length > 22 ? name[..22] : name, SteamAppId = appId,
            FolderNames = o.Str("folder") is { } folder ? [folder] : [], Accent = Accents[Math.Abs(StableHash(id)) % Accents.Length],
            ArtUrl = appId > 0 ? GameDef.SteamArt(appId) : null,
            Custom = true, Engine = engine, ModsFolder = o.Str("modsFolder") ?? "Mods",
            Loader = bepinex ? LoaderKind.Bepinex : LoaderKind.None,
            LoaderName = bepinex ? "BepInEx" : "—",
            LoaderSite = bepinex ? packSite ?? "https://github.com/BepInEx/BepInEx" : "",
            ThunderstoreCommunity = community,
            ThunderstorePackage = package,
            Catalog = catalog, NexusDomain = domain, NexusGameId = (int)o.Long("nexusId"),
            BrowseUrl = community is not null ? $"https://thunderstore.io/c/{community}/" : domain is not null ? $"https://www.nexusmods.com/{domain}/mods" : "",
            Sections = community is not null ? Section.Pick("all", "best", "modpacks") : Section.Pick("all", "best"),
            SignatureExes = [exe], SignatureWith = exe,
            Executables = [exe],
            ModMarker = bepinex ? "dll" : "any",
        };
    }

    static int StableHash(string s) { unchecked { var h = 17; foreach (var c in s) h = h * 31 + c; return h; } }

    // ---------------------------------------------------------------- проверки

    /// <summary>Пробный справочник: номер Steam, имя без номера, папка без номера, встроенная игра под чужим номером.</summary>
    static readonly DirGame[] ProbeDir =
    [
        new("probe-steam", "Deep Rock Galactic", 548430, "deep-rock-galactic", null, "bepinex", "BepInEx-BepInExPack", "Deep Rock Galactic", ["FSD.exe"], null, 1400),
        new("probe-name", "Schedule I", 0, "schedule-i", null, "melonloader", null, null, ["Schedule I.exe"], null, 2100),
        new("probe-folder", "Inscryption", 0, "inscryption", null, "bepinex", "BepInEx-BepInExPack_Inscryption", "InscryptionGame", [], null, 900),
        new("probe-builtin", "R.E.P.O.", 0, "repo", "repo", "bepinex", "BepInEx-BepInExPack", "REPO", ["REPO.exe"], null, 9000),
        new("probe-other-id", "Same Name Game", 777001, null, "samename", null, null, null, [], null, 10),
        new("probe-affix", "Skyrim Special Edition", 0, null, "skyrimspecialedition", null, null, null, [], null, 140000, 1704),
        new("probe-short", "Skyrim", 0, null, "skyrim", null, null, null, [], null, 70000, 110),
        new("probe-series", "Dark Souls", 0, null, "darksouls", null, null, null, [], null, 3000, 160),
        new("probe-roman", "Dark Souls 2", 0, null, "darksouls2", null, null, null, [], null, 2000, 161),
    ];

    /// <summary>
    /// Поддельная библиотека Steam: манифесты читаются одним проходом, игры сопоставляются по номеру,
    /// по имени и по папке; встроенная игра и чужой номер с тем же именем не сопоставляются.
    /// </summary>
    [SelfTest]
    static string DirectoryMatchesInstalledGames()
    {
        var lib = Path.Combine(Path.GetTempPath(), "modlaunch-dirmatch-" + Environment.ProcessId);
        try
        {
            var apps = Path.Combine(lib, "steamapps");
            void Game(int appId, string name, string installDir, string exe, bool manifestOnly = false)
            {
                Directory.CreateDirectory(apps);
                File.WriteAllText(Path.Combine(apps, $"appmanifest_{appId}.acf"),
                    $"\"AppState\"\n{{\n\t\"appid\"\t\t\"{appId}\"\n\t\"name\"\t\t\"{name}\"\n\t\"installdir\"\t\t\"{installDir}\"\n}}\n");
                if (manifestOnly) return;
                var dir = Path.Combine(apps, "common", installDir);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, exe), "MZ");
            }
            Game(548430, "Deep Rock Galactic", "Deep Rock Galactic", "FSD.exe");      // номер Steam
            Game(990001, "SCHEDULE I", "Schedule I", "Schedule I.exe");               // имя (регистр и пробелы не важны)
            Game(990002, "Inscryption Kaycee Build", "InscryptionGame", "Inscryption.exe"); // папка установки
            Game(990003, "R.E.P.O.", "REPO copy", "REPO.exe");                         // встроенная — не предлагаем
            Game(990004, "Same Name Game", "SameName", "Same.exe");                    // имя совпало, номер другой
            Game(990005, "Totally Unknown", "Unknown", "Unknown.exe");                 // не из справочника
            Game(990006, "Gone Game", "Gone", "Gone.exe", manifestOnly: true);         // манифест без папки
            Game(892970, "Valheim", "Valheim", "valheim.exe");                         // встроенная по номеру
            Game(489830, "The Elder Scrolls V: Skyrim Special Edition", "Skyrim Special Edition", "SkyrimSE.exe"); // имя в конце
            Game(374320, "DARK SOULS™ III", "DARK SOULS III", "DarkSoulsIII.exe");     // другая часть серии — не Dark Souls
            Game(335300, "DARK SOULS™ II: Scholar of the First Sin", "Dark Souls II Scholar", "DarkSoulsII.exe"); // римская цифра = арабская

            var steam = Locator.SteamApps([lib]);
            if (steam.Count != 11) throw new Exception($"{steam.Count} manifests read");
            var found = Collect(steam, [], new DirMatcher(ProbeDir));
            string? Key(string name) => found.FirstOrDefault(f => f.Name == name)?.Dir?.Key;
            if (Key("Deep Rock Galactic") != "probe-steam") throw new Exception("steam id: " + Key("Deep Rock Galactic"));
            if (Key("SCHEDULE I") != "probe-name") throw new Exception("name: " + Key("SCHEDULE I"));
            if (Key("Inscryption Kaycee Build") != "probe-folder") throw new Exception("folder: " + Key("Inscryption Kaycee Build"));
            if (found.Any(f => f.Name is "R.E.P.O." or "Valheim" or "Gone Game")) throw new Exception("builtin or missing game offered: " + string.Join(", ", found.Select(f => f.Name)));
            if (found.FirstOrDefault(f => f.Name == "Same Name Game") is not { Dir: null }) throw new Exception("name matched across different steam ids");
            if (found.FirstOrDefault(f => f.Name == "Totally Unknown") is not { Dir: null }) throw new Exception("unknown game lost or matched");
            if (found.First(f => f.Name == "Deep Rock Galactic").Exe != "FSD.exe") throw new Exception("exe not taken from directory");
            if (Key("The Elder Scrolls V: Skyrim Special Edition") != "probe-affix") throw new Exception("suffix: " + Key("The Elder Scrolls V: Skyrim Special Edition"));
            if (Key("DARK SOULS™ III") is not null) throw new Exception("sequel matched the first game: " + Key("DARK SOULS™ III"));
            if (Key("DARK SOULS™ II: Scholar of the First Sin") != "probe-roman") throw new Exception("roman numeral: " + Key("DARK SOULS™ II: Scholar of the First Sin"));
            return $"{steam.Count} manifests in one pass: steam id, name and folder matched; builtin and missing skipped; {found.Count} offered";
        }
        finally { try { Directory.Delete(lib, true); } catch { } }
    }

    /// <summary>Запись из справочника → игра: BepInEx со своим набором, без загрузчика — папка модов из справочника.</summary>
    [SelfTest]
    static string DirectoryGameMakesDef()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "modlaunch-dirmake-" + Environment.ProcessId);
        GameDef Def(DirGame d, string engine) =>
            Make(Record("custom-probe", new FoundGame(d.Name, tmp, d.Exes.FirstOrDefault() ?? "game.exe", 0, "steam", engine, d), (d.Community, d.Pack, d.NexusDomain, 0)))!;

        var il2cpp = Def(new("probe-il2cpp", "V Rising", 1604030, "v-rising", "vrising", "bepinex-il2cpp", "BepInEx-BepInExPack_V_Rising", "VRising", ["VRising.exe"], null, 600), "unity-il2cpp");
        if (il2cpp.Loader != LoaderKind.Bepinex || il2cpp.ThunderstorePackage != "BepInEx-BepInExPack_V_Rising" || il2cpp.ThunderstoreCommunity != "v-rising")
            throw new Exception($"il2cpp: {il2cpp.Loader} {il2cpp.ThunderstorePackage}");
        if (il2cpp.Catalog != CatalogKind.Thunderstore || !il2cpp.Sources.Contains("nexus")) throw new Exception("il2cpp sources: " + string.Join(",", il2cpp.Sources));
        if (il2cpp.SteamAppId != 1604030 || il2cpp.ArtUrl != GameDef.SteamArt(1604030)) throw new Exception("art from steam id lost");
        if (il2cpp.FolderNames is not ["VRising"]) throw new Exception("folder lost");

        var cp = Def(new("probe-cp", "Cyberpunk 2077", 1091500, null, "cyberpunk2077", null, null, "Cyberpunk 2077", ["bin/x64/Cyberpunk2077.exe"], "archive/pc/mod", 16000), "other");
        if (cp.Loader != LoaderKind.None || cp.Catalog != CatalogKind.Nexus || cp.NexusDomain != "cyberpunk2077") throw new Exception($"nexus game: {cp.Loader} {cp.Catalog}");
        if (cp.ModsFolder != Path.Combine("archive", "pc", "mod")) throw new Exception("mods route: " + cp.ModsFolder);

        // Unity-игра, у которой в справочнике MelonLoader: BepInEx не ставим, хоть движок и Unity.
        var melon = Def(new("probe-melon", "Schedule I", 3164500, "schedule-i", null, "melonloader", null, null, ["Schedule I.exe"], null, 2100), "unity");
        if (melon.Loader != LoaderKind.None || melon.ThunderstorePackage is not null) throw new Exception("melonloader game got BepInEx");

        // Запись до 12.0 (без поля loader): Unity — по-прежнему BepInEx.
        var old = Make(new JsonObject { ["id"] = "custom-old", ["name"] = "Old", ["exe"] = "Old.exe", ["engine"] = "unity", ["community"] = "old", ["package"] = "a-BepInExPack" })!;
        if (old.Loader != LoaderKind.Bepinex || old.ThunderstorePackage != "a-BepInExPack") throw new Exception("old record changed behaviour");
        return "il2cpp pack kept, nexus-only mods route, melonloader without BepInEx, old records unchanged";
    }
}
