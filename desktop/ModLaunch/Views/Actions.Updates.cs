using System.Collections.Concurrent;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Features;
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
