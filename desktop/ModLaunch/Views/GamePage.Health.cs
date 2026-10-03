using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>Вкладка «Проверка»: что не так с игрой и кнопка «Исправить» у каждой проблемы.</summary>
public sealed partial class GamePage
{
    Control HealthView()
    {
        var issues = Health.Check(_g);
        var col = Ui.Col(12);
        var serious = issues.Count(i => i.Level != HealthLevel.Info);

        // Сводка: большая галочка или «нашли N проблем».
        var ok = serious == 0;
        var mark = new Border
        {
            Width = 52, Height = 52, CornerRadius = new CornerRadius(26),
            Background = ok ? new SolidColorBrush(Color.FromArgb(40, 61, 214, 140)) : new SolidColorBrush(Color.FromArgb(40, 242, 184, 75)),
            Child = Ui.Icon(ok ? Icons.Check : Icons.Alert, 24, ok ? Ui.Res("Good") : Ui.Res("Warn")),
        };
        var words = Ui.Col(4,
            Ui.Text(ok ? I18n.T("health.ok") : I18n.T("health.found." + I18n.Plural(serious, "one", "few", "many"), ("n", serious)), "h2"),
            Ui.Text(ok ? I18n.T(Features.Vanilla.IsPurged(_g.Def.Id) ? "health.ok.purged" : "health.ok.text", ("game", _g.Def.Name)) : I18n.T("health.found.text"), "muted", wrap: true));
        words.VerticalAlignment = VerticalAlignment.Center;
        var again = Ui.Button(I18n.T("health.again"), () => { _missing = null; Build(); MainWindow.Current?.Toast(I18n.T("health.checked")); }, "", Icons.Refresh);
        again.VerticalAlignment = VerticalAlignment.Center;
        var summary = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 16 };
        summary.Children.Add(mark);
        Grid.SetColumn(words, 1);
        summary.Children.Add(words);
        var report = Ui.Button(I18n.T("report.copy"), () => { _ = TopLevel.GetTopLevel(MainWindow.Current)?.Clipboard?.SetTextAsync(Report.Build(_g)); MainWindow.Current?.Toast(I18n.T("report.copied")); }, "ghost", Icons.List);
        report.VerticalAlignment = VerticalAlignment.Center;
        var tools = Ui.Row(8, report, again);
        tools.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(tools, 2);
        summary.Children.Add(tools);
        col.Children.Add(Ui.Card(summary, 20));

        foreach (var issue in issues) col.Children.Add(IssueRow(issue));

