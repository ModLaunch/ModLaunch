using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Mods;

namespace ModLaunch.Features;

public sealed record ProfileInfo(string Name, DateTime? UpdatedAt, int Count, bool Active);

/// <summary>Профили: какие моды включены. Хранятся в profiles.json, как в 3.x.</summary>
public static class Profiles
{
    const int Max = 30;
    static readonly JsonFile File = new(Path.Combine(Paths.DataDir, "profiles.json"), () => new JsonObject { ["games"] = new JsonObject(), ["active"] = new JsonObject() });

    static JsonObject Game(string gameId) => File.Data.Obj("games").Obj(gameId);
    static JsonObject Active => File.Data.Obj("active");

    public static string Clean(string? value)
    {
        var name = new string((value ?? "").Where(c => c >= ' ').ToArray()).Trim();
        return name.Length > 40 ? name[..40] : name;
    }

    public static List<ProfileInfo> List(string gameId) =>
        Game(gameId).Select(kv => new ProfileInfo(
                kv.Key,
                DateTime.TryParse(kv.Value.Str("updatedAt") ?? kv.Value.Str("createdAt"), out var d) ? d.ToUniversalTime() : null,
                kv.Value.Arr("enabled").Count,
                Active.Str(gameId) == kv.Key))
            .OrderByDescending(p => p.UpdatedAt).ToList();

    public static void Save(string gameId, string rawName, ModRegistry registry)
    {
        var name = Clean(rawName);
        if (name == "") throw new InvalidOperationException(I18n.T("err.PROFILE_NAME"));
        var game = Game(gameId);
        if (!game.ContainsKey(name) && game.Count >= Max) throw new InvalidOperationException(I18n.T("err.PROFILE_LIMIT"));
        registry.Reconcile();
        var at = DateTime.UtcNow.ToString("o");
        var enabled = registry.List().Where(r => r.Bool("enabled", true) && !r.Bool("missing")).Select(r => (JsonNode)r.Str("id")!).ToArray();
        game[name] = new JsonObject { ["createdAt"] = game[name].Str("createdAt") ?? at, ["updatedAt"] = at, ["enabled"] = new JsonArray(enabled) };
        Active[gameId] = name;
        File.Save();
    }

    public static (int On, int Off, int Missing) Apply(string gameId, string name, ModRegistry registry)
    {
        if (Game(gameId)[name] is not JsonObject profile) throw new InvalidOperationException(I18n.T("err.PROFILE_MISSING"));
        registry.Reconcile();
        var wanted = profile.Arr("enabled").Select(x => x?.ToString()).OfType<string>().ToHashSet();
        int on = 0, off = 0;
        foreach (var mod in registry.List())
        {
            var id = mod.Str("id")!;
            var want = wanted.Contains(id);
            if (mod.Bool("enabled", true) == want) continue;
            registry.SetEnabled(id, want);
            if (want) on++; else off++;
        }
        Active[gameId] = name;
        File.Save();
        return (on, off, wanted.Count(id => !registry.Has(id)));
    }

    public static void Rename(string gameId, string from, string rawTo)
    {
        var to = Clean(rawTo);
        var game = Game(gameId);
        if (to == "" || to == from || game[from] is not JsonNode node) return;
        game.Remove(from);
        game[to] = node;
        if (Active.Str(gameId) == from) Active[gameId] = to;
        File.Save();
    }

    public static void Remove(string gameId, string name)
    {
        Game(gameId).Remove(name);
        if (Active.Str(gameId) == name) Active.Remove(gameId);
        File.Save();
    }
}
