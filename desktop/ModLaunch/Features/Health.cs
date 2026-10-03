using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Mods;

namespace ModLaunch.Features;

public enum HealthLevel { Critical, Warning, Info }

/// <summary>
/// Одна найденная проблема. Kind — что это (по нему экран выбирает кнопку «Исправить»),
/// Mods — какие моды затронуты (id в списке установленных).
/// </summary>
public sealed record HealthIssue(string Kind, HealthLevel Level, string Title, string Text, List<string> Mods);

/// <summary>
/// «Проверка игры», как Health Check в Vortex: загрузчик, зависимости, выключенные
/// зависимости, пропавшие файлы, конфликты, ошибки в логе, права на папку и место на диске.
/// Всё без сети и быстро — поэтому то же самое проверяется и перед каждым запуском.
/// </summary>
public static class Health
{
    /// <summary>Проверять ли игру перед запуском (Настройки → Запуск).</summary>
    public static bool BeforePlay => Settings.Data.Bool("checkBeforePlay", true);

    public static List<HealthIssue> Check(GameState g, bool quick = false)
    {
        var list = new List<HealthIssue>();
        if (g.Path is null || g.Status != Detect.Found) return list;
        var def = g.Def;
        // У Minecraft проверка своя — на его странице (версии, загрузчики, обновления по сборкам).
        if (def.IsMinecraft) return list;

        // Очищенную игру (без модов) проверять не нужно: об этом и так говорит полоса над вкладками.
        if (!Vanilla.IsPurged(def.Id) && !g.LoaderInstalled)
            list.Add(new("loader", HealthLevel.Critical, I18n.T("health.loader", ("loader", def.LoaderName)), I18n.T("health.loader.text", ("loader", def.LoaderName)), []));

        if (!CanWrite(g.Path))
            list.Add(new("write", HealthLevel.Critical, I18n.T("health.write"), I18n.T("health.write.text"), []));

        if (g.Registry is { } registry && !Vanilla.IsPurged(def.Id))
        {
            var missing = Deps.Find(registry);
            if (missing.Count > 0)
                list.Add(new("deps", HealthLevel.Critical,
                    I18n.T("health.deps", ("n", missing.Select(m => m.Id).Distinct().Count())),
                    string.Join("\n", missing.GroupBy(m => m.Mod).Take(6).Select(x => I18n.T("health.deps.line", ("mod", x.Key), ("list", string.Join(", ", x.Select(m => m.Name).Distinct()))))),
                    missing.Select(m => m.ModId).Distinct().ToList()));

            var off = DisabledDependencies(registry);
            if (off.Count > 0)
                list.Add(new("depsOff", HealthLevel.Critical,
                    I18n.T("health.depsOff", ("n", off.Count)),
                    string.Join("\n", off.Take(6).Select(o => I18n.T("health.depsOff.line", ("mod", o.Needer), ("dep", o.Name)))),
                    off.Select(o => o.Id).Distinct().ToList()));

            var gone = registry.List().Where(m => m.Bool("missing")).ToList();
            if (gone.Count > 0)
                list.Add(new("files", HealthLevel.Warning, I18n.T("health.files", ("n", gone.Count)),
                    I18n.T("health.files.text", ("list", string.Join(", ", gone.Take(6).Select(m => m.Str("name") ?? m.Str("id"))))),
                    gone.Select(m => m.Str("id")!).ToList()));

            var conflicts = Conflicts.Find(registry);
            if (conflicts.Count > 0)
                list.Add(new("conflicts", HealthLevel.Warning, I18n.T("health.conflicts", ("n", conflicts.Count)),
                    string.Join("\n", conflicts.Take(5).Select(c => I18n.T("v4.conflict.pair", ("a", c.A), ("b", c.B), ("n", c.Files)))),
                    OlderDuplicates(registry, conflicts)));

            if (!quick)
            {
                var culprits = Views.MainWindow.Culprits(g, Logs.Read(def, g.Path).Issues);
                if (culprits.Count > 0)
                    list.Add(new("log", HealthLevel.Warning, I18n.T("health.log", ("n", culprits.Count)),
                        string.Join("\n", culprits.Take(4).Select(c => $"{c.Name}: {c.Message}")),
                        culprits.Select(c => c.ModId).OfType<string>().ToList()));
            }

            if (Launcher.LastExit(def.Id) is { ByUser: false } exit && exit.Code is int code && code != 0 && exit.Ms < 90_000)
                list.Add(new("crash", HealthLevel.Warning, I18n.T("health.crash"), I18n.T("health.crash.text", ("code", code)), []));

            if (ModUpdates.Found.TryGetValue(def.Id, out var updates) && updates.Count > 0)
                list.Add(new("updates", HealthLevel.Info, I18n.T("health.updates", ("n", updates.Count)),
                    string.Join(", ", updates.Take(6).Select(u => $"{u.Name} {u.Current} → {u.Latest}")), updates.Select(u => u.RecordId).ToList()));

            var unused = DepGraph.Build(registry).Unused();
            if (unused.Count > 0)
                list.Add(new("unused", HealthLevel.Info, I18n.T("health.unused", ("n", unused.Count)),
                    I18n.T("health.unused.text", ("list", string.Join(", ", unused.Take(6).Select(m => m.Str("name") ?? m.Str("id"))))),
                    unused.Select(m => m.Str("id")!).ToList()));

            var unmanaged = registry.Unmanaged();
            if (unmanaged.Count > 0)
                list.Add(new("unmanaged", HealthLevel.Info, I18n.T("health.unmanaged", ("n", unmanaged.Count)),
                    I18n.T("health.unmanaged.text", ("list", string.Join(", ", unmanaged.Take(6)))), []));
        }

        if (!quick && FreeSpace(g.Path) is long free && free < 1024L * 1024 * 1024)
            list.Add(new("disk", HealthLevel.Warning, I18n.T("health.disk"), I18n.T("health.disk.text", ("size", Views.GamePage.Size(free))), []));

        return list.OrderBy(i => i.Level).ToList();
    }

