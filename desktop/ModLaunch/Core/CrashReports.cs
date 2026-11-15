using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Social;

namespace ModLaunch.Core;

/// <summary>Отчёт о сбое (так он хранится на диске и уходит на сервер).</summary>
public sealed record AppCrash(string Id, string Version, string Os, string Kind, string Type, string Message, string Stack, string Sig, DateTime? At);

/// <summary>
/// 31.0: отчёты о сбоях программы — только с согласия. Сбой пишется на диск (DataDir/crashes); при следующем
/// запуске, если человек разрешил («Отправлять отчёты о сбоях»), уходит в базу (crashes/, писать может любой,
/// читать — только админ). Без согласия ничего не отправляется. Из текста убраны имя пользователя Windows, имя ПК,
/// пути к профилю, адреса почты и длинные ключи. Админ видит сбои по «подписи» (тип + место в коде) и количеству.
/// </summary>
public static partial class CrashReports
{
    public const int MaxStack = 6000, MaxMessage = 300;
    static string Dir => Path.Combine(Paths.DataDir, "crashes");

    /// <summary>Согласие: null — ещё не спрашивали, true — отправлять, false — нет.</summary>
    public static bool? Consent => Settings.Data["crashReports"] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

    public static void SetConsent(bool value)
    {
        Settings.Data["crashReports"] = value;
        Settings.Save();
        if (!value) foreach (var f in Pending()) try { File.Delete(f); } catch { }
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")] private static partial Regex Email();
    [GeneratedRegex(@"\b[A-Za-z0-9_\-]{32,}\b")] private static partial Regex LongToken();

    /// <summary>Убрать личное: профиль, имя пользователя и ПК, почту, ключи/токены.</summary>
    public static string Scrub(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length > 3) text = text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        foreach (var name in new[] { Environment.UserName, Environment.MachineName })
            if (name.Length >= 3) text = Regex.Replace(text, Regex.Escape(name), "<user>", RegexOptions.IgnoreCase);
        text = Email().Replace(text, "<email>");
        text = LongToken().Replace(text, "<token>");
        return text;
    }

