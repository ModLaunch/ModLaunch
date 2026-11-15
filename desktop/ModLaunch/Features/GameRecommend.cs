using System.Globalization;
using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Games;

namespace ModLaunch.Features;

/// <summary>Совет «вам может понравиться»: игра Steam, почему (из-за какой вашей игры и каких тегов), оценка игроков, цена в долларах.</summary>
public sealed record GamePick(int AppId, string Name, int Score, long Reviews, decimal? PriceUsd, int Discount, string Because, string[] Tags, double Rank);

/// <summary>Игра, по которой подбираем: номер в Steam, имя и сколько в неё наиграно.</summary>
public sealed record Seed(int AppId, string Name, long Minutes);

/// <summary>
/// 29.0: рекомендации игр по тому, во что вы действительно играете. Берём до восьми самых наигранных игр Steam
/// (часы Steam и учёт ModLaunch), их теги из SteamSpy, складываем «вкус» (вес тега — по часам), по трём главным
/// тегам берём списки игр SteamSpy и ранжируем: совпадение тегов × доля положительных отзывов. Уже купленное,
/// желаемое и скрытое не советуем. Всё кэшируется (теги — на месяц, списки — на сутки), запросы — не чаще раза в секунду.
/// </summary>
public static class GameRecommend
{
    const string Api = "https://steamspy.com/api.php";
    static readonly JsonFile Cache = new(Path.Combine(Paths.CacheDir, "recommend.json"), () => new JsonObject { ["tags"] = new JsonObject(), ["lists"] = new JsonObject() });
    static readonly SemaphoreSlim Gate = new(1);
    static DateTime _lastCall = DateTime.MinValue;

    /// <summary>Слишком общие теги: по ним «похожее» не найти.</summary>
    static readonly HashSet<string> Generic = new(StringComparer.OrdinalIgnoreCase)
    {
        "Singleplayer", "Multiplayer", "Indie", "Action", "Adventure", "Casual", "Great Soundtrack", "Atmospheric", "2D", "3D",
        "Free to Play", "Early Access", "Co-op", "Online Co-Op", "Classic", "Funny", "Beautiful", "Colorful", "Cute", "Family Friendly",
        "Story Rich", "Replay Value", "Difficult", "Controller", "Moddable", "Female Protagonist", "Third Person", "First-Person",
        "Fantasy", "Sci-fi", "Masterpiece", "Cartoony", "Stylized", "Realistic", "Violent", "Gore", "Nudity", "Sexual Content", "Mature",
        "LGBTQ+", "Simulation", "Strategy", "RPG", "Sandbox", "Exploration", "Character Customization", "Choices Matter", "PvP", "PvE",
    };

    public static List<string> Dismissed => Settings.Data.Arr("recHidden").Select(n => n?.ToString() ?? "").Where(s => s != "").ToList();

    public static void Dismiss(int appId)
    {
        if (Settings.Data["recHidden"] is not JsonArray a) Settings.Data["recHidden"] = a = new JsonArray();
        if (a.All(n => n?.ToString() != appId.ToString(CultureInfo.InvariantCulture))) a.Add(appId.ToString(CultureInfo.InvariantCulture));
        while (a.Count > 300) a.RemoveAt(0);
        Settings.Save();
    }

    /// <summary>Во что играли: Steam (часы Steam) и игры с номером Steam из «Всех игр» (часы ModLaunch). Самые наигранные — первыми.</summary>
    public static List<Seed> Seeds(int max = 8)
    {
        var seeds = new Dictionary<int, Seed>();
        void Add(int app, string name, long minutes)
        {
            if (app <= 0 || minutes < 30 || name == "" || !SteamCatalog.LooksLikeGame(name) || CustomGames.IsTool(name)) return;
            if (seeds.TryGetValue(app, out var have) && have.Minutes >= minutes) return;
            seeds[app] = new Seed(app, name, minutes);
        }
        foreach (var s in SteamAccount.All) Add(s.AppId, s.Name, s.Minutes);
        foreach (var g in AllGames.Current ?? []) if (g.SteamAppId > 0) Add(g.SteamAppId, g.Name, AllGames.Played(g).Minutes);
        return seeds.Values.OrderByDescending(s => s.Minutes).Take(max).ToList();
    }

