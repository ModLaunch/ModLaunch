using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Mods;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>29.0: готовая сборка в один клик — загрузчик, сборка со всеми модами, свой профиль и запуск.</summary>
public static partial class Actions
{
    /// <summary>
    /// Поставить сборку и (по желанию) сразу играть. Нет загрузчика — ставим его сами; перед сборкой — снимок «как было»;
    /// onlyPack — выключить остальные моды (сборка рассчитана только на свои); после — профиль с именем сборки.
    /// </summary>
    public static async Task<bool> InstallModpack(GameState g, ModInfo pack, bool onlyPack, bool play)
    {
        if (g.Registry is null || g.Path is not { } path) return false;
        if (Launcher.IsRunning(g.Def.Id)) { W.Toast(I18n.T("vanilla.running"), bad: true); return false; }
        if (!g.LoaderInstalled && g.Def.Loader != Games.LoaderKind.None)
        {
            W.Toast(I18n.T("v24.share.loaderFirst", ("loader", g.Def.LoaderName)));
            var installed = await RunAndWait(g.Def.LoaderName, g.Def.Name, async (_, progress, ct) => { await Loaders.Loader.Install(g.Def, path, progress, ct); });
            g.Refresh();
            AppState.Notify();
            if (!installed || !g.LoaderInstalled) return false;
        }
        var registry = g.Registry;
        Snapshots.Take(g, "pack");
        var ok = await RunAndWait(pack.Name, g.Def.Name, async (_, progress, ct) =>
            await Installer.InstallFromCatalog(registry, pack, progress, ct));
        if (!ok) return false;
        if (onlyPack)
        {
            var keep = PackMembers(pack);
            foreach (var r in registry.List().ToList())
            {
                var id = r.Str("id") ?? "";
                if (id == "" || r.Bool("missing") || !r.Bool("enabled", true) || Installer.IsLoader(g.Def, id)) continue;
                if (keep.Contains(g.Def.CatalogId(id) ?? id) || keep.Contains(id)) continue;
                try { registry.SetEnabled(id, false); } catch { /* файл занят — останется включённым, это не страшно */ }
            }
        }
        try { Profiles.Save(g.Def.Id, Profiles.FreeName(g.Def.Id, pack.Name), registry); } catch { }
        Dispatcher.UIThread.Post(() =>
        {
            AppState.Notify();
            W.Toast(I18n.T("v29.packs.installed", ("name", pack.Name)));
            if (play) Play(g);
        });
        return true;
    }

    /// <summary>Что входит в сборку: она сама и её зависимости без версии («Ns-Name-1.2.3» → «Ns-Name»).</summary>
    internal static HashSet<string> PackMembers(ModInfo pack)
    {
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { pack.Id };
        foreach (var d in pack.Dependencies) keep.Add(Recommend.Bare(d));
        return keep;
    }
}

static class ModpackChecks
{
    [SelfTest]
    static string PackMembersStripVersions()
    {
        var pack = new ModInfo
        {
            Source = "thunderstore", Id = "Team-BestPack", Name = "Best Pack",
            Dependencies = ["BepInEx-BepInExPack-5.4.2100", "notnotnotswipez-MoreCompany-1.11.0", "x753-More_Suits-1.4.3", "Odd-Name"],
        };
        var keep = Actions.PackMembers(pack);
        foreach (var want in new[] { "Team-BestPack", "BepInEx-BepInExPack", "notnotnotswipez-MoreCompany", "x753-More_Suits", "Odd-Name" })
            if (!keep.Contains(want)) throw new Exception("missing " + want);
        if (keep.Contains("x753-More_Suits-1.4.3")) throw new Exception("version not stripped");
        return "pack + dependencies without versions are kept on";
    }
}
