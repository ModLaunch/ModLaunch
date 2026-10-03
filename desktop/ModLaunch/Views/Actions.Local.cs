using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Mods;

namespace ModLaunch.Views;

/// <summary>Моды с диска: из файлов и папок, перетаскиванием и «взять в список» вручную поставленные.</summary>
public static partial class Actions
{
    /// <summary>
    /// Поставить файлы и папки одной задачей по очереди (список модов не любит,
    /// когда в него пишут сразу несколько установок).
    /// </summary>
    public static void InstallLocal(GameState g, IReadOnlyList<string> paths)
    {
        if (g.Registry is null || NeedLoader(g)) return;
        var files = paths.Where(Installer.CanInstallLocal).ToList();
        if (files.Count == 0) { W.Toast(I18n.T("local.nothing"), bad: true); return; }
        var registry = g.Registry;
        var title = files.Count == 1 ? Path.GetFileNameWithoutExtension(files[0].TrimEnd('\\', '/')) : I18n.T("local.jobN", ("n", files.Count));
        Jobs.Run(title, g.Def.Name, async (_, progress, ct) =>
        {
            var failed = new List<string>();
            var known = 0;
            foreach (var path in files)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var record = await Installer.InstallLocal(registry, path, progress, ct);
                    if (record.Str("source") != "file") known++;
                }
                catch (Exception e) when (files.Count > 1 && e is not OperationCanceledException) { failed.Add(Path.GetFileName(path.TrimEnd('\\', '/'))); }
            }
            if (known > 0) Dispatcher.UIThread.Post(() => W.Toast(I18n.T("local.identified", ("n", known))));
            if (failed.Count > 0) throw new InvalidOperationException(I18n.T("local.failedSome", ("list", string.Join(", ", failed))));
        });
    }

    /// <summary>Взять в список моды, положенные в папку руками, и узнать их (для обновлений).</summary>
    public static void AdoptAll(GameState g, IReadOnlyList<string> folders)
    {
        if (g.Registry is null || folders.Count == 0) return;
        var registry = g.Registry;
        Jobs.Run(I18n.T("local.adoptJob", ("n", folders.Count)), g.Def.Name, async (_, progress, ct) =>
        {
            var known = 0;
            foreach (var folder in folders)
            {
                progress.Report(new InstallStep("install.extract", folder));
                var record = Installer.Adopt(registry, folder);
                try { if (await Installer.Identify(registry, record.Str("id")!, ct) is not null) known++; } catch { }
            }
            Dispatcher.UIThread.Post(() => W.Toast(known > 0
                ? I18n.T("local.adopted.known", ("n", folders.Count), ("k", known))
                : I18n.T("local.adopted", ("n", folders.Count))));
        });
    }
}
