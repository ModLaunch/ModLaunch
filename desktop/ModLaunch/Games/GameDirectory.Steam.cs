using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModLaunch.Core;

namespace ModLaunch.Games;

/// <summary>
/// 15.0: все игры Steam (100 тыс.+), а не только моддируемые из справочника. Снимок Assets/steamgames.tsv.gz:
/// строка «appid\tимя», порядок — как в поиске магазина (популярные сверху). Собирает CI (gamedir.yml,
/// ModLaunch --gen-steam). Грузится один раз в фоне; пока не загрузился — Count = 0, и страницы
/// показывают справочник как раньше. Такая игра — обычная запись справочника без каталогов модов:
/// её можно добавить, найти на диске, запустить и считать часы, а моды — из своей папки.
/// </summary>
public static partial class SteamCatalog
{
    public const string ResourceName = "steamgames.tsv.gz";
    const int MinGames = 60_000;

    sealed class Data
    {
        public int[] Ids = [];
        public string[] Names = [];
        public string[] Folded = [];
        public Dictionary<int, int> At = [];
    }

    static volatile Data? _data;
    static Task? _loading;
    static readonly object Gate = new();

    /// <summary>Снимок прочитан (из фонового потока).</summary>
    public static event Action? Loaded;

    public static bool Ready => _data is not null;
    public static int Count => _data?.Ids.Length ?? 0;

    /// <summary>Прочитать снимок в фоне — один раз за запуск.</summary>
    public static Task EnsureLoaded()
    {
        lock (Gate)
            return _loading ??= Task.Run(() =>
            {
                _data = Read();
                Loaded?.Invoke();
            });
    }

    static Data Read()
    {
        try
        {
            using var s = typeof(SteamCatalog).Assembly.GetManifestResourceStream(ResourceName);
            if (s is null) return new Data();
            using var gz = new GZipStream(s, CompressionMode.Decompress);
            return Parse(gz);
        }
        catch { return new Data(); }
    }

    static Data Parse(Stream s)
    {
        using var r = new StreamReader(s, Encoding.UTF8);
        var ids = new List<int>(180_000);
        var names = new List<string>(180_000);
        var seen = new HashSet<int>();
        while (r.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var tab = line.IndexOf('\t');
            if (tab <= 0 || !int.TryParse(line.AsSpan(0, tab), out var id) || id <= 0 || !seen.Add(id)) continue;
            var name = line[(tab + 1)..].Trim();
            if (name.Length == 0) continue;
            ids.Add(id);
            names.Add(name);
        }
        var d = new Data { Ids = [.. ids], Names = [.. names], Folded = new string[ids.Count] };
        d.At.EnsureCapacity(ids.Count);
        for (var i = 0; i < d.Ids.Length; i++)
        {
            d.Folded[i] = Fold(d.Names[i]);
            d.At[d.Ids[i]] = i;
        }
        return d;
    }

    /// <summary>Для поиска: строчные буквы и цифры через один пробел («Half-Life: Alyx» → «half life alyx»).</summary>
    public static string Fold(string s)
    {
        var b = new StringBuilder(s.Length);
        var space = false;
        foreach (var raw in s)
        {
            var c = char.ToLowerInvariant(raw == 'ё' || raw == 'Ё' ? 'е' : raw);
            if (char.IsLetterOrDigit(c))
            {
                if (space && b.Length > 0) b.Append(' ');
                b.Append(c);
                space = false;
            }
            else if (c != '\'' && c != '’' && c != '™' && c != '®') space = true;
        }
        return b.ToString();
    }

    static DirGame Make(Data d, int i) => new("steam-" + d.Ids[i], d.Names[i], d.Ids[i], null, null, null, null, null, [], null, 0);

    public static DirGame? Get(int appId) => _data is { } d && d.At.TryGetValue(appId, out var i) ? Make(d, i) : null;

