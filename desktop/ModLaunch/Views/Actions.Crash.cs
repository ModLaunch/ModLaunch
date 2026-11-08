using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Games;
using ModLaunch.Loaders;
using ModLaunch.Mods;
using ModLaunch.Minecraft;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>24.0: кнопка «Исправить» в окне «Почему игра упала» — всё по порядку, потом «Играть».</summary>
public static partial class Actions
{
    static bool _fixingCrash;

    /// <summary>Запустить задачу и дождаться её конца: true — сделано.</summary>
    static Task<bool> RunAndWait(string title, string game, Func<Job, IProgress<InstallStep>, CancellationToken, Task> work)
    {
        var done = new TaskCompletionSource<bool>();
        Job? job = null;
        void OnFinished(Job j)
        {
            if (j != job) return;
            Jobs.Finished -= OnFinished;
            done.TrySetResult(j.Status == JobStatus.Done);
        }
        Jobs.Finished += OnFinished;
        job = Jobs.Run(title, game, work);
        return done.Task;
    }

    /// <summary>
    /// Исправить то, что нашёл разбор вылета: загрузчик → включить нужное → выключить сломанное и дубли →
    /// поставить недостающее → обновить устаревшее. В конце — что сделано и кнопка «Играть».
    /// </summary>
    public static async void FixCrash(GameState g, CrashReport report)
    {
        if (Launcher.IsRunning(g.Def.Id)) { W.Toast(I18n.T("vanilla.running"), bad: true); return; }
        var fixes = report.Causes.Select(c => c.Fix).OfType<CrashFix>().ToList();
        foreach (var url in fixes.Where(f => f.Kind == "url" && f.Url is not null).Select(f => f.Url!).Distinct()) Ui.OpenUrl(url);
        fixes = fixes.Where(f => f.Kind != "url").ToList();
        if (fixes.Count == 0) return;
        if (_fixingCrash) { W.Toast(I18n.T("v24.crash.busy")); return; }
        _fixingCrash = true;
        W.Toast(I18n.T("v24.crash.fixing"));
        var done = new List<string>();
        var failed = new List<string>();
        // Minecraft: чиним ту сборку, которая упала (её могли запустить, не выбирая).
        var mc = g.Def.IsMinecraft ? (report.McId is { } id ? Mc.Get(id) : null) ?? Mc.Active : null;
        try
        {
            if (mc is not null) await FixMinecraft(mc, fixes, done, failed);
            else if (!g.Def.IsMinecraft) await FixMods(g, fixes, done, failed);
        }
        catch (Exception e) { failed.Add(Jobs.Explain(e)); Guard.Log(e); }
        finally { _fixingCrash = false; }
        AppState.Notify();
        if (done.Count > 0) Premium.Count("crashFixed");
        ShowFixed(g, done, failed, mc);
    }

