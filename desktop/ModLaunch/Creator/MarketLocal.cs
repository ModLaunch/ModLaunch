using System.Text.Json.Nodes;
using ModLaunch.Core;

namespace ModLaunch.Creator;

/// <summary>
/// 9.3: то, что рынок креаторов помнит на этом компьютере (как «Желаемое» и «Подписки» в Steam):
/// список желаемого с датой добавления, авторы, на которых вы подписаны, просмотренные лоты и
/// очередь открытий («не интересно» больше не предлагается). Сервер для этого не нужен.
/// </summary>
public static class MarketLocal
{
    static JsonObject Root => Settings.Data.Obj("market93");

    // ---------------------------------------------------------------- желаемое

    public static bool Wished(string id) => Root.Obj("wish").Str(id) is not null;

    public static IReadOnlyList<(string Id, DateTime At)> Wishlist() => Root.Obj("wish")
        .Select(kv => (kv.Key, DateTime.TryParse(kv.Value?.ToString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var at) ? at : DateTime.UtcNow))
        .OrderByDescending(x => x.Item2).ToList();

    public static int WishCount => Root.Obj("wish").Count;

    /// <summary>Добавить или убрать; true — теперь в желаемом.</summary>
    public static bool ToggleWish(string id)
    {
        var wish = Root.Obj("wish");
        var now = wish.Remove(id) is false;
        if (now) wish[id] = DateTime.UtcNow.ToString("o");
        Settings.Save();
        Changed?.Invoke();
        return now;
    }

    // ---------------------------------------------------------------- подписки на авторов

    public static bool Follows(string uid) => Root.Obj("follow").Str(uid) is not null;

    public static IReadOnlyList<string> Following() => Root.Obj("follow").Select(kv => kv.Key).ToList();

    public static bool ToggleFollow(string uid, string name)
    {
        var follow = Root.Obj("follow");
        var now = follow.Remove(uid) is false;
        if (now) follow[uid] = name;
        Settings.Save();
        Changed?.Invoke();
        return now;
    }

    // ---------------------------------------------------------------- просмотренное и очередь открытий

    public static void Viewed(string id)
    {
        var seen = Root.Arr("seen");
        Root["seen"] = new JsonArray([JsonValue.Create(id), .. seen.Where(n => n?.ToString() != id).Take(40).Select(n => (JsonNode?)JsonValue.Create(n!.ToString()))]);
        Settings.Save();
    }

    public static IReadOnlyList<string> Seen() => Root.Arr("seen").Select(n => n?.ToString() ?? "").Where(s => s != "").ToList();

    public static bool Ignored(string id) => Root.Obj("skip").Str(id) is not null;

    public static void Ignore(string id) { Root.Obj("skip")[id] = DateTime.UtcNow.ToString("o"); Settings.Save(); }

    /// <summary>Что-то из желаемого или подписок поменялось — страницы рынка перерисуются.</summary>
    public static event Action? Changed;

    // ---------------------------------------------------------------- оценки

    /// <summary>Сводка отзывов как в Steam: «Очень положительные» и т. п. (ключ строки и цвет).</summary>
    public static (string Key, string Tone) Verdict(double average, int count)
    {
        if (count == 0) return ("v93.rev.none", "muted");
        var share = (average - 1) / 4;
        return share switch
        {
            >= 0.95 when count >= 50 => ("v93.rev.overwhelming", "good"),
            >= 0.8 when count >= 10 => ("v93.rev.very", "good"),
            >= 0.8 => ("v93.rev.positive", "good"),
            >= 0.7 => ("v93.rev.mostly", "good"),
            >= 0.4 => ("v93.rev.mixed", "warn"),
            >= 0.2 => ("v93.rev.mostlyNeg", "bad"),
            _ => ("v93.rev.negative", "bad"),
        };
    }

    /// <summary>Рекомендации «для вас»: лоты для ваших игр, от авторов из подписок и с тегами из желаемого — выше.</summary>
    public static List<Listing> ForYou(IEnumerable<Listing> all, IEnumerable<string> myGames)
    {
        var games = myGames.ToHashSet();
        var list = all.ToList();
        var wishTags = list.Where(l => Wished(l.Id)).SelectMany(l => l.Tags).ToHashSet();
        double Score(Listing l) =>
            (games.Contains(l.Game) ? 3 : l.Game == "" ? 1 : 0) + (Follows(l.Uid) ? 2.5 : 0) + l.Tags.Count(wishTags.Contains) * 0.8
            + Math.Log10(l.Sales + 1) + (l.Free ? 0.4 : 0) - (Ignored(l.Id) ? 10 : 0) - (Owned(l) ? 6 : 0);
        return list.OrderByDescending(Score).ThenByDescending(l => l.Updated).ToList();
    }

    static bool Owned(Listing l) => l.Mine || Market.Owned.Contains(l.Id);

    /// <summary>Авторы рынка: число лотов, продаж и бесплатных — для «Авторов» и профиля.</summary>
    public sealed record Author(string Uid, string Name, int Items, long Sales, int Free, int Workshop, long Downloads, DateTime? Since, IReadOnlyList<string> Games);

    public static List<Author> Authors(IEnumerable<Listing> market, IEnumerable<HubMod> hub)
    {
        var m = market.ToList();
        var h = hub.ToList();
        var uids = m.Select(l => l.Uid).Concat(h.Select(x => x.Uid)).Where(u => u != "").Distinct();
        return uids.Select(uid =>
        {
            var mine = m.Where(l => l.Uid == uid).ToList();
            var mods = h.Where(x => x.Uid == uid).ToList();
            var name = mine.FirstOrDefault()?.Author ?? mods.FirstOrDefault()?.Author ?? "?";
            var since = mine.Select(l => l.Created).Concat(mods.Select(x => x.Created)).Where(d => d is not null).DefaultIfEmpty(null).Min();
            var games = mine.Select(l => l.Game).Concat(mods.Select(x => x.Game)).Where(g => g != "").Distinct().ToList();
            return new Author(uid, name, mine.Count, mine.Sum(l => l.Sales), mine.Count(l => l.Free), mods.Count, mods.Sum(x => x.Downloads), since, games);
        }).OrderByDescending(c => c.Sales * 3 + c.Downloads / 100 + c.Items).ToList();
    }
}
