using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;

namespace ModLaunch.Social;

public sealed record Friend(string Uid, string Name, string State, string Game, string GameName, DateTime? Since, DateTime? Seen);
public sealed record Request(string Uid, string Name, DateTime? At);
public sealed record FriendsView(bool Configured, bool SignedIn, string? Code, string Mode, List<Friend> Friends, List<Request> Incoming, List<Request> Outgoing, string? Error);

/// <summary>
/// Друзья: код друга, запросы и «кто в сети, во что играет» — Firestore, как в 3.x.
/// Профиль обновляется раз в пять минут; «в сети» — если отметка свежее шести.
/// </summary>
public static partial class Friends
{
    const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(6);
    static readonly TimeSpan Beat = TimeSpan.FromMinutes(5);
    static readonly TimeSpan MinRefresh = TimeSpan.FromSeconds(30);
    static readonly TimeSpan LinksTtl = TimeSpan.FromMinutes(5);

    [GeneratedRegex("^[A-HJ-NP-Z2-9]{8}$")] private static partial Regex CodeRe();

    static readonly JsonFile File = new(Path.Combine(Paths.DataDir, "friends.json"), () => new JsonObject());
    static (string Key, string Uid, DateTime At)? _published;
    static List<Dictionary<string, object?>>? _links;
    static DateTime _linksAt, _lastRefresh, _pausedUntil;
    static string? _lastError;
    static (string State, string Game, string GameName) _presence = ("online", "", "");

    public static event Action? Changed;

    public static string Mode
    {
        get => Settings.Data.Str("friendsStatus") is "online" or "hidden" ? Settings.Data.Str("friendsStatus")! : "all";
        set { Settings.Data["friendsStatus"] = value; Settings.Save(); _ = BeatNow(force: true); }
    }

    public static string? Normalize(string? input)
    {
        var code = Regex.Replace((input ?? "").ToUpperInvariant(), @"[\s\-_.]", "");
        return CodeRe().IsMatch(code) ? code : null;
    }

    public static string? Format(string? code) => code is { Length: 8 } ? $"{code[..4]}-{code[4..]}" : null;

    static string Pair(string a, string b) => string.CompareOrdinal(a, b) < 0 ? $"{a}_{b}" : $"{b}_{a}";

    // ---------------------------------------------------------------- сеть

    static async Task<JsonNode?> Call(string url, HttpMethod method, JsonNode? body, string token)
    {
        if (_pausedUntil > DateTime.UtcNow) throw new ServiceError("BUSY", "paused");
        var reply = await Firebase.Send(url, method, body, token);
        if ((int)reply.Status is >= 200 and < 300) return reply.Body;
        var reason = Firebase.Reason(reply);
        var status = (int)reply.Status;
        if (status is 429 or 503 || Regex.IsMatch(reason, "RESOURCE_EXHAUSTED|quota|billing|UNAVAILABLE", RegexOptions.IgnoreCase))
        {
            _pausedUntil = DateTime.UtcNow.AddMinutes(10);
            throw new ServiceError("BUSY", reason);
        }
        if (reason.Contains("ALREADY_EXISTS") || status == 409) throw new ServiceError("EXISTS", reason);
        if (reason.Contains("FAILED_PRECONDITION")) throw new ServiceError("PRECONDITION", reason);
        if (status == 404) throw new ServiceError("NOT_FOUND", reason);
        if (status == 403 || reason.Contains("PERMISSION_DENIED")) throw new ServiceError("DENIED", reason);
        if (Regex.IsMatch(reason, "API.?key", RegexOptions.IgnoreCase)) throw new ServiceError("BAD_KEY", reason);
        throw new ServiceError("SERVER", reason);
    }

    sealed record Me(string Uid, string Token, string Name);

