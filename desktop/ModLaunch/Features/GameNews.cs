using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;

namespace ModLaunch.Features;

/// <summary>Новость игры: заголовок, ссылка, когда и пара строк текста.</summary>
public sealed record NewsItem(string Title, string Url, DateTime When, string Summary);

/// <summary>
/// 30.8: новости игры — официальные объявления разработчиков из Steam (ISteamNews, без ключа и входа). Работает для
/// любой игры, у которой известен номер в Steam, — и для игр из Epic, GOG и других, если номер нашёлся по названию.
/// Ленты сторонних сайтов (топы продаж, обзоры) отсеиваем. Кэш в памяти на 3 часа.
/// </summary>
public static partial class GameNews
{
    static readonly Dictionary<int, (DateTime At, List<NewsItem> Items)> Cache = [];

    [GeneratedRegex(@"\[/?[a-zA-Z0-9*]+(=[^\]]*)?\]")] private static partial Regex BbCode();
    [GeneratedRegex(@"<[^>]+>")] private static partial Regex Html();
    [GeneratedRegex(@"https?://\S+")] private static partial Regex Link();
    [GeneratedRegex(@"\{STEAM_CLAN_IMAGE\}\S*")] private static partial Regex ClanImage();
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
    [GeneratedRegex(@"\[\s+[^\]]*\]")] private static partial Regex SectionHeader();
    [GeneratedRegex(@" ([.,;:!?])")] private static partial Regex SpaceBeforePunct();

    /// <summary>Текст новости без разметки BBCode/HTML, ссылок и картинок — две-три строки для карточки.</summary>
    internal static string Clean(string contents, int max = 220)
    {
        var s = ClanImage().Replace(contents ?? "", " ");
        // Заголовки разделов вида «\[ MISC ]» убираем, пункты списка [*] — через «•».
        s = s.Replace("\\[", "[").Replace("\\]", "]");
        s = SectionHeader().Replace(s, " ");
        s = s.Replace("[*]", " • ");
        s = BbCode().Replace(s, " ");
        s = Html().Replace(s, " ");
        s = Link().Replace(s, " ");
        s = Spaces().Replace(WebUtility.HtmlDecode(s), " ").Trim(' ', '•').Replace("• •", "•");
        // «<i>more</i>.» → «more .» после снятия тегов — пробел перед знаком убираем.
        s = SpaceBeforePunct().Replace(s, "$1");
        if (s.Length <= max) return s;
        var cut = s.LastIndexOf(' ', max);
        return (cut > max / 2 ? s[..cut] : s[..max]).TrimEnd(',', '.', ';', ':', ' ') + "…";
    }

    internal static List<NewsItem> Parse(JsonNode? json) =>
        (json?["appnews"]?["newsitems"] as JsonArray ?? []).OfType<JsonObject>()
            .Select(n => new NewsItem(
                WebUtility.HtmlDecode(n.Str("title") ?? "").Trim(),
                n.Str("url") ?? "",
                DateTimeOffset.FromUnixTimeSeconds(n.Long("date")).UtcDateTime,
                Clean(n.Str("contents") ?? "")))
            .Where(n => n.Title != "" && n.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            .ToList();

    /// <summary>Последние объявления игры (до count).</summary>
    public static async Task<List<NewsItem>> For(int steamAppId, int count = 3)
    {
        if (steamAppId <= 0) return [];
        lock (Cache)
            if (Cache.TryGetValue(steamAppId, out var hit) && DateTime.UtcNow - hit.At < TimeSpan.FromHours(3)) return hit.Items;
        var json = await Http.GetJson($"https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/?appid={steamAppId}&count={count}&maxlength=0&format=json&feeds=steam_community_announcements", default, 15);
        var items = Parse(json);
        lock (Cache) Cache[steamAppId] = (DateTime.UtcNow, items);
        return items;
    }

    /// <summary>--news &lt;номер в Steam&gt;: последние объявления игры текстом (проверка без окна).</summary>
    public static int Cli(string[] args)
    {
        var at = Array.IndexOf(args, "--news");
        if (at < 0 || at + 1 >= args.Length || !int.TryParse(args[at + 1], out var appId)) { Console.WriteLine("usage: --news <steam app id>"); return 2; }
        try
        {
            foreach (var n in For(appId).GetAwaiter().GetResult())
            {
                Console.WriteLine($"{n.When:yyyy-MM-dd} | {n.Title}");
                Console.WriteLine($"    {n.Summary}");
                Console.WriteLine($"    {n.Url}");
            }
            return 0;
        }
        catch (Exception e) { Console.WriteLine("error: " + e.Message); return 1; }
    }

    [SelfTest]
    static string GameNewsCleansAndParses()
    {
        var text = Clean("[h1]Patch 1.2[/h1]{STEAM_CLAN_IMAGE}/123/abc.png [b]Fixed[/b] crashes &amp; <i>more</i>. See https://example.com/notes [url=https://x.y]here[/url]");
        if (text != "Patch 1.2 Fixed crashes & more. See here") throw new Exception("clean: " + text);
        if (!Clean(new string('a', 50) + " " + new string('b', 300)).EndsWith('…')) throw new Exception("cut");
        var list = Clean("[p]\\[ MISC ][/p][list][*][p]Fixed bots[/p][/*][*][p]Faster DX11[/p][/*][/list]");
        if (list != "Fixed bots • Faster DX11") throw new Exception("list: " + list);
        var json = JsonNode.Parse("""{"appnews":{"newsitems":[{"title":"Hotfix &amp; more","url":"https://store.steampowered.com/news/1","date":1755035058,"contents":"[p]Hi[/p]"},{"title":"","url":"https://x","date":1},{"title":"Bad","url":"javascript:alert(1)","date":1}]}}""");
        var items = Parse(json);
        if (items.Count != 1 || items[0].Title != "Hotfix & more" || items[0].Summary != "Hi" || items[0].When.Year != 2025) throw new Exception("parse");
        return "BBCode/HTML/links stripped, long text cut at a word, only https items with titles kept";
    }
}
