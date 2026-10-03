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

    /// <summary>Растёт при каждом изменении списка — чтобы понять, что посчитанное раньше устарело.</summary>
    public int Version { get; private set; }

    void Save()
    {
        Version++;
        _file.Save();
    }

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
        // Переустановка и обновление не снимают «не обновлять», метки и своё название.
        if (Get(id) is { } old)
            foreach (var key in Personal)
                if (!record.ContainsKey(key) && old[key] is { } value) record[key] = value.DeepClone();
        Mods[id] = record;
        Save();
        return record;
    }

    /// <summary>
    /// Сменить номер записи (мод из файла узнали — теперь он с Nexus или Thunderstore).
    /// Если мод с таким номером уже есть, ничего не меняем.
    /// </summary>
    public JsonObject? Rekey(string oldId, string newId, Action<JsonObject> change)
    {
        if (Get(oldId) is not { } mod || (oldId != newId && Has(newId))) return null;
        Mods.Remove(oldId);
        mod["id"] = newId;
        change(mod);
        Mods[newId] = mod;
        // Кто ссылался на старый номер как на «поставлен ради», теперь ссылается на новый.
        foreach (var other in List().Where(m => m.Str("requestedBy") == oldId)) other["requestedBy"] = newId;
        Save();
        return mod;
    }

    /// <summary>То, что человек задал сам, — переживает переустановку.</summary>
    static readonly string[] Personal = ["hold", "tags", "displayName"];

    /// <summary>Поменять запись (метки, своё название) и сохранить.</summary>
    public void Edit(string id, Action<JsonObject> change)
    {
        if (Get(id) is not { } mod) return;
        change(mod);
        Save();
    }

    /// <summary>«Не обновлять этот мод» (чтобы версия совпадала с друзьями по сети).</summary>
    public void SetHold(string id, bool hold)
    {
        if (Get(id) is not { } mod) return;
        if (hold) mod["hold"] = true;
        else mod.Remove("hold");
        Save();
    }

    /// <summary>Папка мода, если она у записи своя (без имени папки путь указал бы на всю папку модов).</summary>
    public string? OwnFolder(JsonObject mod) => mod.Str("folder") is { Length: > 0 } ? FolderFor(mod) : null;

    string BaseFor(JsonObject mod, bool enabled) =>
        // Патчеры BepInEx живут не в plugins, а в BepInEx/patchers (или monomod).
        mod.Str("target") is { } target && BepInExLayout.IsRouted(target)
            ? enabled ? Path.Combine(GamePath, "BepInEx", target) : Path.Combine(StorageDir, "disabled-" + target)
        : mod.Str("kind") == "preset"
        ? enabled ? PresetDir : Path.Combine(StorageDir, "disabled-presets")
        : enabled ? ModsDir : Path.Combine(StorageDir, "disabled");

    public string FolderFor(JsonObject mod) => Path.Combine(BaseFor(mod, mod.Bool("enabled", true)), mod.Str("folder") ?? "");

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

    /// <summary>Части того же архива, которые ставятся отдельно (патчеры): выключаются и удаляются вместе с модом.</summary>
    List<string> Parts(string id) =>
        Mods.Where(kv => kv.Key.StartsWith(id + "#", StringComparison.Ordinal) && kv.Value is JsonObject m && m.Str("target") is not null).Select(kv => kv.Key).ToList();

    public void SetEnabled(string id, bool enabled)
    {
        foreach (var part in Parts(id)) SetEnabled(part, enabled);
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
        mod["enabled"] = enabled;
        Save();
    }

    public void Remove(string id)
    {
        foreach (var part in Parts(id)) Remove(part);
        if (Get(id) is not { } mod) return;
        var folder = FolderFor(mod);
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        else if (mod.Str("kind") == "preset" && File.Exists(folder)) File.Delete(folder);
        Mods.Remove(id);
        Save();
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
        if (changed) Save();
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
