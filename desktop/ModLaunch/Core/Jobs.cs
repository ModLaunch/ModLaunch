using System.Collections.ObjectModel;
using ModLaunch.Mods;

namespace ModLaunch.Core;

public enum JobStatus { Queued, Running, Done, Failed, Canceled }

/// <summary>Одна загрузка или установка — строка в панели «Загрузки».</summary>
public sealed class Job
{
    public required string Title { get; init; }
    public required string GameName { get; init; }
    public string Step { get; set; } = I18n.T("dl.prepare");
    public double Ratio { get; set; } = -1;
    public JobStatus Status { get; set; }
    public string? Error { get; set; }
    public DateTime Started { get; } = DateTime.Now;
    public CancellationTokenSource Cancel { get; } = new();

    /// <summary>Сколько скачано и сколько всего (байты; 0 — пока не качаем) и скорость в байтах в секунду.</summary>
    public long Received { get; set; }
    public long Total { get; set; }
    public double Speed { get; set; }

    /// <summary>Остановлена кнопкой «Пауза»: «Продолжить» докачает с того же места.</summary>
    public bool Paused { get; set; }

    /// <summary>Страница, откуда ждём файл (Nexus без Premium) — чтобы открыть её ещё раз.</summary>
    public string? Link { get; set; }

    /// <summary>Ещё не закончилась: ждёт очереди или идёт.</summary>
    public bool Active => Status is JobStatus.Queued or JobStatus.Running;

    /// <summary>Сама работа — по ней «Повторить» запускает задачу заново.</summary>
    internal Func<Job, IProgress<InstallStep>, CancellationToken, Task> Work { get; init; } = null!;

    /// <summary>Уже запущена заново — второй клик по старой строке ничего не делает.</summary>
    internal bool Retried { get; set; }
}

/// <summary>
/// Очередь установок, как менеджер загрузок в Vortex: одновременно идут не больше
/// нескольких задач (Настройки → Загрузки), остальные ждут. Задачу можно отменить,
/// поставить на паузу и повторить. Интерфейс подписывается на Changed.
/// </summary>
public static class Jobs
{
    public static readonly ObservableCollection<Job> All = [];
    public static event Action<Job>? Changed;
    public static event Action<Job>? Finished;

    static readonly object Gate = new();
    static readonly List<Job> Waiting = [];
    static int _running;

    /// <summary>Задача, внутри которой идёт код, — Http.Download пишет в неё байты и скорость.</summary>
    static readonly AsyncLocal<Job?> Current = new();
    public static Job? CurrentJob => Current.Value;

    public static int Running => All.Count(j => j.Status == JobStatus.Running);

    /// <summary>Сколько задач идёт или ждёт — для значка на кнопке загрузок.</summary>
    public static int ActiveCount => All.Count(j => j.Active);

    /// <summary>Сколько задач идут одновременно (по умолчанию 3).</summary>
    public static int Limit => Settings.Data.Long("maxParallel") is var n && n > 0 ? (int)Math.Clamp(n, 1, 6) : 3;

    /// <summary>
    /// Поставить задачу в очередь. now = true — сразу, мимо очереди: её ждёт другая задача,
    /// которая уже заняла место (иначе обе ждали бы друг друга вечно).
    /// </summary>
    public static Job Run(string title, string gameName, Func<Job, IProgress<InstallStep>, CancellationToken, Task> work, bool now = false)
    {
        var job = new Job { Title = title, GameName = gameName, Work = work, Status = JobStatus.Queued };
        OnUi(() => All.Insert(0, job));
        Raise(job);
        if (now)
        {
            lock (Gate) { job.Status = JobStatus.Running; _running++; }
            Start(job);
            return job;
        }
        lock (Gate) Waiting.Add(job);
        Pump();
        return job;
    }

    /// <summary>Предел поменяли в настройках — если мест стало больше, ждущие стартуют сразу.</summary>
    public static void LimitChanged() => Pump();

    /// <summary>Отменить; с pause = true — пауза (скачанное не удаляется).</summary>
    public static void Cancel(Job job, bool pause = false)
    {
        var dropped = false;
        lock (Gate)
        {
            if (!job.Active) return;
            job.Paused = pause;
            if (job.Status == JobStatus.Queued)
            {
                Waiting.Remove(job);
                SetStopped(job);
                dropped = true;
            }
        }
        if (dropped) Complete(job);
        else job.Cancel.Cancel();
    }

