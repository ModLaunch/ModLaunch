using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Social;

/// <summary>Жалоба: на что (kind/target), на кого (owner), причина из списка и пояснение.</summary>
public sealed record ModReport(string Id, string Kind, string Target, string Owner, string Title, string Reason, string Text,
    string Uid, string Status, string Action, string Note, DateTime? Created)
{
    /// <summary>Жалоба из версии до 31.0 (только мод Hub, причина — свободный текст).</summary>
    public bool Legacy => Kind == "creation" && Status == "" && Owner == "";
}

/// <summary>Снятие админом: мод Hub или лот рынка, причина, отпечаток файла.</summary>
public sealed record Takedown(string Kind, string Target, string Reason, string Note, string Sha256, string Title, string Owner, DateTime? At);

/// <summary>Блокировка человека.</summary>
public sealed record Ban(string Uid, string Name, string Note, DateTime? At);

/// <summary>
/// 31.0: доверие и безопасность. Жалобы на мод, лот, отзыв, комментарий и человека (очередь «Модерация»
/// у админа), снятия по жалобе правообладателя или из-за вируса (takedowns — программа прячет снятое и не
/// ставит его файлы), блокировки (bans — правила базы не дают заблокированному ничего писать) и чёрный
/// список файлов по sha256 (config/blocklist). Всё проверяют правила базы, а не программа.
/// </summary>
public static partial class Safety
{
    public static readonly string[] Kinds = ["creation", "listing", "review", "user", "comment"];
    public static readonly string[] Reasons = ["malware", "copyright", "stolen", "abuse", "spam", "adult", "cheat", "scam", "other"];
    public static readonly string[] TakedownReasons = ["copyright", "malware", "rules", "scam", "other"];
    public const int MaxText = 1000;

    public static event Action? Changed;

    // ---------------------------------------------------------------- жалоба

