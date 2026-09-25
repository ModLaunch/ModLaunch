using System.Collections.ObjectModel;
using ModLaunch.Mods;

namespace ModLaunch.Core;

public enum JobStatus { Running, Done, Failed }

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
}

/// <summary>Очередь установок. Интерфейс подписывается на Changed.</summary>
public static class Jobs
{
    public static readonly ObservableCollection<Job> All = [];
    public static event Action<Job>? Changed;
    public static event Action<Job>? Finished;

    public static int Running => All.Count(j => j.Status == JobStatus.Running);

    public static Job Run(string title, string gameName, Func<Job, IProgress<InstallStep>, CancellationToken, Task> work)
    {
        var job = new Job { Title = title, GameName = gameName };
        All.Insert(0, job);
        Raise(job);
        var progress = new Progress<InstallStep>(step =>
        {
            job.Step = Describe(step);
            job.Ratio = step.Ratio;
            Raise(job);
        });
        _ = Task.Run(async () =>
        {
            try
            {
                await work(job, progress, job.Cancel.Token);
                job.Status = JobStatus.Done;
                job.Step = I18n.T("dl.done");
                job.Ratio = 1;
            }
            catch (Exception e)
            {
                job.Status = JobStatus.Failed;
                job.Error = e is OperationCanceledException ? I18n.T("common.cancel") : Explain(e);
                job.Step = I18n.T("dl.failed");
            }
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Changed?.Invoke(job);
                Finished?.Invoke(job);
            });
        });
        return job;
    }

    static void Raise(Job job) => Avalonia.Threading.Dispatcher.UIThread.Post(() => Changed?.Invoke(job));

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
        HttpRequestException h => I18n.T("err.nexus.offline", ("reason", h.Message)),
        TaskCanceledException => I18n.T("err.nexus.offline", ("reason", "timeout")),
        _ => e.Message,
    };
}