    /// <summary>Что уже есть (не советуем): вся библиотека Steam, игры из «Всех игр» и желаемое.</summary>
    static HashSet<int> Owned()
    {
        var owned = SteamAccount.All.Select(s => s.AppId).ToHashSet();
        foreach (var g in AllGames.Current ?? []) if (g.SteamAppId > 0) owned.Add(g.SteamAppId);
        foreach (var w in Wishlist.Items) owned.Add(w.AppId);
        foreach (var d in Dismissed) if (int.TryParse(d, out var id)) owned.Add(id);
        return owned;
    }

    // ---------------------------------------------------------------- данные SteamSpy

    static async Task<JsonNode?> Call(string query, CancellationToken ct)
    {
        // SteamSpy просит не чаще запроса в секунду.
        var wait = _lastCall + TimeSpan.FromMilliseconds(1100) - DateTime.UtcNow;
        if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
        _lastCall = DateTime.UtcNow;
        return await Http.GetJson($"{Api}?{query}", ct, 40);
    }

    /// <summary>Теги игры (голоса игроков) из appdetails.</summary>
    internal static Dictionary<string, long> ParseTags(JsonNode? details)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        if (details?["tags"] is JsonObject tags)
            foreach (var (k, v) in tags) if (v is JsonValue jv && jv.TryGetValue<long>(out var votes)) result[k] = votes;
        return result;
    }

    static async Task<Dictionary<string, long>> TagsOf(int app, CancellationToken ct)
    {
        var key = app.ToString(CultureInfo.InvariantCulture);
        if (Cache.Data.Obj("tags")[key] is JsonObject c && DateTime.TryParse(c.Str("at"), null, DateTimeStyles.RoundtripKind, out var at) && DateTime.UtcNow - at < TimeSpan.FromDays(30))
            return ParseTags(c);
        var tags = ParseTags(await Call($"request=appdetails&appid={app}", ct));
        var o = new JsonObject { ["at"] = DateTime.UtcNow.ToString("o"), ["tags"] = new JsonObject(tags.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value))) };
        Cache.Data.Obj("tags")[key] = o;
        return tags;
    }

    /// <summary>Список игр с тегом: appid → (имя, плюсы, минусы, цена в центах, скидка).</summary>
    internal static List<(int App, string Name, long Positive, long Negative, long Price, int Discount)> ParseList(JsonNode? list)
    {
        var result = new List<(int, string, long, long, long, int)>();
        if (list is not JsonObject o) return result;
        foreach (var (k, v) in o)
        {
            if (!int.TryParse(k, out var app) || v is not JsonObject g) continue;
            long Num(string key) => g.Long(key); // SteamSpy отдаёт числа то строками, то числами
            result.Add((app, g.Str("name") ?? "", Num("positive"), Num("negative"), Num("price"), (int)Num("discount")));
        }
        return result;
    }

    static async Task<JsonNode?> ListOf(string tag, CancellationToken ct)
    {
        if (Cache.Data.Obj("lists")[tag] is JsonObject c && DateTime.TryParse(c.Str("at"), null, DateTimeStyles.RoundtripKind, out var at) && DateTime.UtcNow - at < TimeSpan.FromDays(1))
            return c["games"];
        var games = await Call($"request=tag&tag={Uri.EscapeDataString(tag)}", ct);
        // Храним только нужное — список «Open World» весит четверть мегабайта.
        var slim = new JsonObject();
        foreach (var g in ParseList(games).Where(g => g.Positive + g.Negative >= 300))
            slim[g.App.ToString(CultureInfo.InvariantCulture)] = new JsonObject
            {
                ["name"] = g.Name, ["positive"] = g.Positive, ["negative"] = g.Negative, ["price"] = g.Price.ToString(CultureInfo.InvariantCulture), ["discount"] = g.Discount.ToString(CultureInfo.InvariantCulture),
            };
        Cache.Data.Obj("lists")[tag] = new JsonObject { ["at"] = DateTime.UtcNow.ToString("o"), ["games"] = slim };
        return slim;
    }

    // ---------------------------------------------------------------- подбор

    /// <summary>Вкус: тег → вес. Вес игры — по часам (логарифм, чтобы одна игра на 2000 ч не заслонила всё), внутри игры — по голосам.</summary>
    internal static Dictionary<string, double> Taste(IEnumerable<(Seed Seed, Dictionary<string, long> Tags)> seeds)
    {
        var taste = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (seed, tags) in seeds)
        {
            var top = tags.Where(t => !Generic.Contains(t.Key)).OrderByDescending(t => t.Value).Take(10).ToList();
            if (top.Count == 0) continue;
            var weight = Math.Log(1 + seed.Minutes / 60.0);
            var max = (double)top[0].Value;
            foreach (var (tag, votes) in top) taste[tag] = taste.GetValueOrDefault(tag) + weight * (votes / max);
        }
        return taste;
    }

    /// <summary>
    /// Ранжировать кандидатов: сумма весов тегов, в списках которых игра есть, × доля положительных (с поправкой на
    /// малое число отзывов). Возвращает лучшие max без уже имеющихся.
    /// </summary>
    internal static List<GamePick> Rank(Dictionary<string, double> taste, IReadOnlyDictionary<string, List<(int App, string Name, long Positive, long Negative, long Price, int Discount)>> lists,
        IReadOnlyList<(Seed Seed, Dictionary<string, long> Tags)> seeds, ISet<int> skip, int max)
    {
        var byApp = new Dictionary<int, (string Name, long Pos, long Neg, long Price, int Discount, List<string> Tags)>();
        foreach (var (tag, games) in lists)
            foreach (var g in games)
            {
                // Программы и служебное (Wallpaper Engine, саундтреки, демо) — не игры, их не советуем.
                if (skip.Contains(g.App) || g.Name == "" || !SteamCatalog.LooksLikeGame(g.Name) || CustomGames.IsTool(g.Name)) continue;
                if (!byApp.TryGetValue(g.App, out var e)) byApp[g.App] = e = (g.Name, g.Positive, g.Negative, g.Price, g.Discount, []);
                e.Tags.Add(tag);
            }
        var picks = new List<GamePick>();
        foreach (var (app, e) in byApp)
        {
            var total = e.Pos + e.Neg;
            if (total < 300) continue;
            // Байесовская поправка: 300 «средних» (75 %) отзывов сверху — у игр с горсткой отзывов оценка не взлетает.
            var share = (e.Pos + 0.75 * 300) / (total + 300.0);
            var match = e.Tags.Sum(t => taste.GetValueOrDefault(t));
            var rank = match * share * share * (1 + Math.Log10(total) / 10);
            var because = seeds.Where(s => e.Tags.Any(t => s.Tags.ContainsKey(t))).OrderByDescending(s => s.Seed.Minutes).Select(s => s.Seed.Name).FirstOrDefault() ?? "";
            picks.Add(new GamePick(app, e.Name, (int)Math.Round(100.0 * e.Pos / total), total, e.Price > 0 ? e.Price / 100m : null, e.Discount, because,
                e.Tags.OrderByDescending(t => taste.GetValueOrDefault(t)).ToArray(), rank));
        }
        return picks.OrderByDescending(p => p.Rank).ThenBy(p => p.AppId).Take(max).ToList();
    }

    /// <summary>
    /// Какие списки брать: два главных тега вкуса и по лучшему тегу у следующих игр, которых в них ещё нет —
    /// иначе одна игра на сотню часов забирает все советы (до четырёх списков).
    /// </summary>
    internal static List<string> ChooseTags(Dictionary<string, double> taste, IReadOnlyList<(Seed Seed, Dictionary<string, long> Tags)> seeds)
    {
        var chosen = taste.OrderByDescending(t => t.Value).Take(2).Select(t => t.Key).ToList();
        foreach (var (_, tags) in seeds)
        {
            if (chosen.Count >= 4) break;
            var top = tags.Where(t => !Generic.Contains(t.Key)).OrderByDescending(t => t.Value).Select(t => t.Key).ToList();
            if (top.Take(3).Any(chosen.Contains) || top.Count == 0) continue;
            chosen.Add(top[0]);
        }
        return chosen;
    }

    /// <summary>Перемешать по играм-причинам: главной — два места подряд, остальным — по одному, по кругу.</summary>
    internal static List<GamePick> Mix(List<GamePick> ranked, IReadOnlyList<string> seedOrder, int max)
    {
        var queues = ranked.GroupBy(p => p.Because).ToDictionary(g => g.Key, g => new Queue<GamePick>(g));
        var order = seedOrder.Where(queues.ContainsKey).Concat(queues.Keys.Where(k => !seedOrder.Contains(k))).ToList();
        var result = new List<GamePick>();
        while (result.Count < max && queues.Values.Any(q => q.Count > 0))
            for (var i = 0; i < order.Count && result.Count < max; i++)
                for (var k = 0; k < (i == 0 ? 2 : 1) && queues[order[i]].Count > 0 && result.Count < max; k++)
                    result.Add(queues[order[i]].Dequeue());
        return result;
    }

    /// <summary>Подобрать игры. Нет сыгранных игр Steam — пусто (страница предложит сначала поиграть).</summary>
    public static async Task<List<GamePick>> Pick(int max = 18, CancellationToken ct = default)
    {
        if (Program.Demo || Program.Screenshot) return Demo();
        await Gate.WaitAsync(ct);
        try
        {
            var seeds = Seeds();
            if (seeds.Count == 0) return [];
            var tagged = new List<(Seed, Dictionary<string, long>)>();
            foreach (var s in seeds) tagged.Add((s, await TagsOf(s.AppId, ct)));
            var taste = Taste(tagged);
            var lists = new Dictionary<string, List<(int, string, long, long, long, int)>>();
            foreach (var tag in ChooseTags(taste, tagged))
                lists[tag] = ParseList(await ListOf(tag, ct));
            Cache.Save();
            return Mix(Rank(taste, lists, tagged, Owned(), 500), tagged.Select(t => t.Item1.Name).ToList(), max);
        }
        finally { Gate.Release(); }
    }

    /// <summary>
    /// 30.13: «Похожие игры» для одной игры — её теги SteamSpy, списки двух-трёх главных тегов и тот же рейтинг,
    /// что у «Для вас». Уже имеющиеся игры и эту саму не предлагаем.
    /// </summary>
    public static async Task<List<GamePick>> Similar(int appId, string name, int max = 6, CancellationToken ct = default)
    {
        if (appId <= 0) return [];
        if (Program.Demo || Program.Screenshot) return Demo().Where(p => p.AppId != appId).Take(max).Select(p => p with { Because = name }).ToList();
        await Gate.WaitAsync(ct);
        try
        {
            var seed = new Seed(appId, name, 60);
            var tagged = new List<(Seed, Dictionary<string, long>)> { (seed, await TagsOf(appId, ct)) };
            if (tagged[0].Item2.Count == 0) return [];
            var taste = Taste(tagged);
            var lists = new Dictionary<string, List<(int, string, long, long, long, int)>>();
            foreach (var tag in ChooseTags(taste, tagged).Take(3))
                lists[tag] = ParseList(await ListOf(tag, ct));
            Cache.Save();
            var skip = Owned();
            skip.Add(appId);
            return Rank(taste, lists, tagged, skip, max);
        }
        finally { Gate.Release(); }
    }

    /// <summary>--similar &lt;номер в Steam&gt;: похожие игры текстом (проверка без окна).</summary>
    public static int SimilarCli(string[] args)
    {
        var at = Array.IndexOf(args, "--similar");
        if (at < 0 || at + 1 >= args.Length || !int.TryParse(args[at + 1], out var appId)) { Console.WriteLine("usage: --similar <steam app id>"); return 2; }
        try
        {
            foreach (var p in Similar(appId, "#" + appId).GetAwaiter().GetResult())
                Console.WriteLine($"{p.AppId,8} {p.Score,3}% {p.Reviews,8} {p.Name}  [{string.Join(", ", p.Tags)}]");
            return 0;
        }
        catch (Exception e) { Console.WriteLine("error: " + e.Message); return 1; }
    }

    /// <summary>--recommend: что подобралось бы на этом ПК (для поддержки и проверки).</summary>
    public static int Cli()
    {
        var seeds = Seeds();
        Console.WriteLine("seeds: " + string.Join(", ", seeds.Select(s => $"{s.Name} ({s.Minutes / 60} h)")));
        try
        {
            foreach (var p in Pick().GetAwaiter().GetResult())
                Console.WriteLine($"{p.Rank,6:0.00}  {p.Name} | {p.Score}% of {p.Reviews} | {(p.PriceUsd is { } usd ? PriceCompare.Usd(usd) : "free")} | because {p.Because} | {string.Join(", ", p.Tags)}");
            return 0;
        }
        catch (Exception e) { Console.WriteLine("failed: " + e.Message); return 1; }
    }

    static List<GamePick> Demo() =>
    [
        new(1145360, "Hades", 98, 287_959, 24.99m, 0, "Hollow Knight", ["Action Roguelike", "Metroidvania", "Souls-like"], 9),
        new(268910, "Cuphead", 95, 160_210, 19.99m, 30, "Hollow Knight", ["Difficult", "Metroidvania"], 8),
        new(1627720, "Lies of P", 92, 78_430, 59.99m, 0, "ELDEN RING", ["Souls-like", "Action RPG"], 7),
        new(632360, "Risk of Rain 2", 91, 245_100, 24.99m, 50, "Hades II", ["Action Roguelike"], 6),
        new(1086940, "Baldur's Gate 3", 96, 650_000, 59.99m, 0, "ELDEN RING", ["Action RPG", "Party-Based RPG"], 5),
        new(367520, "Ori and the Will of the Wisps", 95, 88_000, 29.99m, 75, "Hollow Knight", ["Metroidvania", "Platformer"], 4),
    ];

    [SelfTest]
    static string RecommendRanksByTasteAndScore()
    {
        var hk = (new Seed(367520, "Hollow Knight", 60 * 120), ParseTags(JsonNode.Parse("""{"tags":{"Metroidvania":3000,"Souls-like":2000,"Indie":2500,"Difficult":1500}}""")));
        var cs = (new Seed(730, "CS2", 60 * 10), ParseTags(JsonNode.Parse("""{"tags":{"FPS":5000,"Shooter":4000,"Multiplayer":3000}}""")));
        var taste = Taste([hk, cs]);
        if (taste.ContainsKey("Indie") || taste.ContainsKey("Multiplayer")) throw new Exception("generic tags must be ignored");
        if (!(taste["Metroidvania"] > taste["FPS"])) throw new Exception("120 h should outweigh 10 h");
        var lists = new Dictionary<string, List<(int, string, long, long, long, int)>>
        {
            ["Metroidvania"] = ParseList(JsonNode.Parse("""
                {"1":{"name":"Great Metroidvania","positive":"9000","negative":"300","price":"1999","discount":"0"},
                 "2":{"name":"Bad Metroidvania","positive":"400","negative":"900","price":"999","discount":"0"},
                 "3":{"name":"Tiny Gem","positive":"40","negative":"1","price":"499","discount":"0"},
                 "367520":{"name":"Hollow Knight","positive":"300000","negative":"9000","price":"1499","discount":"0"}}
                """)),
            ["Souls-like"] = ParseList(JsonNode.Parse("""{"1":{"name":"Great Metroidvania","positive":9000,"negative":300,"price":1999,"discount":50}}""")),
            ["FPS"] = ParseList(JsonNode.Parse("""{"4":{"name":"Shooter","positive":"9000","negative":"300","price":"0","discount":"0"}}""")),
        };
        var picks = Rank(taste, lists, [hk, cs], new HashSet<int> { 367520 }, 10);
        if (picks.Count == 0 || picks[0].Name != "Great Metroidvania") throw new Exception("best first: " + string.Join(", ", picks.Select(p => p.Name)));
        if (picks.Any(p => p.AppId == 367520)) throw new Exception("owned game recommended");
        if (picks.Any(p => p.AppId == 3)) throw new Exception("game with 41 reviews recommended");
        if (picks.FindIndex(p => p.AppId == 2) < picks.FindIndex(p => p.AppId == 4)) throw new Exception("badly reviewed game above a good one");
        if (picks[0].Because != "Hollow Knight" || picks[0].PriceUsd != 19.99m || picks[0].Score != 97) throw new Exception("pick details: " + picks[0]);
        var tags = ChooseTags(taste, [hk, cs]);
        if (!tags.Contains("FPS") || tags[0] != "Metroidvania") throw new Exception("second game gets its own list: " + string.Join(", ", tags));
        var mixed = Mix(picks, ["Hollow Knight", "CS2"], 3);
        if (!mixed.Any(p => p.Because == "CS2")) throw new Exception("mix lost the second game");
        var junk = Rank(taste, new Dictionary<string, List<(int, string, long, long, long, int)>> { ["Metroidvania"] = [(9, "Wallpaper Engine", 9000, 10, 499, 0)] }, [hk], new HashSet<int>(), 5);
        if (junk.Count != 0) throw new Exception("software recommended");
        return "generic tags ignored, hours weight taste, owned/tiny/software skipped, good reviews first, every played game gets its own picks";
    }
}