    /// <summary>
    /// id цели в имени документа: только безопасные знаки; иначе (ключ отзыва «steam:730|_game», путь) — хэш,
    /// а настоящий ключ лежит в поле target целиком не помещается — правила сверяют id с kind_target.
    /// </summary>
    public static string TargetId(string target)
    {
        target = (target ?? "").Trim();
        if (Regex.IsMatch(target, "^[A-Za-z0-9_.:-]{1,140}$")) return target;
        return "h" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(target)))[..32].ToLowerInvariant();
    }

    static string Clip(string? s, int max) => Hub.Clip(s ?? "", max, multiline: true);

    /// <summary>Пожаловаться. Повторная жалоба на то же самое — ServiceError REPORT_EXISTS.</summary>
    public static async Task Report(string kind, string target, string owner, string title, string reason, string text)
    {
        if (!Kinds.Contains(kind) || !Reasons.Contains(reason)) throw new ServiceError("INVALID");
        if (!Account.SignedIn) throw new ServiceError("SIGN_IN");
        var (uid, token) = await Account.Token();
        var id = TargetId(target);
        try
        {
            await Hub.Commit([new JsonObject
            {
                ["update"] = new JsonObject
                {
                    ["name"] = Firebase.Doc($"reports/{kind}_{id}__{uid}"),
                    ["fields"] = Firebase.ToFields(new Dictionary<string, object?>
                    {
                        ["kind"] = kind, ["target"] = id, ["owner"] = Clip(owner, 128), ["title"] = Clip(title, 120),
                        ["reason"] = reason, ["text"] = Clip(text, MaxText), ["uid"] = uid, ["status"] = "open",
                    }),
                },
                ["updateTransforms"] = new JsonArray(Hub.ServerTime("created")),
                ["currentDocument"] = new JsonObject { ["exists"] = false },
            }], token);
        }
        catch (ServiceError e) when (e.Code is "EXISTS" or "PRECONDITION") { throw new ServiceError("REPORT_EXISTS"); }
    }

    // ---------------------------------------------------------------- снятое и чёрный список (видят все)

    static readonly SemaphoreSlim Gate = new(1);
    static HashSet<string> _down = new(StringComparer.Ordinal);
    static HashSet<string> _hashes = new(StringComparer.OrdinalIgnoreCase);
    static DateTime _loaded;

    /// <summary>
    /// Снятое админом и запрещённые файлы — с сервера, не чаще раза в 30 минут. Без сети — как было.
    /// 31.0: два чтения на человека (сводка config/takedowns + чёрный список), а не весь список снятого:
    /// при 100 тыс. пользователей список целиком каждые 10 минут стоил бы сотни долларов в день.
    /// </summary>
    public static async Task Refresh(bool force = false)
    {
        if (!Firebase.Configured || Program.Demo) return;
        if (!force && DateTime.UtcNow - _loaded < TimeSpan.FromMinutes(30)) return;
        await Gate.WaitAsync();
        try
        {
            if (!force && DateTime.UtcNow - _loaded < TimeSpan.FromMinutes(30)) return;
            var down = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                var summary = await Hub.Call(Firebase.Url("/config/takedowns"), HttpMethod.Get, null, null) as JsonObject;
                foreach (var id in Firebase.FromFields(summary?["fields"] as JsonObject).A("ids")) down.Add(id);
            }
            catch (ServiceError e) when (e.Code is "NOT_FOUND") { }
            var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var block = await Hub.Call(Firebase.Url("/config/blocklist"), HttpMethod.Get, null, null) as JsonObject;
                foreach (var h in Firebase.FromFields(block?["fields"] as JsonObject).A("hashes"))
                    if (Regex.IsMatch(h, "^[0-9a-fA-F]{64}$")) hashes.Add(h.ToLowerInvariant());
            }
            catch (ServiceError e) when (e.Code is "NOT_FOUND") { }
            _down = down;
            _hashes = hashes;
            _loaded = DateTime.UtcNow;
            Changed?.Invoke();
        }
        catch (ServiceError) { }
        finally { Gate.Release(); }
    }

    public static bool IsDown(string kind, string id) => _down.Contains($"{kind}_{id}");
    public static bool IsBlocked(string? sha256) => sha256 is { Length: 64 } && _hashes.Contains(sha256);
    public static int BlockedCount => _hashes.Count;

    /// <summary>Отпечаток файла (sha256, строчными) — для чёрного списка.</summary>
    public static string Sha256(string file)
    {
        using var s = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
    }

    // ---------------------------------------------------------------- админ: очередь

    static Takedown? TakedownOf(JsonObject d)
    {
        var f = Firebase.FromFields(d["fields"] as JsonObject);
        if (f.S("kind") == "" || f.S("target") == "") return null;
        return new Takedown(f.S("kind"), f.S("target"), f.S("reason"), f.S("note"), f.S("sha256"), f.S("title"), f.S("owner"), Firebase.Time(f.S("at")));
    }

    static ModReport ReportOf(JsonObject d)
    {
        var name = d.Str("name") ?? "";
        var id = name[(name.LastIndexOf('/') + 1)..];
        var f = Firebase.FromFields(d["fields"] as JsonObject);
        // До 31.0: {creation}__{uid}, поле reason — свободный текст.
        if (f.S("kind") == "")
            return new ModReport(id, "creation", f.S("creation"), "", f.S("creation"), "other", f.S("reason"), f.S("uid"), "", "", "", Firebase.Time(f.S("created")));
        return new ModReport(id, f.S("kind"), f.S("target"), f.S("owner"), f.S("title"), f.S("reason"), f.S("text"), f.S("uid"),
            f.S("status"), f.S("action"), f.S("note"), Firebase.Time(f.S("created")));
    }

    static async Task<List<JsonObject>> List(string collection, string? token, int limit = 300, string? order = null)
    {
        var q = new JsonObject { ["from"] = new JsonArray(new JsonObject { ["collectionId"] = collection }), ["limit"] = limit };
        if (order is not null) q["orderBy"] = new JsonArray(new JsonObject { ["field"] = new JsonObject { ["fieldPath"] = order }, ["direction"] = "DESCENDING" });
        var rows = await Hub.Call(Firebase.Url(":runQuery"), HttpMethod.Post, new JsonObject { ["structuredQuery"] = q }, token);
        return (rows as JsonArray ?? []).Select(r => r?["document"] as JsonObject).OfType<JsonObject>().ToList();
    }

    static async Task<(string Uid, string Token)> Admin()
    {
        var (uid, token, _) = await Hub.Member();
        if (!Account.Get().Admin) throw new ServiceError("DENIED");
        return (uid, token);
    }

    /// <summary>Жалобы, по которым ещё ничего не решено (новые сверху), вместе со старыми без статуса.</summary>
    public static async Task<List<ModReport>> OpenReports()
    {
        var (_, token) = await Admin();
        return (await List("reports", token, 500, "created")).Select(ReportOf).Where(r => r.Status is "" or "open").ToList();
    }

    static JsonObject Write(string path, Dictionary<string, object?> fields, string[]? mask = null, bool? exists = null, params string[] serverTimes)
    {
        var w = new JsonObject { ["update"] = new JsonObject { ["name"] = Firebase.Doc(path), ["fields"] = Firebase.ToFields(fields) } };
        if (mask is not null) w["updateMask"] = new JsonObject { ["fieldPaths"] = new JsonArray(mask.Select(m => (JsonNode)m).ToArray()) };
        if (exists is not null) w["currentDocument"] = new JsonObject { ["exists"] = exists.Value };
        if (serverTimes.Length > 0) w["updateTransforms"] = new JsonArray(serverTimes.Select(t => (JsonNode)Hub.ServerTime(t)).ToArray());
        return w;
    }

    static JsonObject Delete(string path) => new() { ["delete"] = Firebase.Doc(path) };

    /// <summary>Закрыть жалобу: resolved (меры приняты) или dismissed (нарушения нет). Старые жалобы просто удаляются.</summary>
    public static async Task Resolve(ModReport r, bool acted, string action, string note)
    {
        var (uid, token) = await Admin();
        if (r.Legacy) { await Hub.Commit([Delete($"reports/{r.Id}")], token); return; }
        await Hub.Commit([Write($"reports/{r.Id}", new()
        {
            ["status"] = acted ? "resolved" : "dismissed", ["action"] = Clip(action, 40), ["note"] = Clip(note, 500), ["by"] = uid,
        }, ["status", "action", "note", "by", "resolved"], exists: true, "resolved")], token);
    }

    // ---------------------------------------------------------------- админ: снятие, возврат, бан

    /// <summary>
    /// Снять мод Hub или лот рынка: он пропадает из каталога у всех, его файл больше не ставится, купить
    /// лот нельзя, а автор не выложит его заново. Вирус — отпечаток файла ещё и в чёрный список.
    /// </summary>
    public static async Task TakeDown(string kind, string target, string reason, string note, string sha256 = "", string title = "", string owner = "")
    {
        if (kind is not ("creation" or "listing") || !TakedownReasons.Contains(reason) || !Regex.IsMatch(target, "^[A-Za-z0-9_]{1,140}$"))
            throw new ServiceError("INVALID");
        var (uid, token) = await Admin();
        sha256 = Regex.IsMatch(sha256 ?? "", "^[0-9a-fA-F]{64}$") ? sha256!.ToLowerInvariant() : "";
        await Hub.Commit([Write($"takedowns/{kind}_{target}", new()
        {
            ["kind"] = kind, ["target"] = target, ["reason"] = reason, ["note"] = Clip(note, 500), ["sha256"] = sha256,
            ["title"] = Clip(title, 120), ["owner"] = Clip(owner, 128), ["by"] = uid,
        }, serverTimes: "at"), Summary($"{kind}_{target}", add: true)], token);
        if (reason == "malware" && sha256 != "") await AddToBlocklist([sha256]);
        _loaded = default;
        await Refresh(force: true);
    }

    /// <summary>Вернуть снятое (жалоба не подтвердилась, автор прислал встречное уведомление).</summary>
    public static async Task Restore(string kind, string target)
    {
        var (_, token) = await Admin();
        await Hub.Commit([Delete($"takedowns/{kind}_{target}"), Summary($"{kind}_{target}", add: false)], token);
        await Refresh(force: true);
    }

    /// <summary>Админ: всё снятое с причинами (сама коллекция; людям хватает сводки config/takedowns).</summary>
    public static async Task<List<Takedown>> Takedowns()
    {
        var (_, token) = await Admin();
        return (await List("takedowns", token, 1000)).Select(TakedownOf).OfType<Takedown>().OrderByDescending(t => t.At).ToList();
    }

    /// <summary>Сводка снятого (config/takedowns.ids): добавить или убрать id той же пачкой записей, что и само снятие.</summary>
    static JsonObject Summary(string id, bool add) => new()
    {
        ["transform"] = new JsonObject
        {
            ["document"] = Firebase.Doc("config/takedowns"),
            ["fieldTransforms"] = new JsonArray(
                new JsonObject
                {
                    ["fieldPath"] = "ids",
                    [add ? "appendMissingElements" : "removeAllFromArray"] = new JsonObject { ["values"] = new JsonArray(Firebase.ToValue(id)) },
                },
                new JsonObject { ["fieldPath"] = "updated", ["setToServerValue"] = "REQUEST_TIME" }),
        },
    };

    /// <summary>Добавить отпечатки в чёрный список (до 5000; старые уходят первыми).</summary>
    public static async Task AddToBlocklist(IEnumerable<string> hashes)
    {
        var (uid, token) = await Admin();
        var list = new List<string>();
        try
        {
            var block = await Hub.Call(Firebase.Url("/config/blocklist"), HttpMethod.Get, null, token) as JsonObject;
            list.AddRange(Firebase.FromFields(block?["fields"] as JsonObject).A("hashes"));
        }
        catch (ServiceError e) when (e.Code is "NOT_FOUND") { }
        foreach (var h in hashes.Select(h => h.Trim().ToLowerInvariant()).Where(h => Regex.IsMatch(h, "^[0-9a-f]{64}$")))
            if (!list.Contains(h)) list.Add(h);
        if (list.Count > 5000) list = list[^5000..];
        await Hub.Commit([Write("config/blocklist", new() { ["hashes"] = list, ["by"] = uid }, serverTimes: "updated")], token);
        _loaded = default;
    }

    public static async Task BanUser(string target, string name, string note)
    {
        if (!Regex.IsMatch(target ?? "", "^[A-Za-z0-9]{6,128}$")) throw new ServiceError("INVALID");
        var (uid, token) = await Admin();
        if (target == uid) throw new ServiceError("OWN");
        await Hub.Commit([Write($"bans/{target}", new() { ["note"] = Clip(note, 500), ["name"] = Account.CleanName(name), ["by"] = uid }, serverTimes: "at")], token);
    }

    public static async Task Unban(string target)
    {
        var (_, token) = await Admin();
        await Hub.Commit([Delete($"bans/{target}")], token);
    }

    public static async Task<List<Ban>> Bans()
    {
        var (_, token) = await Admin();
        return (await List("bans", token, 500)).Select(d =>
        {
            var name = d.Str("name") ?? "";
            var f = Firebase.FromFields(d["fields"] as JsonObject);
            return new Ban(name[(name.LastIndexOf('/') + 1)..], f.S("name"), f.S("note"), Firebase.Time(f.S("at")));
        }).OrderByDescending(b => b.At).ToList();
    }

    /// <summary>Своя блокировка (null — не заблокирован) — чтобы объяснить человеку, почему не публикуется.</summary>
    public static async Task<Ban?> MyBan()
    {
        if (!Account.SignedIn) return null;
        var (uid, token) = await Account.Token();
        try
        {
            var d = await Hub.Call(Firebase.Url($"/bans/{uid}"), HttpMethod.Get, null, token) as JsonObject;
            var f = Firebase.FromFields(d?["fields"] as JsonObject);
            return new Ban(uid, f.S("name"), f.S("note"), Firebase.Time(f.S("at")));
        }
        catch (ServiceError e) when (e.Code is "NOT_FOUND" or "DENIED") { return null; }
    }

    public static string Explain(Exception e) => Firebase.Explain("safety", e);

    [SelfTest]
    static string SafetyIdsAndLists()
    {
        if (TargetId("seller1_lot") != "seller1_lot" || TargetId("steam:730") != "steam:730") throw new Exception("plain ids changed");
        var hashed = TargetId("steam:730|_game/with slash");
        if (!Regex.IsMatch(hashed, "^h[0-9a-f]{32}$") || hashed != TargetId("steam:730|_game/with slash")) throw new Exception("hash id: " + hashed);
        if (Reasons.Length != 9 || !TakedownReasons.Contains("copyright")) throw new Exception("reason lists");
        // Причины совпадают с правилами базы (иначе жалоба уйдёт в отказ).
        string rules;
        using (var stream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://ModLaunch/Assets/firestore.rules"))) rules = new StreamReader(stream).ReadToEnd();
        foreach (var r in Reasons) if (!rules.Contains($"'{r}'")) throw new Exception("reason not in rules: " + r);
        foreach (var k in Kinds) if (!rules.Contains($"'{k}'")) throw new Exception("kind not in rules: " + k);
        return $"ids safe ({hashed}), {Reasons.Length} reasons and {Kinds.Length} kinds match the rules";
    }
}
