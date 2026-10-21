using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModLaunch.Core;

namespace ModLaunch.Games;

/// <summary>
/// Игра из справочника. Key — постоянный id (сообщество Thunderstore, иначе раздел Nexus);
/// Loader — "bepinex" | "bepinex-il2cpp" | "melonloader" | null; Pack — пакет загрузчика на Thunderstore
/// (например, BepInEx-BepInExPack_IL2CPP); Folder — папка игры в Steam (installdir); Exes — имена exe;
/// ModsRoute — папка модов относительно папки игры; Mods — модов на Nexus и Thunderstore вместе (0 — неизвестно);
/// NexusId — номер игры на Nexus (без него не открываются страницы модов; 0 — неизвестен).
/// </summary>
public sealed record DirGame(string Key, string Name, int SteamAppId, string? Community, string? NexusDomain, string? Loader, string? Pack, string? Folder, string[] Exes, string? ModsRoute, long Mods, int NexusId = 0)
{
    /// <summary>Картинка игры: обложка Steam, иначе плитка Nexus (у игр только с Nexus номера Steam нет).</summary>
    public string? Art => SteamAppId > 0 ? GameDef.SteamArt(SteamAppId) : NexusId > 0 ? $"https://staticdelivery.nexusmods.com/Images/games/4_3/tile_{NexusId}.jpg" : null;
}

/// <summary>
/// 12.0: справочник моддируемых игр — больше тысячи, а не только встроенные. Три открытых списка без входа:
/// игры Nexus (сколько модов), сообщества Thunderstore (сколько пакетов) и схема экосистемы Thunderstore
/// (Steam id, папка, exe, загрузчик и его пакет — то, что знает r2modman). Сливаются по имени (Norm) и Steam id.
/// В программе — снимок Assets/gamedir.config.json (его раз в неделю обновляет CI, gamedir.yml); поверх —
/// свежий список на диске (cache/gamedir.json), который RefreshAsync скачивает не чаще раза в 7 дней.
/// </summary>
public static partial class GameDirectory
{
    public const string NexusUrl = "https://data.nexusmods.com/file/nexus-data/games.json";
    public const string CommunitiesUrl = "https://thunderstore.io/api/cyberstorm/community/?page_size=200";
    public const string SchemaUrl = "https://thunderstore.io/api/experimental/schema/dev/latest/";
    public const string SnapshotName = "gamedir.config.json";

    /// <summary>Сколько игр должно быть в полном справочнике (--gen-directory падает, если меньше).</summary>
    public const int Target = 1000;
    /// <summary>Формат снимка и кеша: файл другого формата не читаем.</summary>
    const int Format = 1;
    /// <summary>Меньше игр — файл битый или неполный (например, без Nexus): такой не читаем и не сохраняем.</summary>
    const int MinValid = 250;
    static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    static string CachePath => Path.Combine(Paths.CacheDir, "gamedir.json");

    // ---------------------------------------------------------------- список и поиск

    static readonly object Gate = new();
    static volatile Index? _index;

    static Index Current
    {
        get
        {
            if (_index is { } i) return i;
            lock (Gate) return _index ??= new Index(Load());
        }
    }

    /// <summary>Все игры (по Key). Первое обращение читает кеш с диска, если он цел, иначе встроенный снимок.</summary>
    public static IReadOnlyList<DirGame> All => Current.Games;
    public static int Count => Current.Games.Length;

    /// <summary>Справочник обновился (RefreshAsync). Вызывается из фонового потока.</summary>
    public static event Action? Changed;

    public static DirGame? BySteam(int appId) => Current.BySteam(appId);
    /// <summary>По Key; подходят и сообщество Thunderstore, и раздел Nexus.</summary>
    public static DirGame? ByKey(string key) => Current.ByKey(key);
    /// <summary>По имени: сначала точное (без регистра и знаков), потом через Norm (без «издания»).</summary>
    public static DirGame? ByName(string name) => Current.ByName(name);
    /// <summary>Поиск по имени: полное совпадение, начало, середина; внутри каждой группы — по числу модов.</summary>
    public static List<DirGame> Search(string query, int take = 50) => Current.Search(query, take);

    static (HashSet<int> Steam, HashSet<string> Communities, HashSet<string> Domains)? _builtin;

