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
    /// <summary>Куда кладутся пресеты ReShade — рядом с exe игры.</summary>
    public string PresetDir { get; }

    public ModRegistry(GameDef game, string gamePath)
    {
        Game = game;
        GamePath = gamePath;
        ModsDir = game.ModsDir(gamePath);
        StorageDir = Path.Combine(gamePath, "ModHub");
        PresetDir = Features.ReShade.DirOf(game, gamePath);
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

    string BaseFor(JsonObject mod, bool enabled) => mod.Str("kind") == "preset"
        ? enabled ? PresetDir : Path.Combine(StorageDir, "disabled-presets")
        : enabled ? ModsDir : Path.Combine(StorageDir, "disabled");

    public string FolderFor(JsonObject mod) => Path.Combine(BaseFor(mod, mod.Bool("enabled", true)), mod.Str("folder") ?? "");

    /// <summary>
    /// Части мода вне его папки (BepInEx/patchers/…, BepInEx/monomod/…): пути от папки игры.
    /// Выключенные лежат в &lt;игра&gt;/ModHub/disabled-extra/&lt;тот же путь&gt;.
    /// </summary>
    public static IEnumerable<string> Extras(JsonObject mod) => mod.Arr("extra").Select(x => x?.ToString()).OfType<string>().Where(SafeRelative);

    static bool SafeRelative(string path) => path.Length > 0 && !Path.IsPathRooted(path) && !path.Split('/', '\\').Contains("..");

    string ExtraPath(string relative, bool enabled) => Path.Combine(enabled ? GamePath : Path.Combine(StorageDir, "disabled-extra"), relative);

    /// <summary>Все папки и файлы мода на диске сейчас: основная папка и части вне её.</summary>
    public List<string> PathsOf(JsonObject mod)
    {
        var enabled = mod.Bool("enabled", true);
        var list = new List<string>();
        if (mod.Str("folder") is { Length: > 0 }) list.Add(FolderFor(mod));
        list.AddRange(Extras(mod).Select(e => ExtraPath(e, enabled)));
        return list;
    }

    /// <summary>Мод и записи, которые поставились из того же архива («id#папка»).</summary>
    public List<JsonObject> Family(string id) =>
        List().Where(m => m.Str("id") is { } x && (x == id || x.StartsWith(id + "#", StringComparison.Ordinal))).ToList();

    /// <summary>Удалить с диска (папку или файл), не трогая саму папку модов.</summary>
    public void DeletePath(string path)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        var guard = new[] { GamePath, ModsDir, StorageDir, PresetDir, Path.Combine(GamePath, "BepInEx") }
            .Select(p => Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar));
        if (guard.Any(g => string.Equals(g, full, StringComparison.OrdinalIgnoreCase))) return;
        if (Directory.Exists(full)) Directory.Delete(full, true);
        else if (File.Exists(full)) File.Delete(full);
    }

    /// <summary>Убрать запись из списка, не удаляя файлов.</summary>
    public void Forget(string id)
    {
        if (Mods.Remove(id)) _file.Save();
    }

    static bool Exists(string path) => Directory.Exists(path) || File.Exists(path);

    static void Move(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        if (Directory.Exists(from))
        {
            if (Directory.Exists(to)) Directory.Delete(to, true);
            Directory.Move(from, to);
        }
        else File.Move(from, to, true);
    }

    public void SetEnabled(string id, bool enabled)
    {
        var mod = Get(id) ?? throw new InvalidOperationException(I18n.T("err.modNotFound", ("id", id)));
        if (mod.Bool("enabled", true) == enabled) return;
        var from = FolderFor(mod);
        var to = Path.Combine(BaseFor(mod, enabled), mod.Str("folder") ?? "");
        if (Exists(from))
        {
            Move(from, to);
            mod["missing"] = false;
        }
        else mod["missing"] = true;
        foreach (var extra in Extras(mod))
        {
            var source = ExtraPath(extra, !enabled);
            if (Exists(source)) Move(source, ExtraPath(extra, enabled));
        }
        mod["enabled"] = enabled;
        _file.Save();
    }

    public void Remove(string id)
    {
        if (Get(id) is not { } mod) return;
        var folder = FolderFor(mod);
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        else if (mod.Str("kind") == "preset" && File.Exists(folder)) File.Delete(folder);
        foreach (var extra in Extras(mod)) try { DeletePath(ExtraPath(extra, mod.Bool("enabled", true))); } catch { }
        Mods.Remove(id);
        _file.Save();
    }

    /// <summary>Отметить моды, чьи папки пропали (их удалили руками).</summary>
    public void Reconcile()
    {
        var changed = false;
        foreach (var mod in List())
        {
            var missing = !Exists(FolderFor(mod));
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
