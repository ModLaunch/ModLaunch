using System.Text.Json.Nodes;
using ModLaunch.Core;

namespace ModLaunch.Features;

/// <summary>
/// Коллекции игр, как в библиотеке Steam: «Избранное», свои коллекции и скрытые игры.
/// Хранятся в настройках, переносятся вместе с ними.
/// </summary>
public static class GameCollections
{
    public const string Favorites = "★";

    static JsonObject Root => Settings.Data.Obj("gameCollections");
    static JsonArray List(string name) => Root[name] as JsonArray ?? (JsonArray)(Root[name] = new JsonArray());

    public static List<string> Names() => Root.Where(kv => kv.Key != Favorites).Select(kv => kv.Key).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
    public static List<string> Games(string name) => (Root[name] as JsonArray ?? []).Select(x => x?.ToString()).OfType<string>().ToList();
    public static bool Has(string name, string game) => Games(name).Contains(game);

    public static void Toggle(string name, string game)
    {
        var list = List(name);
        var hit = list.FirstOrDefault(x => x?.ToString() == game);
        if (hit is not null) list.Remove(hit); else list.Add(game);
        Settings.Save();
    }

    public static void Create(string name) { name = name.Trim(); if (name != "" && name != Favorites) List(name); Settings.Save(); }
    public static void Delete(string name) { Root.Remove(name); Settings.Save(); }

    public static bool IsFavorite(string game) => Has(Favorites, game);

    // ---------------------------------------------------------------- скрытые игры

    static JsonArray Hidden => Settings.Data["hiddenGames"] as JsonArray ?? (JsonArray)(Settings.Data["hiddenGames"] = new JsonArray());
    public static bool IsHidden(string game) => Hidden.Any(x => x?.ToString() == game);
    public static int HiddenCount => Hidden.Count;
    public static void ToggleHidden(string game)
    {
        var hit = Hidden.FirstOrDefault(x => x?.ToString() == game);
        if (hit is not null) Hidden.Remove(hit); else Hidden.Add(game);
        Settings.Save();
    }
}