    static async Task<Me> Auth()
    {
        if (!Firebase.Configured) throw new ServiceError("NOT_CONFIGURED");
        if (!Account.SignedIn) throw new ServiceError("SIGN_IN");
        (string Uid, string IdToken) token;
        try { token = await Account.Token(); }
        catch (ServiceError e) { throw new ServiceError(e.Code switch { "OFFLINE" => "OFFLINE", "BUSY" or "TOO_MANY" => "BUSY", "SESSION" => "SIGN_IN", "BAD_KEY" => "BAD_KEY", _ => "SERVER" }, e.Message); }
        if (File.Data.Str("uid") != token.Uid)
        {
            File.Data.Clear();
            File.Data["uid"] = token.Uid;
            File.Save();
            _published = null;
            _links = null;
        }
        var name = Account.CleanName(Account.Get().Name) is { Length: > 0 } n ? n : "ModLaunch";
        return new Me(token.Uid, token.IdToken, name);
    }

    static Task<JsonNode?> Commit(JsonArray writes, Me me) => Call(Firebase.Url(":commit"), HttpMethod.Post, new JsonObject { ["writes"] = writes }, me.Token);

    static async Task<Dictionary<string, object?>?> GetDoc(string relative, Me me)
    {
        try { return Firebase.FromFields((await Call(Firebase.Url("/" + relative), HttpMethod.Get, null, me.Token))?["fields"] as JsonObject); }
        catch (ServiceError e) when (e.Code == "NOT_FOUND") { return null; }
    }

    static JsonObject Server(string field) => new() { ["fieldPath"] = field, ["setToServerValue"] = "REQUEST_TIME" };

    // ---------------------------------------------------------------- код друга