        col.Children.Add(CleanGameCard());
        return col;
    }

    Control IssueRow(HealthIssue issue)
    {
        var color = issue.Level switch { HealthLevel.Critical => Ui.Res("Bad"), HealthLevel.Warning => Ui.Res("Warn"), _ => Ui.Res("Brand2") };
        var icon = issue.Level == HealthLevel.Info ? Icons.Info : Icons.Alert;
        var badge = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(10), Background = Ui.Res("Surface2"), Child = Ui.Icon(icon, 18, color), VerticalAlignment = VerticalAlignment.Top };
        var level = ModRow.Tag(I18n.T("health.level." + issue.Level.ToString().ToLowerInvariant()), Ui.Res("Surface3"), color);
        level.VerticalAlignment = VerticalAlignment.Center;
        var words = Ui.Col(4, Ui.Row(8, Ui.Text(issue.Title, "h3"), level));
        if (issue.Text != "") words.Children.Add(Ui.Text(issue.Text, "small muted", wrap: true));
        var fixes = Ui.Row(8, Fixes(issue).ToArray());
        fixes.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 14 };
        grid.Children.Add(badge);
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);
        Grid.SetColumn(fixes, 2);
        grid.Children.Add(fixes);
        return new Border { Classes = { "card" }, Padding = new Thickness(16, 14), Child = grid };
    }

    /// <summary>Кнопки «Исправить» для каждого вида проблемы.</summary>
    IEnumerable<Control> Fixes(HealthIssue issue)
    {
        var w = MainWindow.Current!;
        var registry = _g.Registry;
        switch (issue.Kind)
        {
            case "loader":
                yield return Ui.Button(I18n.T("games.installLoader", ("loader", _g.Def.LoaderName)), () => Actions.InstallLoader(_g), "primary", Icons.Download);
                break;
            case "purged":
                yield return Ui.Button(I18n.T("vanilla.restore"), RestorePurge, "primary", Icons.Refresh);
                break;
            case "write":
                yield return Ui.Button(I18n.T("games.openFolder"), () => Actions.OpenFolder(_g.Path), "", Icons.Folder);
                break;
            case "deps":
                yield return Ui.Button(I18n.T("deps.install"), () => _ = Actions.InstallMissing(_g), "primary", Icons.Download);
                break;
            case "depsOff":
                yield return Ui.Button(I18n.T("health.fix.enable"), () => SetMany(issue.Mods, true), "primary", Icons.Power);
                break;
            case "files":
                yield return Ui.Button(I18n.T("health.fix.forget"), () =>
                {
                    // Remove удаляет папку мода, если она есть. Папку могли вернуть на место после проверки —
                    // смотрим заново и убираем из списка только то, чего правда нет.
                    try { registry?.Reconcile(); } catch { }
                    foreach (var id in issue.Mods)
                        if (registry?.Get(id) is { } mod && mod.Bool("missing")) try { registry.Remove(id); } catch { }
                    w.Toast(I18n.T("health.fixed"));
                    AppState.Notify();
                }, "", Icons.Trash);
                break;
            case "conflicts" when issue.Mods.Count > 0:
                yield return Ui.Button(I18n.T("health.fix.duplicates"), () => SetMany(issue.Mods, false), "primary", Icons.Check);
                break;
            case "log":
                yield return Ui.Button(I18n.T("crash.log"), () => w.Navigate(() => new GamePage(_g.Def.Id, "log")), "", Icons.Alert);
                if (issue.Mods.Count > 0) yield return Ui.Button(I18n.T("health.fix.disable"), () => SetMany(issue.Mods, false), "primary", Icons.Power);
                break;
            case "crash":
                yield return Ui.Button(I18n.T("crash.log"), () => w.Navigate(() => new GamePage(_g.Def.Id, "log")), "", Icons.Alert);
                break;
            case "unmanaged" when registry is not null:
                yield return Ui.Button(I18n.T("local.adopt"), () => Actions.AdoptAll(_g, registry.Unmanaged()), "primary", Icons.Plus);
                break;
            case "unused" when registry is not null:
                yield return Ui.Button(I18n.T("deps.orphans.review"), () => OfferOrphans(registry, DepGraph.Build(registry).Unused()), "", Icons.Trash);
                break;
            case "updates":
                yield return Ui.Button(I18n.T("health.fix.show"), () => w.Navigate(() => new GamePage(_g.Def.Id, "installed")), "", Icons.List);
                break;
        }
    }

    void SetMany(List<string> ids, bool on)
    {
        var registry = _g.Registry;
        if (registry is null) return;
        var n = 0;
        foreach (var id in ids)
            try { registry.SetEnabled(id, on); n++; } catch { }
        MainWindow.Current?.Toast(I18n.T(on ? "health.enabledN" : "health.disabledN", ("n", n)));
        _missing = null;
        AppState.Notify();
    }

    /// <summary>«Чистая игра»: запустить без модов или убрать моды совсем (и вернуть потом).</summary>
    Control CleanGameCard()
    {
        var supported = _g.Path is not null && Vanilla.Supported(_g.Def, _g.Path);
        var purged = Vanilla.IsPurged(_g.Def.Id);
        var buttons = Ui.Row(10);
        if (supported && !purged) buttons.Children.Add(Ui.Button(I18n.T("vanilla.play"), () => Actions.Play(_g, vanilla: true), "", Icons.Play));
        buttons.Children.Add(purged
            ? Ui.Button(I18n.T("vanilla.restore"), RestorePurge, "primary", Icons.Refresh)
            : Ui.Button(I18n.T("vanilla.purge"), ConfirmPurge, "", Icons.EyeOff));
        return Ui.Card(Ui.Col(12,
            Ui.Row(10, Ui.Icon(Icons.Shield, 18, Ui.Res("Muted")), Ui.Text(I18n.T("vanilla.title"), "h3")),
            Ui.Text(I18n.T(purged ? "vanilla.purged.text" : "vanilla.text"), "muted", wrap: true),
            buttons), 20);
    }

    /// <summary>Спросить и очистить игру: все моды выключаются, загрузчик откладывается.</summary>
    public void ConfirmPurge() => ConfirmPurge(_g);

    public static void ConfirmPurge(GameState g)
    {
        var w = MainWindow.Current!;
        if (Launcher.IsRunning(g.Def.Id)) { w.Toast(I18n.T("vanilla.running"), bad: true); return; }
        var n = g.Registry?.List().Count(m => m.Bool("enabled", true) && !m.Bool("missing")) ?? 0;
        w.Dialog(I18n.T("vanilla.purge.title", ("game", g.Def.Name)),
            Ui.Col(10,
                Ui.Text(I18n.T("vanilla.purge.text", ("n", n), ("loader", g.Def.LoaderName)), "muted", wrap: true),
                Ui.Row(8, Ui.Icon(Icons.Shield, 15, Ui.Res("Good")), Ui.Text(I18n.T("vanilla.purge.safe"), "small muted", wrap: true))),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("vanilla.purge"), () =>
            {
                w.CloseDialog();
                try
                {
                    var off = Vanilla.Purge(g);
                    w.Toast(I18n.T("vanilla.purged", ("n", off)));
                }
                catch (Exception e) { w.Toast(Jobs.Explain(e), bad: true); }
                AppState.Notify();
            }, "primary", Icons.EyeOff));
    }

    void RestorePurge() => RestorePurge(_g);

    public static void RestorePurge(GameState g)
    {
        var w = MainWindow.Current!;
        if (Launcher.IsRunning(g.Def.Id)) { w.Toast(I18n.T("vanilla.running"), bad: true); return; }
        try
        {
            var on = Vanilla.RestorePurge(g);
            w.Toast(I18n.T("vanilla.restored", ("n", on)));
        }
        catch (Exception e) { w.Toast(Jobs.Explain(e), bad: true); }
        AppState.Notify();
    }

    /// <summary>Снимки: проверка с проблемами, окно перед запуском, очистка и «всё в порядке».</summary>
    [DemoShots]
    static void HealthShots(Shots s)
    {
        var w = s.Window;
        var sub = AppState.Game("subnautica");
        var registry = sub.Registry!;
        // Карте нужен выключенный Slot Extender, Nautilus ждёт библиотеку, которой нет.
        var map = registry.Get("nexus:subnautica:12")!;
        var nautilus = registry.Get("nexus:subnautica:1262")!;
        map["requires"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["id"] = "142", ["name"] = "Slot Extender" });
        nautilus["requires"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["id"] = "1457", ["name"] = "ECC Library 2.0" });

        w.Navigate(() => new GamePage("subnautica", "health"));
        s.Save("play-1-health");
        Actions.Play(sub, animate: false);
        s.Save("play-2-gate");
        w.CloseDialog();
        ConfirmPurge(sub);
        s.Save("play-3-purge-confirm");
        w.CloseDialog();
        Vanilla.Purge(sub);
        AppState.Notify();
        w.Navigate(() => new GamePage("subnautica", "health"));
        s.Save("play-4-purged");
        Vanilla.RestorePurge(sub);
        map.Remove("requires");
        nautilus.Remove("requires");
        AppState.Notify();
        w.Navigate(() => new GamePage("lethal-company", "health"));
        s.Save("play-5-ok");
    }

    /// <summary>Полоса над вкладками, пока игра очищена: «моды убраны — вернуть».</summary>
    Control? PurgeBanner()
    {
        if (!Vanilla.IsPurged(_g.Def.Id)) return null;
        var back = Ui.Button(I18n.T("vanilla.restore"), RestorePurge, "primary", Icons.Refresh);
        DockPanel.SetDock(back, Dock.Right);
        var row = new DockPanel { Children = { back } };
        var text = Ui.Row(10, Ui.Icon(Icons.EyeOff, 18, Ui.Res("Brand2")), Ui.Col(2, Ui.Text(I18n.T("vanilla.banner"), "h3"), Ui.Text(I18n.T("vanilla.banner.text"), "small muted", wrap: true)));
        text.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(text);
        return new Border { Background = Ui.Res("BrandSoft"), CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 12), Child = row };
    }
}
