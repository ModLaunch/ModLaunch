using System.Text.Json.Nodes;
using ModLaunch.Games;
using ModLaunch.Sources;

namespace ModLaunch.Core;

/// <summary>Избранные моды — settings.favorites, тот же формат, что в 3.x.</summary>
public static class Favorites
{
    static JsonArray List => Settings.Data["favorites"] as JsonArray ?? (JsonArray)(Settings.Data["favorites"] = new JsonArray());

    public static bool Has(string gameId, string modId) => List.Any(f => f.Str("gameId") == gameId && f.Str("modId") == modId);

    public static bool Toggle(string gameId, ModInfo mod)
    {
        var now = !Has(gameId, mod.Id);
        var rest = List.Where(f => !(f.Str("gameId") == gameId && f.Str("modId") == mod.Id)).Select(f => f!.DeepClone()).ToList();
        if (now)
            rest.Insert(0, new JsonObject
            {
                ["gameId"] = gameId, ["modId"] = mod.Id, ["name"] = mod.Name, ["author"] = mod.Author,
                ["icon"] = mod.Icon, ["description"] = mod.Description, ["at"] = DateTime.UtcNow.ToString("o"),
            });
        Settings.Data["favorites"] = new JsonArray(rest.Take(200).ToArray());
        Settings.Save();
        return now;
    }

    public static List<(string GameId, ModInfo Mod)> All() =>
        List.Select(f =>
        {
            var game = GameCatalog.ById(f.Str("gameId") ?? "");
            if (game is null || f.Str("modId") is not string id) return default;
            return (game.Id, new ModInfo
            {
                Source = game.Catalog switch { CatalogKind.Nexus => "nexus", CatalogKind.Thunderstore => "thunderstore", _ => "modlinks" },
                Id = id, Name = f.Str("name") ?? id, Author = f.Str("author") ?? "", Icon = f.Str("icon"), Description = f.Str("description") ?? "",
            });
        }).Where(x => x.Item2 is not null).ToList();
}
