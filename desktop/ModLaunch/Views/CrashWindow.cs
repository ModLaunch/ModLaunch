using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Minecraft;
using ModLaunch.Loaders;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>
/// 23.9: окно «Почему игра упала». Причины простыми словами, под каждой — что сделает «Исправить»;
/// строка из лога спрятана под «Подробнее». Перед исправлением — снимок «как было».
/// </summary>
public sealed partial class MainWindow
{
    public void ShowWhy(GameState g, CrashReport report, long ms)
    {
        var body = Ui.Col(12, Ui.Text(I18n.T("v23.lead", ("time", PlayControls.Clock(TimeSpan.FromMilliseconds(ms)))), "muted", wrap: true));
        foreach (var c in report.Causes) body.Children.Add(CauseCard(c));
        if (report.Fixable && !g.Def.IsMinecraft)
        {
            var safe = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
            safe.Children.Add(Ui.Icon(Icons.Refresh, 14, Ui.Res("Muted")));
            var note = Ui.Text(I18n.T("v23.safe"), "small muted", wrap: true);
            Grid.SetColumn(note, 1);
            safe.Children.Add(note);
            body.Children.Add(safe);
        }

        var actions = new List<Control> { Ui.Button(I18n.T("common.close"), CloseDialog) };
        actions.Add(Ui.Button(I18n.T("crash.log"), () => { CloseDialog(); OpenLog(g); }, "ghost", Icons.Alert));
        if (report.Fixable)
            actions.Add(Ui.Button(I18n.T("v23.fix"), () => { CloseDialog(); _ = FixCrash(g, report); }, "primary", Icons.Check));
        else if (!g.Def.IsMinecraft && g.Path is not null && Vanilla.Supported(g.Def, g.Path))
            actions.Add(Ui.Button(I18n.T("v23.vanilla"), () => { CloseDialog(); Actions.Play(g, vanilla: true); }, "primary", Icons.Play));
        Dialog(I18n.T("v23.title", ("game", g.Def.Name)), new ScrollViewer { Content = body, MaxHeight = 460 }, 600, actions.ToArray());
    }

    void OpenLog(GameState g)
    {
        if (g.Def.IsMinecraft) Navigate(() => new MinecraftPage("log"));
        else Navigate(() => new GamePage(g.Def.Id, "log"));
    }

    static Control CauseCard(CrashCause c)
    {
        var (icon, color) = c.Kind switch
        {
            "missing" => (Icons.Download, Ui.Res("Brand2")),
            "outdated" => (Icons.ArrowUp, Ui.Res("Warn")),
            "loader" => (Icons.Refresh, Ui.Res("Warn")),
            "memory" => (Icons.Cube, Ui.Res("Warn")),
            "unknown" => (Icons.Info, Ui.Res("Muted")),
            _ => (Icons.Alert, Ui.Res("Bad")),
        };
        var col = Ui.Col(5, Ui.Row(9, Ui.Icon(icon, 16, color), Ui.Text(c.Title, "h3", wrap: true)));
        if (c.Fix != "") col.Children.Add(Ui.Row(7, Ui.Icon(Icons.Check, 13, Ui.Res("Good")), Ui.Text(c.Fix, "small", wrap: true)));
        else if (c.Kind == "unknown") col.Children.Add(Ui.Text(I18n.T("v23.why.unknown.text"), "small muted", wrap: true));
        if (c.Line != "")
        {
            // Строка лога — по нажатию: новичку она не нужна, а тому, кто спросит на форуме, пригодится.
            var line = new TextBlock { Text = c.Line, FontFamily = new FontFamily("Consolas, monospace"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Res("Muted"), IsVisible = false };
            var more = new TextBlock { Text = I18n.T("v23.details") + " ▾", FontSize = 12, Foreground = Ui.Res("Brand2"), Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
            more.PointerPressed += (_, _) => { line.IsVisible = !line.IsVisible; more.Text = I18n.T("v23.details") + (line.IsVisible ? " ▴" : " ▾"); };
            col.Children.Add(more);
            col.Children.Add(line);
        }
        return new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 11), Child = col };
    }

