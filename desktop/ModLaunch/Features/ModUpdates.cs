using System.Collections.Concurrent;
using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Mods;
using ModLaunch.Sources;

namespace ModLaunch.Features;

public sealed record ModUpdate(string RecordId, string CatalogId, string Name, string Current, string Latest, string? Icon, bool Manual);

/// <summary>
/// Обновления установленных модов: версия в списке против версии в каталоге.
/// Thunderstore и ModLinks обновляются в один клик, Nexus — через сайт.
/// </summary>
public static class ModUpdates
{
    /// <summary>Последняя проверка по играм — для значка на вкладке.</summary>
    public static readonly ConcurrentDictionary<string, List<ModUpdate>> Found = new();

    public static bool OnStart => Settings.Data.Bool("checkModUpdates", true);

    public static async Task<List<ModUpdate>> Check(GameState g, CancellationToken ct = default)
    {
        if (g.Registry is null) return [];
        g.Registry.Reconcile();
        var records = g.Registry.List().Where(r => !r.Bool("missing") && r.Str("source") is not (null or "file") && r.Str("kind") != "preset").ToList();
        var result = new ConcurrentBag<ModUpdate>();
        using var gate = new SemaphoreSlim(4);
        await Task.WhenAll(records.Select(async r =>
        {
            var recordId = r.Str("id")!;
            if (recordId.Contains('#')) return;
            var catalogId = g.Def.CatalogId(recordId);
            if (catalogId is null) return;
            await gate.WaitAsync(ct);
            try
            {
                var source = g.Def.SourceOf(recordId, r.Str("source"));
                var latest = (await Catalog.Get(g.Def, catalogId, ct, source))?.Version;
                if (Versions.IsNewer(latest, r.Str("version")))
                    result.Add(new ModUpdate(recordId, catalogId, r.Str("name") ?? recordId, r.Str("version") ?? "", latest!, r.Str("icon"), source == "nexus"));
            }
            catch { /* нет сети или мод убрали из каталога — просто не знаем */ }
            finally { gate.Release(); }
        }));
        var list = result.OrderBy(u => u.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        Found[g.Def.Id] = list;
        return list;
    }

    /// <summary>Новая версия поверх старой; выключенный мод остаётся выключенным.</summary>
    public static async Task Update(GameState g, ModUpdate update, IProgress<InstallStep> progress, CancellationToken ct)
    {
        var registry = g.Registry ?? throw new InvalidOperationException(I18n.T("err.gameNotFound"));
        if (update.Manual) throw new InvalidOperationException(I18n.T("err.updateManual"));
        var old = registry.Get(update.RecordId) ?? throw new InvalidOperationException(I18n.T("err.modNotFound", ("id", update.RecordId)));
        var mod = await Catalog.Get(g.Def, update.CatalogId, ct, g.Def.SourceOf(update.RecordId, old.Str("source"))) ?? throw new InvalidOperationException(I18n.T("err.modNotInCatalog"));
        var wasEnabled = old.Bool("enabled", true);
        if (!wasEnabled) registry.SetEnabled(update.RecordId, true);
        var oldFolder = registry.FolderFor(registry.Get(update.RecordId)!);
        try
        {
            await Installer.InstallFromCatalog(registry, mod, progress, ct, reinstall: true);
            var fresh = registry.Get(update.RecordId);
            if (fresh is not null && registry.FolderFor(fresh) != oldFolder && Directory.Exists(oldFolder)) Directory.Delete(oldFolder, true);
        }
        finally
        {
            if (!wasEnabled && registry.Has(update.RecordId)) registry.SetEnabled(update.RecordId, false);
        }
        if (Found.TryGetValue(g.Def.Id, out var list)) Found[g.Def.Id] = list.Where(u => u.RecordId != update.RecordId).ToList();
    }
}
