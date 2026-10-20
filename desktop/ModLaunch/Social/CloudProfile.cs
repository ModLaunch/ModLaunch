using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Social;

/// <summary>Чего не хватает на этом ПК по сравнению с профилем в облаке.</summary>
public sealed record CloudDiff(List<(GameState Game, bool NeedLoader, List<SetupMod> Missing)> Here, List<(string Id, string Name, int Mods)> NotFound,
    string Device, DateTime At, UiConfig? Style)
{
    public int Mods => Here.Sum(h => h.Missing.Count);
    public bool Empty => Here.Count == 0 && NotFound.Count == 0 && Style is null;
}

/// <summary>
/// 10.0: профиль в облаке. Аккаунт ModLaunch хранит ваши сборки (какие моды стоят в каждой игре,
/// с версиями), награды, кейсы и стиль. На другом компьютере при входе ModLaunch сравнивает облако
/// с тем, что стоит здесь, и предлагает доустановить загрузчики и моды (и взять стиль).
/// Один документ cloud/&lt;uid&gt;, читать и писать его может только хозяин (firebase/firestore.rules).
/// Сборки каждого ПК хранятся отдельно, так что отправка с одного компьютера не стирает сборки другого.
/// </summary>
public static class CloudProfile
{
    public static bool On => Settings.Data.Bool("cloudSync", true);

    public static string DeviceId
    {
        get
        {
            if (Settings.Data.Str("deviceId") is { Length: > 0 } id) return id;
            var fresh = Guid.NewGuid().ToString("N")[..16];
            Settings.Data["deviceId"] = fresh;
            Settings.Save();
            return fresh;
        }
    }

    public static string? LastSync => Settings.Data.Str("cloudSyncedAt");

    static async Task<(string Uid, string Token)> Auth()
    {
        if (!Firebase.Configured) throw new ServiceError("NOT_CONFIGURED");
        if (!Account.SignedIn) throw new ServiceError("SIGN_IN");
        var (uid, token) = await Account.Token();
        return (uid, token);
    }

    /// <summary>Профиль из облака (null — его ещё нет).</summary>
    public static async Task<JsonObject?> Fetch()
    {
        var (uid, token) = await Auth();
        var reply = await Firebase.Send(Firebase.Url($"/cloud/{uid}"), HttpMethod.Get, token: token);
        if ((int)reply.Status == 404) return null;
        if ((int)reply.Status is < 200 or >= 300) throw new ServiceError((int)reply.Status == 403 ? "DENIED" : "SERVER", Firebase.Reason(reply));
        var fields = Firebase.FromFields(reply.Body?["fields"] as JsonObject);
        return JsonNode.Parse(fields.S("data")) as JsonObject;
    }

    /// <summary>
    /// Профиль этого ПК поверх облачного. У каждого ПК своя запись сборок (devices/&lt;id&gt;), поэтому
    /// отправка с одного компьютера никогда не стирает сборки другого; хранятся 6 последних ПК.
    /// </summary>
    public static JsonObject Build(JsonObject? cloud)
    {
        var games = new JsonObject();
        foreach (var g in AppState.Games.Where(g => g.Status == Detect.Found && !g.Def.IsMinecraft && g.Registry is not null))
        {
            List<SetupMod> mods;
            try { mods = ModSetup.Capture(g.Registry!); } catch { continue; }
            if (mods.Count == 0) continue;
            games[g.Def.Id] = new JsonObject { ["name"] = g.Def.Name, ["loader"] = g.LoaderInstalled, ["mods"] = ModSetup.ToJson(mods) };
        }
        var now = DateTime.UtcNow.ToString("o");
        var devices = (cloud?["devices"] as JsonObject)?.DeepClone() as JsonObject ?? new JsonObject();
        devices[DeviceId] = new JsonObject { ["name"] = Environment.MachineName, ["at"] = now, ["games"] = games };
        foreach (var old in devices.OrderByDescending(d => d.Value.Str("at")).Skip(6).Select(d => d.Key).ToList()) devices.Remove(old);
        return new JsonObject
        {
            ["v"] = 2,
            ["device"] = DeviceId,
            ["at"] = now,
            ["devices"] = devices,
            ["profile"] = MergeProfile(cloud?["profile"] as JsonObject, Settings.Data.Obj("profile")),
            ["style"] = Configs.Capture(I18n.T("v10.cloud.styleName", ("pc", Environment.MachineName))),
        };
    }

    /// <summary>Последний другой ПК этого аккаунта (откуда предлагать сборки).</summary>
    public static (string Id, JsonObject Entry)? Other(JsonObject cloud) =>
        (cloud["devices"] as JsonObject ?? []).Where(d => d.Key != DeviceId && d.Value is JsonObject)
            .OrderByDescending(d => d.Value.Str("at")).Select(d => ((string, JsonObject)?)(d.Key, (JsonObject)d.Value!)).FirstOrDefault();

