using System.Net;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Games;

namespace ModLaunch.Sources;

public sealed record Block(string Kind, string Text); // h | p | li
public sealed record Requirement(string Id, string Name, bool Available);
public sealed record ModDetails(ModInfo Mod, List<Block> Blocks, List<string> Images, List<Requirement> Requirements);

/// <summary>
/// Страница мода: описание (HTML Thunderstore, BBCode Nexus, Markdown README),
/// разобранное в простые абзацы и заголовки, и картинки из него — для галереи.
/// </summary>
public static partial class Details
{
    public static async Task<ModDetails> Load(GameDef game, string id, CancellationToken ct = default, string? source = null)
    {
        switch (source ?? Catalog.GuessSource(game, id))
        {
            case "thunderstore":
            {
                var mod = await Thunderstore.Get(game.ThunderstoreCommunity!, id, ct) ?? throw new InvalidOperationException(I18n.T("err.modNotInCatalog"));
                string html = "";
                if (Thunderstore.Split(id) is var (ns, name))
                {
                    try { html = (await Http.GetJson($"https://thunderstore.io/api/cyberstorm/package/{Uri.EscapeDataString(ns)}/{Uri.EscapeDataString(name)}/latest/readme/", ct, 20)).Str("html") ?? ""; } catch { }
                }
                var reqs = mod.Dependencies.Where(d => !d.EndsWith("BepInExPack", StringComparison.OrdinalIgnoreCase) && !d.Contains("BepInExPack_"))
                    .Select(d => new Requirement(d, d[(d.LastIndexOf('-') + 1)..].Replace('_', ' '), true)).ToList();
                return new ModDetails(mod, Html(html), Pictures(html, "https://thunderstore.io/"), reqs);
            }
            case "modlinks":
            {
                var all = await ModLinks.Load(ct);
                var mod = all.FirstOrDefault(m => m.Id == id) ?? throw new InvalidOperationException(I18n.T("err.modNotInCatalog"));
                var md = await Readme(mod.Url, ct);
                var byId = all.ToDictionary(m => m.Id);
                var reqs = mod.Dependencies.Select(d => new Requirement(d, byId.TryGetValue(d, out var m) ? m.Name : d, byId.ContainsKey(d))).ToList();
                return new ModDetails(mod, md.Blocks, md.Images, reqs);
            }
            default:
            {
                var (mod, _, requirements, description) = await Nexus.DetailsFull(game.NexusDomain!, game.NexusGameId, id, ct);
                if (mod is null) throw new InvalidOperationException(I18n.T("err.modNotInCatalog"));
                var html = BbCode(description);
                var images = Pictures(html, "https://www.nexusmods.com/");
                if (mod.Icon is not null) images.Insert(0, mod.Icon);
                var reqs = requirements.Where(r => !game.NexusHide.Contains(r.Id)).Select(r => new Requirement(r.Id, r.Name, true)).ToList();
                return new ModDetails(mod, Html(html), images.Distinct().ToList(), reqs);
            }
        }
    }

    // ---------------------------------------------------------------- README с GitHub (Hollow Knight)

    static async Task<(List<Block> Blocks, List<string> Images)> Readme(string? repoUrl, CancellationToken ct)
    {
        var m = Regex.Match(repoUrl ?? "", @"^https?://github\.com/([^/]+)/([^/#?]+)", RegexOptions.IgnoreCase);
        if (!m.Success) return ([], []);
        var key = $"{m.Groups[1].Value}/{m.Groups[2].Value.Replace(".git", "")}";
        foreach (var name in new[] { "README.md", "readme.md", "Readme.md" })
        {
            try
            {
                var text = await Http.GetString($"https://raw.githubusercontent.com/{key}/HEAD/{name}", ct, 15);
                var baseUrl = $"https://raw.githubusercontent.com/{key}/HEAD/";
                var images = MarkdownImages().Matches(text).Select(x => x.Groups[1].Value)
                    .Concat(Pictures(text, baseUrl))
                    .Select(u => Absolute(u, baseUrl)).OfType<string>().Distinct().Take(12).ToList();
                return (Markdown(text), images);
            }
            catch { }
        }
        return ([], []);
    }

    [GeneratedRegex(@"!\[[^\]]*\]\(([^)\s]+)")] private static partial Regex MarkdownImages();