    /// <summary>
    /// Что мешает запуску прямо сейчас (для окна «Нашли проблемы» перед игрой): только нужные моды,
    /// которых нет или они выключены. Папка «только для чтения» играть не мешает — иначе окно
    /// выскакивало бы перед каждым запуском игры из Program Files.
    /// </summary>
    public static List<HealthIssue> Blockers(GameState g) =>
        Check(g, quick: true).Where(i => i.Level == HealthLevel.Critical && i.Kind is "deps" or "depsOff").ToList();

    static readonly Dictionary<string, (DateTime At, string Stamp, int Count)> Counted = [];

    /// <summary>
    /// Сколько проблем показать на вкладке (важные и предупреждения). Страница перерисовывается
    /// часто (загрузки, поиск игр), поэтому ответ помним несколько секунд — пока не поменялся список модов.
    /// </summary>
    public static int Count(GameState g)
    {
        var stamp = $"{g.Registry?.Version}|{Vanilla.Mode(g.Def.Id)}|{g.LoaderInstalled}";
        if (Counted.TryGetValue(g.Def.Id, out var c) && c.Stamp == stamp && DateTime.UtcNow - c.At < TimeSpan.FromSeconds(5)) return c.Count;
        var n = Check(g).Count(i => i.Level != HealthLevel.Info);
        Counted[g.Def.Id] = (DateTime.UtcNow, stamp, n);
        return n;
    }

    static readonly Dictionary<string, (DateTime At, bool Ok)> Writable = [];

    public sealed record OffDependency(string Id, string Name, string Needer);

    /// <summary>Зависимости, которые стоят, но выключены: включённый мод без них не заработает.</summary>
    public static List<OffDependency> DisabledDependencies(ModRegistry registry)
    {
        var all = registry.List();
        var disabled = all.Where(m => !m.Bool("enabled", true) && !m.Bool("missing")).ToList();
        var result = new List<OffDependency>();
        if (disabled.Count == 0) return result;
        var domain = registry.Game.NexusDomain;
        var enabled = all.Where(m => m.Bool("enabled", true) && !m.Bool("missing")).ToList();
        foreach (var mod in enabled)
        {
            var needer = mod.Str("name") ?? mod.Str("id") ?? "";
            var wants = mod.Arr("dependencies").Select(d => d?.ToString()?.Trim()).OfType<string>().Where(d => d != "")
                .Select(d => (Id: d, Name: d))
                .Concat(mod.Arr("requires").Select(r => (Id: $"nexus:{domain}:{r.Str("id")}", Name: r.Str("name") ?? "")));
            foreach (var (id, name) in wants)
            {
                // Включённая копия (например, новая версия) уже закрывает зависимость — выключенная старая не мешает.
                if (enabled.Any(e => Matches(e, id, name))) continue;
                var hit = disabled.FirstOrDefault(d => Matches(d, id, name));
                if (hit is null || result.Any(r => r.Id == hit.Str("id"))) continue;
                result.Add(new OffDependency(hit.Str("id")!, hit.Str("name") ?? hit.Str("id")!, needer));
            }
        }
        return result;
    }