    /// <summary>Награды и кейсы — объединение двух ПК: открытое на любом остаётся открытым везде.</summary>
    public static JsonObject MergeProfile(JsonObject? a, JsonObject b)
    {
        var result = (b.DeepClone() as JsonObject)!;
        if (a is null) return result;
        foreach (var (key, value) in a)
            if (!result.ContainsKey(key) && value is not null) result[key] = value.DeepClone();
        static void Union(JsonObject into, JsonObject? from, string key)
        {
            if (from?[key] is not JsonArray other) return;
            var mine = into[key] as JsonArray ?? new JsonArray();
            var have = mine.Select(n => n?.ToString()).ToHashSet();
            foreach (var n in other) if (n?.ToString() is { } s && have.Add(s)) mine.Add(JsonValue.Create(s));
            into[key] = mine.DeepClone();
        }
        Union(result, a, "seen");
        if (a["cases"] is JsonObject ac)
        {
            var rc = result["cases"] as JsonObject ?? new JsonObject();
            foreach (var key in new[] { "opened", "unlocked", "weeks", "days" }) Union(rc, ac, key);
            result["cases"] = rc;
        }
        return result;
    }

    /// <summary>Сравнить облако с этим ПК.</summary>
    public static CloudDiff Compare(JsonObject cloud)
    {
        var here = new List<(GameState, bool, List<SetupMod>)>();
        var notFound = new List<(string, string, int)>();
        var other = Other(cloud);
        foreach (var (id, node) in other?.Entry["games"] as JsonObject ?? [])
        {
            if (node is not JsonObject game) continue;
            var mods = ModSetup.FromJson(game["mods"] as JsonArray).Where(m => m.Source != "file").ToList();
            if (mods.Count == 0) continue;
            var g = AppState.Games.FirstOrDefault(x => x.Def.Id == id);
            if (g is null || g.Status != Detect.Found || g.Registry is null) { notFound.Add((id, game.Str("name") ?? id, mods.Count)); continue; }
            var (missing, _) = ModSetup.Differences(g.Registry, mods);
            var needLoader = !g.LoaderInstalled && game.Bool("loader");
            if (missing.Count > 0 || needLoader) here.Add((g, needLoader, missing));
        }
        UiConfig? style = null;
        if (cloud.Str("device") != DeviceId && cloud.Str("style") is { Length: > 0 } text)
        {
            var c = Configs.Parse(text, "cloud");
            if (c.Get("theme") != Look.Theme || c.Get("accent")?.Equals(Look.Accent, StringComparison.OrdinalIgnoreCase) == false) style = c with { Id = "cloud", Name = c.Name };
        }
        var at = DateTime.TryParse(other?.Entry.Str("at"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t : DateTime.MinValue;
        return new CloudDiff(here, notFound, other?.Entry.Str("name") ?? "", at, style);
    }

    /// <summary>Отправить профиль (поверх облачного).</summary>
    public static async Task Push(JsonObject? cloud)
    {
        var (uid, token) = await Auth();
        var data = Build(cloud).ToJsonString();
        if (data.Length > 900_000) throw new ServiceError("TOO_BIG");
        var body = new JsonObject { ["fields"] = Firebase.ToFields(new Dictionary<string, object?> { ["data"] = data, ["device"] = DeviceId }) };
        var reply = await Firebase.Send(Firebase.Url($"/cloud/{uid}"), HttpMethod.Patch, body, token);
        if ((int)reply.Status is < 200 or >= 300) throw new ServiceError((int)reply.Status == 403 ? "DENIED" : "SERVER", Firebase.Reason(reply));
        Settings.Data["cloudSyncedAt"] = DateTime.UtcNow.ToString("o");
        Settings.Save();
    }

    /// <summary>Взять награды и кейсы из облака сразу (без вопросов: это только добавляет открытое).</summary>
    public static void TakeProfile(JsonObject cloud)
    {
        if (cloud["profile"] is not JsonObject p) return;
        Settings.Data["profile"] = MergeProfile(p, Settings.Data.Obj("profile"));
        Settings.Save();
    }

    /// <summary>Спрашивать ли: есть что поставить с другого ПК, и про эту его запись ещё не отвечали «не сейчас».</summary>
    public static bool ShouldOffer(JsonObject cloud, CloudDiff diff) =>
        !diff.Empty && Settings.Data.Str("cloudDismissed") != Stamp(cloud);

    static string Stamp(JsonObject cloud) => (Other(cloud)?.Entry.Str("at") ?? "") + "|" + (cloud.Str("device") != DeviceId ? cloud.Str("at") : "");

    public static void Dismiss(JsonObject cloud) { Settings.Data["cloudDismissed"] = Stamp(cloud); Settings.Save(); }

    [SelfTest]
    static string CloudProfileMergesRewards()
    {
        var a = JsonNode.Parse("""{"frame":"gold","seen":["x"],"cases":{"opened":["a:1"],"unlocked":["cfg:terminal"]}}""") as JsonObject;
        var b = JsonNode.Parse("""{"seen":["y"],"cases":{"opened":["a:2"]}}""") as JsonObject;
        var m = MergeProfile(a, b!);
        if (m.Str("frame") != "gold") throw new Exception("frame lost");
        if (m.Arr("seen").Count != 2 || (m["cases"] as JsonObject).Arr("opened").Count != 2 || (m["cases"] as JsonObject).Arr("unlocked").Count != 1) throw new Exception(m.ToJsonString());
        return "rewards and cases from both PCs are kept";
    }
}