    /// <summary>Запустить ещё раз (после ошибки, отмены или паузы) — на месте старой строки.</summary>
    public static Job Retry(Job job)
    {
        // Строка перерисуется только через миг — двойной клик попал бы в неё второй раз.
        lock (Gate)
        {
            if (job.Active || job.Retried) return job;
            job.Retried = true;
        }
        var fresh = new Job { Title = job.Title, GameName = job.GameName, Work = job.Work, Status = JobStatus.Queued };
        OnUi(() =>
        {
            var at = All.IndexOf(job);
            if (at >= 0) All[at] = fresh;
            else All.Insert(0, fresh);
        });
        Raise(fresh);
        lock (Gate) Waiting.Add(fresh);
        Pump();
        return fresh;
    }

    /// <summary>Убрать из списка всё, что уже закончилось (вызывать из интерфейса — он перерисует себя сам).</summary>
    public static void ClearFinished()
    {
        foreach (var job in All.Where(j => !j.Active).ToList()) All.Remove(job);
    }

    /// <summary>Запустить ждущие задачи, пока есть свободные места.</summary>
    static void Pump()
    {
        var start = new List<Job>();
        lock (Gate)
        {
            while (_running < Limit && Waiting.Count > 0)
            {
                var next = Waiting[0];
                Waiting.RemoveAt(0);
                if (next.Status != JobStatus.Queued) continue;
                next.Status = JobStatus.Running;
                _running++;
                start.Add(next);
            }
        }
        foreach (var job in start) Start(job);
    }

    static void Start(Job job)
    {
        Raise(job);
        var progress = new Reporter(job);
        _ = Task.Run(async () =>
        {
            Current.Value = job;
            try
            {
                await job.Work(job, progress, job.Cancel.Token);
                job.Status = JobStatus.Done;
                job.Step = I18n.T("dl.done");
                job.Ratio = 1;
            }
            catch (Exception) when (job.Cancel.IsCancellationRequested)
            {
                SetStopped(job);
            }
            catch (Exception e)
            {
                job.Status = JobStatus.Failed;
                // Отмену кнопкой поймали выше — здесь «отмена» значит, что вышло время (связь зависла).
                job.Error = e is OperationCanceledException ? I18n.T("err.nexus.offline", ("reason", "timeout")) : Explain(e);
                job.Step = I18n.T("dl.failed");
            }
            job.Speed = 0;
            lock (Gate) _running--;
            Complete(job);
            Pump();
        });
    }

    static void SetStopped(Job job)
    {
        job.Status = JobStatus.Canceled;
        job.Step = I18n.T(job.Paused ? "dl.paused" : "dl.canceled");
    }

    static void Complete(Job job) => OnUi(() =>
    {
        Changed?.Invoke(job);
        Finished?.Invoke(job);
    });

    static void Raise(Job job) => OnUi(() => Changed?.Invoke(job));

