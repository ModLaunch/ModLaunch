using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Games;

namespace ModLaunch.Social;

/// <summary>Куратор: имя, пара слов о себе, сколько игр в подборке и сколько подписчиков.</summary>
public sealed record Curator(string Uid, string Name, string About, long Picks, long Followers, DateTime? Updated);

/// <summary>Мнение куратора об игре: «советую», «не советую» или «на заметку» и короткий текст.</summary>
public sealed record CuratorPick(string Curator, string Game, string Title, string Verdict, string Text, DateTime? Updated);

/// <summary>
/// 30.0: кураторы, как в Steam. Любой вошедший заводит страницу куратора и пишет о играх из своей библиотеки
/// (curators/{uid}/picks/{ключ игры} — тот же ключ, что у отзывов об играх). На кураторов подписываются
/// (curators/{uid}/followers/{uid подписчика} и счётчик followers — одной пачкой); их мнения видны на странице игры.
/// Читать может кто угодно. Пока правил кураторов нет в базе — «временно недоступно».
/// </summary>
public static class Curators
{
    public const int MaxAbout = 300, MaxText = 500, MaxTitle = 80, MaxPicks = 500, MaxFollowing = 50;
    public static readonly string[] Verdicts = ["yes", "no", "info"];

    static readonly JsonFile File = new(Path.Combine(Paths.DataDir, "curators.json"), () => new JsonObject());

    public static event Action? Changed;

    // ---------------------------------------------------------------- подписки (список — на этом ПК, счётчик — в базе)

    /// <summary>На кого подписан: uid → имя (имя — чтобы показать без лишних запросов).</summary>
    public static IReadOnlyList<(string Uid, string Name)> Following =>
        (File.Data["follow"] as JsonObject ?? new JsonObject()).Select(kv => (kv.Key, kv.Value?.GetValue<string>() ?? "")).ToList();

    public static bool Follows(string uid) => (File.Data["follow"] as JsonObject)?.ContainsKey(uid) == true;

    static void Remember(string uid, string? name)
    {
        var map = File.Data["follow"] as JsonObject ?? new JsonObject();
        if (name is null) map.Remove(uid); else map[uid] = name;
        File.Data["follow"] = map;
        File.Save();
        lock (GameCache) GameCache.Clear();
        Changed?.Invoke();
    }

    public static async Task Follow(Curator c)
    {
        var (uid, token, _) = await Hub.Member();
        if (c.Uid == uid) throw new ServiceError("OWN");
        if (Following.Count >= MaxFollowing) throw new ServiceError("TOO_MANY");
        if (!Follows(c.Uid))
        {
            try
            {
                await Hub.Commit(
                [
                    new JsonObject
                    {
                        ["update"] = new JsonObject { ["name"] = Firebase.Doc($"curators/{c.Uid}/followers/{uid}"), ["fields"] = new JsonObject() },
                        ["updateTransforms"] = new JsonArray(Hub.ServerTime("created")),
                        ["currentDocument"] = new JsonObject { ["exists"] = false },
                    },
                    Hub.Increment($"curators/{c.Uid}", "followers", 1),
                ], token);
            }
            catch (ServiceError e) when (e.Code is "EXISTS" or "PRECONDITION") { /* уже подписан с другого ПК */ }
        }
        Remember(c.Uid, c.Name);
    }

    public static async Task Unfollow(string curatorUid)
    {
        var (uid, token, _) = await Hub.Member();
        try
        {
            await Hub.Commit(
            [
                new JsonObject { ["delete"] = Firebase.Doc($"curators/{curatorUid}/followers/{uid}"), ["currentDocument"] = new JsonObject { ["exists"] = true } },
                Hub.Increment($"curators/{curatorUid}", "followers", -1),
            ], token);
        }
        catch (ServiceError e) when (e.Code is "NOT_FOUND" or "PRECONDITION") { /* подписки в базе уже нет */ }
        Remember(curatorUid, null);
    }

