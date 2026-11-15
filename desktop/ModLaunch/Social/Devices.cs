using System.Security.Cryptography;
using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Social;

/// <summary>Устройство, с которого входили в аккаунт.</summary>
public sealed record Device(string Id, string Name, string Os, string App, DateTime? First, DateTime? Last, bool Ack)
{
    public bool Mine => Id == Devices.MyId;
}

/// <summary>
/// 31.0: «Новый вход в аккаунт». Каждый ПК записывает себя в devices/{uid}/list/{id} (случайный id этой копии
/// программы, имя ПК, Windows, версия). Вход с нового ПК видят остальные: «Это вы?» — «Да» снимает
/// предупреждение, «Нет» — письмо для смены пароля (смена пароля закрывает чужие входы). Письмо о новом входе
/// шлёт сервер (server/functions), если он подключён. Первый ПК аккаунта предупреждения не вызывает.
/// </summary>
public static class Devices
{
    public static event Action? Changed;

    /// <summary>Случайный id этой копии программы (не железо ПК) — создаётся один раз.</summary>
    public static string MyId
    {
        get
        {
            if (Settings.Data.Str("deviceId") is { Length: >= 16 } id) return id;
            var fresh = "d" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
            Settings.Data["deviceId"] = fresh;
            Settings.Save();
            return fresh;
        }
    }

    static string MyName() => Hub.Clip(Environment.MachineName, 64);
    static string MyOs() => Hub.Clip(Environment.OSVersion.Version.Build >= 22000 ? $"Windows 11 ({Environment.OSVersion.Version})" : $"Windows ({Environment.OSVersion.Version})", 64);

    static Device Of(JsonObject d)
    {
        var name = d.Str("name") ?? "";
        var f = Firebase.FromFields(d["fields"] as JsonObject);
        return new Device(name[(name.LastIndexOf('/') + 1)..], f.S("name"), f.S("os"), f.S("app"), Firebase.Time(f.S("first")), Firebase.Time(f.S("last")), f.B("ack"));
    }

    /// <summary>Все устройства аккаунта (своё — первым).</summary>
    public static async Task<List<Device>> List()
    {
        if (!Account.SignedIn) return [];
        var (uid, token) = await Account.Token();
        var rows = await Hub.Call(Firebase.DocUrl(Firebase.Doc($"devices/{uid}"), ":runQuery"), HttpMethod.Post, new JsonObject
        {
            ["structuredQuery"] = new JsonObject { ["from"] = new JsonArray(new JsonObject { ["collectionId"] = "list" }), ["limit"] = 100 },
        }, token);
        return (rows as JsonArray ?? []).Select(r => r?["document"] as JsonObject).OfType<JsonObject>().Select(Of)
            .OrderByDescending(d => d.Mine).ThenByDescending(d => d.Last).ToList();
    }

    /// <summary>Записать этот ПК (раз в сутки или после входа). Первый ПК аккаунта сразу «свой».</summary>
    public static async Task Register(bool force = false)
    {
        if (!Account.SignedIn || !Account.Get().Verified && !force || Program.Demo || Program.Screenshot) return;
        var key = $"deviceSeen.{Account.Uid}";
        if (!force && DateTime.TryParse(Settings.Data.Str(key), null, System.Globalization.DateTimeStyles.RoundtripKind, out var seen) && DateTime.UtcNow - seen < TimeSpan.FromHours(20)) return;
        var (uid, token) = await Account.Token();
        var path = $"devices/{uid}/list/{MyId}";
        var fields = new Dictionary<string, object?> { ["name"] = MyName(), ["os"] = MyOs(), ["app"] = Http.Version };
        // Есть ли уже запись этого ПК: повторное «создать» правила отклоняют (first меняться не может) раньше,
        // чем база ответит «уже есть», — поэтому сначала смотрим список.
        var all = await List();
        if (all.All(d => !d.Mine))
        {
            // Новый: first и last — время сервера; «свой» сразу, если других устройств у аккаунта нет.
            var create = new Dictionary<string, object?>(fields) { ["ack"] = all.Count == 0 };
            await Hub.Commit([new JsonObject
            {
                ["update"] = new JsonObject { ["name"] = Firebase.Doc(path), ["fields"] = Firebase.ToFields(create) },
                ["updateTransforms"] = new JsonArray(Hub.ServerTime("first"), Hub.ServerTime("last")),
                ["currentDocument"] = new JsonObject { ["exists"] = false },
            }], token);
        }
        else
        {
            await Hub.Commit([new JsonObject
            {
                ["update"] = new JsonObject { ["name"] = Firebase.Doc(path), ["fields"] = Firebase.ToFields(fields) },
                ["updateMask"] = new JsonObject { ["fieldPaths"] = new JsonArray("name", "os", "app") },
                ["updateTransforms"] = new JsonArray(Hub.ServerTime("last")),
                ["currentDocument"] = new JsonObject { ["exists"] = true },
            }], token);
        }
        Settings.Data[key] = DateTime.UtcNow.ToString("o");
        Settings.Save();
        Changed?.Invoke();
    }

