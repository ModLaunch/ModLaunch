using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Mods;

namespace ModLaunch.Features;

public sealed record ProfileInfo(string Name, DateTime? UpdatedAt, int Count, bool Active);

/// <summary>
/// Профили: какие моды включены (profiles.json, как в 3.x), а с версии 9 — ещё версии модов
/// («mods») и настройки модов в zip рядом. Старые профили без этого тоже работают.
/// </summary>
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
        bool config;
        try { config = ModSetup.SaveConfig(registry, ConfigZip(gameId, name)); }
        catch { config = false; } // настройки не записались (файл занят) — сам профиль всё равно сохраняем
        game[name] = new JsonObject
        {
            ["createdAt"] = game[name].Str("createdAt") ?? at, ["updatedAt"] = at, ["enabled"] = new JsonArray(enabled),
            ["mods"] = ModSetup.ToJson(ModSetup.Capture(registry)), ["config"] = config,
        };
        Active[gameId] = name;
        File.Save();
    }

    /// <summary>Настройки модов профиля — zip в папке данных.</summary>
    static string ConfigZip(string gameId, string name)
    {
        var safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(Paths.DataDir, "profiles", gameId, safe + ".zip");
    }

    /// <summary>Состав профиля с версиями (у старых профилей — только включённые, без версий).</summary>
    public static List<SetupMod> Mods(string gameId, string name)
    {
        if (Game(gameId)[name] is not JsonObject profile) return [];
        if (profile["mods"] is JsonArray mods) return ModSetup.FromJson(mods);
        return profile.Arr("enabled").Select(x => x?.ToString()).OfType<string>().Select(id => new SetupMod(id, id, "", "", true)).ToList();
    }

    /// <summary>Каких модов профиля нет и какие стоят другой версии.</summary>
    public static (List<SetupMod> Missing, List<SetupMod> OtherVersion) Differences(string gameId, string name, ModRegistry registry) =>
        ModSetup.Differences(registry, Mods(gameId, name));

    public static (int On, int Off, int Missing) Apply(string gameId, string name, ModRegistry registry)
    {
        if (Game(gameId)[name] is not JsonObject profile) throw new InvalidOperationException(I18n.T("err.PROFILE_MISSING"));
        registry.Reconcile();
        // Настройки, поменянные в текущем профиле, — в его zip: вернёшься к нему — они будут как оставил.
        if (Active.Str(gameId) is { } current && current != name && Game(gameId)[current] is JsonObject was)
            try { if (ModSetup.SaveConfig(registry, ConfigZip(gameId, current))) was["config"] = true; } catch { }
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
        // Настройки модов этого профиля (старые — в ModHub/config-backup).
        if (profile.Bool("config")) ModSetup.RestoreConfig(registry, ConfigZip(gameId, name));
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
        try { if (System.IO.File.Exists(ConfigZip(gameId, from))) System.IO.File.Move(ConfigZip(gameId, from), ConfigZip(gameId, to), true); } catch { }
        File.Save();
    }

    /// <summary>
    /// Имя, которого ещё нет: «Default», а если занято — «Default 2», «Default 3»…
    /// Длинное имя сначала укорачиваем, чтобы номер влез (иначе Clean его отрежет и цикл не кончится).
    /// </summary>
    public static string FreeName(string gameId, string rawName)
    {
        var game = Game(gameId);
        var name = Clean(rawName);
        var stem = name.Length > 36 ? name[..36].TrimEnd() : name;
        for (var i = 2; game.ContainsKey(name); i++) name = $"{stem} {i}";
        return name;
    }

    /// <summary>Копия профиля (как «Duplicate» в Modrinth App).</summary>
    public static void Duplicate(string gameId, string from, string rawTo)
    {
        var game = Game(gameId);
        if (game[from] is not JsonNode node) return;
        var to = FreeName(gameId, rawTo);
        if (game.Count >= Max) throw new InvalidOperationException(I18n.T("err.PROFILE_LIMIT"));
        var copy = (JsonObject)node.DeepClone();
        // По отдельности: «a = b = строка» кладёт один и тот же узел JSON в два места — это исключение.
        var at = DateTime.UtcNow.ToString("o");
        copy["createdAt"] = at;
        copy["updatedAt"] = at;
        game[to] = copy;
        try { if (System.IO.File.Exists(ConfigZip(gameId, from))) System.IO.File.Copy(ConfigZip(gameId, from), ConfigZip(gameId, to), true); } catch { }
        File.Save();
    }

    public static void Remove(string gameId, string name)
    {
        Game(gameId).Remove(name);
        if (Active.Str(gameId) == name) Active.Remove(gameId);
        try { System.IO.File.Delete(ConfigZip(gameId, name)); } catch { }
        File.Save();
    }

    /// <summary>Настройки, поменянные в профиле, не теряются, когда переключаешься на другой и обратно.</summary>
    [SelfTest]
    static string SwitchKeepsSettings()
    {
        var game = Path.Combine(Paths.DataDir, "profiles-test", "Content Warning");
        var cfg = Path.Combine(Directory.CreateDirectory(Path.Combine(game, "BepInEx", "config")).FullName, "mod.cfg");
        var registry = new ModRegistry(Games.GameCatalog.ById("content-warning")!, game);
        System.IO.File.WriteAllText(cfg, "solo");
        Save("content-warning", "Solo", registry);
        System.IO.File.WriteAllText(cfg, "friends");
        Save("content-warning", "Friends", registry);
        Apply("content-warning", "Solo", registry);
        if (System.IO.File.ReadAllText(cfg) != "solo") throw new Exception("profile settings not applied");
        System.IO.File.WriteAllText(cfg, "solo, tuned");
        Apply("content-warning", "Friends", registry);
        if (System.IO.File.ReadAllText(cfg) != "friends") throw new Exception("other profile settings not applied");
        Apply("content-warning", "Solo", registry);
        if (System.IO.File.ReadAllText(cfg) != "solo, tuned") throw new Exception("settings changed in the profile were lost on switch");
        return "settings changed in a profile survive switching away and back";
    }

    /// <summary>Копия профиля с длинным именем (40 знаков) не зацикливается: номер влезает.</summary>
    [SelfTest]
    static string LongNamesGetNumbers()
    {
        var name = new string('Д', 40);
        var registry = new ModRegistry(Games.GameCatalog.ById("content-warning")!, Path.Combine(Paths.DataDir, "profiles-test", "Content Warning"));
        Save("content-warning", name, registry);
        Duplicate("content-warning", name, name + " (копия)");
        Duplicate("content-warning", name, name + " (копия)");
        var names = List("content-warning").Select(p => p.Name).Where(n => n.StartsWith("ДДДД")).ToList();
        if (names.Count != 3 || names.Any(n => n.Length > 40)) throw new Exception("copies: " + string.Join(" | ", names));
        if (FreeName("content-warning", name) == name) throw new Exception("taken name returned as free");
        return "long names get a number that fits";
    }
}