    // ---------------------------------------------------------------- чтение

    static Curator CuratorOf(JsonObject d)
    {
        var f = Firebase.FromFields(d["fields"] as JsonObject);
        var n = d.Str("name") ?? "";
        return new Curator(n[(n.LastIndexOf('/') + 1)..], f.S("name"), f.S("about"), f.L("picks"), f.L("followers"), Firebase.Time(f.S("updated")));
    }

    static CuratorPick PickOf(string curator, JsonObject d)
    {
        var f = Firebase.FromFields(d["fields"] as JsonObject);
        return new CuratorPick(curator, f.S("game"), f.S("title"), f.S("verdict"), f.S("text"), Firebase.Time(f.S("updated")));
    }

    static async Task<string?> AnyToken()
    {
        try { return Account.SignedIn ? (await Account.Token()).IdToken : null; } catch { return null; }
    }

    /// <summary>Кураторы с самым большим числом подписчиков.</summary>
    public static async Task<List<Curator>> Popular(int limit = 50)
    {
        if (Program.Demo || Program.Screenshot) return DemoCurators();
        var q = new JsonObject
        {
            ["from"] = new JsonArray(new JsonObject { ["collectionId"] = "curators" }),
            ["orderBy"] = new JsonArray(new JsonObject { ["field"] = new JsonObject { ["fieldPath"] = "followers" }, ["direction"] = "DESCENDING" }),
            ["limit"] = limit,
        };
        var rows = await Hub.Call(Firebase.Url(":runQuery"), HttpMethod.Post, new JsonObject { ["structuredQuery"] = q }, await AnyToken());
        return (rows as JsonArray ?? []).Select(r => r?["document"] as JsonObject).OfType<JsonObject>().Select(CuratorOf).ToList();
    }

    public static async Task<Curator?> Get(string uid)
    {
        if (Program.Demo || Program.Screenshot) return DemoCurators().FirstOrDefault(c => c.Uid == uid);
        try { return await Hub.Call(Firebase.Url($"/curators/{uid}"), HttpMethod.Get, null, await AnyToken()) is JsonObject d ? CuratorOf(d) : null; }
        catch (ServiceError e) when (e.Code == "NOT_FOUND") { return null; }
    }

    /// <summary>Подборка куратора: свежие сверху.</summary>
    public static async Task<List<CuratorPick>> Picks(string uid)
    {
        if (Program.Demo || Program.Screenshot) return DemoPicks(uid);
        var rows = await Hub.Call(Firebase.Url($"/curators/{uid}/picks") + "&pageSize=" + MaxPicks, HttpMethod.Get, null, await AnyToken());
        return (rows?["documents"] as JsonArray ?? []).OfType<JsonObject>().Select(d => PickOf(uid, d)).OrderByDescending(p => p.Updated).ToList();
    }

    static readonly Dictionary<string, (DateTime At, List<(string Name, CuratorPick Pick)> Picks)> GameCache = [];

    /// <summary>Что о игре пишут кураторы, на которых подписан (по одному запросу на куратора, параллельно; кэш на 10 минут).</summary>
    public static async Task<List<(string Name, CuratorPick Pick)>> ForGame(string gameKey)
    {
        lock (GameCache)
            if (GameCache.TryGetValue(gameKey, out var hit) && DateTime.UtcNow - hit.At < TimeSpan.FromMinutes(10)) return hit.Picks;
        var found = await LoadForGame(gameKey);
        lock (GameCache) GameCache[gameKey] = (DateTime.UtcNow, found);
        return found;
    }

