using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Mods;
using ModLaunch.Sources;

namespace ModLaunch.Features;

/// <summary>Найденное обновление. Manual — мод с Nexus без Premium: файл придётся взять через браузер.</summary>
public sealed record ModUpdate(string RecordId, string CatalogId, string Name, string Current, string Latest, string? Icon, bool Manual, string Source = "");

/// <summary>
/// Обновления установленных модов: версия в списке против версии в каталоге.
/// Thunderstore и ModLinks обновляются в один клик, Nexus — напрямую с Premium или через браузер.
/// Моды с отметкой «не обновлять» пропускаются.
/// </summary>
public static class ModUpdates
{
    /// <summary>Последняя проверка по играм — для значка на вкладке.</summary>
    public static readonly ConcurrentDictionary<string, List<ModUpdate>> Found = new();

    public static bool OnStart => Settings.Data.Bool("checkModUpdates", true);

    /// <summary>Обновлять моды сами после проверки при запуске программы.</summary>
    public static bool Auto => Settings.Data.Bool("autoUpdateMods", false);

    /// <summary>Перед «Играть» проверить и поставить обновления.</summary>
    public static bool BeforePlay => Settings.Data.Bool("updateBeforePlay", false);

    /// <summary>Моды Nexus качаются сами только с Premium, иначе — через страницу мода.</summary>
    static bool NexusDirect => !string.IsNullOrEmpty(Settings.NexusApiKey) && Settings.NexusPremium;

    /// <summary>Какие записи вообще проверять: с каталогом, на месте, не пресеты и не закреплённые.</summary>
    public static List<JsonObject> Candidates(ModRegistry registry) =>
        registry.List().Where(r => !r.Bool("missing") && !r.Bool("hold") && r.Str("source") is not (null or "file") && r.Str("kind") != "preset"
            && r.Str("id") is { } id && !id.Contains('#')).ToList();

