using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Loaders;
using ModLaunch.Mods;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>Действия, общие для нескольких экранов: установка, запуск, загрузчик.</summary>
public static class Actions
{
    public static readonly HashSet<string> Installing = [];

    /// <summary>Файлы с Nexus без Premium ждём по одному: иначе не понять, какой архив к какому моду.</summary>
    static readonly SemaphoreSlim NexusBrowser = new(1);

    static MainWindow W => MainWindow.Current!;

    public static void InstallLoader(GameState g)
    {
        if (g.Path is null) return;
        var path = g.Path;
        Jobs.Run(g.Def.LoaderName, g.Def.Name, async (_, progress, ct) =>
        {
            await Loader.Install(g.Def, path, progress, ct);
        });
    }

    public static void Install(GameState g, ModInfo mod)
    {
        if (g.Path is null || g.Registry is null) return;
        if (!g.LoaderInstalled)
        {
            W.Dialog(I18n.T("loader.needed.title", ("loader", g.Def.LoaderName)),
                Ui.Text(I18n.T("loader.needed.text", ("loader", g.Def.LoaderName), ("game", g.Def.Name)), "muted", wrap: true),
                Ui.Button(I18n.T("common.cancel"), W.CloseDialog),
                Ui.Button(I18n.T("games.installLoader", ("loader", g.Def.LoaderName)), () => { W.CloseDialog(); InstallLoader(g); }, "primary"));
            return;
        }

        var registry = g.Registry;
        var key = $"{g.Def.Id}/{mod.Id}";
        if (!Installing.Add(key)) return;
        AppState.Notify();

        Jobs.Run(mod.Name, g.Def.Name, async (job, progress, ct) =>
        {
            try
            {
                if (mod.Source == "nexus") await InstallNexus(g, registry, mod, job, progress, ct);
                else await Installer.InstallFromCatalog(registry, mod, progress, ct);
            }
            finally
            {
                Installing.Remove(key);
            }
        });
    }

    static async Task InstallNexus(GameState g, ModRegistry registry, ModInfo mod, Job job, IProgress<InstallStep> progress, CancellationToken ct)
    {
        progress.Report(new InstallStep("install.deps", mod.Name));
        var (_, file, _) = await Nexus.Details(g.Def.NexusDomain!, g.Def.NexusGameId, mod.Id, ct);
        if (file is null) throw new InvalidOperationException(I18n.T("err.nexus.noLink"));

        string archive;
        var temporary = true;
        var apiKey = Settings.NexusApiKey;
        if (!string.IsNullOrEmpty(apiKey) && Settings.NexusPremium)
        {
            var url = await Nexus.DownloadLink(g.Def.NexusDomain!, mod.Id, file.FileId, apiKey, ct: ct);
            archive = await Http.Download(url, file.FileName ?? $"{mod.Id}.zip",
                new Progress<double>(r => progress.Report(new InstallStep("install.download", mod.Name, Ratio: r))), ct: ct);
        }
        else
        {
            // Без Premium Nexus отдаёт файл только после нажатия на сайте.
            await NexusBrowser.WaitAsync(ct);
            try
            {
            var page = Nexus.FilePageUrl(g.Def.NexusDomain!, mod.Id, file.FileId);
            var since = DateTime.Now;
            progress.Report(new InstallStep("install.browser", mod.Name));
            var picked = new TaskCompletionSource<string>();
            await Dispatcher.UIThread.InvokeAsync(() => ShowNexusWait(mod, page, job, picked));
            Ui.OpenUrl(page);
            var watch = DownloadWatch.WaitForArchive(since, ct);
            var done = await Task.WhenAny(watch, picked.Task);
            archive = await done;
            temporary = false;
            await Dispatcher.UIThread.InvokeAsync(W.CloseDialog);
            }
            finally { NexusBrowser.Release(); }
        }

        try
        {
            progress.Report(new InstallStep("install.extract", mod.Name));
            Installer.InstallArchive(registry, archive, new JsonObject
            {
                ["id"] = mod.Id,
                ["name"] = mod.Name,
                ["version"] = file.Version,
                ["author"] = mod.Author,
                ["source"] = "nexus",
                ["url"] = mod.Url,
                ["icon"] = mod.Icon,
            });
        }
        finally
        {
            if (temporary) try { File.Delete(archive); } catch { }
        }
    }

    static void ShowNexusWait(ModInfo mod, string page, Job job, TaskCompletionSource<string> picked)
    {
        var steps = Ui.Col(10,
            Ui.Text("1. " + I18n.T("nexus.wait.step1", ("mod", mod.Name)), wrap: true),
            Ui.Text("2. " + I18n.T("nexus.wait.step2"), wrap: true),
            Ui.Text("3. " + I18n.T("nexus.wait.step3"), wrap: true),
            Ui.Row(10, new ProgressBar { IsIndeterminate = true, Width = 120, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }, Ui.Text(I18n.T("nexus.wait.status"), "muted small")));
        W.Dialog(I18n.T("nexus.wait.title"), steps,
            Ui.Button(I18n.T("common.cancel"), () => { job.Cancel.Cancel(); W.CloseDialog(); }),
            Ui.Button(I18n.T("nexus.wait.file"), async () =>
            {
                var file = await W.PickFile(I18n.T("nexus.wait.file"));
                if (file is not null) picked.TrySetResult(file);
            }, "", Icons.FilePlus),
            Ui.Button(I18n.T("nexus.wait.reopen"), () => Ui.OpenUrl(page), "primary", Icons.External));
    }

    public static async void InstallFromFile(GameState g)
    {
        if (g.Registry is null) return;
        var file = await W.PickFile(I18n.T("inst.fromFile"));
        if (file is null) return;
        var registry = g.Registry;
        Jobs.Run(Path.GetFileNameWithoutExtension(file), g.Def.Name, (_, progress, _) =>
        {
            progress.Report(new InstallStep("install.extract", Path.GetFileName(file)));
            Installer.InstallArchive(registry, file, new JsonObject { ["source"] = "file" });
            return Task.CompletedTask;
        });
    }

    public static void Play(GameState g)
    {
        if (g.Path is null) return;
        var exe = g.Def.LaunchExe(g.Path);
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { WorkingDirectory = g.Path, UseShellExecute = true });
            W.Toast(I18n.T("toast.launched"));
        }
        catch (Exception e) { W.Toast(e.Message, bad: true); }
    }

    public static void OpenFolder(string? path)
    {
        if (path is null || !Directory.Exists(path)) return;
        Ui.OpenUrl(path);
    }

    public static async void PickGameFolder(GameState g)
    {
        var dir = await W.PickFolder(g.Def.Name);
        if (dir is null) return;
        var error = Locator.Validate(g.Def, dir);
        if (error is not null) { W.Toast(I18n.T(error, ("game", g.Def.Name)), bad: true); return; }
        AppState.SetPath(g, dir);
        W.Toast(I18n.T("toast.pathSaved"));
    }
}
