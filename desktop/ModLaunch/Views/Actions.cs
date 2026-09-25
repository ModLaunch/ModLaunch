using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Games;
using ModLaunch.Loaders;
using ModLaunch.Mods;
using ModLaunch.Sources;

namespace ModLaunch.Views;

public sealed record Pin(long FileId, string Version, string? FileName);

/// <summary>Действия, общие для нескольких экранов: установка, запуск, загрузчик, nxm, коллекции.</summary>
public static class Actions
{
    public static readonly HashSet<string> Installing = [];

    /// <summary>Моды Nexus, которые ждём из браузера: «игра:номер» → куда отдать файл (или готовую запись по nxm://).</summary>
    static readonly Dictionary<string, TaskCompletionSource<object>> BrowserWaits = [];

    /// <summary>Окна ожидания файла показываем по одному: иначе человек запутается, какую кнопку нажимать.</summary>
    static readonly SemaphoreSlim NexusBrowser = new(1);

    static MainWindow W => MainWindow.Current!;

    public static bool IsBusy(GameState g, string catalogId) => Installing.Contains($"{g.Def.Id}/{catalogId}");

    /// <summary>Стоит ли мод: номер ищем и как есть, и как мод Nexus (у игры может быть несколько каталогов).</summary>
    public static bool IsInstalled(GameState g, string catalogId) =>
        (g.Registry?.Get(catalogId) ?? (g.Def.NexusDomain is null ? null : g.Registry?.Get(g.Def.RecordId(catalogId, "nexus")))) is { } r && !r.Bool("missing");

    public static void InstallLoader(GameState g)
    {
        if (g.Path is null) return;
        var path = g.Path;
        Jobs.Run(g.Def.LoaderName, g.Def.Name, async (_, progress, ct) => { await Loader.Install(g.Def, path, progress, ct); });
    }

