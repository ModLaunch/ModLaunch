using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Mods;

namespace ModLaunch.Features;

/// <summary>
/// Кто кому нужен среди установленных модов: «требует» и «нужен для». По нему выключение
/// библиотеки предупреждает, какие моды без неё сломаются, включение мода включает и его
/// зависимости, а после удаления видно вспомогательные моды, которые больше никому не нужны.
/// Связи — из manifest (UniqueID, Owner-Name), требований Nexus и отметки «поставлен как зависимость».
/// </summary>
public sealed class DepGraph
{
    readonly Dictionary<string, JsonObject> _mods = new(StringComparer.Ordinal);
    readonly Dictionary<string, HashSet<string>> _needs = [];
    readonly Dictionary<string, HashSet<string>> _neededBy = [];

    /// <summary>
    /// Моды, про которые неизвестно, что им нужно: у BepInEx и HK старые записи и моды из файла
    /// без списка зависимостей. Пока такие есть, «никому не нужен» про помощника сказать нельзя.
    /// </summary>
    readonly HashSet<string> _unknown = [];

    public static DepGraph Build(ModRegistry registry) => new(registry.List(), registry.Game.ModMarker != "manifest");

    DepGraph(IEnumerable<JsonObject> records, bool depsMayBeUnknown = false)
    {
        // Патчеры (target) — часть своего мода, отдельно в связях не участвуют.
        var list = records.Where(r => r.Str("id") is not null && r.Str("target") is null && !r.Bool("missing")).ToList();
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var names = new Dictionary<string, string>();
        foreach (var r in list)
        {
            var id = r.Str("id")!;
            _mods[id] = r;
            ids.TryAdd(id, id);
            if (r.Str("uniqueId") is { Length: > 0 } u) ids.TryAdd(u, id);
            if (NexusId(id) is { } n) ids.TryAdd("nexus#" + n, id);
            // Пустой список у BepInEx/HK не значит «ничего не нужно»: раньше он там не записывался.
            if (depsMayBeUnknown && r.Str("kind") != "preset" && r.Str("source") != "nexus" && NexusId(id) is null
                && r.Arr("dependencies").Count == 0 && r.Arr("requires").Count == 0) _unknown.Add(id);
            foreach (var name in new[] { r.Str("name"), r.Str("folder") })
                if (Deps.Norm(name) is { Length: >= 3 } key) names.TryAdd(key, id);
        }

        string? Resolve(string key, string? name)
        {
            if (ids.TryGetValue(key, out var hit)) return hit;
            // Owner-Name из Thunderstore: мод мог встать из файла под именем без автора.
            foreach (var guess in new[] { name, key, key.Contains('-') ? key[(key.LastIndexOf('-') + 1)..] : null })
                if (Deps.Norm(guess) is { Length: >= 3 } k && names.TryGetValue(k, out hit)) return hit;
            return null;
        }

        foreach (var r in list)
        {
            var id = r.Str("id")!;
            var wants = r.Arr("dependencies").Select(d => d?.ToString()?.Trim()).OfType<string>().Where(d => d != "").Select(d => Resolve(d, null))
                .Concat(r.Arr("requires").Select(q => q.Str("id") is { Length: > 0 } rid ? Resolve("nexus#" + rid, q.Str("name")) : null));
            foreach (var dep in wants.OfType<string>()) Link(id, dep);
            // «Поставлен как зависимость X» — тоже связь, даже если в manifest её нет.
            if (r.Str("requestedBy") is { } parent && _mods.ContainsKey(parent)) Link(parent, id);
        }
    }

    void Link(string needer, string dep)
    {
        if (needer == dep) return;
        (_needs.TryGetValue(needer, out var a) ? a : _needs[needer] = []).Add(dep);
        (_neededBy.TryGetValue(dep, out var b) ? b : _neededBy[dep] = []).Add(needer);
    }

    static string? NexusId(string id) => id.StartsWith("nexus:", StringComparison.Ordinal) ? id[(id.LastIndexOf(':') + 1)..] : null;

    IEnumerable<JsonObject> Records(IEnumerable<string>? ids) => (ids ?? []).Select(i => _mods.GetValueOrDefault(i)).OfType<JsonObject>();

    public List<JsonObject> NeedsOf(string id) => Records(_needs.GetValueOrDefault(id)).ToList();

    public List<JsonObject> NeededByOf(string id) => Records(_neededBy.GetValueOrDefault(id)).ToList();

