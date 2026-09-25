using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;

namespace ModLaunch.Social;

public sealed record Review(string Id, string Uid, string Game, string Mod, string ModName, string Name, int Stars, string Text, string? Created, string? Updated, string? App, string? Role, bool Deleted);
public sealed record ReviewView(string Id, string Name, int Stars, string Text, DateTime? Created, DateTime? Updated, bool Mine, bool Admin, bool Played);
public sealed record Stat(double Avg, int Count);

/// <summary>
/// Отзывы и оценки: общая база в Firestore, копия на диске (reviews.json), догрузка
/// свежего по полю updated. Оценить можно только мод, с которым играли (см. PlayLog).
/// </summary>
public static partial class Reviews
{
    const int Page = 300;
    const int MaxText = 1000;
    const string Epoch = "1970-01-01T00:00:00Z";

    static readonly JsonFile File = new(Path.Combine(Paths.DataDir, "reviews.json"), () => new JsonObject { ["lastSync"] = Epoch, ["reviews"] = new JsonObject() });
    static DateTime _lastSync, _pausedUntil;
    static readonly SemaphoreSlim SyncGate = new(1);
    public static string? LastError { get; private set; }

    public static bool Configured => Firebase.Configured;

    public static string ModKey(string game, string mod) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{game}|{mod}"))).ToLowerInvariant()[..32];