    static async Task FixMods(GameState g, List<CrashFix> fixes, List<string> done, List<string> failed)
    {
        if (g.Registry is not { } registry || g.Path is not { } path) return;
        // Всё можно вернуть: «Профили» → «История изменений».
        try { Snapshots.Take(g, "crash"); } catch { }

        if (fixes.Any(f => f.Kind == "loader"))
        {
            var ok = await RunAndWait(g.Def.LoaderName, g.Def.Name, async (_, progress, ct) => { await Loader.Install(g.Def, path, progress, ct); });
            g.Refresh();
            if (ok) done.Add(I18n.T("v24.fixed.loader", ("loader", g.Def.LoaderName)));
            else { failed.Add(g.Def.LoaderName); return; }
        }

        var enabled = 0;
        foreach (var id in fixes.Where(f => f.Kind == "enable").SelectMany(f => f.Ids).Distinct())
            try { registry.SetEnabled(id, true); enabled++; } catch { }
        if (enabled > 0) done.Add(I18n.T("v24.fixed.enabled", ("n", enabled)));

        var off = new List<string>();
        foreach (var id in fixes.Where(f => f.Kind == "disable").SelectMany(f => f.Ids).Distinct())
        {
            if (registry.Get(id) is not { } r || !r.Bool("enabled", true)) continue;
            try { registry.SetEnabled(id, false); off.Add(r.Str("name") ?? id); } catch { }
        }
        if (off.Count > 0) done.Add(I18n.T("v24.fixed.disabled", ("list", string.Join(", ", off.Take(5)))));
        AppState.Notify();

        if (fixes.Any(f => f.Kind == "deps"))
        {
            var names = fixes.Where(f => f.Kind == "deps").SelectMany(f => f.Names ?? []).ToList();
            var mods = await ResolveMissing(g, names, failed);
            if (mods.Count > 0)
            {
                await InstallQueue(g, I18n.T("deps.install"), mods.Select(m => (m, (Pin?)null)).ToList());
                var got = mods.Where(m => IsInstalled(g, m.Id)).Select(m => m.Name).ToList();
                if (got.Count > 0) done.Add(I18n.T("v24.fixed.installed", ("list", string.Join(", ", got.Take(5)))));
                failed.AddRange(mods.Where(m => !IsInstalled(g, m.Id)).Select(m => m.Name));
            }
        }

        var ids = fixes.Where(f => f.Kind == "update").SelectMany(f => f.Ids).ToHashSet();
        if (ids.Count > 0 && ModUpdates.Found.TryGetValue(g.Def.Id, out var found))
        {
            var list = found.Where(u => ids.Contains(u.RecordId)).ToList();
            if (list.Count > 0)
            {
                await UpdateMods(g, list);
                var ok = list.Count(u => ModUpdates.AlreadyDone(g, u));
                if (ok > 0) done.Add(I18n.T("v24.fixed.updated", ("n", ok)));
                failed.AddRange(list.Where(u => !ModUpdates.AlreadyDone(g, u)).Select(u => u.Name));
            }
        }
    }

