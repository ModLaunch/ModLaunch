using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Sources;

namespace ModLaunch.Features;

/// <summary>Куда ведёт ссылка на мод: игра (может быть пустой — узнаем позже), каталог, номер и версия.</summary>
public sealed record ModLinkTarget(string GameId, string Source, string Id, string? Version);

/// <summary>
/// Ссылки на моды: страница Nexus или Thunderstore, кнопка Thunderstore «Install with Mod Manager»
/// (ror2mm://), ссылки ModLaunch Hub и свои modlaunch://. Вставил в поиск — открылась страница мода.
/// </summary>
public static partial class ModLink
{
    [GeneratedRegex(@"nexusmods\.com/([a-z0-9]+)/mods/(\d+)", RegexOptions.IgnoreCase)] private static partial Regex NexusRe();
    // В имени автора бывают дефисы (FunkFrog-and-Sipondo), в имени пакета — нет.
    [GeneratedRegex(@"thunderstore\.io/c/([\w-]+)/p/([\w-]+)/([\w]+)(?:/v/([\d.]+))?", RegexOptions.IgnoreCase)] private static partial Regex ThunderRe();
    [GeneratedRegex(@"^ror2mm://v1/install/thunderstore\.io/([\w-]+)/([\w]+)/([\d.]+)", RegexOptions.IgnoreCase)] private static partial Regex R2mmRe();
    [GeneratedRegex(@"^modlaunch://mod/([\w-]+)", RegexOptions.IgnoreCase)] private static partial Regex OwnModRe();
    [GeneratedRegex(@"^modlaunch://(nexus|thunderstore)/([\w-]+)/([\w-]+)", RegexOptions.IgnoreCase)] private static partial Regex OwnCatalogRe();
    [GeneratedRegex(@"hub\.html#mod=([\w-]+)", RegexOptions.IgnoreCase)] private static partial Regex HubRe();