    static bool Matches(JsonObject record, string id, string name)
    {
        bool Same(string? a, string? b) => a is { Length: > 0 } && b is { Length: > 0 } && a.Equals(b, StringComparison.OrdinalIgnoreCase);
        if (Same(record.Str("id"), id) || Same(record.Str("uniqueId"), id)) return true;
        var n = Deps.Norm(name);
        return n.Length >= 3 && (Deps.Norm(record.Str("name")) == n || Deps.Norm(record.Str("folder")) == n);
    }

    /// <summary>Из пары одинаковых модов — тот, что поставлен раньше (его и выключаем).</summary>
    static List<string> OlderDuplicates(ModRegistry registry, List<Conflict> conflicts)
    {
        var byName = registry.List().Where(m => m.Bool("enabled", true)).GroupBy(m => m.Str("name") ?? m.Str("id") ?? "").ToDictionary(x => x.Key, x => x.First());
        var result = new List<string>();
        foreach (var c in conflicts)
        {
            if (!byName.TryGetValue(c.A, out var a) || !byName.TryGetValue(c.B, out var b)) continue;
            var older = string.CompareOrdinal(a.Str("installedAt") ?? "", b.Str("installedAt") ?? "") <= 0 ? a : b;
            if (older.Str("id") is string id && !result.Contains(id)) result.Add(id);
        }
        return result;
    }

    /// <summary>Можно ли писать в папку игры (пробный файл, ответ помним минуту).</summary>
    static bool CanWrite(string dir)
    {
        if (Writable.TryGetValue(dir, out var w) && DateTime.UtcNow - w.At < TimeSpan.FromMinutes(1)) return w.Ok;
        bool ok;
        try
        {
            var probe = Path.Combine(dir, ".modlaunch-write-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            ok = true;
        }
        catch { ok = false; }
        Writable[dir] = (DateTime.UtcNow, ok);
        return ok;
    }

    static long? FreeSpace(string path)
    {
        try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!).AvailableFreeSpace; }
        catch { return null; }
    }

    [SelfTest]
    static string FindsDisabledDependency()
    {
        var dir = Path.Combine(Paths.DataDir, "health-test", "Lethal Company");
        Directory.CreateDirectory(Path.Combine(dir, "BepInEx", "plugins"));
        var registry = new ModRegistry(GameCatalog.ById("lethal-company")!, dir);
        registry.Add(new JsonObject { ["id"] = "Evaisa-LethalLib", ["name"] = "LethalLib", ["folder"] = "LethalLib", ["enabled"] = false });
        registry.Add(new JsonObject { ["id"] = "Owner-BigMod", ["name"] = "BigMod", ["folder"] = "BigMod", ["dependencies"] = new JsonArray("Evaisa-LethalLib") });
        var off = DisabledDependencies(registry);
        if (off.Count != 1 || off[0].Id != "Evaisa-LethalLib" || off[0].Needer != "BigMod") throw new Exception("disabled dependency not found");
        return "BigMod needs LethalLib, which is off";
    }

    /// <summary>Выключена старая копия, а сам мод включён — жаловаться (и мешать запуску) не на что.</summary>
    [SelfTest]
    static string EnabledCopyIsEnough()
    {
        var game = GameCatalog.ById("subnautica-below-zero")!;
        var dir = Path.Combine(Paths.DataDir, "health-test", "Below Zero");
        Directory.CreateDirectory(dir);
        var registry = new ModRegistry(game, dir);
        registry.Add(new JsonObject { ["id"] = $"nexus:{game.NexusDomain}:1262", ["name"] = "Nautilus", ["folder"] = "Nautilus" });
        registry.Add(new JsonObject { ["id"] = "local:Nautilus-old", ["name"] = "Nautilus", ["folder"] = "Nautilus-old", ["enabled"] = false });
        registry.Add(new JsonObject
        {
            ["id"] = $"nexus:{game.NexusDomain}:50", ["name"] = "Map", ["folder"] = "Map",
            ["requires"] = new JsonArray(new JsonObject { ["id"] = "1262", ["name"] = "Nautilus" }),
        });
        var off = DisabledDependencies(registry);
        if (off.Count != 0) throw new Exception("Nautilus is on, but the old copy was reported: " + off[0].Id);
        return "Map needs Nautilus: it is on, the old copy being off is fine";
    }
}
