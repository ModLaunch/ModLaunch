using System.Collections.Concurrent;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Games;
using ModLaunch.Mods;

namespace ModLaunch.Views;

/// <summary>Обновления модов: одной задачей по очереди, в том числе моды Nexus и перед запуском игры.</summary>
public static partial class Actions
{
    /// <summary>Какие моды сейчас обновляются: «игра/запись».</summary>
    static readonly ConcurrentDictionary<string, byte> Updating = new();

    /// <summary>Игры, для которых перед запуском идёт проверка обновлений (чтобы двойной клик не запускал её дважды).</summary>
    static readonly HashSet<string> PreparingPlay = [];

    static string UpdateKey(GameState g, string recordId) => $"{g.Def.Id}/{recordId}";

    public static bool IsUpdating(GameState g, string recordId) => Updating.ContainsKey(UpdateKey(g, recordId));

    /// <summary>У игры обновляются моды (или ждут очереди) — запускать её сейчас нельзя: она заняла бы файлы посреди замены.</summary>
    static bool UpdatingGame(GameState g) => Updating.Keys.Any(k => k.StartsWith(g.Def.Id + "/", StringComparison.Ordinal));

    /// <summary>
    /// Обновить моды игры одной задачей, по очереди: так записи в список модов не мешают друг другу,
    /// а в «Загрузках» одна понятная строка. Моды через браузер (Nexus без Premium) — в конце.
    /// Задача завершается, когда всё закончено.
    /// </summary>
    public static Task UpdateMods(GameState g, IReadOnlyList<ModUpdate> updates)
    {
        if (g.Registry is null) return Task.CompletedTask;
        var list = updates.Where(u => Updating.TryAdd(UpdateKey(g, u.RecordId), 0)).OrderBy(u => u.Manual).ToList();
        if (list.Count == 0) return Task.CompletedTask;
        AppState.Notify();
        var registry = g.Registry;
        var title = list.Count == 1 ? list[0].Name : I18n.T("upd.job", ("n", list.Count));
        var done = new TaskCompletionSource();
        var again = false;
        var job = Jobs.Run(title, g.Def.Name, async (job, progress, ct) =>
        {
            var retry = again;
            again = true;
            // Игра запущена (например, «Продолжить» нажали уже в игре) — её файлы заняты, менять моды на ходу нельзя.
            if (Launcher.IsRunning(g.Def.Id)) throw new InvalidOperationException(I18n.T("vanilla.running"));
            // «Повторить» запускает работу заново — снова помечаем моды как обновляемые;
            // те, что уже обновляет другая задача, не трогаем (иначе две задачи пишут в одну папку).
            var mine = retry ? list.Where(u => Updating.TryAdd(UpdateKey(g, u.RecordId), 0)).ToList() : list;
            Snapshots.Take(g, "update");
            var failed = new List<string>();
            try
            {
                for (var i = 0; i < mine.Count; i++)
                {
                    var u = mine[i];
                    // Уже стоит, мод удалили или отметили «не обновлять», пока задача ждала, — пропускаем.
                    if (registry.Get(u.RecordId) is not { } record || record.Bool("hold") || ModUpdates.AlreadyDone(g, u)) { ModUpdates.Forget(g.Def.Id, u.RecordId); continue; }
                    var step = mine.Count > 1 ? new NumberedSteps(progress, i + 1, mine.Count, u.Name) : progress;
                    try
                    {
                        if (u.Source == "nexus")
                            await ModUpdates.Replace(g, u.RecordId, () => InstallNexus(g, registry, u.CatalogId, u.Name, null, job, step, ct, withDeps: true));
                        else await ModUpdates.Update(g, u, step, ct);
                    }
                    catch (Exception) when (mine.Count > 1 && !ct.IsCancellationRequested) { failed.Add(u.Name); }
                    finally
                    {
                        Updating.TryRemove(UpdateKey(g, u.RecordId), out _);
                        Dispatcher.UIThread.Post(AppState.Notify);
                    }
                }
            }
            finally
            {
                foreach (var u in mine) Updating.TryRemove(UpdateKey(g, u.RecordId), out _);
                Dispatcher.UIThread.Post(AppState.Notify);
            }
            if (failed.Count > 0) throw new InvalidOperationException(I18n.T("upd.failedSome", ("list", string.Join(", ", failed))));
        });
        void OnFinished(Job j)
        {
            if (j != job) return;
            Jobs.Finished -= OnFinished;
            // Отменённая в очереди задача не запускалась — отметки снимаем здесь.
            foreach (var u in list) Updating.TryRemove(UpdateKey(g, u.RecordId), out _);
            AppState.Notify();
            done.TrySetResult();
        }
        Jobs.Finished += OnFinished;
        return done.Task;
    }

    /// <summary>Шаги одного мода внутри общей задачи: «2 из 5: Nautilus».</summary>
    sealed class NumberedSteps(IProgress<InstallStep> inner, int index, int total, string name) : IProgress<InstallStep>
    {
        public void Report(InstallStep step) =>
            inner.Report(step.Total > 1 ? step : step with { Index = index, Total = total, Mod = name });
    }

