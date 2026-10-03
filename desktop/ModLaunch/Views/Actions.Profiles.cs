using System.IO.Compression;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Mods;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>Сборки целиком: код друга, снимок «как было», недостающие моды профиля, перенос из r2modman.</summary>
public static partial class Actions
{
    /// <summary>
    /// Поставить сборку из кода как у друга: те же версии модов (другие версии заменяются),
    /// по желанию — выключить лишние и взять настройки. Перед этим — снимок «как было».
    /// </summary>
    public static void InstallCode(GameState g, CodeProfile profile, bool withConfig, bool disableOthers)
    {
        if (g.Registry is null || NeedLoader(g)) return;
        var registry = g.Registry;
        var community = g.Def.ThunderstoreCommunity!;
        Jobs.Run(profile.Name, g.Def.Name, async (_, progress, ct) =>
        {
            Snapshots.Take(g, "code");
            var mods = profile.Mods.Where(m => !Installer.IsLoader(g.Def, m.Id)).ToList();
            var failed = new List<string>();
            for (var i = 0; i < mods.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var m = mods[i];
                if (registry.Get(m.Id) is { } have && !have.Bool("missing") && Versions.Same(have.Str("version"), m.Version)) continue;
                var step = new NumberedSteps(progress, i + 1, mods.Count, m.Id);
                try
                {
                    var setup = new SetupMod(m.Id, registry.Get(m.Id)?.Str("name") ?? m.Id[(m.Id.IndexOf('-') + 1)..].Replace('_', ' '), m.Version, "thunderstore", true);
                    if (await ModSetup.FromArchive(g, setup)) continue;
                    var info = Program.Demo ? Demo.Many(g.Def, [m.Id])[0] : await Thunderstore.Get(community, m.Id, ct) ?? throw new InvalidOperationException(I18n.T("err.modNotInCatalog"));
                    var (ns, name) = Thunderstore.Split(m.Id) ?? throw new InvalidOperationException(m.Id);
                    await Reinstall(g, m.Id, () => Installer.InstallFromCatalog(registry, info, step, ct, reinstall: true, pinVersion: m.Version,
                        pinUrl: $"https://thunderstore.io/package/download/{ns}/{name}/{m.Version}/"));
                }
                catch (Exception) when (!ct.IsCancellationRequested) { failed.Add(m.Id); }
            }
            // Включено и выключено — как у друга; лишнее выключаем, если просили.
            var wanted = mods.Select(m => new SetupMod(m.Id, m.Id, m.Version, "thunderstore", m.Enabled)).ToList();
            if (disableOthers) ModSetup.ApplyEnabled(registry, wanted);
            else foreach (var m in wanted.Where(w => registry.Has(w.Id))) try { registry.SetEnabled(m.Id, m.Enabled); } catch { }
            if (withConfig)
            {
                using var zip = new ZipArchive(new MemoryStream(profile.Zip));
                ModSetup.RestoreConfig(registry, zip);
            }
            // Свой профиль с тем же именем («Default») не затираем — новый получит номер.
            try { Profiles.Save(g.Def.Id, Profiles.FreeName(g.Def.Id, profile.Name), registry); } catch { }
            Dispatcher.UIThread.Post(AppState.Notify);
            if (failed.Count > 0) throw new InvalidOperationException(I18n.T("code.failedSome", ("list", string.Join(", ", failed.Take(8)))));
        });
    }