    static bool NeedLoader(GameState g)
    {
        if (g.LoaderInstalled) return false;
        W.Dialog(I18n.T("loader.needed.title", ("loader", g.Def.LoaderName)),
            Ui.Text(I18n.T("loader.needed.text", ("loader", g.Def.LoaderName), ("game", g.Def.Name)), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), W.CloseDialog),
            Ui.Button(I18n.T("games.installLoader", ("loader", g.Def.LoaderName)), () => { W.CloseDialog(); InstallLoader(g); }, "primary"));
        return true;
    }

    /// <summary>Установить мод из каталога (с зависимостями). Возвращает задачу, чтобы коллекции ставились по очереди.</summary>
    public static Task Install(GameState g, ModInfo mod, Pin? pin = null, bool reinstall = false)
    {
        if (g.Path is null || g.Registry is null || NeedLoader(g)) return Task.CompletedTask;
        var registry = g.Registry;
        var key = $"{g.Def.Id}/{mod.Id}";
        if (!Installing.Add(key)) return Task.CompletedTask;
        AppState.Notify();

        var done = new TaskCompletionSource();
        var job = Jobs.Run(mod.Name, g.Def.Name, async (job, progress, ct) =>
        {
            try
            {
                if (mod.Source == "nexus") await InstallNexus(g, registry, mod.Id, mod.Name, pin, job, progress, ct, withDeps: pin is null);
                else await Installer.InstallFromCatalog(registry, mod, progress, ct, reinstall);
            }
            finally
            {
                Installing.Remove(key);
            }
        });
        void OnFinished(Job j)
        {
            if (j != job) return;
            Jobs.Finished -= OnFinished;
            done.TrySetResult();
        }
        Jobs.Finished += OnFinished;
        return done.Task;
    }

    /// <summary>Мод с Nexus: сначала недостающие требования, потом сам мод. Premium — напрямую, иначе через браузер.</summary>
    static async Task InstallNexus(GameState g, ModRegistry registry, string modId, string title, Pin? pin, Job job, IProgress<InstallStep> progress, CancellationToken ct, bool withDeps)
    {
        var game = g.Def;
        if (withDeps)
        {
            progress.Report(new InstallStep("install.deps", title));
            var plan = await Deps.NexusPlan(game, registry, modId, ct);
            if (plan.Count > 0)
            {
                Dispatcher.UIThread.Post(() => W.Toast(I18n.T("deps.first", ("list", string.Join(", ", plan.Select(p => p.Name))))));
                foreach (var (id, name) in plan)
                {
                    if (IsInstalled(g, id)) continue;
                    await InstallNexus(g, registry, id, name, null, job, progress, ct, withDeps: false);
                }
            }
        }

        var (mod, main, reqs) = await Nexus.Details(game.NexusDomain!, game.NexusGameId, modId, ct);
        if (mod is null) throw new InvalidOperationException(I18n.T("err.modNotInCatalog"));
        var file = pin is not null ? new NexusFile(pin.FileId, "", pin.Version is { Length: > 0 } v ? v : main?.Version ?? "", pin.FileName, 0) : main;
        if (file is null) throw new InvalidOperationException(I18n.T("err.nexus.noFiles"));

        var meta = new JsonObject
        {
            ["id"] = game.RecordId(modId, "nexus"),
            ["name"] = mod.Name,
            ["version"] = file.Version is { Length: > 0 } fv ? fv : mod.Version,
            ["author"] = mod.Author,
            ["source"] = "nexus",
            ["url"] = mod.Url,
            ["icon"] = mod.Icon,
            ["requires"] = new JsonArray(reqs.Where(r => !game.NexusHide.Contains(r.Id)).Select(r => (JsonNode)new JsonObject { ["id"] = r.Id, ["name"] = r.Name }).ToArray()),
        };

        var apiKey = Settings.NexusApiKey;
        string archive;
        var temporary = true;
        if (!string.IsNullOrEmpty(apiKey) && Settings.NexusPremium)
        {
            var url = await Nexus.DownloadLink(game.NexusDomain!, modId, file.FileId, apiKey, ct: ct);
            archive = await Http.Download(url, file.FileName ?? $"{modId}.zip",
                new Progress<double>(r => progress.Report(new InstallStep("install.download", mod.Name, Ratio: r))), ct: ct);
        }
        else
        {
            await NexusBrowser.WaitAsync(ct);
            try
            {
                var got = await WaitFromBrowser(g, modId, mod.Name, file.FileId, job, progress, ct);
                if (got is JsonObject viaNxm) { _ = viaNxm; return; } // пришло по nxm:// и уже установлено
                archive = (string)got;
                temporary = false;
            }
            finally { NexusBrowser.Release(); }
        }

        try
        {
            progress.Report(new InstallStep("install.extract", mod.Name));
            await Installer.InstallAny(registry, archive, meta, progress, ct);
            DownloadArchive.Add(game.Id, game.RecordId(modId, "nexus"), meta.Str("version") ?? "", archive);
        }
        finally
        {
            // Скачанное нами — убираем; скачанное браузером остаётся у человека в «Загрузках».
            if (temporary) try { File.Delete(archive); } catch { }
        }
    }

    static async Task<object> WaitFromBrowser(GameState g, string modId, string name, long fileId, Job job, IProgress<InstallStep> progress, CancellationToken ct)
    {
        var page = Nexus.FilePageUrl(g.Def.NexusDomain!, modId, fileId);
        var waitKey = $"{g.Def.Id}:{modId}";
        var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (BrowserWaits) BrowserWaits[waitKey] = tcs;
        progress.Report(new InstallStep("install.browser", name));
        await Dispatcher.UIThread.InvokeAsync(() => ShowNexusWait(name, page, job, tcs));
        Ui.OpenUrl(page);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var watch = DownloadWatch.WaitForArchive(DownloadWatch.NexusMatcher(modId), stop.Token)
            .ContinueWith(t => { if (t.IsCompletedSuccessfully) tcs.TrySetResult(t.Result); else if (t.Exception?.InnerException is TimeoutException te) tcs.TrySetException(te); }, TaskScheduler.Default);
        using var reg = ct.Register(() => tcs.TrySetCanceled());
        try { return await tcs.Task; }
        finally
        {
            stop.Cancel();
            lock (BrowserWaits) BrowserWaits.Remove(waitKey);
            await Dispatcher.UIThread.InvokeAsync(W.CloseDialog);
        }
    }

    static void ShowNexusWait(string mod, string page, Job job, TaskCompletionSource<object> picked)
    {
        var steps = Ui.Col(10,
            Ui.Text("1. " + I18n.T("nexus.wait.step1", ("mod", mod)), wrap: true),
            Ui.Text("2. " + I18n.T("nexus.wait.step2"), wrap: true),
            Ui.Text("3. " + I18n.T("nexus.wait.step3"), wrap: true),
            Ui.Row(10, new ProgressBar { IsIndeterminate = true, Width = 120, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }, Ui.Text(I18n.T("nexus.wait.status"), "muted small")));
        W.Dialog(I18n.T("nexus.wait.title"), steps,
            Ui.Button(I18n.T("common.cancel"), () => { job.Cancel.Cancel(); picked.TrySetCanceled(); W.CloseDialog(); }),
            Ui.Button(I18n.T("nexus.wait.file"), async () =>
            {
                var file = await W.PickFile(I18n.T("dialog.pickArchive"));
                if (file is not null) picked.TrySetResult(file);
            }, "", Icons.FilePlus),
            Ui.Button(I18n.T("nexus.wait.reopen"), () => Ui.OpenUrl(page), "primary", Icons.External));
    }

    // ---------------------------------------------------------------- nxm://

    /// <summary>Ссылка «Mod Manager Download»: качаем по API (ключ нужен, Premium — нет).</summary>
    public static void InstallNxm(string link)
    {
        var parsed = Nxm.Parse(link);
        if (parsed is null) { W.Toast(I18n.T("err.linkUnparsed"), bad: true); return; }
        var g = AppState.Games.FirstOrDefault(x => x.Def.NexusDomain == parsed.Domain);
        if (g is null) { W.Toast(I18n.T("toast.nxmForeign", ("game", parsed.Domain)), bad: true); return; }
        if (g.Registry is null) { W.Toast(I18n.T("toast.notFound", ("game", g.Def.Name)), bad: true); return; }
        if (NeedLoader(g)) return;
        var apiKey = Settings.NexusApiKey;
        if (string.IsNullOrEmpty(apiKey))
        {
            W.Dialog(I18n.T("nexus.key.title", ("game", g.Def.Name)), Ui.Text(I18n.T("err.nexus.noKey"), "muted", wrap: true),
                Ui.Button(I18n.T("common.close"), W.CloseDialog),
                Ui.Button(I18n.T("nexus.key.paste"), () => { W.CloseDialog(); W.Navigate(() => new SettingsPage("accounts")); }, "primary"));
            return;
        }
        var registry = g.Registry;
        Jobs.Run($"Nexus #{parsed.ModId}", g.Def.Name, async (job, progress, ct) =>
        {
            progress.Report(new InstallStep("install.deps", $"#{parsed.ModId}"));
            var info = await Nexus.FileInfo(parsed.Domain, parsed.ModId, parsed.FileId, apiKey, ct);
            var url = await Nexus.DownloadLink(parsed.Domain, parsed.ModId, parsed.FileId, apiKey, parsed.Key, parsed.Expires, ct);
            var archive = await Http.Download(url, info.FileName ?? "mod.zip", new Progress<double>(r => progress.Report(new InstallStep("install.download", info.Name, Ratio: r))), ct: ct);
            try
            {
                progress.Report(new InstallStep("install.extract", info.Name));
                DownloadArchive.Add(g.Def.Id, g.Def.RecordId(parsed.ModId, "nexus"), info.Version, archive);
                var record = await Installer.InstallAny(registry, archive, new JsonObject
                {
                    ["id"] = g.Def.RecordId(parsed.ModId, "nexus"),
                    ["name"] = info.Name,
                    ["version"] = info.Version,
                    ["author"] = info.Author,
                    ["source"] = "nexus",
                    ["url"] = $"https://www.nexusmods.com/{parsed.Domain}/mods/{parsed.ModId}",
                    ["icon"] = info.Icon,
                }, progress, ct);
                // Этот мод ждали из браузера — ожидание закончено.
                lock (BrowserWaits) if (BrowserWaits.TryGetValue($"{g.Def.Id}:{parsed.ModId}", out var wait)) wait.TrySetResult(record);
            }
            finally { try { File.Delete(archive); } catch { } }
        });
    }

    // ---------------------------------------------------------------- коллекции и наборы

    public static CancellationTokenSource? CollectionRun { get; private set; }

    /// <summary>Поставить моды по очереди (коллекция, набор, сборка из файла).</summary>
    public static async Task InstallQueue(GameState g, string title, IList<(ModInfo Mod, Pin? Pin)> items)
    {
        if (g.Registry is null || NeedLoader(g)) return;
        CollectionRun?.Cancel();
        var run = CollectionRun = new CancellationTokenSource();
        var done = 0;
        var failed = new List<string>();
        for (var i = 0; i < items.Count; i++)
        {
            if (run.IsCancellationRequested) break;
            var (mod, pin) = items[i];
            if (IsInstalled(g, mod.Id)) { done++; continue; }
            W.Toast(I18n.T("coll.run", ("name", title), ("i", i + 1), ("n", items.Count)));
            await Install(g, mod, pin);
            if (IsInstalled(g, mod.Id)) done++; else failed.Add(mod.Name);
        }
        if (run.IsCancellationRequested) W.Toast(I18n.T("coll.stopped", ("done", done), ("n", items.Count)));
        else if (failed.Count > 0) W.Toast(I18n.T("coll.partial", ("done", done), ("n", items.Count), ("list", string.Join(", ", failed.Take(6)))), bad: true);
        else W.Toast(I18n.T("coll.done", ("name", title), ("n", done)));
        if (CollectionRun == run) CollectionRun = null;
    }

    public static void StopQueue() => CollectionRun?.Cancel();

    // ---------------------------------------------------------------- файлы, запуск, папки

    public static async void InstallFromFile(GameState g)
    {
        if (g.Registry is null || NeedLoader(g)) return;
        var file = await W.PickFile(I18n.T("dialog.pickArchive"));
        if (file is null) return;
        var registry = g.Registry;
        Jobs.Run(Path.GetFileNameWithoutExtension(file), g.Def.Name, async (_, progress, ct) =>
        {
            progress.Report(new InstallStep("install.extract", Path.GetFileName(file)));
            await Installer.InstallAny(registry, file, new JsonObject { ["source"] = "file", ["name"] = Path.GetFileNameWithoutExtension(file) }, progress, ct);
        });
    }

    public static void Play(GameState g)
    {
        if (g.Path is null) return;
        try
        {
            var backup = Launcher.Launch(g.Def, g.Path);
            if (g.Registry is not null) try { Social.PlayLog.NoteLaunch(g.Def, g.Registry); } catch { }
            Social.Friends.SetActivity("playing", g.Def.Id, g.Def.Name);
            OverlayWindow.OnGameStarted(g.Def.Id);
            W.Toast(I18n.T("toast.launched") + (backup is null ? "" : " · " + I18n.T("bak.created")));
            if (Settings.Data.Str("afterLaunch") == "minimize") W.WindowState = WindowState.Minimized;
            AppState.Notify();
        }
        catch (Exception e) { W.Toast(Jobs.Explain(e), bad: true); }
    }

    public static void OpenFolder(string? path)
    {
        if (path is null) return;
        if (File.Exists(path)) path = Path.GetDirectoryName(path);
        if (path is null || !Directory.Exists(path)) return;
        Ui.OpenUrl(path);
    }

    public static async void PickGameFolder(GameState g)
    {
        var dir = await W.PickFolder(I18n.T("dialog.pickGame"));
        if (dir is null) return;
        var error = Locator.Validate(g.Def, dir);
        if (error is not null) { W.Toast(I18n.T(error, ("game", g.Def.Name)), bad: true); return; }
        AppState.SetPath(g, dir);
        W.Toast(I18n.T("toast.pathSaved"));
    }

    /// <summary>Переустановить мод из архива загрузок (без интернета, в том числе старую версию).</summary>
    public static void ReinstallFromArchive(GameState g, JsonObject record, ArchivedFile file)
    {
        if (g.Registry is null) return;
        var registry = g.Registry;
        var id = record.Str("id")!;
        Jobs.Run(record.Str("name") ?? id, g.Def.Name, async (_, progress, ct) =>
        {
            var wasEnabled = record.Bool("enabled", true);
            if (!wasEnabled) registry.SetEnabled(id, true);
            var oldFolder = registry.FolderFor(registry.Get(id)!);
            var meta = (JsonObject)record.DeepClone();
            meta["version"] = file.Version;
            meta.Remove("installedAt");
            progress.Report(new InstallStep("install.extract", record.Str("name") ?? id));
            await Installer.InstallAny(registry, file.Path, meta, progress, ct);
            var fresh = registry.Get(id);
            if (fresh is not null && registry.FolderFor(fresh) != oldFolder && Directory.Exists(oldFolder)) Directory.Delete(oldFolder, true);
            if (!wasEnabled) registry.SetEnabled(id, false);
            Dispatcher.UIThread.Post(() => W.Toast(I18n.T("v4.reinstalled")));
        });
    }

    /// <summary>Одобрить мод на Nexus (нужен ключ).</summary>
    public static async Task Endorse(GameState g, string catalogId, string version)
    {
        var key = Settings.NexusApiKey;
        if (string.IsNullOrEmpty(key)) { W.Toast(I18n.T("v4.endorse.key"), bad: true); return; }
        try
        {
            await Nexus.Endorse(g.Def.NexusDomain!, catalogId, version, key);
            var endorsed = Settings.Data.Obj("endorsed");
            endorsed[$"{g.Def.Id}|{catalogId}"] = DateTime.UtcNow.ToString("o");
            Settings.Save();
            W.Toast(I18n.T("v4.endorsed"));
        }
        catch (Exception e) { W.Toast(Jobs.Explain(e), bad: true); }
    }

    public static bool IsEndorsed(GameState g, string catalogId) => Settings.Data.Obj("endorsed").Str($"{g.Def.Id}|{catalogId}") is not null;

    /// <summary>Поставить всё недостающее, что нашлось в каталоге.</summary>
    public static async Task InstallMissing(GameState g)
    {
        if (g.Registry is null) return;
        List<Missing> missing;
        try { missing = await Deps.FindResolved(g.Registry); }
        catch (Exception e) { W.Toast(Jobs.Explain(e), bad: true); return; }
        var ids = missing.Where(m => m.ResolveId is not null).Select(m => m.ResolveId!).Distinct().ToList();
        if (ids.Count == 0) { W.Toast(I18n.T("deps.none"), bad: true); return; }
        W.Toast(I18n.T("deps.after", ("list", string.Join(", ", missing.Where(m => m.ResolveId is not null).Select(m => m.Name).Distinct()))));
        var mods = Program.Demo ? Demo.Many(g.Def, ids) : await Catalog.Many(g.Def, ids);
        await InstallQueue(g, I18n.T("deps.install"), mods.Select(m => (m, (Pin?)null)).ToList());
    }
}