    /// <summary>Схемы ссылок, которые умеет открывать программа (кроме nxm://).</summary>
    public static bool IsAppLink(string text) =>
        text.StartsWith("modlaunch://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("ror2mm://", StringComparison.OrdinalIgnoreCase);

    public static ModLinkTarget? Parse(string text)
    {
        text = text.Trim();
        if (text.Length is 0 or > 500) return null;
        Match m;
        if ((m = NexusRe().Match(text)).Success)
        {
            var domain = m.Groups[1].Value.ToLowerInvariant();
            var game = GameCatalog.All.FirstOrDefault(g => string.Equals(g.NexusDomain, domain, StringComparison.OrdinalIgnoreCase));
            return game is null ? null : new ModLinkTarget(game.Id, "nexus", m.Groups[2].Value, null);
        }
        if ((m = ThunderRe().Match(text)).Success)
        {
            var community = m.Groups[1].Value.ToLowerInvariant();
            var game = GameCatalog.All.FirstOrDefault(g => string.Equals(g.ThunderstoreCommunity, community, StringComparison.OrdinalIgnoreCase));
            return game is null ? null : new ModLinkTarget(game.Id, "thunderstore", $"{m.Groups[2].Value}-{m.Groups[3].Value}", m.Groups[4].Success ? m.Groups[4].Value : null);
        }
        // ror2mm:// не говорит, какая это игра, — узнаем по каталогам найденных игр.
        if ((m = R2mmRe().Match(text)).Success) return new ModLinkTarget("", "thunderstore", $"{m.Groups[1].Value}-{m.Groups[2].Value}", m.Groups[3].Value);
        if ((m = OwnCatalogRe().Match(text)).Success)
        {
            var source = m.Groups[1].Value.ToLowerInvariant();
            var key = m.Groups[2].Value;
            var game = GameCatalog.All.FirstOrDefault(g => source == "nexus"
                ? string.Equals(g.NexusDomain, key, StringComparison.OrdinalIgnoreCase)
                : string.Equals(g.ThunderstoreCommunity, key, StringComparison.OrdinalIgnoreCase));
            return game is null ? null : new ModLinkTarget(game.Id, source, m.Groups[3].Value, null);
        }
        if ((m = OwnModRe().Match(text)).Success || (m = HubRe().Match(text)).Success) return new ModLinkTarget("", "hub", m.Groups[1].Value, null);
        return null;
    }

    /// <summary>Найти игру и сам мод (для ссылок без игры — перебором каталогов найденных игр).</summary>
    public static async Task<(GameState Game, ModInfo Mod)?> Resolve(ModLinkTarget target, CancellationToken ct = default)
    {
        if (target.Source == "hub")
        {
            var hub = await Creator.Hub.Get(target.Id, ct);
            if (hub is null) return null;
            var g = AppState.Games.FirstOrDefault(x => x.Def.Id == hub.Game);
            return g is null ? null : (g, Creator.Hub.ToModInfo(hub));
        }
        if (target.GameId != "")
        {
            var g = AppState.Games.FirstOrDefault(x => x.Def.Id == target.GameId);
            if (g is null) return null;
            // Сначала спрашиваем каталог: иначе в «Недавних» и в заголовке мод звался бы «541», без картинки.
            try { return await Catalog.Get(g.Def, target.Id, ct, target.Source) is { } real ? (g, real) : null; }
            catch (Exception) when (!ct.IsCancellationRequested) { }
            // Нет сети — всё равно открываем страницу: ModPage сам покажет, что не загрузилось.
            return (g, new ModInfo { Source = target.Source, Id = target.Id, Name = target.Id.Contains('-') ? target.Id[(target.Id.LastIndexOf('-') + 1)..].Replace('_', ' ') : target.Id, Version = target.Version ?? "" });
        }
        foreach (var g in AppState.Games.Where(x => x.Def.ThunderstoreCommunity is not null).OrderByDescending(x => x.Status == Detect.Found))
        {
            try
            {
                if (await Thunderstore.Get(g.Def.ThunderstoreCommunity!, target.Id, ct) is { } mod) return (g, mod);
            }
            catch { }
        }
        return null;
    }

    [SelfTest]
    static string ParsesModLinks()
    {
        var cases = new (string Link, string Expect)[]
        {
            ("https://www.nexusmods.com/stardewvalley/mods/541?tab=files", "stardew-valley|nexus|541|"),
            ("https://thunderstore.io/c/lethal-company/p/notnotnotswipez/MoreCompany/", "lethal-company|thunderstore|notnotnotswipez-MoreCompany|"),
            ("https://thunderstore.io/c/lethal-company/p/Evaisa/LethalLib/v/0.16.1/", "lethal-company|thunderstore|Evaisa-LethalLib|0.16.1"),
            ("ror2mm://v1/install/thunderstore.io/Evaisa/LethalLib/0.16.1/", "|thunderstore|Evaisa-LethalLib|0.16.1"),
            // Автор с дефисами (старые имена Thunderstore), как ShareSuite в «Нужных» Risk of Rain 2.
            ("https://thunderstore.io/c/riskofrain2/p/FunkFrog-and-Sipondo/ShareSuite/", "risk-of-rain-2|thunderstore|FunkFrog-and-Sipondo-ShareSuite|"),
            ("ror2mm://v1/install/thunderstore.io/FunkFrog-and-Sipondo/ShareSuite/2.9.0/", "|thunderstore|FunkFrog-and-Sipondo-ShareSuite|2.9.0"),
            ("modlaunch://mod/abc123def456ghi789jk_slots", "|hub|abc123def456ghi789jk_slots|"),
            ("modlaunch://thunderstore/valheim/ValheimModding-Jotunn", "valheim|thunderstore|ValheimModding-Jotunn|"),
        };
        foreach (var (link, expect) in cases)
        {
            var t = Parse(link);
            var got = t is null ? "null" : $"{t.GameId}|{t.Source}|{t.Id}|{t.Version}";
            if (got != expect) throw new Exception($"{link} → {got}, expected {expect}");
        }
        if (Parse("better slots") is not null) throw new Exception("plain text parsed as a link");
        return $"{cases.Length} link shapes parsed";
    }
}