    /// <summary>Включённые моды, которые без этих сломаются (и те, что сломаются уже без них).</summary>
    public List<JsonObject> Dependents(IEnumerable<string> ids)
    {
        var start = ids.ToHashSet();
        var seen = new HashSet<string>(start);
        var queue = new Queue<string>(start);
        var result = new List<JsonObject>();
        while (queue.Count > 0)
            foreach (var by in _neededBy.GetValueOrDefault(queue.Dequeue()) ?? [])
            {
                if (!seen.Add(by) || _mods[by] is not { } mod || !mod.Bool("enabled", true)) continue;
                result.Add(mod);
                queue.Enqueue(by);
            }
        return result;
    }

    /// <summary>Выключенные моды, без которых этот не заработает (со всей цепочкой).</summary>
    public List<JsonObject> DisabledNeeds(string id)
    {
        var seen = new HashSet<string> { id };
        var queue = new Queue<string>([id]);
        var result = new List<JsonObject>();
        while (queue.Count > 0)
            foreach (var dep in _needs.GetValueOrDefault(queue.Dequeue()) ?? [])
            {
                if (!seen.Add(dep)) continue;
                if (!_mods[dep].Bool("enabled", true)) result.Add(_mods[dep]);
                queue.Enqueue(dep);
            }
        return result;
    }

