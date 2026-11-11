using System.Globalization;
using System.Text.Json.Nodes;
using ModLaunch.Core;

namespace ModLaunch.Creator;

/// <summary>Счётчики одного мода Hub на момент снимка.</summary>
public readonly record struct ModCounts(long Downloads, long Likes, long Comments)
{
    public static ModCounts Of(HubMod m) => new(m.Downloads, m.Likes, m.Comments);

    public long Get(string metric) => metric switch { "likes" => Likes, "comments" => Comments, _ => Downloads };
}

/// <summary>
/// Что нового у автора с прошлого раза: kind — downloads, likes, comments (у одного мода) или sales (рынок).
/// Строка колокольчика и полоски «С прошлого визита».
/// </summary>
public sealed record CreatorNote(string Kind, long N, string? ModId = null, string? ModName = null);

/// <summary>Шаг «Пути автора»: create, run, publish, share.</summary>
public sealed record JourneyStep(string Id, bool Done);

/// <summary>
/// 26.0: статистика автора по дням. Сервер хранит только итоговые счётчики (истории по дням в Firestore нет, а новые
/// правила базы здесь не заводим), поэтому ModLaunch сам записывает на этом ПК снимок счётчиков своих модов Hub —
/// один на день, последний за день побеждает — всякий раз, когда данные Hub загружаются. Так копится честная история:
/// с первого снимка, без выдуманных точек. Формат в настройках: days {yyyy-MM-dd: {id: {downloads, likes, comments}}}.
/// </summary>
public static class CreatorStats
{
    /// <summary>Сколько дней истории хранить: график — до 30 дней, остальное — запас на сравнение «за неделю».</summary>
    public const int KeepDays = 90;