    static async Task<List<(string Name, CuratorPick Pick)>> LoadForGame(string gameKey)
    {
        var follow = Following;
        if (Program.Demo || Program.Screenshot)
            return DemoCurators().Take(3).Select((c, i) => (c.Name, new CuratorPick(c.Uid, gameKey, "", Verdicts[i % 3], DemoText(i), DateTime.UtcNow.AddDays(-i * 4)))).ToList();
        if (follow.Count == 0) return [];
        var token = await AnyToken();
        var tasks = follow.Select(async f =>
        {
            try
            {
                return await Hub.Call(Firebase.Url($"/curators/{f.Uid}/picks/{Uri.EscapeDataString(gameKey)}"), HttpMethod.Get, null, token) is JsonObject d
                    ? (f.Name, PickOf(f.Uid, d)) : ((string, CuratorPick)?)null;
            }
            catch (ServiceError e) when (e.Code == "NOT_FOUND") { return null; }
        });
        return (await Task.WhenAll(tasks)).OfType<(string, CuratorPick)>().OrderByDescending(p => p.Item2.Updated).ToList();
    }

    // ---------------------------------------------------------------- своя страница

    /// <summary>Создать или обновить свою страницу куратора (имя и «о себе»). Подписчиков и счётчик игр не трогаем.</summary>
    public static async Task<Curator> SaveProfile(string name, string about, Curator? existing)
    {
        var (uid, token, _) = await Hub.Member();
        name = Account.CleanName(name);
        about = Hub.Clip(about, MaxAbout, multiline: true);
        if (name == "") throw new ServiceError("NO_NAME");
        var fields = new Dictionary<string, object?> { ["name"] = name, ["about"] = about };
        if (existing is null) { fields["picks"] = 0L; fields["followers"] = 0L; }
        await Hub.Commit(
        [
            new JsonObject
            {
                ["update"] = new JsonObject { ["name"] = Firebase.Doc($"curators/{uid}"), ["fields"] = Firebase.ToFields(fields) },
                ["updateMask"] = new JsonObject { ["fieldPaths"] = new JsonArray(fields.Keys.Select(k => (JsonNode)k).ToArray()) },
                ["updateTransforms"] = new JsonArray(Hub.ServerTime("updated")),
                ["currentDocument"] = new JsonObject { ["exists"] = existing is not null },
            },
        ], token);
        return new Curator(uid, name, about, existing?.Picks ?? 0, existing?.Followers ?? 0, DateTime.UtcNow);
    }

    /// <summary>Поля мнения — ровно то, что пропустят правила (ключ игры до 40, название до 80, текст до 500).</summary>
    internal static Dictionary<string, object?> PickFields(string game, string title, string verdict, string text)
    {
        if (game.Length is 0 or > 40 || game.Contains('/')) throw new ServiceError("INVALID", "game");
        if (!Verdicts.Contains(verdict)) throw new ServiceError("INVALID", "verdict");
        title = Hub.Clip(title, MaxTitle);
        if (title == "") throw new ServiceError("INVALID", "title");
        return new() { ["game"] = game, ["title"] = title, ["verdict"] = verdict, ["text"] = Hub.Clip(text, MaxText, multiline: true) };
    }

    /// <summary>Написать или поправить мнение об игре. Новое — вместе с «+1» к счётчику игр на странице.</summary>
    public static async Task SavePick(LibGame g, string verdict, string text, bool isNew)
    {
        var (uid, token, _) = await Hub.Member();
        var game = GameReviews.Key(g);
        var writes = new JsonArray
        {
            new JsonObject
            {
                ["update"] = new JsonObject { ["name"] = Firebase.Doc($"curators/{uid}/picks/{game}"), ["fields"] = Firebase.ToFields(PickFields(game, g.Name, verdict, text)) },
                ["updateTransforms"] = new JsonArray(Hub.ServerTime("updated")),
                ["currentDocument"] = new JsonObject { ["exists"] = !isNew },
            },
        };
        if (isNew) writes.Add(CountWrite(uid, 1));
        await Hub.Commit(writes, token);
        Changed?.Invoke();
    }

    public static async Task RemovePick(string game)
    {
        var (uid, token, _) = await Hub.Member();
        await Hub.Commit(
        [
            new JsonObject { ["delete"] = Firebase.Doc($"curators/{uid}/picks/{game}"), ["currentDocument"] = new JsonObject { ["exists"] = true } },
            CountWrite(uid, -1),
        ], token);
        Changed?.Invoke();
    }

