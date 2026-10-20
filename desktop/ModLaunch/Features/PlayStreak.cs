using System.Text.Json.Nodes;
using ModLaunch.Core;

namespace ModLaunch.Features;

/// <summary>
/// Серия дней с игрой — для виджета «Серия». День отмечается при каждом запуске игры через
/// ModLaunch; хранятся последние 400 дней (settings.profile.days). В 10.0.0 дни лежали
/// в profile.cases.days — их подхватываем, чтобы серия не обнулилась.
/// </summary>
public static class PlayStreak
{
    static JsonArray Days()
    {
        var profile = Settings.Data.Obj("profile");
        if (profile["days"] is JsonArray days) return days;
        var old = profile["cases"]?["days"] as JsonArray;
        var fresh = old?.DeepClone() as JsonArray ?? new JsonArray();
        profile["days"] = fresh;
        return fresh;
    }

    public static void Note(DateTime? when = null)
    {
        var day = (when ?? DateTime.Now).Date.ToString("yyyy-MM-dd");
        var days = Days();
        if (days.Any(n => n?.ToString() == day)) return;
        days.Add(JsonValue.Create(day));
        while (days.Count > 400) days.RemoveAt(0);
        Settings.Save();
    }

    /// <summary>Сколько дней подряд вы играли (сегодня ещё не играли — считаем до вчера).</summary>
    public static int Current()
    {
        var days = Days().Select(n => DateTime.TryParse(n?.ToString(), out var x) ? x.Date : DateTime.MinValue).ToHashSet();
        var day = DateTime.Today;
        if (!days.Contains(day)) day = day.AddDays(-1);
        var n = 0;
        while (days.Contains(day)) { n++; day = day.AddDays(-1); }
        return n;
    }
}