    public static string Day(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static DateTime? ParseDay(string key) =>
        DateTime.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.Date : null;

    /// <summary>Ссылка на мод Hub для друзей (та же, что «Поделиться» на странице мода).</summary>
    public static string ShareUrl(string id) => $"https://modlaunch.github.io/ModLaunch/hub.html#mod={Uri.EscapeDataString(id)}";

    // ---------------------------------------------------------------- снимки

    /// <summary>Записать снимок дня (заменяет более ранний снимок того же дня) и убрать слишком старые.</summary>
    public static void Record(JsonObject days, DateTime today, IEnumerable<KeyValuePair<string, ModCounts>> mods)
    {
        var day = new JsonObject();
        foreach (var (id, c) in mods) day[id] = new JsonObject { ["downloads"] = c.Downloads, ["likes"] = c.Likes, ["comments"] = c.Comments };
        days[Day(today)] = day;
        Prune(days, today);
    }

    /// <summary>Убрать дни старше keep (и испорченные ключи). Возвращает, сколько убрано.</summary>
    public static int Prune(JsonObject days, DateTime today, int keep = KeepDays)
    {
        var from = today.Date.AddDays(-keep);
        var drop = days.Select(kv => kv.Key).Where(k => ParseDay(k) is not { } d || d < from).ToList();
        foreach (var k in drop) days.Remove(k);
        return drop.Count;
    }

    public static Dictionary<string, ModCounts> ReadMods(JsonObject? day)
    {
        var mods = new Dictionary<string, ModCounts>(StringComparer.Ordinal);
        if (day is null) return mods;
        foreach (var (id, node) in day)
            if (node is JsonObject c) mods[id] = new ModCounts(c.Long("downloads"), c.Long("likes"), c.Long("comments"));
        return mods;
    }

    /// <summary>С какого дня копится история (null — снимков ещё нет).</summary>
    public static DateTime? Since(JsonObject days) => days.Select(kv => ParseDay(kv.Key)).Where(d => d is not null).Min();

    /// <summary>Итог метрики по дням снимков: по всем модам или по одному (дни, где мода нет, пропускаются).</summary>
    public static SortedDictionary<DateTime, long> Totals(JsonObject days, string metric, string? mod = null)
    {
        var totals = new SortedDictionary<DateTime, long>();
        foreach (var (key, node) in days)
        {
            if (ParseDay(key) is not { } d || node is not JsonObject day) continue;
            var mods = ReadMods(day);
            if (mod is null) totals[d] = mods.Values.Sum(c => c.Get(metric));
            else if (mods.TryGetValue(mod, out var c)) totals[d] = c.Get(metric);
        }
        return totals;
    }

    /// <summary>
    /// Ряд для графика: n дней по сегодняшний. Дни без снимка берут последнее известное значение (Real = false) —
    /// линия не падает в ноль оттого, что в тот день программу не открывали; до первого снимка — null.
    /// </summary>
    public static List<(DateTime Day, long? Value, bool Real)> Series(JsonObject days, DateTime today, int n, string metric, string? mod = null)
    {
        var totals = Totals(days, metric, mod);
        var start = today.Date.AddDays(-(n - 1));
        long? last = null;
        foreach (var (d, v) in totals) if (d < start) last = v;
        var list = new List<(DateTime, long?, bool)>(n);
        for (var i = 0; i < n; i++)
        {
            var d = start.AddDays(i);
            if (totals.TryGetValue(d, out var v)) { last = v; list.Add((d, v, true)); }
            else list.Add((d, last, false));
        }
        return list;
    }

    /// <summary>
    /// Прирост за span дней: последний снимок минус последний снимок не позже «сегодня − span». Если истории меньше —
    /// от самого раннего снимка (Since скажет, с какого дня). null — снимок пока один, сравнивать не с чем.
    /// </summary>
    public static (long Delta, DateTime Since)? Change(JsonObject days, DateTime today, int span, string metric, string? mod = null)
    {
        var totals = Totals(days, metric, mod);
        if (totals.Count < 2) return null;
        var latest = totals.Last();
        var cut = today.Date.AddDays(-span);
        var older = totals.Where(kv => kv.Key <= cut).ToList();
        var from = older.Count > 0 ? older[^1] : totals.First();
        if (from.Key == latest.Key) return null;
        return (latest.Value - from.Value, from.Key);
    }

    // ---------------------------------------------------------------- уведомления

    /// <summary>
    /// Что прибавилось с прошлого просмотра: скачивания и лайки — одной строкой на все моды, новые комментарии —
    /// по модам (до трёх, самые обсуждаемые первыми), продажи — отдельно. Мод, которого в прошлый раз не было
    /// (только что выложен), считается с нуля. Убыль (сняли лайк, удалили комментарий) не показывается.
    /// </summary>
    public static List<CreatorNote> Notes(IReadOnlyDictionary<string, ModCounts> seen, IReadOnlyDictionary<string, ModCounts> now,
        IReadOnlyDictionary<string, string> names, long seenSales, long nowSales)
    {
        long downloads = 0, likes = 0;
        var comments = new List<CreatorNote>();
        foreach (var (id, c) in now)
        {
            var was = seen.TryGetValue(id, out var s) ? s : default;
            downloads += Math.Max(0, c.Downloads - was.Downloads);
            likes += Math.Max(0, c.Likes - was.Likes);
            if (c.Comments > was.Comments) comments.Add(new CreatorNote("comments", c.Comments - was.Comments, id, names.TryGetValue(id, out var n) ? n : id));
        }
        var notes = new List<CreatorNote>();
        if (nowSales > seenSales) notes.Add(new CreatorNote("sales", nowSales - seenSales));
        notes.AddRange(comments.OrderByDescending(c => c.N).ThenBy(c => c.ModName, StringComparer.CurrentCultureIgnoreCase).Take(3));
        if (downloads > 0) notes.Add(new CreatorNote("downloads", downloads));
        if (likes > 0) notes.Add(new CreatorNote("likes", likes));
        return notes;
    }

    // ---------------------------------------------------------------- путь автора

    /// <summary>
    /// Шаги «Пути автора» по тому, что уже есть. Поздний шаг подтверждает ранние: мод, выложенный с другого ПК,
    /// значит, что его создали и запускали, — галочки не должны требовать сделать это ещё раз.
    /// </summary>
    public static List<JourneyStep> Journey(bool created, bool ran, bool published, bool shared)
    {
        var ids = new[] { "create", "run", "publish", "share" };
        var raw = new[] { created, ran, published, shared };
        return ids.Select((id, i) => new JourneyStep(id, raw.Skip(i).Any(x => x))).ToList();
    }

    [SelfTest]
    static string CreatorStatsSnapshots()
    {
        var days = new JsonObject();
        var today = new DateTime(2026, 11, 11);
        KeyValuePair<string, ModCounts> M(string id, long d, long l, long c) => new(id, new ModCounts(d, l, c));
        CreatorStats.Record(days, today.AddDays(-120), [M("a", 1, 0, 0)]);
        CreatorStats.Record(days, today.AddDays(-10), [M("a", 100, 10, 1)]);
        CreatorStats.Record(days, today.AddDays(-6), [M("a", 150, 12, 2), M("b", 5, 0, 0)]);
        CreatorStats.Record(days, today.AddDays(-2), [M("a", 190, 12, 3), M("b", 25, 2, 0)]);
        days["garbage"] = 1;
        // Тот же день второй раз — заменяет первый снимок дня.
        CreatorStats.Record(days, today, [M("a", 200, 15, 3), M("b", 30, 2, 1)]);
        CreatorStats.Record(days, today, [M("a", 210, 15, 4), M("b", 30, 2, 1)]);
        if (days.ContainsKey(Day(today.AddDays(-120))) || days.ContainsKey("garbage")) throw new Exception("prune");
        if (days.Count != 4) throw new Exception("days " + days.Count);
        if (Since(days) != today.AddDays(-10)) throw new Exception("since");
        // Неделя: сегодня (240) против последнего снимка не позже −7 дней (−10: 100).
        if (Change(days, today, 7, "downloads") is not { Delta: 140 } week || week.Since != today.AddDays(-10)) throw new Exception("week " + Change(days, today, 7, "downloads"));
        // Мод b появился 6 дней назад: прирост — от его первого снимка.
        if (Change(days, today, 7, "downloads", "b") is not { Delta: 25 } b || b.Since != today.AddDays(-6)) throw new Exception("new mod");
        if (Change(days, today, 7, "comments", "a") is not { Delta: 3 }) throw new Exception("comments");
        var single = new JsonObject();
        CreatorStats.Record(single, today, [M("a", 5, 0, 0)]);
        if (Change(single, today, 7, "downloads") is not null) throw new Exception("one snapshot has no change");
        // Ряд: пропуски берут прошлое значение, до первого снимка — пусто.
        var series = Series(days, today, 14, "downloads");
        if (series.Count != 14 || series[^1] is not { Value: 240, Real: true }) throw new Exception("series end");
        if (series[0].Value is not null || series[3] is not { Value: 100, Real: true } || series[4] is not { Value: 100, Real: false }) throw new Exception("series carry " + string.Join(",", series.Select(s => s.Value)));
        if (Prune(days, today, keep: 5) != 2 || days.Count != 2) throw new Exception("prune keep");
        return "one snapshot a day, 90-day pruning, week change from the nearest older snapshot, carried-forward series";
    }

    [SelfTest]
    static string CreatorNotesAndJourney()
    {
        var seen = new Dictionary<string, ModCounts> { ["a"] = new(100, 10, 2), ["b"] = new(50, 5, 0) };
        var now = new Dictionary<string, ModCounts> { ["a"] = new(160, 9, 5), ["b"] = new(50, 5, 1), ["c"] = new(4, 1, 0) };
        var names = new Dictionary<string, string> { ["a"] = "Alpha", ["b"] = "Beta", ["c"] = "Gamma" };
        var notes = Notes(seen, now, names, 3, 5);
        var kinds = string.Join(",", notes.Select(n => n.Kind + ":" + n.N + (n.ModName is null ? "" : ":" + n.ModName)));
        // Лайк у «a» сняли (10 → 9) — не уведомление; новый мод «c» считается с нуля.
        if (kinds != "sales:2,comments:3:Alpha,comments:1:Beta,downloads:64,likes:1") throw new Exception("notes " + kinds);
        if (Notes(now, now, names, 5, 5).Count != 0) throw new Exception("nothing new");
        var steps = Journey(created: true, ran: false, published: false, shared: false);
        if (string.Join("", steps.Select(s => s.Done ? 1 : 0)) != "1000") throw new Exception("journey start");
        // Выложен с другого ПК (своих проектов здесь нет) — создать и запустить уже не просим.
        steps = Journey(created: false, ran: false, published: true, shared: false);
        if (string.Join("", steps.Select(s => s.Done ? 1 : 0)) != "1110" || steps.First(s => !s.Done).Id != "share") throw new Exception("journey implied");
        if (Journey(false, false, false, false).Any(s => s.Done)) throw new Exception("journey empty");
        return "gains since last seen (sales, comments per mod, downloads, likes), journey steps implied by later ones";
    }
}