    /// <summary>Входы, о которых стоит спросить: чужие устройства без отметки «это я», появившиеся после этого ПК.</summary>
    public static async Task<List<Device>> Alerts()
    {
        var all = await List();
        var me = all.FirstOrDefault(d => d.Mine);
        if (me is null) return [];
        return all.Where(d => !d.Mine && !d.Ack && (me.First is null || d.First is null || d.First > me.First)).ToList();
    }

    /// <summary>«Это я» — снять предупреждение (видно на всех ПК).</summary>
    public static async Task Acknowledge(Device d)
    {
        var (uid, token) = await Account.Token();
        await Hub.Commit([new JsonObject
        {
            ["update"] = new JsonObject { ["name"] = Firebase.Doc($"devices/{uid}/list/{d.Id}"), ["fields"] = Firebase.ToFields(new Dictionary<string, object?> { ["ack"] = true }) },
            ["updateMask"] = new JsonObject { ["fieldPaths"] = new JsonArray("ack") },
            ["updateTransforms"] = new JsonArray(Hub.ServerTime("last")),
            ["currentDocument"] = new JsonObject { ["exists"] = true },
        }], token);
        Changed?.Invoke();
    }

    /// <summary>Убрать устройство из списка (на нём программа запишет себя снова при следующем входе).</summary>
    public static async Task Forget(Device d)
    {
        var (uid, token) = await Account.Token();
        await Hub.Commit([new JsonObject { ["delete"] = Firebase.Doc($"devices/{uid}/list/{d.Id}") }], token);
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- 31.0: защита от подбора пароля

    const int FreeTries = 5;

    /// <summary>
    /// Сколько ещё ждать перед следующей попыткой входа на этот адрес: после 5 неудач — 30 с, дальше вдвое
    /// дольше за каждую, до 15 минут. Сервер Firebase тоже режет перебор (TOO_MANY_ATTEMPTS); это — ещё и тут.
    /// </summary>
    public static TimeSpan LockedFor(string email)
    {
        var o = Settings.Data["loginFails"] as JsonObject;
        if (o?[Key(email)] is not JsonObject e) return TimeSpan.Zero;
        var count = (int)e.Long("n");
        if (!DateTime.TryParse(e.Str("at"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var at)) return TimeSpan.Zero;
        if (DateTime.UtcNow - at > TimeSpan.FromHours(1)) return TimeSpan.Zero;
        if (count < FreeTries) return TimeSpan.Zero;
        var wait = TimeSpan.FromSeconds(Math.Min(900, 30 * Math.Pow(2, count - FreeTries)));
        var left = at + wait - DateTime.UtcNow;
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    static string Key(string email) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant())))[..16];

    public static void Failed(string email)
    {
        var o = Settings.Data["loginFails"] as JsonObject ?? new JsonObject();
        var e = o[Key(email)] as JsonObject;
        var fresh = e is null || !DateTime.TryParse(e.Str("at"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var at) || DateTime.UtcNow - at > TimeSpan.FromHours(1);
        o[Key(email)] = new JsonObject { ["n"] = (fresh ? 0 : e!.Long("n")) + 1, ["at"] = DateTime.UtcNow.ToString("o") };
        Settings.Data["loginFails"] = o;
        Settings.Save();
    }

    public static void Succeeded(string email)
    {
        if (Settings.Data["loginFails"] is JsonObject o && o.Remove(Key(email))) Settings.Save();
    }

    [SelfTest]
    static string LoginBackoffGrows()
    {
        const string mail = "backoff@example.test";
        Succeeded(mail);
        for (var i = 0; i < FreeTries - 1; i++) Failed(mail);
        if (LockedFor(mail) != TimeSpan.Zero) throw new Exception("locked before 5 failures");
        Failed(mail);
        var first = LockedFor(mail);
        Failed(mail);
        var second = LockedFor(mail);
        if (first <= TimeSpan.FromSeconds(25) || second <= first || second > TimeSpan.FromMinutes(15)) throw new Exception($"backoff {first} → {second}");
        Succeeded(mail);
        if (LockedFor(mail) != TimeSpan.Zero) throw new Exception("not cleared after success");
        if (MyId != MyId || MyId.Length < 16) throw new Exception("device id unstable");
        return $"5 free tries, then {first.TotalSeconds:0} s, then {second.TotalSeconds:0} s; success clears; device id {MyId[..5]}…";
    }
}
