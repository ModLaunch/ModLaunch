using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ModLaunch.Core;

/// <summary>Одно объявление: внешнее (из ленты, открывается в браузере) или своё (ведёт на экран программы).</summary>
public sealed record Ad(string Id, string Title, string Text, string? Image, string? Url, string? Go, string Icon, bool House, string? Badge = null);

/// <summary>
/// Реклама (8.4). Перенесена из ModLaunch 3: лента — обычный JSON-файл на своём сайте
/// (адрес в Assets/ads.config.json), меняется без пересборки программы.
///
///   { "rotateSeconds": 15,
///     "items": [ { "id": "shop-1", "title": "Магазин ключей", "text": "Скидки до −70%",
///                  "image": "https://…/logo.png", "url": "https://…", "until": "2026-12-31", "lang": "ru" } ] }
///
/// Всё из сети проверяется: только https, короткие тексты, просроченные объявления
/// отбрасываются. Последняя удачная лента хранится на диске — без интернета показывается она.
/// Пока своих рекламодателей нет, место занимают объявления самого ModLaunch
/// (Hub, Creator Hub, друзья) и «Разместить рекламу».
/// </summary>
public static class Ads
{
    const int MaxItems = 12;
    static readonly TimeSpan Fresh = TimeSpan.FromHours(6);

    public static readonly string? FeedUrl;
    public static readonly string? AdvertiseUrl;
    public static int RotateSeconds { get; private set; }

    static List<Ad> _remote = [];
    static DateTime _loadedAt;
    static Task? _loading;
    public static event Action? Changed;

    static Ads()
    {
        var json = Resources.Json("ads.config.json");
        FeedUrl = HttpsUrl(json.Str("feedUrl"));
        AdvertiseUrl = HttpsUrl(json.Str("advertiseUrl"));
        RotateSeconds = Rotate(json?["rotateSeconds"]?.ToString(), 15);
        // Прошлая лента — сразу, ещё до сети.
        try
        {
            if (File.Exists(CacheFile) && JsonNode.Parse(File.ReadAllText(CacheFile)) is JsonObject cached && cached.Str("url") == FeedUrl)
            {
                _remote = Sanitize(cached["data"], DateTime.UtcNow);
                if (DateTime.TryParse(cached.Str("at"), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var at)) _loadedAt = at;
            }
        }
        catch { }
    }

    static string CacheFile => Path.Combine(Paths.DataDir, "ads-cache.json");

    /// <summary>Реклама включена (премиум потом сможет её убрать).</summary>
    public static bool Enabled => !Settings.Data.Bool("adsHidden");

    public static bool Configured => FeedUrl is not null;

    /// <summary>Последняя ошибка загрузки ленты (для «Состояния сервисов»).</summary>
    public static string? LastError { get; private set; }
    public static int RemoteCount => _remote.Count;

    /// <summary>Что крутить: реклама из ленты, а если её нет — свои объявления.</summary>
    public static List<Ad> Current()
    {
        if (!Enabled) return [];
        if (Configured && !Program.Demo && DateTime.UtcNow - _loadedAt > Fresh) _ = Refresh();
        var lang = I18n.Lang.Split('-')[0];
        var remote = _remote.Where(a => a.Badge is null || a.Badge == lang).ToList();
        return remote.Count > 0 ? remote : House();
    }

    /// <summary>Объявления ModLaunch — чтобы место не пустовало.</summary>
    public static List<Ad> House()
    {
        var list = new List<Ad>
        {
            new("house-hub", I18n.T("ad.hub.title"), I18n.T("ad.hub.text"), null, null, "hub", "globe", true),
            new("house-creator", I18n.T("ad.creator.title"), I18n.T("ad.creator.text"), null, null, "creator", "creator", true),
            new("house-friends", I18n.T("ad.friends.title"), I18n.T("ad.friends.text"), null, null, "friends", "users", true),
        };
        if (AdvertiseUrl is not null) list.Add(new("house-advertise", I18n.T("ad.advertise.title"), I18n.T("ad.advertise.text"), null, AdvertiseUrl, null, "megaphone", true));
        return list;
    }