    /// <summary>Поставить недостающие моды и нужные версии: из архива загрузок, с Thunderstore точной версии, с Nexus — последней.</summary>
    public static void FixSetup(GameState g, string title, IReadOnlyList<SetupMod> items)
    {
        if (g.Registry is null || items.Count == 0 || NeedLoader(g)) return;
        var registry = g.Registry;
        Jobs.Run(title, g.Def.Name, async (job, progress, ct) =>
        {
            var failed = new List<string>();
            for (var i = 0; i < items.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var m = items[i];
                var step = new NumberedSteps(progress, i + 1, items.Count, m.Name);
                try
                {
                    // Из архива мод ставится включённым — выключенный в составе выключаем и тут.
                    if (await ModSetup.FromArchive(g, m)) { if (!m.Enabled) registry.SetEnabled(m.Id, false); continue; }
                    var source = g.Def.SourceOf(m.Id, m.Source);
                    var catalogId = g.Def.CatalogId(m.Id);
                    if (catalogId is null || m.Source == "file") { failed.Add(m.Name); continue; }
                    if (source == "nexus") await Reinstall(g, m.Id, () => InstallNexus(g, registry, catalogId, m.Name, null, job, step, ct, withDeps: true));
                    else
                    {
                        var info = Program.Demo ? Demo.Many(g.Def, [catalogId])[0] : await Catalog.Get(g.Def, catalogId, ct, source) ?? throw new InvalidOperationException(I18n.T("err.modNotInCatalog"));
                        string? url = source == "thunderstore" && m.Version != "" && Thunderstore.Split(catalogId) is var (ns, name) ? $"https://thunderstore.io/package/download/{ns}/{name}/{m.Version}/" : null;
                        await Reinstall(g, m.Id, () => Installer.InstallFromCatalog(registry, info, step, ct, reinstall: true, pinVersion: url is null ? null : m.Version, pinUrl: url));
                    }
                    if (registry.Has(m.Id) && !m.Enabled) registry.SetEnabled(m.Id, false);
                }
                catch (Exception) when (!ct.IsCancellationRequested) { failed.Add(m.Name); }
            }
            Dispatcher.UIThread.Post(AppState.Notify);
            if (failed.Count > 0) throw new InvalidOperationException(I18n.T("code.failedSome", ("list", string.Join(", ", failed.Take(8)))));
        });
    }

    /// <summary>Другая версия уже стоит — ставим поверх так же, как обновление: старая папка не остаётся рядом.</summary>
    static Task Reinstall(GameState g, string id, Func<Task> install) =>
        g.Registry?.Has(id) == true ? ModUpdates.Replace(g, id, install) : install();

    /// <summary>Вернуть снимок: включённые моды и настройки сразу, недостающее и другие версии — задачей.</summary>
    public static void RestoreSnapshot(GameState g, Snapshot snapshot)
    {
        if (g.Registry is not { } registry) return;
        if (Launcher.IsRunning(g.Def.Id)) { W.Toast(I18n.T("vanilla.running"), bad: true); return; }
        List<SetupMod> mods;
        try { mods = Snapshots.Mods(snapshot); }
        catch (Exception e) { W.Toast(Jobs.Explain(e), bad: true); return; }
        // Сам возврат тоже можно отменить — делаем снимок и перед ним.
        Snapshots.Take(g, "restore");
        ModSetup.ApplyEnabled(registry, mods);
        if (Snapshots.ConfigZip(snapshot) is { } zip) ModSetup.RestoreConfig(registry, zip);
        var (missing, other) = ModSetup.Differences(registry, mods);
        var fix = missing.Concat(other).ToList();
        if (fix.Count > 0) FixSetup(g, I18n.T("snap.job"), fix);
        W.Toast(fix.Count > 0 ? I18n.T("snap.restored.fix", ("n", fix.Count)) : I18n.T("snap.restored"));
        AppState.Notify();
    }

    /// <summary>Перенести профиль r2modman / Gale одной задачей.</summary>
    public static void ImportManager(GameState g, ManagerProfile profile)
    {
        if (g.Registry is null || NeedLoader(g)) return;
        Jobs.Run($"{profile.Manager}: {profile.Name}", g.Def.Name, (_, progress, _) =>
        {
            var (copied, missing) = ManagerImport.Import(g, profile, progress);
            Dispatcher.UIThread.Post(() =>
            {
                W.Toast(I18n.T("mgr.done", ("n", copied), ("name", profile.Name)));
                AppState.Notify();
            });
            // Моды без папки (профиль не докачан) — ставим с Thunderstore нужной версии.
            if (missing.Count > 0)
                Dispatcher.UIThread.Post(() => FixSetup(g, profile.Name, profile.Mods.Where(m => missing.Contains(m.Id))
                    .Select(m => new SetupMod(m.Id, m.Id[(m.Id.IndexOf('-') + 1)..].Replace('_', ' '), m.Version, "thunderstore", m.Enabled)).ToList()));
            return Task.CompletedTask;
        });
    }
}