    /// <summary>«Обновлять перед игрой»: проверка (не дольше 20 секунд, без сети — молча дальше), обновление, запуск.</summary>
    static async Task UpdateThenPlay(GameState g, bool animate)
    {
        if (!PreparingPlay.Add(g.Def.Id)) return;
        try
        {
            W.Toast(I18n.T("upd.beforePlay.checking"));
            List<ModUpdate> found;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                found = await ModUpdates.Check(g, cts.Token);
            }
            catch { found = []; }
            var auto = found.Where(u => !u.Manual).ToList();
            if (auto.Count > 0)
            {
                W.Toast(I18n.T("upd.beforePlay.updating." + I18n.Plural(auto.Count, "one", "few", "many"), ("n", auto.Count)));
                await UpdateMods(g, auto);
            }
        }
        finally { PreparingPlay.Remove(g.Def.Id); }
        Play(g, animate, skipUpdate: true);
    }

    /// <summary>Сколько обновлений ждёт везде: моды игр и сборки Minecraft.</summary>
    public static int PendingUpdates() =>
        ModUpdates.Found.Sum(kv => kv.Value.Count) + (Minecraft.Mc.State is { Status: Detect.Found } ? Minecraft.Mc.Instances().Sum(Minecraft.McContent.UpdateCount) : 0);

    /// <summary>
    /// 23.9: «Обновить всё» одной кнопкой — без окна со списком: моды всех игр (каждая игра — своя задача,
    /// перед ней снимок «как было») и сборки Minecraft. Запущенные игры пропускаются.
    /// </summary>
    public static void UpdateAll(IReadOnlyList<(GameState Game, ModUpdate Update)>? only = null, bool minecraft = true)
    {
        var items = only ?? AppState.Games.SelectMany(g => (ModUpdates.Found.GetValueOrDefault(g.Def.Id) ?? []).Select(u => (g, u))).ToList();
        var n = 0;
        var skipped = new List<string>();
        foreach (var group in items.GroupBy(x => x.Game))
        {
            if (Launcher.IsRunning(group.Key.Def.Id)) { skipped.Add(group.Key.Def.ShortName); continue; }
            var list = group.Select(x => x.Update).ToList();
            n += list.Count;
            _ = UpdateMods(group.Key, list);
        }
        if (minecraft && Minecraft.Mc.State is { Status: Detect.Found } && !Launcher.IsRunning(Minecraft.Mc.Id))
            foreach (var i in Minecraft.Mc.Instances())
            {
                var ups = Minecraft.McContent.Updates(i);
                if (ups.Count == 0) continue;
                n += ups.Count;
                Jobs.Run(I18n.T("mine.updates.job", ("n", ups.Count)), "Minecraft · " + i.Name, async (_, progress, ct) =>
                {
                    var failed = 0;
                    foreach (var u in ups)
                    {
                        try { await Minecraft.McModrinth.Update(i, u, progress, ct); }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception e) { failed++; Guard.Log(e); }
                    }
                    Dispatcher.UIThread.Post(Minecraft.Mc.Notify);
                    if (failed > 0) throw new InvalidOperationException(I18n.T("mine.updates.failed", ("n", failed)));
                });
            }
        if (n > 0) W.Toast(I18n.T("v23.upd.started", ("n", n)) + (skipped.Count > 0 ? " " + I18n.T("v23.upd.running", ("list", string.Join(", ", skipped))) : ""));
        else if (skipped.Count > 0) W.Toast(I18n.T("vanilla.running"), bad: true);
        AppState.Notify();
    }

    /// <summary>
    /// Проверить обновления всех игр с модами (при запуске и раз в 6 часов). Игра обновилась в Steam —
    /// проверяем её в любом случае и говорим, сколько модов можно обновить.
    /// </summary>
    public static async Task CheckAllUpdates(bool startup)
    {
        var libraries = await Task.Run(Games.Locator.SteamLibraries);
        var patched = new List<(GameState Game, int N)>();
        foreach (var g in AppState.Games.Where(g => g.Status == Detect.Found && (g.ModCount > 0 || g.Def.IsMinecraft)))
        {
            var changed = false;
            try { changed = GamePatch.Changed(g, libraries); } catch { }
            if (!changed && !ModUpdates.OnStart) continue;
            try { var found = await ModUpdates.Check(g); if (changed) patched.Add((g, found.Count)); } catch { }
        }
        AppState.Notify();
        foreach (var (g, n) in patched.Where(p => p.N > 0))
            W.Toast(I18n.T("v23.upd.patched", ("game", g.Def.Name), ("n", n)));
        if (startup || ModUpdates.Auto) AutoUpdate();
    }

    /// <summary>«Обновлять моды сами»: после проверки при запуске программы — всё, что можно без браузера.</summary>
    public static void AutoUpdate()
    {
        if (!ModUpdates.Auto) return;
        foreach (var g in AppState.Games)
        {
            if (!ModUpdates.Found.TryGetValue(g.Def.Id, out var list)) continue;
            var auto = list.Where(u => !u.Manual).ToList();
            if (auto.Count > 0 && !Launcher.IsRunning(g.Def.Id)) _ = UpdateMods(g, auto);
        }
    }
}