    /// <summary>
    /// Поиск по всем играм Steam: сначала точное имя, потом начало имени, потом все слова где угодно;
    /// внутри — порядок снимка (популярные выше). skip — номера Steam, которые уже показаны (справочник).
    /// </summary>
    public static List<DirGame> Search(string query, int take, ISet<int>? skip = null)
    {
        if (_data is not { } d || take <= 0) return [];
        var q = Fold(query);
        if (q.Length == 0) return [];
        var words = q.Split(' ');
        List<int> exact = [], prefix = [], inside = [];
        for (var i = 0; i < d.Folded.Length; i++)
        {
            var f = d.Folded[i];
            if (f.Length < q.Length) continue;
            if (f == q) { if (exact.Count < take) exact.Add(i); continue; }
            if (prefix.Count < take && f.StartsWith(q, StringComparison.Ordinal)) { prefix.Add(i); continue; }
            if (inside.Count >= take) continue;
            var all = true;
            foreach (var w in words)
                if (!f.Contains(w, StringComparison.Ordinal)) { all = false; break; }
            if (all) inside.Add(i);
        }
        var result = new List<DirGame>(take);
        foreach (var i in exact.Concat(prefix).Concat(inside))
        {
            if (result.Count >= take) break;
            if (skip is not null && skip.Contains(d.Ids[i])) continue;
            result.Add(Make(d, i));
        }
        return result;
    }

    /// <summary>Самые популярные игры снимка по порядку, кроме skip: продолжение списка «Все игры» без поиска.</summary>
    public static List<DirGame> Popular(int from, int take, ISet<int>? skip = null)
    {
        if (_data is not { } d || take <= 0) return [];
        var result = new List<DirGame>(take);
        var passed = 0;
        for (var i = 0; i < d.Ids.Length && result.Count < take; i++)
        {
            if (skip is not null && skip.Contains(d.Ids[i])) continue;
            if (passed++ < from) continue;
            result.Add(Make(d, i));
        }
        return result;
    }

    /// <summary>Сколько игр снимка нет среди номеров Steam справочника — их добавляем к «поддерживаемым».</summary>
    public static int ExtraOver(IEnumerable<int> known)
    {
        if (_data is not { } d) return 0;
        var overlap = known.Distinct().Count(id => d.At.ContainsKey(id));
        return d.Ids.Length - overlap;
    }

    // ---------------------------------------------------------------- сборка снимка (CI)

