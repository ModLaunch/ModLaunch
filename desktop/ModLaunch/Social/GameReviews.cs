using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Games;

namespace ModLaunch.Social;

/// <summary>
/// 28.0: отзывы об играх — только от тех, кто в них играл (час и больше по часам Steam или учёту ModLaunch).
/// Лежат в той же базе отзывов, что и отзывы о модах (правила там уже выложены): «мод» у такого отзыва — "_game",
/// «игра» — общий для всех ключ: номер в Steam, если он известен, иначе номер в магазине игры, иначе имя.
/// </summary>
public static partial class GameReviews
{
    public const string Mod = "_game";
    public const int MinMinutes = 60;

    static readonly HashSet<string> GlobalIds = ["epic", "gog", "xbox", "ea", "ubisoft", "battlenet"];

    [GeneratedRegex(@"[^\p{L}\p{N}]+")] private static partial Regex NotWord();

    static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant()[..24];

    /// <summary>Ключ игры для отзывов (до 40 символов, как требуют правила): одинаковый у всех, у кого эта игра есть.</summary>
    public static string Key(LibGame g)
    {
        if (g.SteamAppId > 0) return $"steam:{g.SteamAppId}";
        var colon = g.Key.IndexOf(':');
        if (colon > 0 && GlobalIds.Contains(g.Store))
        {
            var id = g.Key[(colon + 1)..];
            return g.Store.Length + 1 + id.Length <= 40 ? $"{g.Store}:{id}" : $"{g.Store}:{Hash(id)}";
        }
        // Своя копия или репак без номера: по имени (регистр, пробелы и знаки не важны).
        return "name:" + Hash(NotWord().Replace(g.Name.ToLowerInvariant(), ""));
    }

    public static Stat? Stat(LibGame g) => Reviews.Stats().GetValueOrDefault($"{Key(g)}|{Mod}");

    public static List<ReviewView> For(LibGame g) => Reviews.ForMod(Key(g), Mod);

    public static Review? Mine(LibGame g) => Reviews.Mine(Key(g), Mod);

    /// <summary>Сколько наиграно и хватает ли этого, чтобы оценить игру.</summary>
    public static (bool Ok, long Minutes) Gate(LibGame g)
    {
        var minutes = AllGames.Played(g).Minutes;
        return (minutes >= MinMinutes, minutes);
    }

    public static async Task Submit(LibGame g, int stars, string text, string name)
    {
        if (!Gate(g).Ok) throw new ServiceError("NOT_PLAYED");
        await Reviews.Submit(Key(g), Mod, g.Name, stars, text, name, played: true);
    }

    public static Task Remove(LibGame g) => Reviews.Remove(Key(g), Mod);

    [SelfTest]
    static string GameReviewKeysAreShared()
    {
        LibGame G(string key, string store, string name, int steam = 0) => new(key, store, name, null, steam, true, 0, 0, 0, null, null);
        var cases = new (LibGame Game, string Want)[]
        {
            (G("steam:730", "steam", "Counter-Strike 2", 730), "steam:730"),
            (G("epic:Fortnite", "epic", "Fortnite", 0), "epic:Fortnite"),
            (G("ml:hk", "other", "Hollow Knight", 367520), "steam:367520"),
        };
        foreach (var (game, want) in cases)
            if (Key(game) != want) throw new Exception($"{game.Key} → {Key(game)}, want {want}");
        var longXbox = Key(G("xbox:Microsoft.SomeVeryLongPackageFamilyName_8wekyb3d8bbwe", "xbox", "Some Game"));
        if (longXbox.Length > 40 || !longXbox.StartsWith("xbox:")) throw new Exception("long xbox key: " + longXbox);
        var a = Key(G("other:C:/Games/Elden Ring", "other", "ELDEN RING"));
        var b = Key(G("other:D:/repack/elden", "other", "Elden-Ring"));
        if (a != b || a.Length > 40) throw new Exception($"name keys differ: {a} vs {b}");
        return "Steam id wins, store ids kept (long ones hashed to ≤40), copies without ids match by name";
    }
}

public static partial class Reviews
{
    /// <summary>Снимки экрана: несколько отзывов об игре без сервера.</summary>
    internal static void DemoGame(string key, string gameName)
    {
        var now = DateTime.UtcNow;
        (string Name, int Stars, string Text, int Days)[] rows =
        [
            ("Lena", 5, "Сотни часов, и всё ещё нахожу новое. С модами из ModLaunch — вообще другая игра.", 2),
            ("kirill_d", 4, "Отличная игра, но первые часы затянуты. Потом не оторваться.", 6),
            ("Max", 5, "Лучшее, во что я играл в этом году.", 11),
            ("Арина", 3, "Красиво, но на слабом ноутбуке тормозит даже на низких.", 20),
        ];
        var i = 0;
        foreach (var r in rows)
        {
            var id = $"demo{i++}_{ModKey(key, GameReviews.Mod)}";
            var at = now.AddDays(-r.Days).ToString("o");
            Store[id] = new System.Text.Json.Nodes.JsonObject
            {
                ["id"] = id, ["uid"] = "demo" + i, ["game"] = key, ["mod"] = GameReviews.Mod, ["modName"] = gameName, ["key"] = ModKey(key, GameReviews.Mod),
                ["name"] = r.Name, ["stars"] = r.Stars, ["text"] = r.Text, ["deleted"] = false, ["app"] = Http.Version, ["created"] = at, ["updated"] = at,
            };
        }
    }
}
