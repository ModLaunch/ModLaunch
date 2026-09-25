using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Sources;

namespace ModLaunch.Features;

public sealed record RecentMod(string Game, ModInfo Mod, DateTime At);

/// <summary>Недавно просмотренные моды (как «Recently viewed» на Nexus): последние 60.</summary>
public static class Recent
{
    const int Keep = 60;
    static JsonArray Items => Settings.Data["recentMods"] as JsonArray ?? (JsonArray)(Settings.Data["recentMods"] = new JsonArray());

    public static void Add(string game, ModInfo mod)
    {
        if (Program.Demo) return;
        var items = Items;
        for (var i = items.Count - 1; i >= 0; i--)
            if (items[i] is JsonObject o && o.Str("game") == game && o.Str("source") == mod.Source && o.Str("id") == mod.Id) items.RemoveAt(i);
        items.Insert(0, new JsonObject
        {
            ["game"] = game, ["source"] = mod.Source, ["id"] = mod.Id, ["name"] = mod.Name, ["author"] = mod.Author,
            ["icon"] = mod.Icon, ["version"] = mod.Version, ["downloads"] = mod.Downloads, ["at"] = DateTime.UtcNow.ToString("o"),
        });
        while (items.Count > Keep) items.RemoveAt(items.Count - 1);
        Settings.Save();
    }

    public static List<RecentMod> All() => Items.OfType<JsonObject>().Select(o => new RecentMod(o.Str("game") ?? "", new ModInfo
    {
        Source = o.Str("source") ?? "", Id = o.Str("id") ?? "", Name = o.Str("name") ?? "", Author = o.Str("author") ?? "",
        Icon = o.Str("icon"), Version = o.Str("version") ?? "", Downloads = o.Long("downloads"),
    }, DateTime.TryParse(o.Str("at"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : DateTime.UtcNow)).ToList();

    public static void Clear() { Settings.Data.Remove("recentMods"); Settings.Save(); }
}