    static HttpClient Client()
    {
        var http = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All, ConnectTimeout = TimeSpan.FromSeconds(20) }) { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"ModLaunch/{Http.Version} (+https://github.com/ModLaunch/modlaunch)");
        http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.8");
        return http;
    }

    /// <summary>GET с повтором: на 429/5xx ждём всё дольше (магазин Steam ограничивает частоту).</summary>
    static async Task<string> Get(HttpClient http, string url)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var res = await http.GetAsync(url);
                if ((int)res.StatusCode is 429 or >= 500 && attempt < 8)
                {
                    var wait = res.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Min(120, 15 * attempt));
                    Console.WriteLine($"  {(int)res.StatusCode}, waiting {wait.TotalSeconds:0}s");
                    await Task.Delay(wait);
                    continue;
                }
                res.EnsureSuccessStatusCode();
                return await res.Content.ReadAsStringAsync();
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException && attempt < 8)
            {
                Console.WriteLine($"  {e.GetType().Name}: {e.Message}, retry {attempt}");
                await Task.Delay(TimeSpan.FromSeconds(5 * attempt));
            }
        }
    }

    /// <summary>1. Официальный список магазина (только игры) — если в CI есть секрет STEAM_API_KEY.</summary>
    static async Task<List<(int, string)>> FromStoreService(HttpClient http)
    {
        var key = Environment.GetEnvironmentVariable("STEAM_API_KEY");
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("no STEAM_API_KEY");
        var list = new List<(int, string)>();
        var last = 0;
        for (var page = 0; page < 20; page++)
        {
            var json = await Get(http, $"https://api.steampowered.com/IStoreService/GetAppList/v1/?key={key}&include_games=true&include_dlc=false&include_software=false&include_videos=false&include_hardware=false&max_results=50000&last_appid={last}");
            using var doc = JsonDocument.Parse(json);
            var res = doc.RootElement.GetProperty("response");
            if (res.TryGetProperty("apps", out var apps))
                foreach (var a in apps.EnumerateArray())
                    if (a.TryGetProperty("appid", out var id) && a.TryGetProperty("name", out var name) && name.GetString() is { Length: > 0 } n)
                        list.Add((id.GetInt32(), n));
            if (!(res.TryGetProperty("have_more_results", out var more) && more.GetBoolean()) || !res.TryGetProperty("last_appid", out var l)) break;
            last = l.GetInt32();
        }
        return list;
    }

    [GeneratedRegex(@"data-ds-appid=""(\d+)""[^>]*>.*?<span class=""title"">([^<]*)</span>", RegexOptions.Singleline)]
    private static partial Regex SearchRow();

    /// <summary>2. Поиск магазина, категория «Игры»: по 100 штук, в порядке «по релевантности» — популярные сверху.</summary>
    static async Task<List<(int, string)>> FromStoreSearch(HttpClient http)
    {
        var list = new List<(int, string)>();
        var seen = new HashSet<int>();
        var total = int.MaxValue;
        var empty = 0;
        for (var start = 0; start < total; start += 100)
        {
            var json = await Get(http, $"https://store.steampowered.com/search/results/?query=&start={start}&count=100&dynamic_data=&sort_by=_ASC&category1=998&infinite=1&cc=us&l=english&ndl=1");
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("total_count", out var t) && t.ValueKind == JsonValueKind.Number) total = t.GetInt32();
            var html = doc.RootElement.TryGetProperty("results_html", out var h) ? h.GetString() ?? "" : "";
            var added = 0;
            foreach (Match m in SearchRow().Matches(html))
                if (int.TryParse(m.Groups[1].Value, out var id) && seen.Add(id))
                {
                    var name = WebUtility.HtmlDecode(m.Groups[2].Value).Trim();
                    if (name.Length > 0) { list.Add((id, name)); added++; }
                }
            if (start % 5000 == 0) Console.WriteLine($"  search {start}/{total}: {list.Count}");
            empty = added == 0 ? empty + 1 : 0;
            if (empty >= 3) { Console.WriteLine($"  search: no results from {start}, stopping"); break; }
            await Task.Delay(350);
        }
        return list;
    }

    /// <summary>Не игра по имени: дополнения, саундтреки, демо, серверы, инструменты (для списка без типов).</summary>
    [GeneratedRegex(@"(\bDLC\b|Soundtrack|\bOST\b|Season Pass|Artbook|Art Book|Wallpaper|Dedicated Server|\bServer\b|\bSDK\b|Redistributable|Steamworks|Proton|Playtest|\bDemo\b|\bBeta\b|\bTrailer\b|\bTest\b|Upgrade|Expansion Pass|Costume|Skin Pack|Character Pack|Bonus Content|Digital Deluxe Upgrade|Supporter Pack| - .+ Pack$|Tool(s|kit)?$|Editor$|Benchmark|ValveTestApp)", RegexOptions.IgnoreCase)]
    private static partial Regex NotGameName();

    public static bool LooksLikeGame(string name) => name.Length > 1 && !NotGameName().IsMatch(name);

    /// <summary>3. Старый полный список приложений (устарел в 2025-м, но может отвечать): без типов, фильтр по имени.</summary>
    static async Task<List<(int, string)>> FromAppListV2(HttpClient http)
    {
        var json = await Get(http, "https://api.steampowered.com/ISteamApps/GetAppList/v2/");
        using var doc = JsonDocument.Parse(json);
        var list = new List<(int, string)>();
        foreach (var a in doc.RootElement.GetProperty("applist").GetProperty("apps").EnumerateArray())
            if (a.GetProperty("name").GetString() is { } n && LooksLikeGame(n.Trim()))
                list.Add((a.GetProperty("appid").GetInt32(), n.Trim()));
        return list;
    }

    /// <summary>
    /// ModLaunch --gen-steam PATH: собрать снимок из первого источника, давшего не меньше MinGames игр.
    /// Иначе — код 1, файл не тронут (в ветку не попадёт обрезанный список).
    /// </summary>
    public static async Task<int> Generate(string[] args)
    {
        var i = Array.IndexOf(args, "--gen-steam");
        var output = i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine("Assets", ResourceName);
        using var http = Client();
        (string Title, Func<HttpClient, Task<List<(int, string)>>> Load)[] sources =
            [("IStoreService", FromStoreService), ("store search", FromStoreSearch), ("GetAppList v2", FromAppListV2)];
        var best = new List<(int, string)>();
        foreach (var (title, load) in sources)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                Console.WriteLine($"steam: {title}…");
                var list = await load(http);
                Console.WriteLine($"steam: {title} → {list.Count} games in {sw.Elapsed.TotalMinutes:0.0} min");
                if (list.Count > best.Count) best = list;
                if (list.Count >= MinGames) break;
            }
            catch (Exception e) { Console.WriteLine($"steam: {title} failed: {e.GetType().Name}: {e.Message}"); }
        }
        if (best.Count < MinGames)
        {
            Console.WriteLine($"::error title=steam games::only {best.Count} games, need {MinGames}; {output} not changed");
            return 1;
        }
        Save(output, best);
        Console.WriteLine($"wrote {best.Count} games to {output}; top: " + string.Join(", ", best.Take(10).Select(g => g.Item2)));
        return 0;
    }

    static void Save(string path, List<(int Id, string Name)> games)
    {
        var tmp = path + ".tmp";
        using (var file = File.Create(tmp))
        using (var gz = new GZipStream(file, CompressionLevel.SmallestSize))
        using (var w = new StreamWriter(gz, new UTF8Encoding(false)) { NewLine = "\n" })
        {
            w.WriteLine($"# ModLaunch: Steam games, {games.Count}");
            foreach (var (id, name) in games) w.WriteLine($"{id}\t{name.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ')}");
        }
        File.Move(tmp, path, true);
    }

    // ---------------------------------------------------------------- самотесты

    [SelfTest]
    static string SteamCatalogSearches()
    {
        var text = "# test\n570\tDota 2\n730\tCounter-Strike 2\n413150\tStardew Valley\n1086940\tBaldur's Gate 3\n400\tPortal\n620\tPortal 2\n730\tduplicate\nbad\tline\n";
        var d = Parse(new MemoryStream(Encoding.UTF8.GetBytes(text)));
        if (d.Ids.Length != 6) throw new Exception("parsed " + d.Ids.Length);
        var saved = _data;
        _data = d;
        try
        {
            var portal = Search("portal", 5);
            if (portal.Count != 2 || portal[0].SteamAppId != 400 || portal[1].SteamAppId != 620) throw new Exception("portal: " + string.Join(",", portal.Select(p => p.Name)));
            if (Search("baldurs gate", 5).FirstOrDefault()?.SteamAppId != 1086940) throw new Exception("apostrophe fold");
            if (Search("counter strike", 5).FirstOrDefault()?.SteamAppId != 730) throw new Exception("hyphen fold");
            if (Search("portal", 5, new HashSet<int> { 400 }).Single().SteamAppId != 620) throw new Exception("skip");
            if (Get(413150)?.Key != "steam-413150" || Get(1) is not null) throw new Exception("get");
            if (Popular(1, 2).Select(g => g.SteamAppId).SequenceEqual([730, 413150]) is false) throw new Exception("popular");
            if (ExtraOver([570, 570, 999]) != 5) throw new Exception("extra");
        }
        finally { _data = saved; }
        if (LooksLikeGame("Terraria Soundtrack") || LooksLikeGame("Elden Ring - Shadow of the Erdtree DLC") || !LooksLikeGame("Terraria") || !LooksLikeGame("Portal 2"))
            throw new Exception("game filter");
        var html = "<a href=\"x\" data-ds-appid=\"1,2\" class=\"row\"><span class=\"title\">Bundle</span></a><a data-ds-appid=\"620\" data-ds-itemkey=\"App_620\"><div><span class=\"title\">Portal 2 &amp; Co</span></div></a>";
        var rows = SearchRow().Matches(html).Select(m => (m.Groups[1].Value, WebUtility.HtmlDecode(m.Groups[2].Value))).ToList();
        if (rows.Count != 1 || rows[0] != ("620", "Portal 2 & Co")) throw new Exception("search row: " + string.Join("|", rows));
        return "steam catalog parse, fold, search, skip, filter";
    }

    /// <summary>В сборке есть снимок (если его уже собрал CI): 60 тыс.+ игр и узнаваемые имена.</summary>
    [SelfTest]
    static string SteamCatalogSnapshot()
    {
        using var s = typeof(SteamCatalog).Assembly.GetManifestResourceStream(ResourceName);
        if (s is null) return "no snapshot in this build yet (CI: gamedir.yml)";
        using var gz = new GZipStream(s, CompressionMode.Decompress);
        var d = Parse(gz);
        if (d.Ids.Length < MinGames) throw new Exception($"only {d.Ids.Length} games");
        if (!d.At.ContainsKey(620) && !d.At.ContainsKey(730)) throw new Exception("Portal 2 / CS2 missing");
        return $"{d.Ids.Length} Steam games";
    }
}
