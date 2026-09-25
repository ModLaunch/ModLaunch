using System.Xml.Linq;
using ModLaunch.Core;

namespace ModLaunch.Sources;

/// <summary>Каталог Hollow Knight: открытый ModLinks.xml с прямыми ссылками и sha256.</summary>
public static class ModLinks
{
    const string ModLinksUrl = "https://raw.githubusercontent.com/hk-modding/modlinks/main/ModLinks.xml";
    const string ApiLinksUrl = "https://raw.githubusercontent.com/hk-modding/modlinks/main/ApiLinks.xml";

    static readonly string[] Popular =
    [
        "Benchwarp", "Custom Knight", "Randomizer 4", "QoL", "HKMP", "DebugMod", "Pale Court", "Transcendence",
        "Toggleable Bindings", "Lightbringer", "Fyrenest", "MapChanger", "RandoMapMod", "Charm Changer", "GodSeekerPlus",
        "AdditionalMaps", "Mantis Gods", "HKTimer", "RandoPlus", "Satchel", "ItemChanger",
    ];

    static (DateTime At, List<ModInfo> Mods)? _cache;

    static string PlatformKey => OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "Mac" : "Linux";

    static XElement? Child(XElement? e, string name) => e?.Elements().FirstOrDefault(x => x.Name.LocalName == name);
    static IEnumerable<XElement> Children(XElement? e, string name) => e?.Elements().Where(x => x.Name.LocalName == name) ?? [];
    static string Text(XElement? e) => e?.Value.Trim() ?? "";

    static (string Url, string? Sha) PickLink(XElement manifest)
    {
        if (Child(manifest, "Link") is { } link) return (Text(link), link.Attribute("SHA256")?.Value);
        var links = Child(manifest, "Links");
        var node = Child(links, PlatformKey) ?? Child(links, "Windows") ?? Child(links, "Linux") ?? Child(links, "Mac");
        return (Text(node), node?.Attribute("SHA256")?.Value);
    }

    public static async Task<List<ModInfo>> Load(CancellationToken ct = default)
    {
        if (_cache is { } c && DateTime.UtcNow - c.At < TimeSpan.FromMinutes(15)) return c.Mods;
        var xml = XDocument.Parse(await Http.GetString(ModLinksUrl, ct, 30));
        var mods = new List<ModInfo>();
        foreach (var m in xml.Root?.Elements().Where(e => e.Name.LocalName == "Manifest") ?? [])
        {
            var name = Text(Child(m, "Name"));
            var (url, sha) = PickLink(m);
            if (name == "" || url == "") continue;
            var repo = Text(Child(m, "Repository"));
            mods.Add(new ModInfo
            {
                Source = "modlinks",
                Id = name,
                Name = Text(Child(m, "DisplayName")) is { Length: > 0 } dn ? dn : name,
                Author = string.Join(", ", Children(Child(m, "Authors"), "Author").Select(Text).Where(s => s != "")) is { Length: > 0 } a ? a : GithubOwner(repo),
                Version = Text(Child(m, "Version")),
                Description = Text(Child(m, "Description")),
                Url = repo == "" ? null : repo,
                DownloadUrl = url,
                Sha256 = sha,
                Categories = Children(Child(m, "Tags"), "Tag").Select(Text).Where(s => s != "").ToArray(),
                Dependencies = Children(Child(m, "Dependencies"), "Dependency").Select(Text).Where(s => s != "").ToList(),
            });
        }
        _cache = (DateTime.UtcNow, mods);
        return mods;
    }

    static string GithubOwner(string url)
    {
        var m = System.Text.RegularExpressions.Regex.Match(url, @"^https?://github\.com/([^/]+)/", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : "";
    }

    static int Rank(ModInfo m)
    {
        var i = Array.FindIndex(Popular, p => p.Equals(m.Id, StringComparison.OrdinalIgnoreCase));
        return i < 0 ? int.MaxValue : i;
    }

    public static async Task<Page> Search(Query q, string[] tags, CancellationToken ct = default)
    {
        IEnumerable<ModInfo> result = await Load(ct);
        if (tags.Length > 0) result = result.Where(m => m.Categories.Any(tags.Contains));
        var needle = q.Text.Trim();
        if (needle != "")
        {
            result = result
                .Where(m => m.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) || m.Author.Contains(needle, StringComparison.OrdinalIgnoreCase) || m.Description.Contains(needle, StringComparison.OrdinalIgnoreCase))
                .OrderBy(m => m.Name.StartsWith(needle, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(Rank).ThenBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase);
        }
        else result = result.OrderBy(Rank).ThenBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase);

        var all = result.ToList();
        const int size = 24;
        var page = all.Skip((q.Page - 1) * size).Take(size).ToList();
        return new Page(page, all.Count, q.Page * size < all.Count, q.Page);
    }

    public static async Task<(List<ModInfo> Order, List<string> Missing)> Resolve(string rootId, CancellationToken ct = default)
    {
        var byId = (await Load(ct)).ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        var order = new List<ModInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        void Visit(string id, HashSet<string> trail)
        {
            if (seen.Contains(id) || trail.Contains(id)) return;
            if (!byId.TryGetValue(id, out var mod)) { missing.Add(id); return; }
            var next = new HashSet<string>(trail, StringComparer.OrdinalIgnoreCase) { id };
            foreach (var dep in mod.Dependencies) Visit(dep, next);
            seen.Add(id);
            order.Add(mod);
        }
        Visit(rootId, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        return (order, missing);
    }

    public static async Task<(string Version, string Url, string? Sha)> LoadApi(CancellationToken ct = default)
    {
        var xml = XDocument.Parse(await Http.GetString(ApiLinksUrl, ct, 30));
        var manifest = xml.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "Manifest")
            ?? throw new InvalidOperationException("ApiLinks.xml");
        var node = Child(Child(manifest, "Links"), PlatformKey);
        return (Text(Child(manifest, "Version")), Text(node), node?.Attribute("SHA256")?.Value);
    }
}
