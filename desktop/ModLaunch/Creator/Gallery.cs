using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Social;

namespace ModLaunch.Creator;

public sealed record Creation(string Id, string Uid, string Author, string Name, string About, string Game, string Version, string Code, DateTime? Updated, bool Mine);

/// <summary>
/// Галерея Creator Hub: моды сообщества в Firestore (коллекция creations).
/// Выкладывается исходник на ModScript — у другого человека мод собирается
/// из него на месте, чужие файлы не скачиваются. Правила — firebase/creations.rules.
/// </summary>
public static class Gallery
{
    public const int MaxCode = 20000;

    static async Task<JsonNode?> Call(string url, HttpMethod method, JsonNode? body, string? token)
    {
        var reply = await Firebase.Send(url, method, body, token);
        if ((int)reply.Status is >= 200 and < 300) return reply.Body;
        var reason = Firebase.Reason(reply);
        var status = (int)reply.Status;
        if (status is 429 or 503) throw new ServiceError("BUSY", reason);
        if (status == 404) throw new ServiceError("NOT_FOUND", reason);
        if (status == 403 || reason.Contains("PERMISSION_DENIED")) throw new ServiceError("DENIED", reason);
        throw new ServiceError("SERVER", reason);
    }

    public static string DocId(string uid, string name)
    {
        var id = $"{uid}_{Projects.PackageName(name)}".ToLowerInvariant();
        return id.Length > 120 ? id[..120] : id;
    }

    /// <summary>Свежие моды сообщества (по дате обновления), по желанию — одной игры.</summary>
    public static async Task<List<Creation>> Browse(string? game = null, CancellationToken ct = default)
    {
        if (!Firebase.Configured) throw new ServiceError("NOT_CONFIGURED");
        var query = new JsonObject
        {
            ["from"] = new JsonArray(new JsonObject { ["collectionId"] = "creations" }),
            ["orderBy"] = new JsonArray(new JsonObject { ["field"] = new JsonObject { ["fieldPath"] = "updated" }, ["direction"] = "DESCENDING" }),
            ["limit"] = 60,
        };
        var rows = await Call(Firebase.Url(":runQuery"), HttpMethod.Post, new JsonObject { ["structuredQuery"] = query }, null);
        var me = Account.Uid;
        var list = new List<Creation>();
        foreach (var row in rows as JsonArray ?? [])
        {
            if (row?["document"] is not JsonObject doc) continue;
            var f = Firebase.FromFields(doc["fields"] as JsonObject);
            var name = doc.Str("name") ?? "";
            var c = new Creation(name[(name.LastIndexOf('/') + 1)..], f.S("uid"), f.S("author"), f.S("name"), f.S("about"), f.S("game"),
                f.S("version"), f.S("code"), Firebase.Time(f.S("updated")), me is not null && f.S("uid") == me);
            if (game is null || c.Game == game) list.Add(c);
        }
        return list;
    }

    /// <summary>Выложить (или обновить) свой мод. Нужен аккаунт ModLaunch с почтой.</summary>
    public static async Task<string> Publish(ModBuild b, string code)
    {
        if (!Firebase.Configured) throw new ServiceError("NOT_CONFIGURED");
        if (!Account.SignedIn) throw new ServiceError("SIGN_IN");
        if (!b.Ok) throw new ServiceError("INVALID");
        if (code.Length > MaxCode) throw new ServiceError("TOO_BIG");
        var (uid, token) = await Account.Token();
        var author = Account.CleanName(Account.Get().Name) is { Length: > 0 } n ? n : "ModLaunch";
        var id = DocId(uid, b.Name);
        var path = Firebase.Doc($"creations/{id}");
        var exists = true;
        try { await Call(Firebase.Url($"/creations/{id}"), HttpMethod.Get, null, token); }
        catch (ServiceError e) when (e.Code == "NOT_FOUND") { exists = false; }

        var fields = Firebase.ToFields(new Dictionary<string, object?>
        {
            ["uid"] = uid,
            ["author"] = author,
            ["name"] = Clip(b.Name, 60),
            ["about"] = Clip(b.About, 300),
            ["game"] = Clip(b.Game, 40),
            ["version"] = Clip(b.Version, 20),
            ["code"] = code,
            ["app"] = Http.Version,
        });
        var transforms = new JsonArray(new JsonObject { ["fieldPath"] = "updated", ["setToServerValue"] = "REQUEST_TIME" });
        if (!exists) transforms.Add(new JsonObject { ["fieldPath"] = "created", ["setToServerValue"] = "REQUEST_TIME" });
        var write = new JsonObject
        {
            ["update"] = new JsonObject { ["name"] = path, ["fields"] = fields },
            ["updateTransforms"] = transforms,
        };
        if (exists)
        {
            write["updateMask"] = new JsonObject { ["fieldPaths"] = new JsonArray("uid", "author", "name", "about", "game", "version", "code", "app") };
            write["currentDocument"] = new JsonObject { ["exists"] = true };
        }
        else write["currentDocument"] = new JsonObject { ["exists"] = false };
        await Call(Firebase.Url(":commit"), HttpMethod.Post, new JsonObject { ["writes"] = new JsonArray(write) }, token);
        return id;
    }

    public static async Task Unpublish(string id)
    {
        var (_, token) = await Account.Token();
        await Call(Firebase.Url($"/creations/{id}"), HttpMethod.Delete, null, token);
    }

    static string Clip(string s, int max)
    {
        s = Regex.Replace(s ?? "", @"[\u0000-\u001f]", " ").Trim();
        return s.Length > max ? s[..max] : s;
    }
}