    static List<Block> Markdown(string text)
    {
        var blocks = new List<Block>();
        var para = new List<string>();
        void Flush() { if (para.Count > 0) { blocks.Add(new Block("p", Inline(string.Join(' ', para)))); para.Clear(); } }
        var fence = false;
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.TrimStart().StartsWith("```")) { fence = !fence; Flush(); continue; }
            if (fence) continue;
            if (Regex.IsMatch(line, @"^\s*#{1,6}\s")) { Flush(); blocks.Add(new Block("h", Inline(line.TrimStart('#', ' ')))); }
            else if (Regex.IsMatch(line, @"^\s*([-*+]|\d+\.)\s+")) { Flush(); blocks.Add(new Block("li", Inline(Regex.Replace(line, @"^\s*([-*+]|\d+\.)\s+", "")))); }
            else if (line.Trim() == "" || Regex.IsMatch(line, @"^\s*(<[^>]+>\s*)+$")) Flush();
            else para.Add(line.Trim());
        }
        Flush();
        return blocks.Where(b => b.Text.Trim() != "").Take(120).ToList();
    }

    static string Inline(string s)
    {
        s = Regex.Replace(s, @"!\[[^\]]*\]\([^)]*\)", "");
        s = Regex.Replace(s, @"\[([^\]]*)\]\([^)]*\)", "$1");
        s = Regex.Replace(s, @"<[^>]+>", "");
        s = Regex.Replace(s, @"(\*\*|__|`|~~)", "");
        return WebUtility.HtmlDecode(s).Trim();
    }

    // ---------------------------------------------------------------- BBCode и HTML

    static string BbCode(string text)
    {
        var s = text;
        s = Regex.Replace(s, @"\[img\](.*?)\[/img\]", "<img src=\"$1\">", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        s = Regex.Replace(s, @"\[(size|font|color|center|right|left|u|i|b|s|url|spoiler|quote|youtube|line|heading)[^\]]*\]", m =>
            m.Groups[1].Value.ToLowerInvariant() is "heading" or "size" ? "<h3>" : m.Groups[1].Value.ToLowerInvariant() == "line" ? "<br>" : "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\[/(size|heading)\]", "</h3>", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\[\*\]", "<li>", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\[/?[a-z]+[^\]]*\]", "", RegexOptions.IgnoreCase);
        return s;
    }

    static List<Block> Html(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return [];
        var s = Regex.Replace(html, @"<(script|style)[\s\S]*?</\1>", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<pre[\s\S]*?</pre>", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<h[1-6][^>]*>", "\n\u0001", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"</h[1-6]>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<li[^>]*>", "\n\u0002", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<(br|/p|/div|/li|/ul|/ol|p|div|hr)[^>]*>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<[^>]+>", "");
        s = WebUtility.HtmlDecode(s);
        var blocks = new List<Block>();
        foreach (var raw in s.Replace("\r", "").Split('\n'))
        {
            var line = Regex.Replace(raw, @"[ \t]+", " ").Trim();
            if (line.Trim('\u0001', '\u0002', ' ') == "") continue;
            if (line.StartsWith('\u0001')) blocks.Add(new Block("h", line.Trim('\u0001', ' ')));
            else if (line.StartsWith('\u0002')) blocks.Add(new Block("li", line.Trim('\u0002', ' ')));
            else if (blocks.Count > 0 && blocks[^1].Kind == "p" && blocks[^1].Text.Length < 600 && !blocks[^1].Text.EndsWith('.') && !blocks[^1].Text.EndsWith(':'))
                blocks[^1] = blocks[^1] with { Text = blocks[^1].Text + " " + line };
            else blocks.Add(new Block("p", line));
        }
        return blocks.Take(120).ToList();
    }

    [GeneratedRegex("<img[^>]+src=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase)] private static partial Regex ImgTag();

    static List<string> Pictures(string html, string baseUrl) =>
        ImgTag().Matches(html ?? "").Select(m => Absolute(WebUtility.HtmlDecode(m.Groups[1].Value), baseUrl)).OfType<string>()
            .Where(u => !Regex.IsMatch(u, @"shields\.io|badge|badgen|img\.shields|discord(app)?\.com/api|ko-fi\.com/img|buymeacoffee|paypal|patreon|\.svg($|\?)", RegexOptions.IgnoreCase))
            .Distinct().Take(12).ToList();

    static string? Absolute(string url, string baseUrl)
    {
        if (url.StartsWith("//")) return "https:" + url;
        if (Uri.TryCreate(url, UriKind.Absolute, out var abs)) return abs.Scheme == "https" ? abs.ToString() : null;
        return Uri.TryCreate(new Uri(baseUrl), url, out var rel) && rel.Scheme == "https" ? rel.ToString() : null;
    }
}