    /// <summary>«Исправить»: всё, что предложено, по порядку. Потом — «Играть» или «Вернуть как было».</summary>
    async Task FixCrash(GameState g, CrashReport report)
    {
        if (Features.Launcher.IsRunning(g.Def.Id)) { Toast(I18n.T("vanilla.running"), bad: true); return; }
        var done = new List<string>();
        var snapped = false;
        if (!g.Def.IsMinecraft && g.Registry is not null) { Snapshots.Take(g, "crashfix"); snapped = true; }
        var mc = g.Def.IsMinecraft ? Mc.Active : null;
        try
        {
            foreach (var c in report.Causes.Where(c => c.Fixable))
            {
                switch (c.Kind)
                {
                    case "missing":
                        if (await InstallNeeds(g, mc, c.Needs) is > 0 and var n) done.Add(I18n.T("v23.done.installed", ("n", n)));
                        break;
                    case "outdated" or "broken" when c.Update is not null:
                        await Actions.UpdateMods(g, [c.Update]);
                        done.Add(I18n.T("v23.done.updated", ("mod", c.ModName ?? ""), ("version", c.Update.Latest)));
                        break;
                    case "outdated" or "broken" when c.McUpdate is not null && mc is not null:
                        await Task.Run(() => McModrinth.Update(mc, c.McUpdate, null, CancellationToken.None));
                        done.Add(I18n.T("v23.done.updated", ("mod", c.ModName ?? ""), ("version", c.McUpdate.Latest.Number)));
                        break;
                    case "outdated" or "broken":
                        if (c.ModId is not null) g.Registry?.SetEnabled(c.ModId, false);
                        else if (c.McItem is not null && mc is not null) McContent.SetEnabled(mc, c.McItem, false);
                        done.Add(I18n.T("v23.done.disabled", ("mod", c.ModName ?? "")));
                        break;
                    case "loader" when g.Path is not null:
                        var path = g.Path;
                        await Task.Run(() => Loader.Install(g.Def, path, null, CancellationToken.None));
                        g.Refresh();
                        done.Add(I18n.T("v23.done.loader", ("loader", g.Def.LoaderName)));
                        break;
                    case "memory" when mc is not null:
                        mc.MemoryMb = c.Memory;
                        Mc.Save(mc);
                        done.Add(I18n.T("v23.done.memory", ("to", c.Memory / 1024)));
                        break;
                    case "recent":
                        var off = 0;
                        foreach (var id in c.Recent)
                        {
                            if (id.StartsWith("mc:") && mc is not null && McContent.List(mc, "mod").FirstOrDefault(x => "mc:" + x.Key == id) is { } item) { McContent.SetEnabled(mc, item, false); off++; }
                            else if (g.Registry?.Has(id) == true) { g.Registry.SetEnabled(id, false); off++; }
                        }
                        done.Add(I18n.T("v23.done.recent", ("n", off)));
                        break;
                }
            }
        }
        catch (Exception e) { Toast(Jobs.Explain(e), bad: true); }
        if (mc is not null) Mc.Notify();
        AppState.Notify();
        var list = Ui.Col(6);
        foreach (var d in done) list.Children.Add(Ui.Row(8, Ui.Icon(Icons.Check, 14, Ui.Res("Good")), Ui.Text(d, "", wrap: true)));
        if (done.Count == 0) list.Children.Add(Ui.Text(I18n.T("v23.done.none"), "muted", wrap: true));
        var buttons = new List<Control> { Ui.Button(I18n.T("common.close"), CloseDialog) };
        if (snapped && Snapshots.List(g.Def.Id).FirstOrDefault() is { } snap)
            buttons.Add(Ui.Button(I18n.T("snap.back"), () => { CloseDialog(); Actions.RestoreSnapshot(g, snap); }, "ghost", Icons.Refresh));
        buttons.Add(Ui.Button(I18n.T("v23.play"), () => { CloseDialog(); Actions.Play(g); }, "primary", Icons.Play));
        Dialog(I18n.T("v23.done.title"), list, 520, buttons.ToArray());
    }

    /// <summary>Поставить недостающее: по зависимостям из манифестов (у Stardew — через smapi.io) и по имени из каталога.</summary>
    static async Task<int> InstallNeeds(GameState g, McInstance? mc, List<string> needs)
    {
        if (mc is not null)
        {
            var n = 0;
            foreach (var need in needs)
            {
                try
                {
                    MrProject? p = null;
                    try { p = await McModrinth.Project(need); } catch { }
                    p ??= (await McModrinth.Search(CrashDoctor.Pretty(need), "mod", "relevance", 0, mc)).Hits.FirstOrDefault();
                    if (p is null) continue;
                    await Task.Run(() => McModrinth.Install(mc, p));
                    n++;
                }
                catch (Exception e) { Guard.Log(e); }
            }
            return n;
        }
        if (g.Registry is null) return 0;
        var ids = new List<string>();
        try { ids.AddRange((await Deps.FindResolved(g.Registry)).Where(m => m.ResolveId is not null).Select(m => m.ResolveId!)); } catch { }
        var mods = new List<ModInfo>();
        try { mods.AddRange(Program.Demo ? Demo.Many(g.Def, ids) : await Catalog.Many(g.Def, ids.Distinct())); } catch { }
        // Чего нет в манифестах (Hollow Knight называет сборку, а не мод): ищем по имени в каталоге игры.
        foreach (var need in needs.Select(CrashDoctor.Pretty))
        {
            if (mods.Any(m => m.Name.Equals(need, StringComparison.OrdinalIgnoreCase) || m.Id.EndsWith("-" + need, StringComparison.OrdinalIgnoreCase))) continue;
            try { if (await Catalog.Get(g.Def, need) is { } m) mods.Add(m); } catch { }
        }
        var list = mods.DistinctBy(m => m.Id).Where(m => !Actions.IsInstalled(g, m.Id)).ToList();
        if (list.Count == 0) return 0;
        await Actions.InstallQueue(g, I18n.T("deps.install"), list.Select(m => (m, (Pin?)null)).ToList());
        return list.Count(m => Actions.IsInstalled(g, m.Id));
    }
}