    public static Task Refresh()
    {
        if (FeedUrl is null) return Task.CompletedTask;
        return _loading ??= Task.Run(async () =>
        {
            try
            {
                var data = await Http.GetJson(FeedUrl, default, 10);
                _remote = Sanitize(data, DateTime.UtcNow);
                RotateSeconds = Rotate(data?["rotateSeconds"]?.ToString(), RotateSeconds);
                _loadedAt = DateTime.UtcNow;
                LastError = null;
                try
                {
                    Directory.CreateDirectory(Paths.DataDir);
                    File.WriteAllText(CacheFile, new JsonObject { ["at"] = DateTime.UtcNow.ToString("o"), ["url"] = FeedUrl, ["data"] = data?.DeepClone() }.ToJsonString());
                }
                catch { }
            }
            catch (Exception e)
            {
                LastError = e.Message;
                _loadedAt = DateTime.UtcNow; // не долбить сервер: следующая попытка через 6 часов
            }
            finally { _loading = null; }
            Changed?.Invoke();
        });
    }

    /// <summary>Привести ленту к безопасному виду.</summary>
    public static List<Ad> Sanitize(JsonNode? data, DateTime now)
    {
        var list = data as JsonArray ?? data?["items"] as JsonArray ?? [];
        var items = new List<Ad>();
        var seen = new HashSet<string>();
        foreach (var raw in list.OfType<JsonObject>())
        {
            var title = Clip(raw.Str("title"), 48);
            var url = HttpsUrl(raw.Str("url"));
            if (title == "" || url is null) continue;
            if (DateTime.TryParse(raw.Str("until"), null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var until) && until.AddDays(1) <= now) continue;
            if (DateTime.TryParse(raw.Str("from"), null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var from) && from > now) continue;
            var id = Clip(raw.Str("id") is { Length: > 0 } i ? i : title, 64);
            if (!seen.Add(id)) continue;
            var lang = raw.Str("lang") is { Length: >= 2 } l ? l.Split('-')[0].ToLowerInvariant() : null;
            items.Add(new Ad(id, title, Clip(raw.Str("text"), 80), HttpsUrl(raw.Str("image")), url, null, "megaphone", false, lang));
            if (items.Count >= MaxItems) break;
        }
        return items;
    }

    public static string? HttpsUrl(string? value) =>
        Uri.TryCreate(value ?? "", UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps ? u.ToString() : null;

    static string Clip(string? value, int max)
    {
        var s = Regex.Replace(Regex.Replace(value ?? "", @"[\u0000-\u001f]", " "), @"\s+", " ").Trim();
        return s.Length > max ? s[..max] : s;
    }

    static int Rotate(string? value, int fallback) =>
        double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) && n > 0 ? (int)Math.Clamp(Math.Round(n), 6, 120) : fallback;

    [SelfTest]
    static string SanitizesFeed()
    {
        var feed = JsonNode.Parse("""
            { "rotateSeconds": 3, "items": [
              { "id": "a", "title": "Ок", "url": "https://example.com", "text": "Текст" },
              { "id": "b", "title": "Не https", "url": "http://example.com" },
              { "id": "c", "title": "Просрочено", "url": "https://example.com", "until": "2020-01-01" },
              { "id": "a", "title": "Повтор", "url": "https://example.com" },
              { "title": "", "url": "https://example.com" } ] }
            """);
        var list = Sanitize(feed, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        if (list.Count != 1 || list[0].Id != "a" || list[0].House) throw new Exception("sanitize wrong: " + list.Count);
        if (Rotate("3", 15) != 6 || Rotate("x", 15) != 15) throw new Exception("rotate wrong");
        return "1 of 5 kept (https, until, duplicates, empty)";
    }
}