    /// <summary>
    /// Вспомогательные моды (поставленные как зависимость), которые после удаления removed
    /// больше никому не нужны — со всей цепочкой: библиотека библиотеки тоже.
    /// </summary>
    public List<JsonObject> Orphans(IEnumerable<string> removed)
    {
        var gone = removed.ToHashSet();
        var result = new List<JsonObject>();
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var (id, mod) in _mods)
            {
                if (gone.Contains(id) || mod.Str("requestedBy") is null) continue;
                var by = _neededBy.GetValueOrDefault(id) ?? [];
                if (by.Count == 0 || !by.All(gone.Contains)) continue;
                gone.Add(id);
                result.Add(mod);
                changed = true;
            }
        }
        // Оставшемуся моду без списка зависимостей любой из них может быть нужен — не предлагаем.
        return _unknown.Any(id => !gone.Contains(id)) ? [] : result;
    }

    /// <summary>Вспомогательные моды, которые уже сейчас никому не нужны (их хозяина удалили раньше).</summary>
    public List<JsonObject> Unused()
    {
        var unused = _mods.Where(kv => kv.Value.Str("requestedBy") is not null && (_neededBy.GetValueOrDefault(kv.Key)?.Count ?? 0) == 0).Select(kv => kv.Key).ToHashSet();
        return _unknown.Any(id => !unused.Contains(id)) ? [] : unused.Select(id => _mods[id]).ToList();
    }

    /// <summary>«Что нужно этому моду» деревом — отвечает на вопрос «зачем это установлено».</summary>
    public List<(int Depth, JsonObject Mod)> Tree(string id, int maxDepth = 4)
    {
        var result = new List<(int, JsonObject)>();
        void Walk(string at, int depth, HashSet<string> path)
        {
            if (depth > maxDepth) return;
            foreach (var dep in (_needs.GetValueOrDefault(at) ?? []).OrderBy(d => _mods[d].Str("name")))
            {
                if (!path.Add(dep)) continue;
                result.Add((depth, _mods[dep]));
                Walk(dep, depth + 1, path);
                path.Remove(dep);
            }
        }
        Walk(id, 1, [id]);
        return result;
    }

    [SelfTest]
    static string FindsDependentsAndOrphans()
    {
        JsonObject M(string id, string name, bool on = true, string? by = null, string[]? deps = null, (string Id, string Name)[]? requires = null) => new()
        {
            ["id"] = id, ["name"] = name, ["folder"] = name, ["enabled"] = on, ["requestedBy"] = by,
            ["dependencies"] = new JsonArray((deps ?? []).Select(d => (JsonNode)d).ToArray()),
            ["requires"] = new JsonArray((requires ?? []).Select(r => (JsonNode)new JsonObject { ["id"] = r.Id, ["name"] = r.Name }).ToArray()),
        };
        var graph = new DepGraph(
        [
            M("Owner-BigMod", "BigMod", deps: ["Evaisa-LethalLib"]),
            M("Evaisa-LethalLib", "LethalLib", by: "Owner-BigMod", deps: ["Owner-Core"]),
            M("Owner-Core", "Core", on: false, by: "Evaisa-LethalLib"),
            M("nexus:subnautica:12", "Map", requires: [("1262", "Nautilus")]),
            M("nexus:subnautica:1262", "Nautilus"),
            M("Someone-Leftover", "Leftover", by: "Owner-Removed"),
        ]);
        if (string.Join(",", graph.Dependents(["Owner-Core"]).Select(m => m.Str("name"))) != "LethalLib,BigMod") throw new Exception("dependents chain is wrong");
        if (graph.NeededByOf("nexus:subnautica:1262").SingleOrDefault()?.Str("name") != "Map") throw new Exception("nexus requirement not linked");
        if (string.Join(",", graph.DisabledNeeds("Owner-BigMod").Select(m => m.Str("name"))) != "Core") throw new Exception("disabled needs are wrong");
        if (string.Join(",", graph.Orphans(["Owner-BigMod"]).Select(m => m.Str("name")).Order()) != "Core,LethalLib") throw new Exception("orphans are wrong");
        if (graph.Unused().SingleOrDefault()?.Str("name") != "Leftover") throw new Exception("unused helper not found");
        if (graph.Tree("Owner-BigMod").Count != 2) throw new Exception("tree is wrong");
        return "Core → LethalLib → BigMod; removing BigMod frees LethalLib and Core; Map needs Nautilus";
    }

    /// <summary>
    /// BepInEx: библиотеку поставили ради BigMod, а OtherMod встал позже и она ему тоже нужна —
    /// после удаления BigMod её нельзя предлагать убрать. И если про какой-то мод неизвестно,
    /// что ему нужно (старая запись без списка), — тоже не предлагаем.
    /// </summary>
    [SelfTest]
    static string SharedHelperIsNotOffered()
    {
        var root = Path.Combine(Paths.DataDir, "orphans-test");
        Directory.CreateDirectory(root);
        var registry = new ModRegistry(Games.GameCatalog.ById("valheim")!, Path.Combine(root, "Valheim"));
        void Install(string name, string? by, params string[] deps)
        {
            var zip = Path.Combine(root, name + ".zip");
            using (var archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
            {
                void Add(string entry, string text) { using var w = new StreamWriter(archive.CreateEntry(entry).Open()); w.Write(text); }
                Add("manifest.json", $"{{\"name\":\"{name}\",\"version_number\":\"1.0.0\",\"dependencies\":[{string.Join(",", deps.Select(d => $"\"{d}-1.0.0\""))}]}}");
                Add($"plugins/{name}.dll", "dll");
            }
            Installer.InstallArchive(registry, zip, new JsonObject { ["id"] = "Owner-" + name, ["name"] = name, ["source"] = "thunderstore", ["requestedBy"] = by });
        }
        Install("BigMod", null, "denikson-BepInExPack_Valheim", "Owner-SharedLib");
        Install("SharedLib", "Owner-BigMod", "denikson-BepInExPack_Valheim");
        Install("OtherMod", null, "denikson-BepInExPack_Valheim", "Owner-SharedLib");
        if (Build(registry).Orphans(["Owner-BigMod"]).Count != 0) throw new Exception("SharedLib offered for removal though OtherMod needs it");
        if (Build(registry).NeededByOf("Owner-SharedLib").Count != 2) throw new Exception("dependencies from manifest.json not recorded");
        registry.Edit("Owner-OtherMod", m => m["dependencies"] = new JsonArray());
        if (Build(registry).Orphans(["Owner-BigMod"]).Count != 0 || Build(registry).Unused().Count != 0) throw new Exception("helper offered though OtherMod's needs are unknown");
        registry.Remove("Owner-OtherMod");
        var orphans = Build(registry).Orphans(["Owner-BigMod"]);
        if (orphans.SingleOrDefault()?.Str("id") != "Owner-SharedLib")
        {
            // Подробности для журнала самопроверки: какие записи и связи увидел граф.
            var graph = Build(registry);
            var state = string.Join("; ", registry.List().Select(m =>
                $"{m.Str("id")}[deps={string.Join(",", m.Arr("dependencies").Select(d => d?.ToString()))} by={m.Str("requestedBy")} kind={m.Str("kind")} missing={m.Bool("missing")} target={m.Str("target")} unknown={graph._unknown.Contains(m.Str("id") ?? "")} neededBy={string.Join(",", graph._neededBy.GetValueOrDefault(m.Str("id") ?? "") ?? [])}]"));
            throw new Exception($"real orphan not found; orphans=[{string.Join(",", orphans.Select(o => o.Str("id")))}]; {state}");
        }
        foreach (var id in new[] { "Owner-BigMod", "Owner-SharedLib" }) registry.Remove(id);
        return "SharedLib kept while OtherMod needs it (or might); offered once it is really unused";
    }
}
