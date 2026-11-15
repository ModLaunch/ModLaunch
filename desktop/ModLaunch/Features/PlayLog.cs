using System.Text.Json.Nodes;
using ModLaunch.Core;

namespace ModLaunch.Features;

/// <summary>Итог за период: всего, по играм, по дням, по дням недели, дни подряд.</summary>
public sealed record PlaySummary(long Seconds, IReadOnlyList<(string Id, long Seconds)> Games, IReadOnlyDictionary<DateOnly, long> Days,
    long[] Weekdays, int ActiveDays, int Streak, DateOnly? BestDay);

/// <summary>
/// 30.2: журнал игрового времени по дням (playlog.json) — для «Итогов». PlayTime хранит только общие часы,
/// а здесь: день → игра → секунды. Сеанс через полночь делится между днями (по местному времени).
/// Пишется для игр ModLaunch, игр из «Всех игр» и игр Steam, запущенных при открытом ModLaunch. Хранится 3 года.
/// </summary>
public static class PlayLog
{
    const int KeepDays = 3 * 366;
    static readonly Lazy<JsonFile> File = new(() => new JsonFile(Path.Combine(Paths.DataDir, "playlog.json"), () => new JsonObject()));

    public static event Action? Changed;

    /// <summary>С какого дня ведётся журнал (первый записанный день).</summary>
    public static DateOnly? Since => Days(File.Value.Data).Keys.Order().Cast<DateOnly?>().FirstOrDefault();

    static JsonObject DaysNode(JsonObject root)
    {
        if (root["days"] is JsonObject d) return d;
        var created = new JsonObject();
        root["days"] = created;
        return created;
    }