    /// <summary>Подпись сбоя: тип исключения + три верхних кадра из нашего кода — одинаковые сбои складываются вместе.</summary>
    public static string Signature(Exception e)
    {
        var frames = (e.StackTrace ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("at ModLaunch", StringComparison.Ordinal))
            .Select(l => Regex.Replace(l, @" in .*$", "")).Take(3);
        var key = e.GetType().FullName + "|" + string.Join("|", frames);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16].ToLowerInvariant();
    }

    /// <summary>Записать сбой на диск (вызывает Guard). fatal — программа закрылась.</summary>
    public static void Save(Exception e, bool fatal)
    {
        try
        {
            if (Consent == false) return;
            var inner = e is AggregateException { InnerException: { } ie } ? ie : e;
            Directory.CreateDirectory(Dir);
            // Не больше 30 отчётов в очереди: при повторяющейся ошибке не забиваем диск.
            if (Pending().Count >= 30) return;
            var report = new JsonObject
            {
                ["version"] = Http.Version,
                ["os"] = Environment.OSVersion.VersionString,
                ["kind"] = fatal ? "fatal" : "error",
                ["type"] = inner.GetType().FullName,
                ["message"] = Clip(Scrub(inner.Message), MaxMessage),
                ["stack"] = Clip(Scrub(inner.ToString()), MaxStack),
                ["sig"] = Signature(inner),
                ["at"] = DateTime.UtcNow.ToString("o"),
            };
            File.WriteAllText(Path.Combine(Dir, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{report.Str("sig")}.json"), report.ToJsonString());
        }
        catch { }
    }

    static string Clip(string s, int max) => s.Length <= max ? s : s[..max];

    public static List<string> Pending() => Directory.Exists(Dir) ? Directory.GetFiles(Dir, "*.json").OrderBy(f => f).ToList() : [];

    /// <summary>Отправить накопившиеся отчёты (только с согласия). Возвращает, сколько ушло.</summary>
    public static async Task<int> Send()
    {
        if (Consent != true || !Firebase.Configured || Program.Demo || Offline.On) return 0;
        var sent = 0;
        string? token = null;
        foreach (var file in Pending().Take(10))
        {
            try
            {
                var r = JsonNode.Parse(await File.ReadAllTextAsync(file)) as JsonObject ?? throw new InvalidDataException();
                token ??= (await Account.Token()).IdToken;
                await Creator.Hub.Commit([new JsonObject
                {
                    ["update"] = new JsonObject
                    {
                        ["name"] = Firebase.Doc($"crashes/{Path.GetFileNameWithoutExtension(file)}_{Guid.NewGuid():N}"),
                        ["fields"] = Firebase.ToFields(new Dictionary<string, object?>
                        {
                            ["version"] = Clip(r.Str("version") ?? "", 20), ["os"] = Clip(r.Str("os") ?? "", 80), ["kind"] = r.Str("kind") == "fatal" ? "fatal" : "error",
                            ["type"] = Clip(r.Str("type") ?? "", 200), ["message"] = Clip(r.Str("message") ?? "", MaxMessage),
                            ["stack"] = Clip(r.Str("stack") ?? "", MaxStack), ["sig"] = Clip(r.Str("sig") ?? "", 16),
                        }),
                    },
                    ["updateTransforms"] = new JsonArray(Creator.Hub.ServerTime("created")),
                    ["currentDocument"] = new JsonObject { ["exists"] = false },
                }], token);
                File.Delete(file);
                sent++;
            }
            catch (InvalidDataException) { try { File.Delete(file); } catch { } }
            catch (Exception e) when (e is ServiceError or HttpRequestException) { break; }
        }
        return sent;
    }

    /// <summary>Админ: последние сбои (до 500), сгруппированные по подписи — самые частые сверху.</summary>
    public static async Task<List<(string Sig, int Count, AppCrash Latest, List<string> Versions)>> Top()
    {
        if (!Account.Get().Admin) throw new ServiceError("DENIED");
        var (_, token) = await Account.Token();
        var q = new JsonObject
        {
            ["from"] = new JsonArray(new JsonObject { ["collectionId"] = "crashes" }),
            ["orderBy"] = new JsonArray(new JsonObject { ["field"] = new JsonObject { ["fieldPath"] = "created" }, ["direction"] = "DESCENDING" }),
            ["limit"] = 500,
        };
        var rows = await Creator.Hub.Call(Firebase.Url(":runQuery"), HttpMethod.Post, new JsonObject { ["structuredQuery"] = q }, token);
        var list = (rows as JsonArray ?? []).Select(x => x?["document"] as JsonObject).OfType<JsonObject>().Select(d =>
        {
            var name = d.Str("name") ?? "";
            var f = Firebase.FromFields(d["fields"] as JsonObject);
            return new AppCrash(name[(name.LastIndexOf('/') + 1)..], f.S("version"), f.S("os"), f.S("kind"), f.S("type"), f.S("message"), f.S("stack"), f.S("sig"), Firebase.Time(f.S("created")));
        }).ToList();
        return list.GroupBy(c => c.Sig).Select(g => (g.Key, g.Count(), g.First(), g.Select(c => c.Version).Distinct().OrderDescending().ToList()))
            .OrderByDescending(g => g.Item2).ToList();
    }

    [SelfTest]
    static string CrashReportsScrubAndQueue()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dirty = $"Failed at {profile}\\Games\\x.dll for player@example.com token abcdefghijklmnopqrstuvwxyz0123456789ABCD on {Environment.MachineName}";
        var clean = Scrub(dirty);
        if (clean.Contains(profile, StringComparison.OrdinalIgnoreCase) || clean.Contains("player@example.com") || clean.Contains("abcdefghijklmnopqrstuvwxyz0123456789ABCD")
            || clean.Contains(Environment.MachineName, StringComparison.OrdinalIgnoreCase)) throw new Exception("not scrubbed: " + clean);
        // Без согласия — ничего не копим; с согласием — отчёт на диске, одинаковые ошибки — одна подпись.
        var before = Settings.Data["crashReports"]?.DeepClone();
        SetConsent(false);
        Save(new InvalidOperationException("x"), fatal: true);
        if (Pending().Count != 0) throw new Exception("saved without consent");
        SetConsent(true);
        Exception Boom() { try { throw new InvalidOperationException("boom " + profile); } catch (Exception e) { return e; } }
        var a = Boom();
        var b = Boom();
        Save(a, fatal: true);
        if (Pending().Count != 1 || Signature(a) != Signature(b)) throw new Exception("queue/signature");
        var saved = File.ReadAllText(Pending()[0]);
        if (saved.Contains(profile, StringComparison.OrdinalIgnoreCase)) throw new Exception("profile path in the saved report");
        foreach (var f in Pending()) File.Delete(f);
        if (before is null) Settings.Data.Remove("crashReports"); else Settings.Data["crashReports"] = before;
        return $"profile, user/PC name, e-mail and tokens removed ({clean}); nothing kept without consent; same error → same signature";
    }
}
