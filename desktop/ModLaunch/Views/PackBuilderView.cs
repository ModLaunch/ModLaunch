using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Games;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>Интеллектуальный конструктор сборок: цели → подбор → правка → установка или публикация.</summary>
public sealed partial class CreatorPage
{
    string? _pbGame;
    readonly HashSet<string> _pbGoals = ["qol", "content"];
    int _pbSize = 20;
    PackPlan? _plan;
    bool _pbBusy;
    string? _pbError;
    CancellationTokenSource? _pbRun;

    /// <summary>Для скриншотов: готовый план без сети.</summary>
    public static PackPlan? DemoPlan;

    static readonly (string Id, int Size)[] Sizes = [("light", 8), ("medium", 20), ("big", 40)];

    async Task BuildPlan()
    {
        var game = GameCatalog.ById(_pbGame ?? "");
        if (game is null) return;
        _pbRun?.Cancel();
        var run = _pbRun = new CancellationTokenSource();
        _pbBusy = true;
        _pbError = null;
        Build();
        try
        {
            var state = AppState.Games.FirstOrDefault(g => g.Def.Id == game.Id);
            var plan = await PackBuilder.Build(game, [.. _pbGoals], _pbSize, Settings.Data.Bool("adult"), state?.Registry, run.Token);
            if (!run.IsCancellationRequested) _plan = plan;
        }
        catch (Exception e) when (!run.IsCancellationRequested) { _pbError = Jobs.Explain(e); }
        _pbBusy = false;
        Build();
    }