    /// <summary>Список и события — только в потоке интерфейса (без окна, в проверках, — сразу).</summary>
    static void OnUi(Action action)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess() || Avalonia.Application.Current is null) action();
        else Avalonia.Threading.Dispatcher.UIThread.Post(action);
    }

    /// <summary>Шаги установки — сразу в задачу (без Progress: тот путает порядок из фоновых потоков).</summary>
    sealed class Reporter(Job job) : IProgress<InstallStep>
    {
        public void Report(InstallStep step)
        {
            job.Step = Describe(step);
            job.Ratio = step.Ratio;
            if (step.Code != "install.download" && step.Code != "loader.download") job.Total = 0;
            Raise(job);
        }
    }

    static string Describe(InstallStep s)
    {
        var what = s.Code switch
        {
            "install.deps" => I18n.T("dl.deps"),
            "install.download" or "loader.download" => I18n.T("dl.download"),
            "install.extract" or "loader.install" or "loader.unpack" or "loader.backup" => I18n.T("dl.extract"),
            "install.browser" => I18n.T("dl.browser"),
            "install.done" or "loader.done" => I18n.T("dl.done"),
            _ => I18n.T("dl.prepare"),
        };
        return s.Total > 1 ? $"{what} {I18n.T("dl.detail.of", ("i", s.Index), ("n", s.Total), ("name", s.Mod))}" : what;
    }

    /// <summary>Понятный текст ошибки вместо исключения.</summary>
    public static string Explain(Exception e) => e switch
    {
        UnauthorizedAccessException => I18n.T("games.noWrite"),
        OfflineException => I18n.T("offline.err"),
        HttpRequestException h => I18n.T("err.nexus.offline", ("reason", h.Message)),
        TaskCanceledException => I18n.T("err.nexus.offline", ("reason", "timeout")),
        _ => e.Message,
    };

    /// <summary>Очередь: при пределе 2 из пяти задач одновременно идут не больше двух, отмена ждущей её не запускает.</summary>
    [SelfTest]
    static string QueueRespectsLimit()
    {
        Settings.Data["maxParallel"] = 2;
        var now = 0;
        var peak = 0;
        var ran = 0;
        var jobs = Enumerable.Range(0, 5).Select(i => Run($"test {i}", "test", async (_, _, ct) =>
        {
            var n = Interlocked.Increment(ref now);
            lock (Gate) peak = Math.Max(peak, n);
            Interlocked.Increment(ref ran);
            await Task.Delay(120, ct);
            Interlocked.Decrement(ref now);
        })).ToList();
        Cancel(jobs[4]);
        var until = DateTime.UtcNow.AddSeconds(10);
        while (jobs.Any(j => j.Active) && DateTime.UtcNow < until) Thread.Sleep(20);
        Settings.Data.Remove("maxParallel");
        if (jobs.Any(j => j.Active)) throw new Exception("jobs did not finish");
        if (peak > 2) throw new Exception($"{peak} jobs ran at once");
        if (jobs[4].Status != JobStatus.Canceled || ran != 4) throw new Exception("canceled job still ran");
        foreach (var j in jobs) All.Remove(j);
        return $"peak {peak} at limit 2, canceled job skipped";
    }

    /// <summary>Двойной клик по «Повторить» на старой строке не запускает работу дважды.</summary>
    [SelfTest]
    static string RetryTwiceRunsOnce()
    {
        var ran = 0;
        var job = Run("retry test", "test", (_, _, _) => { Interlocked.Increment(ref ran); throw new IOException("first try fails"); });
        var until = DateTime.UtcNow.AddSeconds(5);
        while (job.Active && DateTime.UtcNow < until) Thread.Sleep(10);
        var a = Retry(job);
        var b = Retry(job);
        until = DateTime.UtcNow.AddSeconds(5);
        while ((a.Active || b.Active) && DateTime.UtcNow < until) Thread.Sleep(10);
        foreach (var j in new[] { job, a, b }) All.Remove(j);
        if (ran != 2) throw new Exception($"work ran {ran} times (expected 2: first try + one retry)");
        return "second click on the same row is ignored";
    }

    /// <summary>Задача ждёт другую (окно Nexus ждёт установку по nxm://): при занятой очереди та стартует сразу, без вечного ожидания.</summary>
    [SelfTest]
    static string WaitedJobSkipsFullQueue()
    {
        Settings.Data["maxParallel"] = 1;
        var got = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Job? helper = null;
        var waiting = Run("waits", "test", async (_, _, ct) =>
        {
            helper = Run("helper", "test", (_, _, _) => { got.TrySetResult(); return Task.CompletedTask; }, now: true);
            await got.Task.WaitAsync(TimeSpan.FromSeconds(3), ct);
        });
        var until = DateTime.UtcNow.AddSeconds(6);
        while ((waiting.Active || helper?.Active != false) && DateTime.UtcNow < until) Thread.Sleep(10);
        Settings.Data.Remove("maxParallel");
        foreach (var j in new[] { waiting, helper }) if (j is not null) All.Remove(j);
        if (waiting.Status != JobStatus.Done) throw new Exception($"waiting job ended {waiting.Status}: the job it waited for never got a place in the queue");
        return "helper started at once, waiting job done";
    }
}
