using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Sources;

namespace ModLaunch.Features;

public sealed record Tracked(string Game, string Source, string Id, string Name, string Version, string? Icon, DateTime Since);
public sealed record TrackedUpdate(Tracked Item, ModInfo Now);

/// <summary>
/// Отслеживание модов (как «Track» на Nexus): и тех, что не установлены.
/// Когда у мода выходит новая версия — он попадает в уведомления.
/// </summary>
public static class Tracking
{
    static JsonObject Root => Settings.Data.Obj("tracked");
    static string Key(string game, string source, string id) => $"{game}|{source}|{id}";

    public static bool Has(string game, ModInfo mod) => Root.ContainsKey(Key(game, mod.Source, mod.Id));

    public static bool Toggle(string game, ModInfo mod)
    {
        var key = Key(game, mod.Source, mod.Id);
        if (Root.Remove(key)) { Settings.Save(); return false; }
        Root[key] = new JsonObject
        {
            ["game"] = game, ["source"] = mod.Source, ["id"] = mod.Id, ["name"] = mod.Name,
            ["version"] = mod.Version, ["icon"] = mod.Icon, ["since"] = DateTime.UtcNow.ToString("o"),
        };
        Settings.Save();
        return true;
    }

    public static List<Tracked> All() => Root.Select(kv => kv.Value as JsonObject).OfType<JsonObject>().Select(o => new Tracked(
        o.Str("game") ?? "", o.Str("source") ?? "", o.Str("id") ?? "", o.Str("name") ?? "", o.Str("version") ?? "", o.Str("icon"),
        DateTime.TryParse(o.Str("since"), out var d) ? d : DateTime.UtcNow)).ToList();

    public static List<TrackedUpdate> Updates { get; private set; } = [];
    public static event Action? Changed;

    /// <summary>Проверить все отслеживаемые моды (по 4 параллельно).</summary>
    public static async Task Check(CancellationToken ct = default)
    {
        var found = new List<TrackedUpdate>();
        using var gate = new SemaphoreSlim(4);
        await Task.WhenAll(All().Select(async t =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var game = Games.GameCatalog.ById(t.Game);
                if (game is null) return;
                var now = await Catalog.Get(game, t.Id, ct, t.Source);
                if (now is not null && now.Version != "" && now.Version != t.Version) lock (found) found.Add(new TrackedUpdate(t, now));
            }
            catch { }
            finally { gate.Release(); }
        }));
        Updates = found;
        Changed?.Invoke();
    }

    /// <summary>Отметить обновление просмотренным: запомнить новую версию.</summary>
    public static void Seen(TrackedUpdate u)
    {
        if (Root[Key(u.Item.Game, u.Item.Source, u.Item.Id)] is JsonObject o) o["version"] = u.Now.Version;
        Settings.Save();
        Updates = Updates.Where(x => x != u).ToList();
        Changed?.Invoke();
    }
}
