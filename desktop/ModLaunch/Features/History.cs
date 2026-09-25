using System.Text.Json.Nodes;
using ModLaunch.Core;

namespace ModLaunch.Features;

public sealed record HistoryEntry(DateTime At, string Title, string Game, bool Ok, string? Error);

/// <summary>История загрузок (как «Download history» на Nexus): последние 300 установок.</summary>
public static class History
{
    const int Keep = 300;
    static readonly JsonFile File = new(Path.Combine(Paths.DataDir, "history.json"), () => new JsonObject { ["items"] = new JsonArray() });

    public static void Add(Job job)
    {
        var items = File.Data.Arr("items");
        if (File.Data["items"] is not JsonArray) File.Data["items"] = items;
        items.Insert(0, new JsonObject
        {
            ["at"] = DateTime.UtcNow.ToString("o"),
            ["title"] = job.Title,
            ["game"] = job.GameName,
            ["ok"] = job.Status == JobStatus.Done,
            ["error"] = job.Error,
        });
        while (items.Count > Keep) items.RemoveAt(items.Count - 1);
        File.Save();
    }

    public static List<HistoryEntry> All() => File.Data.Arr("items").OfType<JsonObject>().Select(o => new HistoryEntry(
        DateTime.TryParse(o.Str("at"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : DateTime.UtcNow,
        o.Str("title") ?? "", o.Str("game") ?? "", o.Bool("ok"), o.Str("error"))).ToList();

    public static void Clear() { File.Data["items"] = new JsonArray(); File.Save(); }
}