    public static async Task<List<ModUpdate>> Check(GameState g, CancellationToken ct = default)
    {
        // У Minecraft свои обновления — по сборке (Modrinth, по хешам файлов): в фоне, список — на странице Minecraft.
        if (g.Def.IsMinecraft)
        {
            if (Minecraft.Mc.Active is { } a && !Program.Demo)
                await Task.Run(async () => { await Minecraft.McContent.Identify(a, ct); await Minecraft.McContent.CheckUpdates(a, ct); }, ct);
            return [];
        }
        if (g.Registry is null) return [];
        g.Registry.Reconcile();
        var records = Candidates(g.Registry);
        var result = new ConcurrentBag<ModUpdate>();
        using var gate = new SemaphoreSlim(4);
        await Task.WhenAll(records.Select(async r =>
        {
            var recordId = r.Str("id")!;
            var catalogId = g.Def.CatalogId(recordId);
            if (catalogId is null) return;
            await gate.WaitAsync(ct);
            try
            {
                var source = g.Def.SourceOf(recordId, r.Str("source"));
                var latest = (await Catalog.Get(g.Def, catalogId, ct, source))?.Version;
                if (Versions.IsNewer(latest, r.Str("version")))
                    result.Add(new ModUpdate(recordId, catalogId, r.Str("name") ?? recordId, r.Str("version") ?? "", latest!, r.Str("icon"), source == "nexus" && !NexusDirect, source));
            }
            catch { /* нет сети или мод убрали из каталога — просто не знаем */ }
            finally { gate.Release(); }
        }));
        var list = result.OrderBy(u => u.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        Found[g.Def.Id] = list;
        return list;
    }

    /// <summary>Уже стоит нужная версия (например, обновилась вместе с другим модом как зависимость).</summary>
    public static bool AlreadyDone(GameState g, ModUpdate update) =>
        g.Registry?.Get(update.RecordId) is { } r && !Versions.IsNewer(update.Latest, r.Str("version"));

    /// <summary>Каталожный мод (Thunderstore, ModLinks, Hub): новая версия поверх старой, заодно — устаревшие зависимости.</summary>
    public static Task Update(GameState g, ModUpdate update, IProgress<InstallStep> progress, CancellationToken ct)
    {
        var registry = g.Registry ?? throw new InvalidOperationException(I18n.T("err.gameNotFound"));
        if (update.Source == "nexus") throw new InvalidOperationException(I18n.T("err.updateManual"));
        return Replace(g, update.RecordId, async () =>
        {
            var old = registry.Get(update.RecordId)!;
            var mod = await Catalog.Get(g.Def, update.CatalogId, ct, g.Def.SourceOf(update.RecordId, old.Str("source"))) ?? throw new InvalidOperationException(I18n.T("err.modNotInCatalog"));
            await Installer.InstallFromCatalog(registry, mod, progress, ct, reinstall: true, refreshDeps: true);
        });
    }

    /// <summary>
    /// Заменить мод новой версией (install ставит её): выключенный мод остаётся выключенным,
    /// старая папка убирается, если новая версия легла в другую.
    /// </summary>
    public static async Task Replace(GameState g, string recordId, Func<Task> install)
    {
        var registry = g.Registry ?? throw new InvalidOperationException(I18n.T("err.gameNotFound"));
        var old = registry.Get(recordId) ?? throw new InvalidOperationException(I18n.T("err.modNotFound", ("id", recordId)));
        var wasEnabled = old.Bool("enabled", true);
        if (!wasEnabled) registry.SetEnabled(recordId, true);
        var oldFolder = registry.OwnFolder(registry.Get(recordId)!);
        try
        {
            await install();
            var fresh = registry.Get(recordId);
            if (oldFolder is not null && fresh is not null && registry.FolderFor(fresh) != oldFolder && Directory.Exists(oldFolder)) Directory.Delete(oldFolder, true);
        }
        finally
        {
            if (!wasEnabled && registry.Has(recordId)) registry.SetEnabled(recordId, false);
        }
        Forget(g.Def.Id, recordId);
    }

    /// <summary>Убрать обновление из найденных (поставили или отметили «не обновлять»).</summary>
    public static void Forget(string gameId, string recordId)
    {
        if (Found.TryGetValue(gameId, out var list)) Found[gameId] = list.Where(u => u.RecordId != recordId).ToList();
    }

    /// <summary>«Не обновлять»: мод больше не проверяется, пока отметку не снимут.</summary>
    public static void Hold(GameState g, string recordId, bool hold)
    {
        g.Registry?.SetHold(recordId, hold);
        if (hold) Forget(g.Def.Id, recordId);
    }

    [SelfTest]
    static string HeldModsAreSkipped()
    {
        var dir = Path.Combine(Paths.DataDir, "updates-test", "Lethal Company");
        Directory.CreateDirectory(Path.Combine(dir, "BepInEx", "plugins", "LethalLib"));
        Directory.CreateDirectory(Path.Combine(dir, "BepInEx", "plugins", "MoreCompany"));
        var registry = new ModRegistry(GameCatalog.ById("lethal-company")!, dir);
        registry.Add(new JsonObject { ["id"] = "Evaisa-LethalLib", ["name"] = "LethalLib", ["folder"] = "LethalLib", ["source"] = "thunderstore", ["version"] = "0.15.0" });
        registry.Add(new JsonObject { ["id"] = "notnotnotswipez-MoreCompany", ["name"] = "MoreCompany", ["folder"] = "MoreCompany", ["source"] = "thunderstore", ["version"] = "1.9.0" });
        registry.SetHold("notnotnotswipez-MoreCompany", true);
        // Переустановка не снимает отметку.
        registry.Add(new JsonObject { ["id"] = "notnotnotswipez-MoreCompany", ["name"] = "MoreCompany", ["folder"] = "MoreCompany", ["source"] = "thunderstore", ["version"] = "1.9.1" });
        var ids = Candidates(registry).Select(r => r.Str("id")).ToList();
        if (ids.Contains("notnotnotswipez-MoreCompany")) throw new Exception("held mod is still checked");
        if (!ids.Contains("Evaisa-LethalLib")) throw new Exception("normal mod is not checked");
        if (registry.OwnFolder(new JsonObject { ["id"] = "x" }) is not null) throw new Exception("record without folder points at the mods folder");
        return "MoreCompany is held (even after reinstall), LethalLib is checked";
    }
}