    public static async Task<string> MyCode()
    {
        var me = await Auth();
        if (File.Data.Str("code") is string known) return Format(known)!;
        var profile = await GetDoc($"profiles/{me.Uid}", me);
        if (profile is not null && CodeRe().IsMatch(profile.S("code")))
        {
            var owner = await GetDoc($"codes/{profile.S("code")}", me);
            if (owner?.S("uid") == me.Uid) return Keep(profile.S("code"));
        }
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var code = new string(RandomNumberGenerator.GetBytes(8).Select(b => Alphabet[b % Alphabet.Length]).ToArray());
            try
            {
                await Commit(new JsonArray(new JsonObject
                {
                    ["update"] = new JsonObject { ["name"] = Firebase.Doc($"codes/{code}"), ["fields"] = Firebase.ToFields(new Dictionary<string, object?> { ["uid"] = me.Uid, ["name"] = me.Name }) },
                    ["updateTransforms"] = new JsonArray(Server("created")),
                    ["currentDocument"] = new JsonObject { ["exists"] = false },
                }), me);
                var shown = Keep(code);
                _published = null;
                try { await BeatNow(); } catch { }
                return shown;
            }
            catch (ServiceError e) when (e.Code is "EXISTS" or "PRECONDITION") { }
        }
        throw new ServiceError("SERVER", "code");
    }

    static string Keep(string code)
    {
        File.Data["code"] = code;
        File.Save();
        return Format(code)!;
    }

    // ---------------------------------------------------------------- «в сети», «в игре»

    public static void SetActivity(string state, string game = "", string gameName = "")
    {
        _presence = (state, state == "playing" ? game : "", state == "playing" ? gameName : "");
        _ = BeatNow(force: true).ContinueWith(_ => { });
    }

    static (string State, string Game, string GameName) Shown()
    {
        var (state, game, name) = _presence;
        if (Mode == "hidden" || state == "offline") return ("offline", "", "");
        if (Mode == "online" || state != "playing") return ("online", "", "");
        return (state, game, name);
    }

    /// <summary>Отметка «я в сети» (не чаще раза в пять минут, если ничего не поменялось).</summary>
    public static async Task<bool> BeatNow(bool force = false)
    {
        if (!Firebase.Configured || !Account.SignedIn) return false;
        var me = await Auth();
        var shown = Shown();
        var key = $"{shown.State}|{shown.Game}";
        var changed = _published is not { } p || p.Key != key || p.Uid != me.Uid;
        if (!changed && !force && shown.State == "offline") return false;
        if (!changed && !force && DateTime.UtcNow - _published!.Value.At < Beat - TimeSpan.FromSeconds(15)) return false;
        var fields = new Dictionary<string, object?>
        {
            ["name"] = me.Name,
            ["code"] = File.Data.Str("code") ?? "",
            ["state"] = shown.State,
            ["game"] = shown.Game,
            ["gameName"] = shown.GameName,
            ["app"] = Http.Version,
        };
        var transforms = new JsonArray(Server("seen"));
        if (changed) transforms.Add(Server("since"));
        await Commit(new JsonArray(new JsonObject
        {
            ["update"] = new JsonObject { ["name"] = Firebase.Doc($"profiles/{me.Uid}"), ["fields"] = Firebase.ToFields(fields) },
            ["updateMask"] = new JsonObject { ["fieldPaths"] = new JsonArray(fields.Keys.Select(k => (JsonNode)k).ToArray()) },
            ["updateTransforms"] = transforms,
        }), me);
        _published = (key, me.Uid, DateTime.UtcNow);
        return true;
    }

    public static async Task GoOffline()
    {
        if (_published is not { } p || p.Key.StartsWith("offline|")) return;
        var before = _presence;
        _presence = ("offline", "", "");
        try { await BeatNow(force: true); }
        finally { _presence = before.State == "offline" ? ("online", "", "") : before; _published = null; }
    }

    // ---------------------------------------------------------------- список

    public static FriendsView View()
    {
        var signedIn = Account.SignedIn;
        var mine = signedIn && File.Data.Str("uid") == Account.Uid;
        var view = File.Data["view"];
        List<Friend> friends = [];
        List<Request> incoming = [], outgoing = [];
        if (mine && view is JsonObject v)
        {
            friends = v.Arr("friends").Select(f => new Friend(f.Str("uid") ?? "", f.Str("name") ?? "—", f.Str("state") ?? "offline", f.Str("game") ?? "", f.Str("gameName") ?? "", Firebase.Time(f.Str("since")), Firebase.Time(f.Str("seen")))).ToList();
            incoming = v.Arr("incoming").Select(r => new Request(r.Str("uid") ?? "", r.Str("name") ?? "—", Firebase.Time(r.Str("at")))).ToList();
            outgoing = v.Arr("outgoing").Select(r => new Request(r.Str("uid") ?? "", r.Str("name") ?? "—", Firebase.Time(r.Str("at")))).ToList();
        }
        return new FriendsView(Firebase.Configured, signedIn, mine ? Format(File.Data.Str("code")) : null, Mode, friends, incoming, outgoing, _lastError);
    }

    static readonly SemaphoreSlim RefreshGate = new(1);

    public static async Task<FriendsView> Refresh(bool force = false)
    {
        if (!force && File.Data["view"] is not null && DateTime.UtcNow - _lastRefresh < MinRefresh) return View();
        await RefreshGate.WaitAsync();
        try
        {
            var me = await Auth();
            if (force || _links is null || DateTime.UtcNow - _linksAt > LinksTtl)
            {
                var rows = await Call(Firebase.Url(":runQuery"), HttpMethod.Post, new JsonObject
                {
                    ["structuredQuery"] = new JsonObject
                    {
                        ["from"] = new JsonArray(new JsonObject { ["collectionId"] = "friendships" }),
                        ["where"] = new JsonObject { ["fieldFilter"] = new JsonObject { ["field"] = new JsonObject { ["fieldPath"] = "users" }, ["op"] = "ARRAY_CONTAINS", ["value"] = new JsonObject { ["stringValue"] = me.Uid } } },
                        ["limit"] = 200,
                    },
                }, me.Token);
                _links = (rows as JsonArray ?? [])
                    .Select(r => r?["document"]).OfType<JsonNode>()
                    .Select(doc => { var f = Firebase.FromFields(doc["fields"] as JsonObject); f["id"] = doc.Str("name")?.Split('/')[^1]; return f; })
                    .Where(l => l.A("users").Contains(me.Uid))
                    .ToList();
                _linksAt = DateTime.UtcNow;
            }
            string Other(Dictionary<string, object?> l) => l.A("users").FirstOrDefault(u => u != me.Uid) ?? "";
            var accepted = _links.Where(l => l.S("status") == "accepted" && Other(l) != "").ToList();

            var profiles = new Dictionary<string, Dictionary<string, object?>?>();
            foreach (var chunk in accepted.Chunk(100))
            {
                var rows = await Call(Firebase.Url(":batchGet"), HttpMethod.Post, new JsonObject
                {
                    ["documents"] = new JsonArray(chunk.Select(l => (JsonNode)Firebase.Doc($"profiles/{Other(l)}")).ToArray()),
                }, me.Token);
                foreach (var row in rows as JsonArray ?? [])
                {
                    var name = row?["found"].Str("name") ?? row.Str("missing");
                    if (name is null) continue;
                    profiles[name.Split('/')[^1]] = row?["found"] is JsonNode found ? Firebase.FromFields(found["fields"] as JsonObject) : null;
                }
            }

            var serverNow = DateTime.UtcNow + Firebase.Skew;
            DateTime? Local(string? v) => Firebase.Time(v) is DateTime t ? t - Firebase.Skew : null;
            var rank = new Dictionary<string, int> { ["playing"] = 0, ["online"] = 1, ["offline"] = 2 };
            var friends = accepted.Select(l =>
            {
                var uid = Other(l);
                var profile = profiles.GetValueOrDefault(uid);
                var seen = Firebase.Time(profile?.S("seen"));
                var online = profile is not null && profile.S("state") != "offline" && seen is DateTime s && serverNow - s < OnlineWindow;
                var playing = online && profile!.S("state") == "playing" && profile.S("game") != "";
                var name = Account.CleanName(profile?.S("name")) is { Length: > 0 } pn ? pn : Account.CleanName(l.S("from") == uid ? l.S("fromName") : l.S("toName"));
                return new JsonObject
                {
                    ["uid"] = uid,
                    ["name"] = name == "" ? "—" : name,
                    ["state"] = playing ? "playing" : online ? "online" : "offline",
                    ["game"] = playing ? profile!.S("game") : "",
                    ["gameName"] = playing ? (profile!.S("gameName") is { Length: > 0 } gn ? gn : profile.S("game")) : "",
                    ["since"] = online ? Local(profile!.S("since"))?.ToString("o") : null,
                    ["seen"] = seen is null ? null : Local(profile!.S("seen"))?.ToString("o"),
                };
            }).OrderBy(f => rank[f.Str("state")!]).ThenBy(f => f.Str("name"), StringComparer.CurrentCultureIgnoreCase).ToList();

            JsonObject Req(Dictionary<string, object?> l, string uidKey, string nameKey) => new()
            {
                ["uid"] = l.S(uidKey),
                ["name"] = Account.CleanName(l.S(nameKey)) is { Length: > 0 } n ? n : "—",
                ["at"] = Local(l.S("created"))?.ToString("o"),
            };
            File.Data["view"] = new JsonObject
            {
                ["friends"] = new JsonArray(friends.ToArray<JsonNode>()),
                ["incoming"] = new JsonArray(_links.Where(l => l.S("status") == "pending" && l.S("to") == me.Uid).Select(l => (JsonNode)Req(l, "from", "fromName")).ToArray()),
                ["outgoing"] = new JsonArray(_links.Where(l => l.S("status") == "pending" && l.S("from") == me.Uid).Select(l => (JsonNode)Req(l, "to", "toName")).ToArray()),
                ["at"] = DateTime.UtcNow.ToString("o"),
            };
            File.Save();
            _lastRefresh = DateTime.UtcNow;
            _lastError = null;
            Changed?.Invoke();
            return View();
        }
        catch (ServiceError e) { _lastError = e.Code; throw; }
        finally { RefreshGate.Release(); }
    }

    public static async Task<(string Status, string Name)> Add(string input)
    {
        var code = Normalize(input) ?? throw new ServiceError("BAD_CODE");
        var me = await Auth();
        var target = await GetDoc($"codes/{code}", me);
        var targetUid = target?.S("uid") ?? "";
        if (targetUid == "") throw new ServiceError("NO_SUCH_CODE");
        if (targetUid == me.Uid) throw new ServiceError("SELF");
        var name = Account.CleanName(target!.S("name")) is { Length: > 0 } n ? n : "—";
        var id = Pair(me.Uid, targetUid);
        var users = string.CompareOrdinal(me.Uid, targetUid) < 0 ? new[] { me.Uid, targetUid } : new[] { targetUid, me.Uid };
        try
        {
            await Commit(new JsonArray(new JsonObject
            {
                ["update"] = new JsonObject
                {
                    ["name"] = Firebase.Doc($"friendships/{id}"),
                    ["fields"] = Firebase.ToFields(new Dictionary<string, object?> { ["users"] = users, ["from"] = me.Uid, ["to"] = targetUid, ["fromName"] = me.Name, ["toName"] = name, ["status"] = "pending" }),
                },
                ["updateTransforms"] = new JsonArray(Server("created"), Server("updated")),
                ["currentDocument"] = new JsonObject { ["exists"] = false },
            }), me);
            try { await Refresh(force: true); } catch { }
            return ("sent", name);
        }
        catch (ServiceError e) when (e.Code is "EXISTS" or "PRECONDITION" or "DENIED")
        {
            Dictionary<string, object?>? link = null;
            try { link = await GetDoc($"friendships/{id}", me); } catch (ServiceError r) when (r.Code == "DENIED") { }
            if (link is null) throw;
            if (link.S("status") == "accepted") return ("already", name);
            if (link.S("to") == me.Uid) { await Accept(targetUid); return ("accepted", name); }
            return ("pending", name);
        }
    }

    public static async Task Accept(string uid)
    {
        var me = await Auth();
        await Commit(new JsonArray(new JsonObject
        {
            ["update"] = new JsonObject { ["name"] = Firebase.Doc($"friendships/{Pair(me.Uid, uid)}"), ["fields"] = Firebase.ToFields(new Dictionary<string, object?> { ["status"] = "accepted", ["toName"] = me.Name }) },
            ["updateMask"] = new JsonObject { ["fieldPaths"] = new JsonArray("status", "toName") },
            ["updateTransforms"] = new JsonArray(Server("updated")),
            ["currentDocument"] = new JsonObject { ["exists"] = true },
        }), me);
        try { await Refresh(force: true); } catch { }
    }

    public static async Task Remove(string uid)
    {
        var me = await Auth();
        await Commit(new JsonArray(new JsonObject { ["delete"] = Firebase.Doc($"friendships/{Pair(me.Uid, uid)}") }), me);
        try { await Refresh(force: true); } catch { }
    }

    /// <summary>Перед удалением аккаунта: убрать дружбы, профиль и код.</summary>
    public static async Task Forget()
    {
        var me = await Auth();
        FriendsView view;
        try { view = await Refresh(force: true); } catch { view = View(); }
        var writes = view.Friends.Select(f => f.Uid).Concat(view.Incoming.Select(r => r.Uid)).Concat(view.Outgoing.Select(r => r.Uid))
            .Select(uid => (JsonNode)new JsonObject { ["delete"] = Firebase.Doc($"friendships/{Pair(me.Uid, uid)}") }).ToList();
        writes.Add(new JsonObject { ["delete"] = Firebase.Doc($"profiles/{me.Uid}") });
        if (File.Data.Str("code") is string code) writes.Add(new JsonObject { ["delete"] = Firebase.Doc($"codes/{code}") });
        foreach (var chunk in writes.Chunk(400)) await Commit(new JsonArray(chunk.Select(w => w.DeepClone()).ToArray()), me);
        File.Data.Clear();
        File.Save();
        _published = null;
        _links = null;
    }

    public static string Explain(Exception e) => Firebase.Explain("friends", e);
}