    Control PackBuilderView()
    {
        if (Program.Screenshot && DemoPlan is not null) _plan ??= DemoPlan;
        _pbGame ??= _plan?.Game.Id ?? AppState.Games.FirstOrDefault(g => g.Status == Detect.Found && g.Def.Catalog != CatalogKind.None)?.Def.Id ?? "lethal-company";

        // Слева — настройки, справа — результат.
        var gameBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        var games = AppState.Games.Select(g => g.Def).Concat(GameCatalog.Builtin).Where(g => g.Catalog != CatalogKind.None).DistinctBy(g => g.Id).ToList();
        foreach (var g in games) gameBox.Items.Add(g.Name);
        gameBox.SelectedIndex = Math.Max(0, games.FindIndex(g => g.Id == _pbGame));
        gameBox.SelectionChanged += (_, _) => { var id = games[Math.Max(0, gameBox.SelectedIndex)].Id; if (id != _pbGame) { _pbGame = id; _plan = null; Build(); } };

        var goals = new WrapPanel();
        foreach (var goal in PackBuilder.Goals)
        {
            var id = goal.Id;
            var on = _pbGoals.Contains(id);
            var chip = new Button
            {
                Classes = { "chip" }, Margin = new Thickness(0, 0, 6, 6),
                Content = Ui.Row(6, Ui.Icon(goal.Icon, 14, on ? Brushes.White : Ui.Hex(goal.Color)), Ui.Text(I18n.T("pb.goal." + id), "small")),
            };
            if (on) { chip.Classes.Add("active"); chip.Background = Ui.Hex(goal.Color); }
            chip.Click += (_, _) => { if (!_pbGoals.Remove(id)) _pbGoals.Add(id); if (_pbGoals.Count == 0) _pbGoals.Add(id); Build(); };
            goals.Children.Add(chip);
        }

        var sizes = Ui.Row(6);
        foreach (var (id, n) in Sizes)
        {
            var nn = n;
            var b = Ui.Button(I18n.T("pb.size." + id, ("n", n)), () => { _pbSize = nn; Build(); }, "chip");
            if (_pbSize == n) b.Classes.Add("active");
            sizes.Children.Add(b);
        }
        var make = Ui.Button(I18n.T(_plan is null ? "pb.build" : "pb.rebuild"), () => _ = BuildPlan(), "primary", Icons.Wand);
        make.IsEnabled = !_pbBusy;
        make.HorizontalAlignment = HorizontalAlignment.Stretch;
        make.HorizontalContentAlignment = HorizontalAlignment.Center;
        var settings = Ui.Card(Ui.Col(16,
            Ui.Col(4, Ui.Text(I18n.T("pb.eyebrow"), "eyebrow"), Ui.Text(I18n.T("pb.title"), "h2"), Ui.Text(I18n.T("pb.text"), "small muted", wrap: true)),
            Ui.Col(6, Ui.Text(I18n.T("cr.r.game"), "small muted"), gameBox),
            Ui.Col(6, Ui.Text(I18n.T("pb.goals"), "small muted"), goals),
            Ui.Col(6, Ui.Text(I18n.T("pb.size"), "small muted"), sizes),
            make,
            Ui.Text(I18n.T("pb.how"), "small muted", wrap: true)), 20);
        settings.VerticalAlignment = VerticalAlignment.Top;

        Control result;
        if (_pbBusy)
            result = Ui.Card(Ui.Col(14, Ui.Row(10, new ProgressBar { IsIndeterminate = true, Width = 160, VerticalAlignment = VerticalAlignment.Center }, Ui.Text(I18n.T("pb.busy"), "muted")),
                Ui.Text(I18n.T("pb.busy.text"), "small muted", wrap: true)), 24);
        else if (_pbError is not null)
            result = Ui.Card(Ui.Col(8, Ui.Row(10, Ui.Icon(Icons.Alert, 20, Ui.Res("Warn")), Ui.Text(I18n.T("pb.error"), "h3")), Ui.Text(_pbError, "muted", wrap: true)), 24);
        else if (_plan is null)
            result = new Border
            {
                Classes = { "card", "hero" }, Padding = new Thickness(32),
                Child = Ui.Col(12, Ui.Icon(Icons.Wand, 40, Ui.Res("Brand2")), Ui.Text(I18n.T("pb.empty"), "h2"), Ui.Text(I18n.T("pb.empty.text"), "muted", wrap: true)),
            };
        else result = PlanView(_plan);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("340,*"), ColumnSpacing = 18 };
        grid.Children.Add(settings);
        Grid.SetColumn(result, 1);
        grid.Children.Add(result);
        return grid;
    }

    Control PlanView(PackPlan plan)
    {
        var col = new StackPanel { Spacing = 14 };
        var chosen = plan.Chosen.ToList();

        // Итог: сколько модов, библиотек и что делать дальше.
        var summary = Ui.Col(4,
            Ui.Text(I18n.T("pb.result", ("n", chosen.Count), ("libs", plan.Libraries.Count)), "h2"),
            Ui.Text(string.Join(" · ", plan.Goals.Select(g => I18n.T("pb.goal." + g))) + " · " + plan.Game.Name, "small muted"));
        var actions = Ui.Row(8,
            Ui.Button(I18n.T("pb.save"), () => _ = SavePack(plan), "", Icons.Save),
            plan.Game.Loader == LoaderKind.Bepinex ? Ui.Button(I18n.T("pb.publish"), () => PublishPack(plan), "", Icons.Upload) : new Control(),
            Ui.Button(I18n.T("pb.install", ("n", plan.Total)), () => InstallPack(plan), "primary", Icons.Download));
        var head = new DockPanel();
        head.Children.Add(DockRight(actions));
        head.Children.Add(summary);
        col.Children.Add(Ui.Card(head, 18));

        foreach (var warn in plan.Warnings)
            col.Children.Add(new Border
            {
                Classes = { "inset" }, Padding = new Thickness(14, 10),
                Child = Ui.Row(10, Ui.Icon(Icons.Alert, 16, Ui.Res("Warn")), Ui.Text(warn, "small", wrap: true)),
            });

        // По целям: каждая — своим цветом.
        foreach (var goalId in plan.Goals)
        {
            var goal = PackBuilder.Goal(goalId);
            var picks = plan.Picks.Where(p => p.Goal == goalId && p.On).ToList();
            if (picks.Count == 0) continue;
            var rows = new StackPanel { Spacing = 6 };
            foreach (var p in picks) rows.Children.Add(PickRow(plan, p));
            col.Children.Add(Ui.Card(Ui.Col(10,
                Ui.Row(8, new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(8), Background = Ui.Hex(goal.Color), Child = Ui.Icon(goal.Icon, 14, Brushes.White) },
                    Ui.Text(I18n.T("pb.goal." + goalId), "h3"), Ui.Text(picks.Count.ToString(), "small muted")),
                rows), 16));
        }

        if (plan.Libraries.Count > 0)
        {
            var libs = new WrapPanel();
            foreach (var l in plan.Libraries) libs.Children.Add(new Border { Classes = { "pill" }, Margin = new Thickness(0, 0, 6, 6), Child = Ui.Text(l.Name, "small") });
            col.Children.Add(Ui.Card(Ui.Col(8, Ui.Row(8, Ui.Icon(Icons.Layers, 16, Ui.Res("Muted")), Ui.Text(I18n.T("pb.libs", ("n", plan.Libraries.Count)), "h3")),
                Ui.Text(I18n.T("pb.libs.text"), "small muted", wrap: true), libs), 16));
        }

        var spare = plan.Picks.Where(p => !p.On).ToList();
        if (spare.Count > 0)
        {
            var rows = new StackPanel { Spacing = 6 };
            foreach (var p in spare) rows.Children.Add(PickRow(plan, p));
            col.Children.Add(Ui.Card(Ui.Col(10, Ui.Text(I18n.T("pb.spare"), "h3"), Ui.Text(I18n.T("pb.spare.text"), "small muted", wrap: true), rows), 16));
        }
        return col;
    }

    Control PickRow(PackPlan plan, PackPick p)
    {
        var toggle = new CheckBox { IsChecked = p.On, VerticalAlignment = VerticalAlignment.Center };
        toggle.IsCheckedChanged += async (_, _) =>
        {
            p.On = toggle.IsChecked == true;
            await PackBuilder.Refresh(plan);
            Build();
        };
        var reasons = new WrapPanel();
        foreach (var r in p.Reasons.Take(4))
            reasons.Children.Add(new Border { Classes = { "pill" }, Padding = new Thickness(8, 2), Margin = new Thickness(0, 4, 6, 0), Child = Ui.Text(r, "small muted") });
        var right = Ui.Row(6);
        if (p.Alternatives.Count > 0)
        {
            var swap = new Button { Classes = { "ghost" }, Content = Ui.Row(6, Ui.Icon(Icons.Refresh, 13, Ui.Res("Muted")), Ui.Text(I18n.T("pb.swap", ("n", p.Alternatives.Count)), "small")) };
            var menu = new MenuFlyout();
            foreach (var alt in p.Alternatives)
            {
                var a = alt;
                var item = new MenuItem { Header = $"{a.Name} · ↓ {I18n.Compact(a.Downloads)}" };
                item.Click += async (_, _) =>
                {
                    var old = p.Mod;
                    p.Alternatives.Remove(a);
                    p.Alternatives.Insert(0, old);
                    p.Mod = a;
                    p.Reasons.Clear();
                    p.Reasons.Add(I18n.T("pb.why.swapped", ("name", old.Name)));
                    await PackBuilder.Refresh(plan);
                    Build();
                };
                menu.Items.Add(item);
            }
            swap.Flyout = menu;
            right.Children.Add(swap);
        }
        var game = plan.Game;
        right.Children.Add(Ui.Button("", () => W.Navigate(() => new ModPage(game.Id, p.Mod)), "icon ghost", Icons.Eye, I18n.T("hub.open")));
        var row = new DockPanel { Opacity = p.On ? 1 : 0.7 };
        row.Children.Add(DockRight(right));
        var who = Ui.Col(1,
            Ui.Row(8, Ui.Text(p.Mod.Name, "strong"), p.Installed ? Ui.Text(I18n.T("pb.installed"), "small", color: Ui.Res("Good")) : new Control()),
            Ui.Text(I18n.T("mod.by", ("author", p.Mod.Author)) + (p.Mod.Categories.Length > 0 ? " · " + p.Mod.Categories[0] : ""), "small muted"),
            reasons);
        var left = Ui.Row(12, toggle, Ui.Thumb(p.Mod.Icon, p.Mod.Name, 40, 10), who);
        row.Children.Add(left);
        return new Border { Classes = { "inset" }, Padding = new Thickness(12, 8), Child = row };
    }

    void InstallPack(PackPlan plan)
    {
        var state = AppState.Games.FirstOrDefault(g => g.Def.Id == plan.Game.Id);
        if (state?.Path is null) { W.Toast(I18n.T("toast.notFound", ("game", plan.Game.Name)), bad: true); return; }
        var mods = plan.Chosen.Where(p => !p.Installed).Select(p => (p.Mod, (Pin?)null)).ToList();
        if (mods.Count == 0) { W.Toast(I18n.T("pb.allInstalled")); return; }
        Confirm(I18n.T("pb.install.title", ("game", plan.Game.Name)), I18n.T("pb.install.text", ("n", mods.Count), ("libs", plan.Libraries.Count)),
            () => _ = Actions.InstallQueue(state, I18n.T("pb.packName"), mods));
    }

    async Task SavePack(PackPlan plan)
    {
        var name = I18n.T("pb.packFile", ("game", plan.Game.ShortName));
        var file = await W.PickSaveFile(I18n.T("pb.save"), name + ".modhub.json");
        if (file is null) return;
        await File.WriteAllTextAsync(file, Features.ModPack.FromList(plan.Game, name, plan.Chosen.Select(p => p.Mod)));
        W.Toast(I18n.T("pb.saved"));
    }

    void PublishPack(PackPlan plan)
    {
        var name = I18n.T("pb.packFile", ("game", plan.Game.ShortName));
        HubPublish.Show(new HubDraft
        {
            Name = name, Game = plan.Game.Id, Version = "1.0.0", Code = PackBuilder.ToScript(plan, name),
            Summary = I18n.T("pb.script.about", ("n", plan.Chosen.Count())), Tags = ["modpack"],
        }, fromProject: true, pack: null, done: () => { _tab = "published"; _ = LoadHub(force: true); });
    }
}