    /// <summary>Недостающие моды: по манифестам (Deps) и по именам из лога — в каталоге этой игры.</summary>
    static async Task<List<ModInfo>> ResolveMissing(GameState g, List<string> names, List<string> failed)
    {
        var ids = new List<string>();
        try
        {
            var missing = await Deps.FindResolved(g.Registry!);
            ids.AddRange(missing.Where(m => m.ResolveId is not null).Select(m => m.ResolveId!).Distinct());
            // Чего каталог не знает — назовём в итоге, чтобы поставить руками.
            foreach (var m in missing.Where(m => m.ResolveId is null)) if (!names.Contains(m.Name, StringComparer.OrdinalIgnoreCase)) names.Add(m.Name);
        }
        catch { }
        var result = ids.Count == 0 ? [] : Program.Demo ? Demo.Many(g.Def, ids) : await Catalog.Many(g.Def, ids);
        foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ModInfo? hit = null;
            try { hit = Program.Demo ? null : await FindModByName(g.Def, name); } catch { }
            if (hit is null) { failed.Add(CrashDoctor.Pretty(name)); continue; }
            if (!result.Any(r => r.Id == hit.Id) && !IsInstalled(g, hit.Id)) result.Add(hit);
        }
        return result.Where(m => !IsInstalled(g, m.Id)).ToList();
    }

    /// <summary>
    /// Мод по имени из лога: UniqueID SMAPI — через smapi.io в номер Nexus, имя Hollow Knight — в ModLinks,
    /// GUID BepInEx («evaisa.lethallib») — поиском по каталогу с точным совпадением имени (и автора, если есть).
    /// </summary>
    public static async Task<ModInfo?> FindModByName(GameDef def, string name, CancellationToken ct = default)
    {
        // «nexus:1915» — SMAPI сам дал ссылку на страницу мода.
        if (name.StartsWith("nexus:", StringComparison.Ordinal) && def.NexusDomain is not null && name[6..].All(char.IsDigit) && name.Length > 6)
            return await Catalog.Get(def, name[6..], ct, "nexus");
        if (def.Loader == LoaderKind.Smapi && name.Contains('.') && !name.Contains(' '))
        {
            var found = await SmapiIndex.Lookup([name], ct);
            if (found.TryGetValue(name, out var hit) && hit.NexusId is { Length: > 0 } nexusId && def.NexusDomain is not null)
                return await Catalog.Get(def, nexusId, ct, "nexus");
        }
        var pretty = CrashDoctor.Pretty(name);
        var n = Deps.Norm(pretty);
        if (n.Length < 3) return null;
        if (def.Catalog == CatalogKind.ModLinks)
            return (await ModLinks.Load(ct)).FirstOrDefault(m => Deps.Norm(m.Name) == n || Deps.Norm(m.Id) == n);
        var page = await Catalog.Browse(def, new Query(Text: pretty), ct);
        var author = name.Contains('.') && !name.Contains(' ') && name.Split('.') is { Length: >= 2 } parts ? Deps.Norm(parts[^2]) : null;
        var matches = page.Mods.Where(m => Deps.Norm(m.Name) == n || Deps.Norm(m.Id.Split('-').Last()) == n).ToList();
        return matches.FirstOrDefault(m => author is { Length: > 0 } && (Deps.Norm(m.Author) == author || Deps.Norm(m.Id.Split('-')[0]) == author)) ?? matches.FirstOrDefault();
    }

    // ---------------------------------------------------------------- Minecraft

    /// <summary>id мода в логе → проект Modrinth (где они расходятся).</summary>
    static readonly Dictionary<string, string> McSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fabric"] = "fabric-api", ["fabricapi"] = "fabric-api", ["cloth-config2"] = "cloth-config", ["cloth_config"] = "cloth-config",
        ["yet_another_config_lib_v3"] = "yacl", ["yet-another-config-lib"] = "yacl", ["fabric_language_kotlin"] = "fabric-language-kotlin",
        ["kotlinforforge"] = "kotlin-for-forge", ["playeranimator"] = "playeranimator", ["owo"] = "owo-lib",
    };

    static async Task<MrProject?> FindMrProject(McInstance a, string need, CancellationToken ct)
    {
        var slug = McSlugs.GetValueOrDefault(need, need);
        try
        {
            var p = await McModrinth.Project(slug, ct);
            if (p.Type == "mod") return p;
        }
        catch { }
        var n = Deps.Norm(need);
        var (hits, _) = await McModrinth.Search(need, "mod", "relevance", 0, a, ct: ct);
        return hits.FirstOrDefault(h => Deps.Norm(h.Slug) == n || Deps.Norm(h.Title) == n);
    }

    static async Task FixMinecraft(McInstance a, List<CrashFix> fixes, List<string> done, List<string> failed)
    {
        // Мод требует более новый загрузчик: берём последнюю стабильную версию для этой версии игры.
        if (fixes.Any(f => f.Kind == "mcloader") && a.Modded)
        {
            var before = a.LoaderVersion;
            a.LoaderVersion = "";
            var ok = await RunAndWait(a.LoaderTitle, "Minecraft", async (_, progress, ct) => { await McInstall.EnsureVersion(a, progress, ct); });
            if (ok)
            {
                try { McProfiles.Sync(a); } catch { }
                done.Add(I18n.T("v24.fixed.mcloader", ("loader", $"{a.LoaderTitle} {a.LoaderVersion}")));
            }
            else
            {
                a.LoaderVersion = before;
                Mc.Save(a);
                failed.Add(a.LoaderTitle);
            }
        }
        if (fixes.FirstOrDefault(f => f.Kind == "memory") is { } memory && memory.Number > a.MemoryMb)
        {
            a.MemoryMb = memory.Number;
            Mc.Save(a);
            try { McProfiles.Sync(a); } catch { }
            done.Add(I18n.T("v24.fixed.memory", ("mb", memory.Number)));
        }
        if (fixes.FirstOrDefault(f => f.Kind == "java") is { } java)
        {
            string? exe = null;
            var ok = await RunAndWait("Java " + java.Number, "Minecraft", async (_, progress, ct) => { exe = await McInstall.Java(progress, ct, java.Number); });
            if (ok && exe is not null)
            {
                a.JavaPath = exe;
                Mc.Save(a);
                try { McProfiles.Sync(a); } catch { }
                done.Add(I18n.T("v24.fixed.java", ("n", java.Number)));
            }
            else failed.Add("Java " + java.Number);
        }
        var keys = fixes.Where(f => f.Kind == "disable").SelectMany(f => f.Ids).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var off = new List<string>();
        foreach (var item in McContent.List(a, "mod").Where(x => keys.Contains(x.Key) && x.Enabled))
            try { McContent.SetEnabled(a, item, false); off.Add(item.Name); } catch { }
        if (off.Count > 0) done.Add(I18n.T("v24.fixed.disabled", ("list", string.Join(", ", off.Take(5)))));

        var needs = fixes.Where(f => f.Kind == "deps").SelectMany(f => f.Names ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (needs.Count > 0)
        {
            var got = new List<string>();
            var miss = new List<string>();
            await RunAndWait(I18n.T("deps.install"), "Minecraft", async (_, progress, ct) =>
            {
                foreach (var need in needs)
                {
                    try
                    {
                        if (await FindMrProject(a, need, ct) is not { } p) { miss.Add(need); continue; }
                        await McModrinth.Install(a, p, null, progress, ct);
                        got.Add(p.Title);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception e) { Guard.Log(e); miss.Add(need); }
                }
            });
            if (got.Count > 0) done.Add(I18n.T("v24.fixed.installed", ("list", string.Join(", ", got.Take(5)))));
            failed.AddRange(miss);
            Mc.Notify();
        }

        var updates = fixes.Where(f => f.Kind == "update").SelectMany(f => f.Ids).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (updates.Count > 0)
        {
            var list = McContent.Updates(a).Where(u => updates.Contains("*") || updates.Contains(u.Item.Key)).ToList();
            if (list.Count > 0)
            {
                var ok = await UpdateMc(a, list);
                if (ok > 0) done.Add(I18n.T("v24.fixed.updated", ("n", ok)));
                if (ok < list.Count) failed.Add(I18n.T("mine.updates.failed", ("n", list.Count - ok)));
            }
        }
    }

    /// <summary>Итог: что сделано, что не вышло, и «Играть».</summary>
    internal static void ShowFixed(GameState g, List<string> done, List<string> failed, McInstance? mc = null)
    {
        if (done.Count == 0 && failed.Count == 0) return;
        // «Играть» запускает выбранную сборку — выбираем ту, что чинили.
        void PlayFixed()
        {
            if (mc is not null && Mc.Active?.Id != mc.Id) Mc.Active = mc;
            Play(g);
        }
        var body = Ui.Col(10);
        foreach (var line in done) body.Children.Add(Ui.Row(8, Ui.Icon(Icons.Check, 15, Ui.Res("Good")), Ui.Text(line, wrap: true)));
        if (failed.Count > 0)
            body.Children.Add(Ui.Row(8, Ui.Icon(Icons.Alert, 15, Ui.Res("Warn")), Ui.Text(I18n.T("v24.fixed.failed", ("list", string.Join(", ", failed.Distinct().Take(6)))), wrap: true)));
        if (!g.Def.IsMinecraft && done.Count > 0) body.Children.Add(Ui.Text(I18n.T("v24.fixed.undo"), "small muted", wrap: true));
        W.Dialog(I18n.T(done.Count > 0 ? "v24.fixed.title" : "v24.fixed.none"), body,
            Ui.Button(I18n.T("common.close"), W.CloseDialog),
            Ui.Button(I18n.T("v24.fixed.play"), () => { W.CloseDialog(); PlayFixed(); }, "primary", Icons.Play));
    }

    // ---------------------------------------------------------------- обновления Minecraft одной задачей

    /// <summary>
    /// Какие файлы Minecraft сейчас обновляются: «сборка|вид|файл». Сборка в ключе — одинаковые файлы
    /// бывают в нескольких сборках (копия сборки), и обновление одной не должно прятать обновления другой.
    /// Трогается только в потоке интерфейса.
    /// </summary>
    static readonly HashSet<string> McUpdating = [];

    static string McKey(McInstance a, McItem item) => $"{a.Id}|{item.Kind}|{item.Key}";

    public static bool IsMcUpdating(McInstance a, McItem item) => McUpdating.Contains(McKey(a, item));

    public static bool McBuildUpdating(McInstance a) => McUpdating.Any(k => k.StartsWith(a.Id + "|", StringComparison.Ordinal));

    /// <summary>
    /// Обновить моды сборки Minecraft одной задачей. Возвращает, сколько обновилось. Отмена и ошибки
    /// снимают отметки «обновляется» (раньше кнопка на странице оставалась выключенной после отмены);
    /// «Повторить» отмечает заново. Пока Minecraft запущен, файлы не меняем. Удалённый или выключенный
    /// после проверки мод не ставится обратно.
    /// </summary>
    public static Task<int> UpdateMc(McInstance a, IReadOnlyList<McUpdate> updates)
    {
        if (Launcher.IsRunning(Mc.Id)) { W.Toast(I18n.T("vanilla.running"), bad: true); return Task.FromResult(0); }
        var list = updates.Where(u => McUpdating.Add(McKey(a, u.Item))).ToList();
        if (list.Count == 0) return Task.FromResult(0);
        Mc.Notify();
        var ok = 0;
        var again = false;
        var result = new TaskCompletionSource<int>();
        Job? job = null;
        void OnFinished(Job j)
        {
            if (j != job) return;
            Jobs.Finished -= OnFinished;
            foreach (var u in list) McUpdating.Remove(McKey(a, u.Item));
            Mc.Notify();
            result.TrySetResult(ok);
        }
        Jobs.Finished += OnFinished;
        job = Jobs.Run(list.Count == 1 ? list[0].Item.Name : I18n.T("mine.updates.job", ("n", list.Count)), "Minecraft", async (_, progress, ct) =>
        {
            if (Launcher.IsRunning(Mc.Id)) throw new InvalidOperationException(I18n.T("vanilla.running"));
            // «Повторить»: та же работа ещё раз — снова отмечаем файлы и снимаем отметки в конце.
            var retry = again;
            again = true;
            if (retry) await Dispatcher.UIThread.InvokeAsync(() => { foreach (var u in list) McUpdating.Add(McKey(a, u.Item)); Mc.Notify(); });
            var failed = 0;
            ok = 0;
            try
            {
                foreach (var u in list)
                {
                    // Мод удалили или выключили после проверки — не возвращаем и не включаем его.
                    var current = McContent.List(a, u.Item.Kind).FirstOrDefault(x => x.Key == u.Item.Key);
                    if (current is null || !current.Enabled) { McContent.ForgetUpdate(a, u.Item); continue; }
                    // Уже обновили (например, при прошлой попытке).
                    if (McContent.Updates(a).All(x => x.Item.Key != u.Item.Key)) continue;
                    try { await McModrinth.Update(a, u, progress, ct); Interlocked.Increment(ref ok); Premium.Count("modsUpdated"); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception e) { failed++; Guard.Log(e); }
                }
            }
            finally
            {
                if (retry) Dispatcher.UIThread.Post(() => { foreach (var u in list) McUpdating.Remove(McKey(a, u.Item)); Mc.Notify(); });
            }
            Dispatcher.UIThread.Post(() => W.Toast(failed == 0 ? I18n.T("mr.updated") : I18n.T("mine.updates.failed", ("n", failed)), bad: failed > 0));
            if (failed > 0 && failed == list.Count) throw new InvalidOperationException(I18n.T("mine.updates.failed", ("n", failed)));
        });
        return result.Task;
    }
}
