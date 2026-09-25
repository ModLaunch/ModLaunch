using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Games;

namespace ModLaunch.Mods;

/// <summary>
/// Список установленных модов игры: games/&lt;id&gt;.json в папке данных —
/// тот же файл и тот же формат, что у версии 3.x. Выключенный мод лежит
/// в &lt;игра&gt;/ModHub/disabled и возвращается на место при включении.
/// </summary>
public sealed class ModRegistry
{
    readonly JsonFile _file;
    public GameDef Game { get; }
    public string GamePath { get; }
    public string ModsDir { get; }
    public string StorageDir { get; }

    public ModRegistry(GameDef game, string gamePath)
    {
        Game = game;
        GamePath = gamePath;
        ModsDir = game.ModsDir(gamePath);
        StorageDir = Path.Combine(gamePath, "ModHub");
        _file = new JsonFile(Path.Combine(Paths.GamesDir, $"{game.Id}.json"), () => new JsonObject { ["mods"] = new JsonObject() });
    }

    JsonObject Mods => _file.Data.Obj("mods");

    public IReadOnlyList<JsonObject> List() =>
        Mods.Select(kv => kv.Value).OfType<JsonObject>()
            .OrderBy(m => m.Str("name") ?? "", StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public JsonObject? Get(string id) => Mods[id] as JsonObject;
    public bool Has(string id) => Mods.ContainsKey(id);

    public JsonObject Add(JsonObject record)
    {
        var id = record.Str("id")!;
        record["enabled"] ??= true;
        record["installedAt"] ??= DateTime.UtcNow.ToString("o");
        Mods[id] = record;
        _file.Save();
        return record;
    }

    string BaseFor(JsonObject mod, bool enabled) =>
        enabled ? ModsDir : Path.Combine(StorageDir, "disabled");

    public string FolderFor(JsonObject mod) => Path.Combine(BaseFor(mod, mod.Bool("enabled", true)), mod.Str("folder") ?? "");

    public void SetEnabled(string id, bool enabled)
    {
        var mod = Get(id) ?? throw new InvalidOperationException(I18n.T("err.modNotFound", ("id", id)));
        if (mod.Bool("enabled", true) == enabled) return;
        var from = FolderFor(mod);
        var to = Path.Combine(BaseFor(mod, enabled), mod.Str("folder") ?? "");
        if (Directory.Exists(from))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            if (Directory.Exists(to)) Directory.Delete(to, true);
            Directory.Move(from, to);
            mod["missing"] = false;
        }
        else mod["missing"] = true;
        mod["enabled"] = enabled;
        _file.Save();
    }

    public void Remove(string id)
    {
        if (Get(id) is not { } mod) return;
        var folder = FolderFor(mod);
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        Mods.Remove(id);
        _file.Save();
    }

    /// <summary>Отметить моды, чьи папки пропали (их удалили руками).</summary>
    public void Reconcile()
    {
        var changed = false;
        foreach (var mod in List())
        {
            if (mod.Str("kind") == "preset") continue;
            var missing = !Directory.Exists(FolderFor(mod));
            if (mod.Bool("missing") != missing) { mod["missing"] = missing; changed = true; }
        }
        if (changed) _file.Save();
    }

    /// <summary>Папки в папке модов, которых нет в списке, — поставлены вручную.</summary>
    public List<string> Unmanaged()
    {
        if (!Directory.Exists(ModsDir)) return [];
        var known = List().Where(m => m.Bool("enabled", true) && m.Str("kind") != "preset")
            .Select(m => m.Str("folder") ?? "").ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Directory.EnumerateDirectories(ModsDir).Select(Path.GetFileName).OfType<string>()
            .Where(n => !n.StartsWith('.') && !known.Contains(n)).ToList();
    }
}