    /// <summary>Игра уже встроена в программу (тот же Steam id, сообщество Thunderstore или раздел Nexus).</summary>
    public static bool IsBuiltin(DirGame g)
    {
        var (steam, communities, domains) = _builtin ??= (
            GameCatalog.Builtin.Select(b => b.SteamAppId).Where(id => id > 0).ToHashSet(),
            GameCatalog.Builtin.Select(b => b.ThunderstoreCommunity).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase),
            GameCatalog.Builtin.Select(b => b.NexusDomain).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase));
        return g.SteamAppId > 0 && steam.Contains(g.SteamAppId) || g.Community is { } c && communities.Contains(c) || g.NexusDomain is { } d && domains.Contains(d);
    }

    /// <summary>Готовый к поиску список: имена для сравнения посчитаны заранее, порядок «по популярности» — тоже.</summary>
    sealed class Index
    {
        public readonly DirGame[] Games;
        readonly string[] _norms;
        readonly int[] _popular;
        readonly Dictionary<int, DirGame> _steam = new();
        readonly Dictionary<string, DirGame> _keys = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, DirGame> _strict = new(StringComparer.Ordinal);
        /// <summary>Все игры с тем же Norm — по популярности (Skyrim и Skyrim Special Edition).</summary>
        readonly Dictionary<string, List<DirGame>> _same = new(StringComparer.Ordinal);

        public Index(IEnumerable<DirGame> games)
        {
            Games = games.OrderBy(g => g.Key, StringComparer.Ordinal).ToArray();
            _norms = Array.ConvertAll(Games, g => Norm(g.Name));
            _popular = Enumerable.Range(0, Games.Length).OrderByDescending(n => Games[n].Mods).ThenBy(n => _norms[n], StringComparer.Ordinal).ThenBy(n => n).ToArray();
            foreach (var g in Games) _keys[g.Key] = g;
            // Псевдонимы — после ключей и по популярности: ключ другой игры не перебить, из двух одноимённых — та, где больше модов.
            foreach (var n in _popular)
            {
                var g = Games[n];
                if (g.Community is { } c) _keys.TryAdd(c, g);
                if (g.NexusDomain is { } d) _keys.TryAdd(d, g);
                if (g.SteamAppId > 0) _steam.TryAdd(g.SteamAppId, g);
                _strict.TryAdd(Strict(g.Name), g);
                if (_same.TryGetValue(_norms[n], out var same)) same.Add(g); else _same[_norms[n]] = [g];
            }
        }

        public DirGame? BySteam(int appId) => appId > 0 && _steam.TryGetValue(appId, out var g) ? g : null;
        public DirGame? ByKey(string key) => !string.IsNullOrEmpty(key) && _keys.TryGetValue(key, out var g) ? g : null;

        public DirGame? ByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return _strict.TryGetValue(Strict(name), out var g) ? g : _same.TryGetValue(Norm(name), out var same) ? same[0] : null;
        }

        public List<DirGame> Search(string query, int take = 50)
        {
            if (take <= 0) return [];
            var q = Norm(query ?? "");
            List<DirGame> head = [], middle = [];
            // Обход — от популярных к редким: когда обе группы набраны, дальше смотреть незачем.
            foreach (var n in _popular)
            {
                if (head.Count >= take && (middle.Count >= take || q.Length == 0)) break;
                var name = _norms[n];
                if (name.StartsWith(q, StringComparison.Ordinal))
                {
                    if (name.Length != q.Length && head.Count < take) head.Add(Games[n]);
                }
                else if (middle.Count < take && name.Contains(q, StringComparison.Ordinal)) middle.Add(Games[n]);
            }
            var exact = q.Length > 0 && _same.TryGetValue(q, out var same) ? same : [];
            return [.. exact.Concat(head).Concat(middle).Take(take)];
        }
    }

    // ---------------------------------------------------------------- имена

    static readonly string[][] Editions = Array.ConvertAll(new[]
    {
        "game of the year edition", "game of the year", "goty edition", "goty", "definitive edition", "enhanced edition",
        "complete edition", "special edition", "anniversary edition", "deluxe edition", "ultimate edition",
        "legendary edition", "gold edition", "directors cut", "remastered", "remaster",
    }, e => e.Split(' '));

    /// <summary>
    /// Имя для сравнения: строчные буквы и цифры без пробелов и знаков, без ™®©, ударений и «The» в начале,
    /// без слов издания (Remastered, Definitive Edition, GOTY…): «The Witcher 3: Wild Hunt — GOTY» → witcher3wildhunt.
    /// </summary>
    public static string Norm(string s) => Join(Words(s), editions: true);

    /// <summary>Строже Norm: издание остаётся (Skyrim и Skyrim Special Edition на Nexus — разные игры).</summary>
    static string Strict(string s) => Join(Words(s), editions: false);

    /// <summary>Слова имени для сравнения по началу и концу (The Elder Scrolls V: Skyrim Special Edition ↔ Skyrim Special Edition).</summary>
    public static string[] NameWords(string s)
    {
        var w = Words(s);
        if (w.Count > 1 && w[0] == "the") w.RemoveAt(0);
        return [.. w];
    }

    /// <summary>Строгое имя (издание не выкидывается) — для точного совпадения раньше Norm.</summary>
    public static string StrictName(string s) => Strict(s);

    /// <summary>Только буквы и цифры, «the» остаётся: id сообщества и раздела (the-forest ↔ theforest).</summary>
    static string Squash(string s) => string.Concat(Words(s));

    static List<string> Words(string? s)
    {
        var words = new List<string>();
        if (string.IsNullOrEmpty(s)) return words;
        var sb = new StringBuilder();
        foreach (var ch in s.Normalize(NormalizationForm.FormD))
        {
            // é → e, Baldur's → baldurs; ™®© и прочие знаки — просто разделители.
            if (ch is '\'' or '’' or '`' or 'ʼ' || CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch)) { sb.Append(char.ToLowerInvariant(ch)); continue; }
            Flush();
            if (ch == '&') words.Add("and");
        }
        Flush();
        return words;

        void Flush()
        {
            if (sb.Length == 0) return;
            words.Add(sb.ToString());
            sb.Clear();
        }
    }

    static string Join(List<string> w, bool editions)
    {
        if (w.Count > 1 && w[0] == "the") w = w.GetRange(1, w.Count - 1);
        if (!editions) return string.Concat(w);
        var kept = new List<string>(w.Count);
        for (var i = 0; i < w.Count;)
        {
            var cut = Array.Find(Editions, e => At(w, i, e));
            if (cut is null) kept.Add(w[i++]);
            else i += cut.Length;
        }
        // Имя целиком из «слов издания» («Remastered») оставляем как есть.
        return string.Concat(kept.Count > 0 ? kept : w);
    }

    static bool At(List<string> words, int at, string[] phrase)
    {
        if (at + phrase.Length > words.Count) return false;
        for (var k = 0; k < phrase.Length; k++)
            if (words[at + k] != phrase[k]) return false;
        return true;
    }

    // ---------------------------------------------------------------- сборка из живых списков

    /// <summary>Игра, пока её собирают из списков.</summary>
    sealed class Entry
    {
        public string? Community, Domain, Loader, Pack, Folder, Route;
        public string Name = "";
        public int Steam;
        public string[] Exes = [];
        public long TsMods, NexusMods;
        public int NexusId;
        /// <summary>Число пакетов пришло из списка сообществ (а не «неизвестно»).</summary>
        public bool TsKnown;
    }

    /// <summary>
    /// Собрать справочник из сырого JSON трёх списков (любого может не быть): схема экосистемы Thunderstore,
    /// игры Nexus и страницы списка сообществ Thunderstore. Один и тот же код у RefreshAsync и --gen-directory.
    /// Порядок и состав результата зависят только от входа (снимок в git не «дрожит»).
    /// </summary>
    public static List<DirGame> Build(string? schema, string? nexus, IEnumerable<string>? communityPages)
    {
        var ts = new List<Entry>();
        var byCommunity = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(schema))
        {
            using var doc = JsonDocument.Parse(schema);
            var loaders = Arr(doc.RootElement, "modloaderPackages")
                .Select(p => (Id: Str(p, "packageId") ?? "", Loader: Str(p, "loader") ?? "")).Where(p => p.Id != "").ToList();
            if (Prop(doc.RootElement, "games") is { ValueKind: JsonValueKind.Object } games)
                foreach (var game in games.EnumerateObject())
                    if (FromSchema(game.Name, game.Value, loaders) is { } e && byCommunity.TryAdd(e.Community!, e)) ts.Add(e);
        }
        foreach (var page in communityPages ?? [])
        {
            using var doc = JsonDocument.Parse(page);
            var results = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : Arr(doc.RootElement, "results").ToList();
            foreach (var c in results)
            {
                if (Str(c, "identifier") is not { Length: > 0 } id) continue;
                var count = Long(c, "total_package_count");
                if (byCommunity.TryGetValue(id, out var e)) { e.TsMods = Math.Max(e.TsMods, count); e.TsKnown = true; }
                // Сообщество, которого нет в схеме: без загрузчика, но каталог есть.
                else if (count > 0) { e = new Entry { Community = id, Name = Str(c, "name") ?? id, TsMods = count, TsKnown = true }; byCommunity[id] = e; ts.Add(e); }
            }
        }
        var nx = new List<Entry>();
        if (!string.IsNullOrWhiteSpace(nexus))
        {
            using var doc = JsonDocument.Parse(nexus);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
                foreach (var n in doc.RootElement.EnumerateArray())
                    // «site» — раздел Nexus для инструментов, а не игра.
                    if (Str(n, "domain_name") is { Length: > 0 } d && d != "site" && Str(n, "name") is { Length: > 0 } name && Long(n, "mods") > 0)
                        nx.Add(new Entry { Domain = d, Name = WebUtility.HtmlDecode(name).Trim(), NexusMods = Long(n, "mods"), NexusId = (int)Long(n, "id") });
        }
        // Порог по числу модов на Nexus: 5, а если игр всё равно меньше тысячи с запасом — ниже.
        List<DirGame> result = [];
        foreach (var min in new[] { 5, 3, 1 })
        {
            result = Merge(ts, nx.Where(n => n.NexusMods >= min).ToList(), nx);
            if (result.Count >= Target + Target / 5) break;
        }
        return result;
    }

    /// <summary>Склеить игры Thunderstore и Nexus: одна игра — одна запись с обоими каталогами (allNx — Nexus до порога).</summary>
    static List<DirGame> Merge(List<Entry> ts, List<Entry> nx, List<Entry> allNx)
    {
        var pair = new Dictionary<Entry, Entry>();
        var taken = new HashSet<Entry>();
        void Pair(Entry t, Entry n)
        {
            if (!pair.ContainsKey(t) && !taken.Contains(n)) { pair[t] = n; taken.Add(n); }
        }
        var popularNx = nx.OrderByDescending(n => n.NexusMods).ThenBy(n => n.Domain, StringComparer.Ordinal).ToList();
        var tsByCommunity = ts.ToDictionary(t => t.Community!, StringComparer.OrdinalIgnoreCase);
        var nxByDomain = nx.GroupBy(n => n.Domain!, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var builtinByCommunity = GameCatalog.Builtin.Where(b => b.ThunderstoreCommunity is not null).GroupBy(b => b.ThunderstoreCommunity!, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var builtinByDomain = GameCatalog.Builtin.Where(b => b.NexusDomain is not null).GroupBy(b => b.NexusDomain!, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // 1) Встроенные игры: пара «сообщество — раздел» уже известна точно, а имя — полное (H3VR → Hot Dogs, Horseshoes & Hand Grenades).
        foreach (var b in GameCatalog.Builtin)
        {
            if (b.ThunderstoreCommunity is not { } c || !tsByCommunity.TryGetValue(c, out var t)) continue;
            t.Name = b.Name;
            if (b.NexusDomain is { } d && nxByDomain.TryGetValue(d, out var n)) Pair(t, n);
        }
        // 2) Адрес раздела Nexus — это id сообщества без дефисов (lethal-company ↔ lethalcompany).
        var tsBySquash = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var t in ts) tsBySquash.TryAdd(Squash(t.Community!), t);
        foreach (var n in popularNx)
            if (tsBySquash.TryGetValue(n.Domain!, out var t)) Pair(t, n);
        // 3) Одно и то же имя без регистра и знаков.
        var tsByStrict = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var t in ts.OrderByDescending(t => t.TsMods)) tsByStrict.TryAdd(Strict(t.Name), t);
        foreach (var n in popularNx)
            if (!taken.Contains(n) && tsByStrict.TryGetValue(Strict(n.Name), out var t)) Pair(t, n);
        // 4) Имя без «издания» — только если с обеих сторон такое имя ровно у одной игры (считая и Nexus ниже порога):
        // иначе Dark Souls Remastered из Thunderstore ушла бы к старой Dark Souls.
        var looseTs = ts.GroupBy(t => Norm(t.Name)).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single());
        var looseNx = allNx.GroupBy(n => Norm(n.Name)).Where(g => g.Count() == 1).Select(g => g.Key).ToHashSet();
        foreach (var n in nx)
            if (!taken.Contains(n) && Norm(n.Name) is var name && looseNx.Contains(name) && looseTs.TryGetValue(name, out var t)) Pair(t, n);

        var result = new List<DirGame>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in ts)
        {
            pair.TryGetValue(t, out var n);
            // Сообщество без единого пакета и без Nexus — не моддируемая игра.
            if (t.TsKnown && t.TsMods == 0 && n is null) continue;
            var steam = t.Steam;
            var pack = t.Pack;
            if (builtinByCommunity.TryGetValue(t.Community!, out var b))
            {
                if (steam == 0) steam = b.SteamAppId;
                // Для встроенной игры пакет загрузчика уже выверен — тот же, что ставит программа.
                if (b.Loader == LoaderKind.Bepinex && b.ThunderstorePackage is { } p && t.Loader is "bepinex" or "bepinex-il2cpp") pack = p;
            }
            if (steam == 0 && n is not null && builtinByDomain.TryGetValue(n.Domain!, out var bd)) steam = bd.SteamAppId;
            keys.Add(t.Community!);
            result.Add(new DirGame(t.Community!, t.Name, steam, t.Community, n?.Domain, t.Loader, pack, t.Folder, t.Exes, t.Route, t.TsMods + (n?.NexusMods ?? 0), n?.NexusId ?? 0));
        }
        foreach (var n in nx.Where(n => !taken.Contains(n)).OrderBy(n => n.Domain, StringComparer.Ordinal))
        {
            // Адрес раздела совпал с id чужого сообщества — ключ с приставкой, чтобы не спутать.
            var key = keys.Add(n.Domain!) ? n.Domain! : "nexus-" + n.Domain;
            if (key != n.Domain && !keys.Add(key)) continue;
            var steam = builtinByDomain.TryGetValue(n.Domain!, out var b) ? b.SteamAppId : 0;
            result.Add(new DirGame(key, n.Name, steam, null, n.Domain, null, null, null, [], null, n.NexusMods, n.NexusId));
        }
        result.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        return result;
    }

    /// <summary>Игра из схемы экосистемы Thunderstore (games[label]): сообщество, Steam, папка, exe и загрузчик.</summary>
    static Entry? FromSchema(string label, JsonElement g, List<(string Id, string Loader)> loaders)
    {
        var ts = Prop(g, "thunderstore");
        var r2list = Arr(g, "r2modman").ToList();
        // У Valheim и Risk of Rain 2 второй вариант — выделенный сервер; нужна сама игра.
        JsonElement? r2 = r2list.Count == 0 ? null : r2list.FirstOrDefault(x => Str(x, "gameInstanceType") != "server") is { ValueKind: JsonValueKind.Object } game ? game : r2list[0];
        // Скрытое сообщество без поддержки в r2modman — моды там не публикуют.
        if (r2 is null && (ts is null || Prop(ts.Value, "listed") is { ValueKind: JsonValueKind.False })) return null;
        var community = r2 is { } r && Str(r, "packageIndex") is { } index && CommunityOf(index) is { } c ? c : ts is not null ? label : null;
        if (community is null) return null;

        var e = new Entry
        {
            Community = community,
            Name = (ts is { } t ? Str(t, "displayName") : null) ?? Str(Prop(g, "meta"), "displayName") ?? Str(Prop(r2, "meta"), "displayName") ?? label,
        };
        foreach (var d in Arr(r2, "distributions").Concat(Arr(g, "distributions")))
            if (Str(d, "platform") is "steam" or "steam-direct" && int.TryParse(Str(d, "identifier"), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0) { e.Steam = id; break; }
        e.Folder = Str(r2, "steamFolderName") is { Length: > 0 } folder ? folder.Replace('\\', '/') : null;
        var exes = Arr(r2, "exeNames").Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : null).OfType<string>().Where(x => x != "").ToArray();
        e.Exes = exes.Any(x => x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) ? exes.Where(x => x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).ToArray() : exes;

        // Загрузчик: из r2modman; без него — по пакету, который Thunderstore сам показывает в сообществе (autolist).
        var raw = Str(r2, "packageLoader");
        var family = Family(raw);
        foreach (var a in Arr(ts, "autolistPackageIds").Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : null))
            foreach (var (id, loader) in loaders)
                if (id == a && Family(loader) is { } f && (raw is null || f == family) && e.Pack is null) { e.Pack = id; family = f; }
        if (family == "bepinex") e.Pack ??= GuessPack(community, e.Name, loaders);
        // IL2CPP — другая сборка BepInEx (6, с interop); видно по пакету загрузчика.
        if (family == "bepinex" && e.Pack?.Contains("IL2CPP", StringComparison.OrdinalIgnoreCase) == true) family = "bepinex-il2cpp";
        e.Loader = family;
        var route = Arr(r2, "installRules").Where(x => Prop(x, "isDefaultLocation") is { ValueKind: JsonValueKind.True }).Select(x => Str(x, "route")).FirstOrDefault(x => !string.IsNullOrEmpty(x));
        e.Route = family switch
        {
            "bepinex" or "bepinex-il2cpp" => route ?? "BepInEx/plugins",
            // У MelonLoader папка модов — Mods (в r2modman своя служебная папка по умолчанию).
            "melonloader" => "Mods",
            // Игры без загрузчика: папка, куда r2modman кладёт моды как есть.
            _ => raw == "none" ? route : null,
        };
        return e;
    }

    /// <summary>Загрузчики, которые программа умеет ставить; прочие (Shimloader, Godot ModLoader…) — null.</summary>
    static string? Family(string? loader) => loader switch
    {
        "bepinex" => "bepinex",
        "melonloader" or "recursive-melonloader" => "melonloader",
        _ => null,
    };

    /// <summary>https://thunderstore.io/c/&lt;сообщество&gt;/api/… → сообщество.</summary>
    static string? CommunityOf(string url)
    {
        var at = url.IndexOf("/c/", StringComparison.Ordinal);
        if (at < 0) return null;
        var end = url.IndexOf('/', at + 3);
        var id = end < 0 ? url[(at + 3)..] : url[(at + 3)..end];
        return id.Length > 0 ? id : null;
    }

    /// <summary>
    /// Пакет BepInEx игры, если сообщество его не называет: у многих игр он свой — BepInExPack_Valheim,
    /// BepInEx_Rogue_Tower, BepInExPack_TABS (Totally Accurate Battle Simulator — по первым буквам).
    /// </summary>
    static string? GuessPack(string community, string name, List<(string Id, string Loader)> loaders)
    {
        var parts = community.Split('-', StringSplitOptions.RemoveEmptyEntries);
        var initials = parts.Length >= 3 ? string.Concat(parts.Select(p => p.All(char.IsDigit) || p.Length <= 2 ? p : p[..1])) : "";
        var names = new HashSet<string>(StringComparer.Ordinal) { Squash(community), Strict(name), initials.ToLowerInvariant() };
        foreach (var (id, loader) in loaders)
        {
            if (loader != "bepinex") continue;
            var tail = Squash(id[(id.IndexOf('-') + 1)..]);
            foreach (var prefix in new[] { "bepinexpack", "bepinex" })
                if (tail.StartsWith(prefix, StringComparison.Ordinal)) { tail = tail[prefix.Length..]; break; }
            if (tail.Length >= 3 && names.Contains(tail)) return id;
        }
        return null;
    }

    static JsonElement? Prop(JsonElement? e, string key) =>
        e is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty(key, out var v) && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) ? v : null;

    static string? Str(JsonElement? e, string key) => Prop(e, key) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    static long Long(JsonElement? e, string key) => Prop(e, key) switch
    {
        { ValueKind: JsonValueKind.Number } v => v.TryGetInt64(out var l) ? l : (long)v.GetDouble(),
        { ValueKind: JsonValueKind.String } v => long.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : 0,
        _ => 0,
    };

    static IEnumerable<JsonElement> Arr(JsonElement? e, string key) =>
        Prop(e, key) is { ValueKind: JsonValueKind.Array } a ? a.EnumerateArray() : [];

    // ---------------------------------------------------------------- снимок и кеш

    /// <summary>Снимок: {"v":1,"games":[…]} — по игре на строку, по Key, короткие поля, пустые не пишутся.</summary>
    public static string Serialize(IEnumerable<DirGame> games)
    {
        var sb = new StringBuilder("{\"v\":").Append(Format).Append(",\"games\":[\n");
        var first = true;
        var options = new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        foreach (var g in games.OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            if (!first) sb.Append(",\n");
            first = false;
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms, options))
            {
                w.WriteStartObject();
                w.WriteString("k", g.Key);
                w.WriteString("n", g.Name);
                if (g.SteamAppId > 0) w.WriteNumber("s", g.SteamAppId);
                void Opt(string key, string? value) { if (!string.IsNullOrEmpty(value)) w.WriteString(key, value); }
                Opt("c", g.Community);
                Opt("d", g.NexusDomain);
                if (g.NexusId > 0) w.WriteNumber("i", g.NexusId);
                Opt("l", g.Loader);
                Opt("p", g.Pack);
                Opt("f", g.Folder);
                if (g.Exes.Length > 0)
                {
                    w.WriteStartArray("e");
                    foreach (var exe in g.Exes) w.WriteStringValue(exe);
                    w.WriteEndArray();
                }
                Opt("r", g.ModsRoute);
                if (g.Mods > 0) w.WriteNumber("m", g.Mods);
                w.WriteEndObject();
            }
            sb.Append(Encoding.UTF8.GetString(ms.ToArray()));
        }
        return sb.Append("\n]}\n").ToString();
    }

    /// <summary>Разобрать снимок или кеш; null — другой формат, мусор или слишком мало игр.</summary>
    static List<DirGame>? Parse(JsonNode? root, int min = MinValid)
    {
        if (root is not JsonObject o || o.Long("v") != Format) return null;
        var list = new List<DirGame>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in o.Arr("games").OfType<JsonObject>())
        {
            if (g.Str("k") is not { Length: > 0 } key || g.Str("n") is not { Length: > 0 } name || !seen.Add(key)) continue;
            var exes = g.Arr("e").Select(x => x is JsonValue v && v.TryGetValue<string>(out var s) ? s : null).OfType<string>().ToArray();
            list.Add(new DirGame(key, name, (int)g.Long("s"), g.Str("c"), g.Str("d"), g.Str("l"), g.Str("p"), g.Str("f"), exes, g.Str("r"), g.Long("m"), (int)g.Long("i")));
        }
        return list.Count >= min ? list : null;
    }

    static List<DirGame>? ReadFile(string path)
    {
        try { return File.Exists(path) ? Parse(JsonNode.Parse(File.ReadAllText(path))) : null; }
        catch { return null; }
    }

    static List<DirGame> Load() => ReadFile(CachePath) ?? Parse(Resources.Json(SnapshotName)) ?? [];

    /// <summary>Запись целиком или никак: сначала во временный файл, потом подмена.</summary>
    static void SaveAtomic(string path, string text)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var tmp = full + ".tmp";
        File.WriteAllText(tmp, text, new UTF8Encoding(false));
        File.Move(tmp, full, true);
    }

    // ---------------------------------------------------------------- обновление

    static Task<bool>? _running;
    static bool _tried;

    /// <summary>Кеш пора обновить: его нет или ему больше 7 дней (часы ушли назад — тоже).</summary>
    static bool Stale(DateTime? written, DateTime now) => written is not { } w || now - w > MaxAge || w > now + TimeSpan.FromDays(1);

    static DateTime? CacheTime()
    {
        try { return File.Exists(CachePath) ? File.GetLastWriteTimeUtc(CachePath) : null; }
        catch { return null; }
    }

    /// <summary>
    /// Скачать живые списки, собрать справочник, сохранить кеш и сообщить Changed. Не чаще раза в 7 дней
    /// (по дате кеша) и одной попытки за запуск; force — сейчас. Не бросает: false — справочник прежний.
    /// Два вызова подряд получают одну и ту же загрузку.
    /// </summary>
    public static Task<bool> RefreshAsync(bool force = false)
    {
        lock (Gate)
        {
            if (_running is { IsCompleted: false } running) return running;
            if (!force && (_tried || !Stale(CacheTime(), DateTime.UtcNow))) return Task.FromResult(false);
            _tried = true;
            return _running = Task.Run(Refresh);
        }
    }

    static async Task<bool> Refresh()
    {
        try
        {
            var (schema, nexus, pages) = await Download(20);
            var list = Build(schema, nexus, pages);
            // Один список не скачался (нет Nexus) — справочник обрезан: такой не нужен, остаётся прежний.
            if (list.Count < Math.Max(MinValid, Current.Games.Length * 4 / 5)) return false;
            SaveAtomic(CachePath, Serialize(list));
            lock (Gate) _index = new Index(list);
            try { Changed?.Invoke(); } catch { }
            return true;
        }
        catch { return false; }
    }

    /// <summary>Три живых списка, сырым JSON. Бросает при сбое сети (--gen-directory и --selfcheck показывают причину).</summary>
    public static async Task<(string Schema, string Nexus, List<string> Communities)> Download(int timeoutSec = 20, CancellationToken ct = default)
    {
        async Task<List<string>> Pages()
        {
            var pages = new List<string>();
            string? url = CommunitiesUrl;
            // Страниц немного (сотни сообществ по 200); предел — на случай зацикленной ссылки next.
            for (var guard = 0; url is not null && guard < 30; guard++)
            {
                var page = await Get(url, timeoutSec, ct);
                pages.Add(page);
                url = JsonNode.Parse(page).Str("next");
            }
            return pages;
        }
        var schema = Get(SchemaUrl, timeoutSec, ct);
        var nexus = Get(NexusUrl, timeoutSec, ct);
        var communities = Pages();
        await Task.WhenAll(schema, nexus, communities);
        return (schema.Result, nexus.Result, communities.Result);
    }

    static async Task<string> Get(string url, int timeoutSec, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await Http.Client.SendAsync(request, cts.Token);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase} — {url}", null, response.StatusCode);
        return await response.Content.ReadAsStringAsync(cts.Token);
    }

    // ---------------------------------------------------------------- --gen-directory

    /// <summary>
    /// --gen-directory &lt;файл&gt;: собрать справочник из живых списков и записать снимок (так его обновляет CI).
    /// Код выхода 1 — игр меньше тысячи или сбой сети; файл тогда не трогаем.
    /// Без сети (первый снимок, отладка): --schema-file / --nexus-file / --communities-file вместо загрузки и --min N.
    /// </summary>
    public static async Task<int> Generate(string[] args)
    {
        string? Arg(string name)
        {
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
        var output = Arg("--gen-directory") ?? SnapshotName;
        var min = int.TryParse(Arg("--min"), out var m) ? m : Target;
        try
        {
            string? schema, nexus;
            List<string> pages;
            if (args.Any(a => a is "--schema-file" or "--nexus-file" or "--communities-file"))
            {
                schema = Arg("--schema-file") is { } s ? File.ReadAllText(s) : null;
                nexus = Arg("--nexus-file") is { } n ? File.ReadAllText(n) : null;
                pages = Arg("--communities-file") is { } c ? [File.ReadAllText(c)] : [];
            }
            else (schema, nexus, pages) = await Download(120);
            var list = Build(schema, nexus, pages);
            Console.WriteLine($"games: {list.Count}; thunderstore {list.Count(g => g.Community is not null)}, nexus {list.Count(g => g.NexusDomain is not null)}, both {list.Count(g => g.Community is not null && g.NexusDomain is not null)}");
            Console.WriteLine($"steam ids {list.Count(g => g.SteamAppId > 0)}, loaders: " + string.Join(", ", list.GroupBy(g => g.Loader ?? "none").OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key} {g.Count()}")) + $", packs {list.Count(g => g.Pack is not null)}, builtin {list.Count(IsBuiltin)}");
            Console.WriteLine("top: " + string.Join(", ", list.OrderByDescending(g => g.Mods).Take(15).Select(g => $"{g.Name} ({g.Mods})")));
            if (list.Count < min)
            {
                Console.WriteLine($"::error title=gamedir::only {list.Count} games, need {min}; {output} not changed");
                return 1;
            }
            SaveAtomic(output, Serialize(list));
            Console.WriteLine($"wrote {list.Count} games to {output}");
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine($"::error title=gamedir::{e.GetType().Name}: {e.Message}");
            return 1;
        }
    }
}