    static Dictionary<DateOnly, Dictionary<string, long>> Days(JsonObject root)
    {
        var result = new Dictionary<DateOnly, Dictionary<string, long>>();
        foreach (var (day, games) in DaysNode(root))
        {
            if (!DateOnly.TryParseExact(day, "yyyy-MM-dd", out var date) || games is not JsonObject g) continue;
            result[date] = g.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value!.GetValue<long>());
        }
        return result;
    }

    /// <summary>Добавить сеанс в журнал (root — для самопроверки; обычно файл playlog.json).</summary>
    internal static void Add(JsonObject root, string id, DateTime startUtc, DateTime endUtc, TimeZoneInfo zone)
    {
        if (endUtc <= startUtc || endUtc - startUtc > TimeSpan.FromDays(2)) return;
        var days = DaysNode(root);
        var from = TimeZoneInfo.ConvertTimeFromUtc(startUtc, zone);
        var to = TimeZoneInfo.ConvertTimeFromUtc(endUtc, zone);
        while (from < to)
        {
            var midnight = from.Date.AddDays(1);
            var end = to < midnight ? to : midnight;
            var key = from.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            if (days[key] is not JsonObject day) { day = new JsonObject(); days[key] = day; }
            day[id] = (day[id]?.GetValue<long>() ?? 0) + (long)(end - from).TotalSeconds;
            from = end;
        }
        // Старше трёх лет — убираем, чтобы файл не рос бесконечно.
        var cutoff = DateOnly.FromDateTime(to).AddDays(-KeepDays);
        foreach (var old in days.Select(kv => kv.Key).Where(k => DateOnly.TryParseExact(k, "yyyy-MM-dd", out var d) && d < cutoff).ToList()) days.Remove(old);
    }

    /// <summary>Записать сеанс игры (короче 15 секунд — не считаем, как и общие часы).</summary>
    public static void Add(string id, DateTime startUtc, DateTime endUtc)
    {
        if (endUtc - startUtc < TimeSpan.FromSeconds(15)) return;
        try
        {
            lock (File.Value)
            {
                Add(File.Value.Data, id, startUtc, endUtc, TimeZoneInfo.Local);
                File.Value.Save();
            }
            Changed?.Invoke();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* журнал — не главное, игру не роняем */ }
    }

    /// <summary>Запомнить имя игры (игру могут удалить, а в итогах её имя должно остаться).</summary>
    public static void Remember(string id, string name)
    {
        if (name == "") return;
        lock (File.Value)
        {
            if (File.Value.Data["names"] is not JsonObject names) { names = new JsonObject(); File.Value.Data["names"] = names; }
            if (names[id]?.GetValue<string>() == name) return;
            names[id] = name;
        }
    }

    public static string? NameOf(string id)
    {
        lock (File.Value) return (File.Value.Data["names"] as JsonObject)?[id]?.GetValue<string>();
    }

    /// <summary>Итог за дни [from; to] включительно.</summary>
    internal static PlaySummary Summarize(JsonObject root, DateOnly from, DateOnly to)
    {
        var all = Days(root).Where(kv => kv.Key >= from && kv.Key <= to).ToList();
        var byGame = new Dictionary<string, long>();
        var byDay = new Dictionary<DateOnly, long>();
        var week = new long[7];
        foreach (var (day, games) in all)
        {
            var sum = games.Values.Sum();
            if (sum <= 0) continue;
            byDay[day] = sum;
            week[((int)day.DayOfWeek + 6) % 7] += sum;
            foreach (var (id, s) in games) byGame[id] = byGame.GetValueOrDefault(id) + s;
        }
        // Самая длинная серия дней подряд с игрой.
        int streak = 0, run = 0;
        DateOnly? prev = null;
        foreach (var d in byDay.Keys.Order())
        {
            run = prev is { } p && p.AddDays(1) == d ? run + 1 : 1;
            streak = Math.Max(streak, run);
            prev = d;
        }
        var best = byDay.Count > 0 ? byDay.MaxBy(kv => kv.Value).Key : (DateOnly?)null;
        return new PlaySummary(byDay.Values.Sum(), byGame.OrderByDescending(kv => kv.Value).Select(kv => (kv.Key, kv.Value)).ToList(),
            byDay, week, byDay.Count, streak, best);
    }

    public static PlaySummary Summarize(DateOnly from, DateOnly to)
    {
        lock (File.Value) return Summarize(File.Value.Data, from, to);
    }

    /// <summary>Снимки экрана: месяц правдоподобных сеансов для данных игр (данные — во временной папке демо).</summary>
    internal static void Demo(IReadOnlyList<string> ids)
    {
        if (!Program.Screenshot || ids.Count == 0) return;
        var rnd = new Random(30);
        var today = DateTime.Today;
        lock (File.Value)
        {
            for (var back = 40; back >= 0; back--)
            {
                if (rnd.NextDouble() < 0.3) continue;
                var day = today.AddDays(-back).AddHours(18 + rnd.Next(4));
                var weekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                var minutes = rnd.Next(25, weekend ? 300 : 150);
                var id = ids[Math.Min(ids.Count - 1, (int)(Math.Pow(rnd.NextDouble(), 1.8) * ids.Count))];
                Add(File.Value.Data, id, day.ToUniversalTime(), day.AddMinutes(minutes).ToUniversalTime(), TimeZoneInfo.Local);
            }
            File.Value.Save();
        }
    }

    [SelfTest]
    static string PlayLogSplitsDaysAndSums()
    {
        var root = new JsonObject();
        var utc = TimeZoneInfo.Utc;
        // 23:00 → 01:30: час в первый день, полтора — во второй.
        Add(root, "g1", new DateTime(2026, 11, 13, 23, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 14, 1, 30, 0, DateTimeKind.Utc), utc);
        Add(root, "g2", new DateTime(2026, 11, 14, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 14, 10, 20, 0, DateTimeKind.Utc), utc);
        Add(root, "g1", new DateTime(2026, 11, 16, 20, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 16, 21, 0, 0, DateTimeKind.Utc), utc);
        Add(root, "bad", new DateTime(2026, 11, 16, 20, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 19, 21, 0, 0, DateTimeKind.Utc), utc);
        var s = Summarize(root, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30));
        if (s.Seconds != 3600 + 5400 + 1200 + 3600) throw new Exception("total " + s.Seconds);
        if (s.Days[new DateOnly(2026, 11, 13)] != 3600 || s.Days[new DateOnly(2026, 11, 14)] != 5400 + 1200) throw new Exception("midnight split");
        if (s.Games[0] != ("g1", 3600 + 5400 + 3600) || s.Games.Count != 2) throw new Exception("by game");
        if (s.ActiveDays != 3 || s.Streak != 2 || s.BestDay != new DateOnly(2026, 11, 14)) throw new Exception($"days {s.ActiveDays} streak {s.Streak} best {s.BestDay}");
        if (s.Weekdays[4] != 3600 || s.Weekdays[5] != 6600 || s.Weekdays[0] != 3600) throw new Exception("weekdays " + string.Join(",", s.Weekdays));
        if (!root.ToJsonString().Contains("2026-11-14")) throw new Exception("serialize");
        return "sessions split at local midnight, totals by game/day/weekday, streak and best day";
    }
}