    static string Clean(string? value, int max)
    {
        var s = (value ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        s = Regex.Replace(s, @"[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]", "");
        s = Regex.Replace(s, @"\n{3,}", "\n\n").Trim();
        return s.Length > max ? s[..max] : s;
    }

    static bool AppAtLeast(string? app, string since = "1.9.7")
    {
        static int[]? P(string? v) => Regex.Match(v ?? "", @"^(\d+)\.(\d+)(?:\.(\d+))?") is { Success: true } m
            ? [int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0] : null;
        var have = P(app);
        var need = P(since);
        if (have is null || need is null) return false;
        for (var i = 0; i < 3; i++) if (have[i] != need[i]) return have[i] > need[i];
        return true;
    }

    static JsonObject Store => File.Data.Obj("reviews");

    static async Task<JsonNode?> Call(string url, HttpMethod method, JsonNode? body = null, string? token = null)
    {
        if (_pausedUntil > DateTime.UtcNow) throw new ServiceError("BUSY", "paused");
        var reply = await Firebase.Send(url, method, body, token);
        if ((int)reply.Status is >= 200 and < 300) return reply.Body;
        var reason = Firebase.Reason(reply);
        var status = (int)reply.Status;
        if (status is 429 or 503 || Regex.IsMatch(reason, "RESOURCE_EXHAUSTED|quota|billing|exceeded|UNAVAILABLE|TOO_MANY_ATTEMPTS", RegexOptions.IgnoreCase))
        {
            _pausedUntil = DateTime.UtcNow.AddMinutes(30);
            throw new ServiceError("BUSY", reason);
        }
        if (Regex.IsMatch(reason, "OPERATION_NOT_ALLOWED|ADMIN_ONLY_OPERATION|CONFIGURATION_NOT_FOUND|has not been used|is disabled", RegexOptions.IgnoreCase)) throw new ServiceError("AUTH_DISABLED", reason);
        if (Regex.IsMatch(reason, "API.?key", RegexOptions.IgnoreCase)) throw new ServiceError("BAD_KEY", reason);
        if (status == 404 && Regex.IsMatch(reason, @"database\s+\S+\s+does not exist|does not exist for project", RegexOptions.IgnoreCase)) throw new ServiceError("NO_DATABASE", reason);
        if (status == 404) throw new ServiceError("NOT_FOUND", reason);
        if (status == 403 || reason.Contains("PERMISSION_DENIED")) throw new ServiceError("DENIED", reason);
        throw new ServiceError("SERVER", reason);
    }

    static async Task<(string Uid, string IdToken)> Token()
    {
        if (!Configured) throw new ServiceError("NOT_CONFIGURED");
        try { return await Account.Token(); }
        catch (ServiceError e) { throw new ServiceError(e.Code switch { "OFFLINE" => "OFFLINE", "BUSY" or "TOO_MANY" => "BUSY", "NOT_ENABLED" => "AUTH_DISABLED", "BAD_KEY" => "BAD_KEY", "SESSION" => "SESSION", _ => "SERVER" }, e.Message); }
    }

    // ---------------------------------------------------------------- чтение

    /// <summary>Догрузить изменения с сервера (раз в неделю — целиком, чтобы убрать удалённое).</summary>
    public static async Task Sync(bool force = false)
    {
        if (!Configured) return;
        if (!force && DateTime.UtcNow - _lastSync < TimeSpan.FromSeconds(20)) return;
        await SyncGate.WaitAsync();
        try
        {
            var full = DateTime.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(File.Data.Long("lastFull")).UtcDateTime > TimeSpan.FromDays(7);
            var since = full ? Epoch : File.Data.Str("lastSync") ?? Epoch;
            var seen = new HashSet<string>();
            var complete = false;
            for (var round = 0; round < 50; round++)
            {
                var rows = await Call(Firebase.Url(":runQuery"), HttpMethod.Post, new JsonObject
                {
                    ["structuredQuery"] = new JsonObject
                    {
                        ["from"] = new JsonArray(new JsonObject { ["collectionId"] = "reviews" }),
                        ["where"] = new JsonObject { ["fieldFilter"] = new JsonObject { ["field"] = new JsonObject { ["fieldPath"] = "updated" }, ["op"] = "GREATER_THAN_OR_EQUAL", ["value"] = new JsonObject { ["timestampValue"] = since } } },
                        ["orderBy"] = new JsonArray(new JsonObject { ["field"] = new JsonObject { ["fieldPath"] = "updated" }, ["direction"] = "ASCENDING" }),
                        ["limit"] = Page,
                    },
                });
                var docs = (rows as JsonArray ?? []).Select(r => r?["document"]).OfType<JsonNode>().ToList();
                var progressed = 0;
                foreach (var doc in docs)
                {
                    var id = doc.Str("name")!.Split('/')[^1];
                    if (seen.Add(id)) progressed++;
                    var fields = (doc["fields"] as JsonObject ?? new JsonObject());
                    var record = new JsonObject { ["id"] = id };
                    foreach (var (k, v) in Firebase.FromFields(fields))
                        record[k] = v switch { null => null, string s => s, long l => l, double d => d, bool b => b, _ => v.ToString() };
                    Store[id] = record;
                    var updated = record.Str("updated");
                    if (updated is not null && Firebase.Micros(updated) > Firebase.Micros(since)) since = updated;
                }
                if (docs.Count < Page || progressed == 0) { complete = true; break; }
            }
            if (full && complete)
            {
                foreach (var id in Store.Select(kv => kv.Key).Where(id => !seen.Contains(id)).ToList()) Store.Remove(id);
                File.Data["lastFull"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
            File.Data["lastSync"] = since;
            File.Save();
            _lastSync = DateTime.UtcNow;
            LastError = null;
        }
        catch (ServiceError e) { LastError = e.Code; throw; }
        finally { SyncGate.Release(); }
    }

    static Review From(JsonNode r) => new(r.Str("id") ?? "", r.Str("uid") ?? "", r.Str("game") ?? "", r.Str("mod") ?? "", r.Str("modName") ?? "", r.Str("name") ?? "—",
        (int)r.Long("stars"), r.Str("text") ?? "", r.Str("created"), r.Str("updated"), r.Str("app"), r.Str("role"), r.Bool("deleted"));

    public static List<Review> List(string? game = null, string? mod = null) =>
        Store.Select(kv => kv.Value).OfType<JsonNode>().Select(From)
            .Where(r => !r.Deleted && r.Stars >= 1 && (game is null || r.Game == game) && (mod is null || r.Mod == mod))
            .OrderByDescending(r => Firebase.Micros(r.Updated)).ToList();

    public static Review? Mine(string game, string mod) => Account.Uid is string uid ? List(game, mod).FirstOrDefault(r => r.Uid == uid) : null;

    public static List<ReviewView> ForMod(string game, string mod)
    {
        var uid = Account.Uid;
        return List(game, mod).Select(r => new ReviewView(r.Id, r.Name, r.Stars, r.Text, Firebase.Time(r.Created), Firebase.Time(r.Updated),
            uid is not null && r.Uid == uid, r.Role == "admin", AppAtLeast(r.App))).ToList();
    }

    public static Dictionary<string, Stat> Stats() =>
        List().GroupBy(r => $"{r.Game}|{r.Mod}").ToDictionary(g => g.Key, g => new Stat(Math.Round(g.Average(r => r.Stars), 2), g.Count()));

    // ---------------------------------------------------------------- запись

    static async Task Commit(string docId, Dictionary<string, object?> fields, bool create, string token)
    {
        var write = new JsonObject
        {
            ["update"] = new JsonObject { ["name"] = Firebase.Doc($"reviews/{docId}"), ["fields"] = Firebase.ToFields(fields) },
            ["updateTransforms"] = new JsonArray(new JsonObject { ["fieldPath"] = "updated", ["setToServerValue"] = "REQUEST_TIME" }),
            ["currentDocument"] = new JsonObject { ["exists"] = !create },
        };
        if (create) ((JsonArray)write["updateTransforms"]!).Add(new JsonObject { ["fieldPath"] = "created", ["setToServerValue"] = "REQUEST_TIME" });
        else
        {
            var paths = fields.Keys.ToList();
            if (!paths.Contains("role")) paths.Add("role");
            write["updateMask"] = new JsonObject { ["fieldPaths"] = new JsonArray(paths.Select(p => (JsonNode)p).ToArray()) };
        }
        await Call(Firebase.Url(":commit"), HttpMethod.Post, new JsonObject { ["writes"] = new JsonArray(write) }, token);
    }

    static async Task<bool> Exists(string docId)
    {
        try { await Call(Firebase.Url($"/reviews/{docId}"), HttpMethod.Get); return true; }
        catch (ServiceError e) when (e.Code == "NOT_FOUND") { return false; }
    }

    public static async Task Submit(string game, string mod, string modName, int stars, string text, string name, bool played)
    {
        game = Clean(game, 40);
        mod = Clean(mod, 200);
        name = Regex.Replace(Clean(name, 32), @"\s+", " ");
        text = Clean(text, MaxText);
        if (game == "" || mod == "" || stars is < 1 or > 5 || name == "") throw new ServiceError("BAD_INPUT");
        var (uid, token) = await Token();
        var key = ModKey(game, mod);
        var docId = $"{uid}_{key}";
        var fields = new Dictionary<string, object?>
        {
            ["game"] = game, ["mod"] = mod, ["modName"] = Clean(modName == "" ? mod : modName, 200), ["key"] = key, ["uid"] = uid,
            ["name"] = name, ["stars"] = stars, ["text"] = text, ["deleted"] = false,
        };
        if (played) fields["app"] = Http.Version;
        if (Account.Get().Admin) fields["role"] = "admin";
        var create = !await Exists(docId);
        await Commit(docId, fields, create, token);
        try { await Sync(force: true); } catch { }
        if (Store[docId] is not JsonObject saved || saved.Bool("deleted") || saved.Long("stars") != stars || saved.Str("text") != text)
        {
            var now = DateTime.UtcNow.ToString("o");
            var record = new JsonObject { ["id"] = docId, ["created"] = (Store[docId] as JsonObject).Str("created") ?? now, ["updated"] = now };
            foreach (var (k, v) in fields) record[k] = v switch { null => null, string s => s, int i => i, bool b => b, _ => v.ToString() };
            Store[docId] = record;
            File.Save();
        }
    }

    public static async Task Remove(string game, string mod)
    {
        var (uid, token) = await Token();
        var key = ModKey(game, mod);
        var docId = $"{uid}_{key}";
        var current = Store[docId] as JsonObject;
        await Commit(docId, new Dictionary<string, object?>
        {
            ["game"] = game, ["mod"] = mod, ["modName"] = current.Str("modName") ?? mod, ["key"] = key, ["uid"] = uid,
            ["name"] = current.Str("name") ?? "—", ["stars"] = 0, ["text"] = "", ["deleted"] = true, ["app"] = Http.Version,
        }, create: false, token);
        if (current is not null) { current["deleted"] = true; current["stars"] = 0; current["text"] = ""; File.Save(); }
        try { await Sync(force: true); } catch { }
    }

    /// <summary>Скрыть чужой отзыв (только администратор — это проверяют правила базы).</summary>
    public static async Task Moderate(string reviewId)
    {
        if (!Regex.IsMatch(reviewId, "^[A-Za-z0-9]{6,128}_[0-9a-f]{32}$")) throw new ServiceError("BAD_INPUT");
        var (_, token) = await Token();
        await Call(Firebase.Url(":commit"), HttpMethod.Post, new JsonObject
        {
            ["writes"] = new JsonArray(new JsonObject
            {
                ["update"] = new JsonObject { ["name"] = Firebase.Doc($"reviews/{reviewId}"), ["fields"] = Firebase.ToFields(new Dictionary<string, object?> { ["deleted"] = true, ["stars"] = 0, ["text"] = "", ["moderated"] = true }) },
                ["updateMask"] = new JsonObject { ["fieldPaths"] = new JsonArray("deleted", "stars", "text", "moderated") },
                ["updateTransforms"] = new JsonArray(new JsonObject { ["fieldPath"] = "updated", ["setToServerValue"] = "REQUEST_TIME" }),
                ["currentDocument"] = new JsonObject { ["exists"] = true },
            }),
        }, token);
        if (Store[reviewId] is JsonObject local) { local["deleted"] = true; local["stars"] = 0; local["text"] = ""; File.Save(); }
        try { await Sync(force: true); } catch { }
    }

    public static string Explain(Exception e) => Firebase.Explain("reviews", e);
}