    /// <summary>Счётчик игр на своей странице: ±1 и время правки — одним преобразованием.</summary>
    static JsonObject CountWrite(string uid, int by) => new()
    {
        ["transform"] = new JsonObject
        {
            ["document"] = Firebase.Doc($"curators/{uid}"),
            ["fieldTransforms"] = new JsonArray(
                new JsonObject { ["fieldPath"] = "picks", ["increment"] = new JsonObject { ["integerValue"] = by.ToString(System.Globalization.CultureInfo.InvariantCulture) } },
                Hub.ServerTime("updated")),
        },
    };

    public static string Explain(Exception e) => e is ServiceError s ? s.Code switch
    {
        "DENIED" or "NOT_CONFIGURED" => I18n.T("v30.cur.off"),
        "OWN" => I18n.T("v30.cur.own"),
        "TOO_MANY" => I18n.T("v30.cur.tooMany", ("n", MaxFollowing)),
        "NO_NAME" => I18n.T("v30.cur.noName"),
        _ => Firebase.Explain("market", e),
    } : Firebase.Explain("market", e);

    // ---------------------------------------------------------------- демо (снимки экрана)

    static List<Curator> DemoCurators() =>
    [
        new("demo-c1", "Cozy Corner", "Уютные игры без спешки: фермы, острова, строительство.", 64, 12840, DateTime.UtcNow.AddDays(-1)),
        new("demo-c2", "Survival Notes", "Выживание и крафт. Пишу только о том, во что наиграл 20+ часов.", 41, 8310, DateTime.UtcNow.AddDays(-3)),
        new("demo-c3", "Indie Radar", "Маленькие студии и смелые идеи.", 128, 5120, DateTime.UtcNow.AddDays(-2)),
        new("demo-c4", "Mod Friendly", "Игры, которые расцветают с модами.", 37, 2290, DateTime.UtcNow.AddDays(-6)),
    ];

    static string DemoText(int i) => i switch
    {
        0 => "Лучшая песочница про океан: строить базу под водой — отдельное удовольствие.",
        1 => "Сюжет тянется под конец, но первые 15 часов — отличные.",
        _ => "Ставьте моды на интерфейс — играть станет заметно удобнее.",
    };

    static List<CuratorPick> DemoPicks(string uid) =>
    [
        new(uid, "steam:264710", "Subnautica", "yes", DemoText(0), DateTime.UtcNow.AddDays(-1)),
        new(uid, "steam:892970", "Valheim", "yes", "Лучший кооператив про викингов: строить вместе — отдельный жанр.", DateTime.UtcNow.AddDays(-4)),
        new(uid, "steam:1245620", "Elden Ring", "info", "Очень сложно, но честно. Для терпеливых.", DateTime.UtcNow.AddDays(-9)),
        new(uid, "steam:1966720", "Lethal Company", "no", "Весело в компании, но в одиночку быстро надоедает.", DateTime.UtcNow.AddDays(-15)),
    ];

    [SelfTest]
    static string PickFieldsMatchRules()
    {
        var f = PickFields("steam:264710", "Subnautica", "yes", new string('a', 900));
        if (f.Count != 4 || ((string)f["text"]!).Length > MaxText) throw new Exception("text clip");
        foreach (var (game, verdict) in new[] { ("", "yes"), (new string('x', 41), "yes"), ("a/b", "yes"), ("steam:1", "maybe") })
        {
            try { PickFields(game, "T", verdict, ""); throw new Exception($"accepted {game}/{verdict}"); }
            catch (ServiceError) { }
        }
        if (((string)PickFields("name:abc", new string('t', 200), "info", "")["title"]!).Length > MaxTitle) throw new Exception("title clip");
        return "pick fields: game key ≤ 40 without '/', verdict yes/no/info, title ≤ 80, text ≤ 500";
    }
}
