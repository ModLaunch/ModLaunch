using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>
/// 10.0: кейсы. В центре — кейс в 3D, он медленно покачивается (настоящий поворот в перспективе).
/// «Открыть»: кейс вздрагивает, крышка откидывается, изнутри бьёт свет цвета редкости, и вместо
/// кейса поднимается карточка с тем, что выпало, — с кнопкой «Применить» для функции.
/// Ниже — остальные кейсы, что может выпасть и что уже выпадало.
/// </summary>
public sealed class CasesPage : Page
{
    int _pick;
    DispatcherTimer? _timer;
    readonly System.Diagnostics.Stopwatch _clock = new();
    Loot? _shown;
    string _shownTier = "common";
    bool _opening;

    public override string Title => I18n.T("v10.case.title");

    public override void Build()
    {
        _timer?.Stop();
        var ready = Cases.Ready();
        if (_pick >= ready.Count) _pick = 0;
        var col = StoreKit.Column(spacing: 18, top: 22);
        col.Children.Add(Ui.Col(6,
            new TextBlock { Text = I18n.T("v10.case.title"), FontFamily = Look.Display, FontSize = 30, FontWeight = FontWeight.Bold, LetterSpacing = -0.8 },
            Ui.Text(I18n.T("v10.case.lead"), "muted", wrap: true)));
        col.Children.Add(Stage(ready));
        if (ready.Count > 1) col.Children.Add(Queue(ready));
        col.Children.Add(Pool());
        if (Cases.History() is { Count: > 0 } history) col.Children.Add(History(history));
        Content = new ScrollViewer { Content = col, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    // ---------------------------------------------------------------- сцена

    Control Stage(List<GameCase> ready)
    {
        var tier = _shown is not null ? _shownTier : ready.ElementAtOrDefault(_pick)?.Tier ?? "common";
        var tint = CaseBox.TierColor(tier);
        var floor = new Border
        {
            Background = new RadialGradientBrush
            {
                Center = new RelativePoint(0.5, 0.78, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0.5, 0.78, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.5, RelativeUnit.Relative), RadiusY = new RelativeScalar(0.6, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Gx.Alpha(tint, 70), 0), new GradientStop(Gx.Alpha(tint, 0), 1) },
            },
        };
        var stage = new Panel { Height = 420, ClipToBounds = true, Children = { floor } };
        var frame = new Border { Classes = { "card" }, ClipToBounds = true, Child = stage, Background = Ui.Hex("#0D0B16") };

        if (_shown is { } loot) { stage.Children.Add(Reward(loot, tier)); return frame; }

        if (ready.Count == 0)
        {
            var empty = CaseKit.Small(180, "common");
            empty.Opacity = 0.35;
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            empty.VerticalAlignment = VerticalAlignment.Center;
            empty.Margin = new Thickness(0, 0, 0, 70);
            var text = Ui.Col(4, new TextBlock { Text = I18n.T("v10.case.empty"), FontSize = 18, FontWeight = FontWeight.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = I18n.T("v10.case.empty.text"), FontSize = 13, Foreground = Ui.Hex("#B9B4CC"), HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 460 });
            text.VerticalAlignment = VerticalAlignment.Bottom;
            text.Margin = new Thickness(0, 0, 0, 40);
            stage.Children.Add(empty);
            stage.Children.Add(text);
            return frame;
        }

        var c = ready[_pick];
        var box = new CaseBox(230, c.Tier) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 60) };
        var label = Ui.Col(4,
            new TextBlock { Text = CaseKit.TierName(c.Tier).ToUpper(I18n.Culture), FontSize = 11.5, FontWeight = FontWeight.Bold, LetterSpacing = 2, Foreground = new SolidColorBrush(tint), HorizontalAlignment = HorizontalAlignment.Center },
            new TextBlock { Text = From(c), FontSize = 13, Foreground = Ui.Hex("#B9B4CC"), HorizontalAlignment = HorizontalAlignment.Center });
        var open = Ui.Button(I18n.T("v10.case.open"), () => Open(c, box, stage), "primary", Icons.Cube);
        open.Padding = new Thickness(30, 12);
        open.HorizontalAlignment = HorizontalAlignment.Center;
        var bottom = Ui.Col(14, label, open);
        bottom.VerticalAlignment = VerticalAlignment.Bottom;
        bottom.HorizontalAlignment = HorizontalAlignment.Center;
        bottom.Margin = new Thickness(0, 0, 0, 26);
        stage.Children.Add(box);
        stage.Children.Add(bottom);
        Sway(box);
        return frame;
    }

    static string From(GameCase c) => c.From == "week"
        ? I18n.T("v10.case.fromWeek")
        : Achievements.All().FirstOrDefault(a => a.Id == c.From) is { } a ? I18n.T("v10.case.fromAch", ("name", a.Title)) : "";

    /// <summary>Покачивание: поворот вокруг вертикали ±14° и лёгкое парение.</summary>
    void Sway(CaseBox box)
    {
        if (!Animate.On) { box.RenderTransform = new Rotate3DTransform(-8, -16, 0, 0, 0, 0, 900); return; }
        _clock.Restart();
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) =>
        {
            if (_opening) return;
            var t = _clock.Elapsed.TotalSeconds;
            box.RenderTransform = new TransformGroup
            {
                Children =
                {
                    new Rotate3DTransform(-8 + Math.Sin(t * 0.9) * 3, -14 * Math.Sin(t * 0.6), 0, 0, 0, 0, 900),
                    new TranslateTransform(0, Math.Sin(t * 1.4) * 6),
                },
            };
        });
        box.DetachedFromVisualTree += (_, _) => _timer?.Stop();
        _timer.Start();
    }

    /// <summary>Открытие: дрожь, крышка вверх и назад, свет изнутри, кейс уходит — карточка награды поднимается.</summary>
    void Open(GameCase c, CaseBox box, Panel stage)
    {
        if (_opening) return;
        _opening = true;
        var loot = Cases.Open(c);
        foreach (var b in stage.Children.OfType<StackPanel>()) b.IsVisible = false;
        void Finish()
        {
            _timer?.Stop();
            _opening = false;
            _shown = loot;
            _shownTier = c.Tier;
            Build();
        }
        if (!Animate.On) { Finish(); return; }
        _clock.Restart();
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => { });
        timer.Tick += (_, _) =>
        {
            var ms = _clock.Elapsed.TotalMilliseconds;
            double K(double a, double b) => Math.Clamp((ms - a) / (b - a), 0, 1);
            double Out(double k) => 1 - Math.Pow(1 - k, 3);
            // 0–700: дрожь нарастает.
            var shake = ms < 700 ? Math.Sin(ms / 22.0) * 3.2 * K(0, 700) : 0;
            // 650–1150: крышка откидывается назад, свет и луч.
            var lid = Out(K(650, 1150));
            box.Lid.RenderTransform = new TransformGroup { Children = { new Rotate3DTransform(lid * 70, 0, 0, 0, 0, 0, 700), new TranslateTransform(0, -lid * 34) } };
            box.Lid.Opacity = 1 - K(950, 1300);
            box.Glow.Opacity = Out(K(650, 1000)) * (1 - K(1500, 1900) * 0.6);
            box.Beam.Opacity = Out(K(700, 1000)) * (1 - K(1300, 1700));
            box.RenderTransform = new TransformGroup
            {
                Children =
                {
                    new Rotate3DTransform(-8, -6 * (1 - K(0, 600)), shake, 0, 0, 0, 900),
                    new ScaleTransform(1 + 0.04 * K(0, 650) - 0.25 * Out(K(1300, 1750)), 1 + 0.04 * K(0, 650) - 0.25 * Out(K(1300, 1750))),
                },
            };
            box.Body.Opacity = 1 - K(1350, 1750);
            if (ms >= 1800) { timer.Stop(); Finish(); }
        };
        _timer?.Stop();
        _timer = timer;
        timer.Start();
    }

    /// <summary>Карточка того, что выпало: значок, название, описание и что с этим сделать.</summary>
    Control Reward(Loot loot, string tier)
    {
        var tint = CaseBox.TierColor(loot.Kind == "hint" ? "common" : loot.Tier);
        var icon = loot.Kind switch
        {
            "cfg" => Icons.Palette, "widget" => Icons.Grid, "accent" => Icons.Sparkles, "frame" => Icons.User, "banner" => Icons.Image,
            "title" => Icons.Flag, "badge" => Icons.Star, _ => Icons.Info,
        };
        var badge = new Border
        {
            Width = 76, Height = 76, CornerRadius = new CornerRadius(22), HorizontalAlignment = HorizontalAlignment.Center,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Look.Mix(tint, Colors.White, 0.25), 0), new GradientStop(CaseBox.TierColor2(loot.Kind == "hint" ? "common" : loot.Tier), 1) },
            },
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 40, Spread = 2, Color = Gx.Alpha(tint, 150) }),
            Child = Ui.Icon(icon, 34, Brushes.White),
        };
        var kind = new TextBlock { Text = I18n.T("v10.loot.kind." + loot.Kind).ToUpper(I18n.Culture), FontSize = 11.5, FontWeight = FontWeight.Bold, LetterSpacing = 2, Foreground = new SolidColorBrush(tint), HorizontalAlignment = HorizontalAlignment.Center };
        var name = new TextBlock { Text = LootName(loot), FontFamily = Look.Display, FontSize = 26, FontWeight = FontWeight.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 560 };
        var text = new TextBlock { Text = LootText(loot), FontSize = 14, Foreground = Ui.Hex("#C9C4DA"), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 560 };
        var buttons = Ui.Row(10);
        if (Use(loot) is { } use) buttons.Children.Add(use);
        buttons.Children.Add(Ui.Button(Cases.Ready().Count > 0 ? I18n.T("v10.case.next") : I18n.T("v10.case.done"), () => { _shown = null; Build(); }, Use(loot) is null ? "primary" : "hero-ghost", Cases.Ready().Count > 0 ? Icons.Cube : Icons.Check));
        buttons.HorizontalAlignment = HorizontalAlignment.Center;
        var card = Ui.Col(12, badge, kind, name, text, new Border { Height = 4 }, buttons);
        card.VerticalAlignment = VerticalAlignment.Center;
        card.HorizontalAlignment = HorizontalAlignment.Center;
        if (Animate.On)
        {
            Animate.From(badge, "scale(0.5)", 520, 0, new BackEaseOut());
            Animate.From(card, "translateY(34px)", 520, 0, new QuinticEaseOut());
        }
        return card;
    }

    public static string LootName(Loot l) => l.Kind switch
    {
        "hint" => I18n.T("v10.loot.hint"),
        "frame" or "banner" or "title" or "badge" => Cosmetics.Name(new Reward(l.Kind, l.Id)),
        _ => I18n.T($"v10.loot.{l.Kind}.{l.Id}"),
    };

    public static string LootText(Loot l) => l.Kind switch
    {
        "hint" => I18n.T("v10.hint." + l.Id),
        "frame" or "banner" or "title" or "badge" => I18n.T("v10.loot.cosmetic.d", ("kind", Cosmetics.KindName(l.Kind).ToLower(I18n.Culture))),
        _ => I18n.T($"v10.loot.{l.Kind}.{l.Id}.d"),
    };

    /// <summary>Кнопка «сразу применить» для функции, что выпала.</summary>
    static Button? Use(Loot l)
    {
        var w = MainWindow.Current;
        switch (l.Kind)
        {
            case "cfg":
                return Ui.Button(I18n.T("v10.case.use.cfg"), () => { if (Configs.BuiltIn().FirstOrDefault(c => c.Id == l.Id) is { } cfg) Configs.Apply(cfg); w?.Toast(I18n.T("v10.cfg.applied", ("name", LootName(l)))); }, "primary", Icons.Palette);
            case "widget":
                return Ui.Button(I18n.T("v10.case.use.widget"), () => { Widgets.Add(l.Id); w?.Toast(I18n.T("v10.w.added")); }, "primary", Icons.Plus);
            case "accent":
                return Ui.Button(I18n.T("v10.case.use.accent"), () => Look.SetAccent(Cases.GoldAccent), "primary", Icons.Sparkles);
            case "frame" or "banner" or "title" or "badge":
                return Ui.Button(I18n.T(l.Kind == "badge" ? "v99.rewards.show" : "v99.rewards.wear"), () => { Cosmetics.Toggle(new Reward(l.Kind, l.Id)); w?.RefreshAccount(); w?.Toast(I18n.T("v10.case.worn")); }, "primary", Icons.Check);
        }
        return null;
    }

    // ---------------------------------------------------------------- остальное

    Control Queue(List<GameCase> ready)
    {
        var row = new WrapPanel();
        for (var i = 0; i < ready.Count && i < 24; i++)
        {
            var at = i;
            var c = ready[i];
            var tile = new Border
            {
                Classes = { "store-tile" }, Width = 134, Height = 104, Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(8),
                BorderBrush = i == _pick ? new SolidColorBrush(CaseBox.TierColor(c.Tier)) : Ui.Res("Line"), BorderThickness = new Thickness(i == _pick ? 2 : 1),
                Child = Ui.Col(6, new Border { Height = 64, Child = CaseKit.Small(62, c.Tier) },
                    new TextBlock { Text = CaseKit.TierName(c.Tier), FontSize = 11.5, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Ui.Res("Muted") }),
            };
            StoreKit.OnClick(tile, () => { if (_opening) return; _pick = at; _shown = null; Build(); });
            row.Children.Add(tile);
        }
        return Ui.Col(10, Ui.Text(I18n.T("v10.case.queue", ("n", ready.Count)), "h3"), row);
    }

    static Control Pool()
    {
        var have = Cases.Unlocked();
        var grid = new WrapPanel();
        foreach (var f in Cases.Features)
        {
            var got = have.Contains(f.Key);
            var tint = CaseBox.TierColor(f.Tier);
            var item = new Border
            {
                Classes = { "card" }, Width = 220, Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(14, 12), Opacity = got ? 1 : 0.75,
                Child = Ui.Col(4,
                    Ui.Row(8, Ui.Dot(new SolidColorBrush(tint), 8), new TextBlock { Text = CaseKit.TierName(f.Tier), FontSize = 11, Foreground = Ui.Res("Muted") }, got ? Ui.Icon(Icons.Check, 13, Ui.Res("Good")) : Ui.Icon(Icons.Lock, 12, Ui.Res("Faint"))),
                    new TextBlock { Text = LootName(f), FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                    new TextBlock { Text = I18n.T("v10.loot.kind." + f.Kind), FontSize = 11.5, Foreground = Ui.Res("Faint") }),
            };
            grid.Children.Add(item);
        }
        return Ui.Col(10, Ui.Row(10, Ui.Text(I18n.T("v10.case.pool"), "h3"), Gx.Tag($"{Cases.Features.Count(f => have.Contains(f.Key))} / {Cases.Features.Length}", Ui.Res("Surface3"))),
            Ui.Text(I18n.T("v10.case.pool.text"), "small muted", wrap: true), grid);
    }

    static Control History(List<(Loot Loot, DateTime At)> history)
    {
        var list = Ui.Col(6);
        foreach (var (l, at) in history.Take(8))
            list.Children.Add(new Border
            {
                Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10),
                Child = new DockPanel
                {
                    Children =
                    {
                        new TextBlock { Text = Ui.Ago(at), FontSize = 12, Foreground = Ui.Res("Faint"), [DockPanel.DockProperty] = Dock.Right, VerticalAlignment = VerticalAlignment.Center },
                        Ui.Col(2, Ui.Row(8, Ui.Dot(new SolidColorBrush(CaseBox.TierColor(l.Tier)), 7), new TextBlock { Text = LootName(l), FontWeight = FontWeight.SemiBold }),
                            new TextBlock { Text = LootText(l), FontSize = 12, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis }),
                    },
                },
            });
        return Ui.Col(10, Ui.Text(I18n.T("v10.case.history"), "h3"), list);
    }

    /// <summary>Для снимков экрана: нажать «Открыть» у первого кейса (с анимацией, если она включена).</summary>
    public void DemoOpen()
    {
        if (Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(this).OfType<CaseBox>().FirstOrDefault() is { } box && box.Parent is Panel stage && Cases.Ready().FirstOrDefault() is { } c)
            Open(c, box, stage);
    }

    /// <summary>Для снимков экрана: показать карточку награды без анимации.</summary>
    public void DemoShow(Loot loot, string tier) { _shown = loot; _shownTier = tier; Build(); }
}
