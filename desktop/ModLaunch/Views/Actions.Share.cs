using System.IO.Compression;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Minecraft;
using ModLaunch.Mods;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>24.0: сборка по коду друга — для любой игры (Nexus, Thunderstore, ModLinks, Hub) и для Minecraft.</summary>
public static partial class Actions
{
    /// <summary>
    /// Поставить сборку из кода: недостающее и другие версии — те же, что у друга (Thunderstore и Nexus — точная версия,
    /// ModLinks — последняя, другой там нет), включено и выключено — как у друга, по желанию — его настройки.
    /// Перед этим — снимок «как было». Задача завершается, когда сборка встала (true) или нет.
    /// </summary>
    public static async Task<bool> InstallShared(GameState g, SharePack pack, bool withConfig, bool disableOthers)
    {
        if (g.Registry is null || g.Path is not { } path) return false;
        if (Launcher.IsRunning(g.Def.Id)) { W.Toast(I18n.T("vanilla.running"), bad: true); return false; }
        // Новичок пришёл по коду друга, а SMAPI / BepInEx / Modding API ещё нет — ставим загрузчик сами
        // и сразу продолжаем со сборкой (раньше код терялся: окно «нужен загрузчик» и всё сначала).
        if (!g.LoaderInstalled && g.Def.Loader != Games.LoaderKind.None)
        {
            W.Toast(I18n.T("v24.share.loaderFirst", ("loader", g.Def.LoaderName)));
            var installed = await RunAndWait(g.Def.LoaderName, g.Def.Name, async (_, progress, ct) => { await Loaders.Loader.Install(g.Def, path, progress, ct); });
            g.Refresh();
            AppState.Notify();
            if (!installed || !g.LoaderInstalled) return false;
        }
        var registry = g.Registry;
        var wanted = pack.Mods.Where(m => !Installer.IsLoader(g.Def, m.Id)).ToList();
        W.Toast(I18n.T("v24.share.installing", ("name", pack.Name)));
        return await RunAndWait(pack.Name, g.Def.Name, async (job, progress, ct) =>
        {
            Snapshots.Take(g, "code");
            var (missing, other) = ModSetup.Differences(registry, wanted.Select(m => m.Setup));
            var todo = missing.Concat(other).ToList();
            var failed = new List<string>();
            for (var i = 0; i < todo.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var m = todo[i];
                var share = wanted.First(w => w.Id == m.Id);
                var source = g.Def.SourceOf(m.Id, m.Source);
                // Точную версию можно взять не везде: у ModLinks есть только последняя, у Nexus — если знаем файл.
                var exact = source == "thunderstore" || source == "nexus" && share.FileId > 0;
                if (!exact && registry.Get(m.Id) is { } have && !have.Bool("missing") && !Versions.IsNewer(m.Version, have.Str("version"))) continue;
                var step = new NumberedSteps(progress, i + 1, todo.Count, m.Name);
                try
                {
                    if (await ModSetup.FromArchive(g, m)) continue;
                    var catalogId = g.Def.CatalogId(m.Id);
                    if (catalogId is null || m.Source == "file") { failed.Add(m.Name); continue; }
                    if (source == "nexus")
                    {
                        var pin = share.FileId > 0 ? new Pin(share.FileId, m.Version, null) : null;
                        await Reinstall(g, m.Id, () => InstallNexus(g, registry, catalogId, m.Name, pin, job, step, ct, withDeps: true));
                    }
                    else
                    {
                        var info = Program.Demo ? Demo.Many(g.Def, [catalogId])[0] : await Catalog.Get(g.Def, catalogId, ct, source) ?? throw new InvalidOperationException(I18n.T("err.modNotInCatalog"));
                        string? url = source == "thunderstore" && m.Version != "" && Thunderstore.Split(catalogId) is var (ns, name) ? $"https://thunderstore.io/package/download/{ns}/{name}/{m.Version}/" : null;
                        await Reinstall(g, m.Id, () => Installer.InstallFromCatalog(registry, info, step, ct, reinstall: true, pinVersion: url is null ? null : m.Version, pinUrl: url));
                    }
                }
                catch (Exception) when (!ct.IsCancellationRequested) { failed.Add(m.Name); }
            }
            // Включено и выключено — как у друга; лишнее выключаем, если просили.
            var setups = wanted.Select(m => m.Setup).ToList();
            if (disableOthers) ModSetup.ApplyEnabled(registry, setups);
            else foreach (var m in setups.Where(s => registry.Has(s.Id))) try { registry.SetEnabled(m.Id, m.Enabled); } catch { }
            if (withConfig && pack.Config is { Length: > 0 } config)
                try { using var zip = new ZipArchive(new MemoryStream(config)); ModSetup.RestoreConfig(registry, zip, fromCode: true); } catch { }
            // Свой профиль с тем же именем не затираем — новый получит номер.
            try { Profiles.Save(g.Def.Id, Profiles.FreeName(g.Def.Id, pack.Name), registry); } catch { }
            Dispatcher.UIThread.Post(() =>
            {
                AppState.Notify();
                if (failed.Count == 0) { W.Toast(I18n.T("v24.share.installed", ("name", pack.Name))); Premium.Count("codesUsed"); }
            });
            if (failed.Count > 0) throw new InvalidOperationException(I18n.T("code.failedSome", ("list", string.Join(", ", failed.Take(8)))));
        });
    }

    /// <summary>Сборка Minecraft из кода — новая сборка (свою не трогаем), как модпак Modrinth.</summary>
    public static void InstallSharedMc(SharePack pack)
    {
        W.Toast(I18n.T("mine.pack.started", ("title", pack.Name)));
        Jobs.Run(pack.Name, "Minecraft", async (_, progress, ct) =>
        {
            var file = ShareCode.WriteMrpack(pack);
            try
            {
                var i = Program.Demo ? McDemo.PackInstance(new MrProject("demo", "demo", pack.Name, "", "", null, 0, 0, "modpack", [], [], [], null, null))
                    : await McModrinth.ImportPack(file, pack.Name, progress, ct);
                Dispatcher.UIThread.Post(() =>
                {
                    Mc.Active = i;
                    W.Toast(I18n.T("mine.pack.done", ("title", i.Name)));
                    Mc.Notify();
                    W.Navigate(() => new MinecraftPage("mods"));
                });
            }
            finally { try { File.Delete(file); } catch { } }
        });
    }
}
