using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>
/// 9.9: профиль игрока — баннер, аватар в рамке, титул, уровень и витрина значков.
/// Вкладки: «Достижения» (общий набор и набор каждой игры), «Награды» (что открыто и что надеть)
/// и «Итоги» (как Steam Replay).
/// </summary>
public sealed class ProfilePage : Page
{
    static string _tab = "achievements";
    static string _set = Achievements.Main;

    public ProfilePage(string tab = "achievements", string? set = null)
    {
        _tab = tab;
        if (set is not null) _set = set;
    }

    public override string Title => I18n.T("v99.profile");

    public override void Build()
    {
        var all = Achievements.All();
        var unlocked = Cosmetics.Unlocked(all);
        var col = StoreKit.Column(spacing: 18, top: 18);
        col.Children.Add(Animated(Header(all, unlocked), 0));
        col.Children.Add(Ui.Row(6,
            Tab("achievements", I18n.T("v99.tab.ach"), Icons.Trophy),
            Tab("rewards", I18n.T("v99.tab.rewards", ("n", unlocked.Count)), Icons.Star),
            Tab("replay", I18n.T("v98.replay"), Icons.Chart)));
        col.Children.Add(Animated(_tab switch { "rewards" => Rewards(all, unlocked), "replay" => Replay(), _ => AchievementsView(all) }, 1));
        Content = new ScrollViewer { Content = col, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    Control Animated(Control c, int i) { if (!Shown) Animate.Rise(c, i); return c; }

    Button Tab(string id, string text, string icon)
    {
        var b = Ui.Button(text, () => { _tab = id; Build(); }, "tab", icon);
        if (_tab == id) b.Classes.Add("active");
        return b;
    }

    // ---------------------------------------------------------------- шапка

    static Control Header(List<Achievement> all, HashSet<string> unlocked)
    {
        var (level, xp, next) = Achievements.Level(all);
        var name = ProfileKit.PlayerName();
        var done = all.Count(a => a.Done);

        var avatar = new Panel { Width = 112, Height = 112, VerticalAlignment = VerticalAlignment.Bottom, Children = { ProfileKit.Avatar(name, 112, ring: 6) } };
        var lvl = new Border
        {
            Background = ProfileKit.FrameBrush(Cosmetics.Equipped("frame", unlocked)), CornerRadius = new CornerRadius(999), Padding = new Thickness(9, 2),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, -8),
            BorderBrush = Ui.Res("Surface"), BorderThickness = new Thickness(2),
            Child = new TextBlock { Text = level.ToString(), FontFamily = Gx.Display, FontWeight = FontWeight.Bold, FontSize = 14, Foreground = Brushes.White },
        };
        avatar.Children.Add(lvl);

        var bar = new ProgressBar { Minimum = 0, Maximum = 300, Value = 300 - next, Height = 6, Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
        var words = Ui.Col(6,
            new TextBlock { Text = name, FontFamily = Gx.Display, FontSize = 30, FontWeight = FontWeight.Bold, Foreground = Brushes.White, LetterSpacing = -0.6, TextTrimming = TextTrimming.CharacterEllipsis },
            ProfileKit.Title(Cosmetics.Equipped("title", unlocked)),
            Ui.Row(10, new TextBlock { Text = I18n.T("v98.level", ("n", level), ("xp", xp)), FontSize = 13, Foreground = Ui.Hex("#E6E7EE"), VerticalAlignment = VerticalAlignment.Center }, bar),
            new TextBlock { Text = I18n.T("v98.level.next", ("n", next)) + " · " + I18n.T("v98.ach.count", ("done", done), ("all", all.Count)), FontSize = 12, Foreground = Ui.Hex("#C9CFDB") });
        words.VerticalAlignment = VerticalAlignment.Bottom;

        // Витрина: до трёх значков, пустые места — пунктиром.
        var badges = Cosmetics.Badges(unlocked);
        var showcase = Ui.Row(8);
        foreach (var b in badges) showcase.Children.Add(ProfileKit.Badge(b, 48));
        for (var i = badges.Count; i < 3; i++)
            showcase.Children.Add(new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(24), BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), BorderThickness = new Thickness(1.5), Child = Ui.Icon(Icons.Plus, 16, new SolidColorBrush(Color.FromArgb(140, 255, 255, 255))) });
        var right = Ui.Col(8, new TextBlock { Text = I18n.T("v99.showcase"), FontSize = 11, FontWeight = FontWeight.Bold, LetterSpacing = 1, Foreground = Ui.Hex("#C9CFDB"), HorizontalAlignment = HorizontalAlignment.Right }, showcase);
        right.VerticalAlignment = VerticalAlignment.Bottom;
        StoreKit.OnClick(right, () => MainWindow.Current?.Navigate(() => new ProfilePage("rewards")));

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 22, Margin = new Thickness(28, 0, 28, 24), VerticalAlignment = VerticalAlignment.Bottom };
        grid.Children.Add(avatar);
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        return new Border
        {
            Classes = { "card" }, Padding = new Thickness(0), Height = 260, ClipToBounds = true,
            Child = new Panel { Children = { ProfileKit.Banner(Cosmetics.Equipped("banner", unlocked)), grid } },
        };
    }

    // ---------------------------------------------------------------- достижения

    static Control AchievementsView(List<Achievement> all)
    {
        var sets = Achievements.Sets(all);
        if (!sets.Any(s => s.Set == _set)) _set = Achievements.Main;

        // Наборы — плитки: картинка игры, название и «3 / 6».
        var chips = new WrapPanel();
        foreach (var (set, list) in sets)
        {
            var on = set == _set;
            var done = list.Count(a => a.Done);
            Control pic = set == Achievements.Main
                ? new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(10), Background = Ui.Res("BrandSoft"), Child = new Image { Source = Images.Asset("icon.png", 64), Width = 22, Height = 22 } }
                : new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = Ui.GameImage(AppState.Game(set).Def, 120, art: Images.Art.Cover) };
            var words = Ui.Col(1, new TextBlock { Text = Achievements.SetName(set), FontWeight = FontWeight.SemiBold, FontSize = 13, MaxWidth = 150, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = $"{done} / {list.Count}", FontSize = 11.5, Foreground = done == list.Count ? Ui.Res("Good") : Ui.Res("Muted") });
            words.VerticalAlignment = VerticalAlignment.Center;
            var chip = new Border
            {
                Classes = { "card" }, Padding = new Thickness(8, 7, 14, 7), Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(14),
                BorderBrush = on ? Ui.Res("Brand") : Ui.Res("Line"), BorderThickness = new Thickness(on ? 2 : 1),
                Background = on ? Ui.Res("BrandSoft") : Ui.Res("Surface"),
                Child = Ui.Row(10, pic, words), Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            };
            var s = set;
            StoreKit.OnClick(chip, () => { _set = s; MainWindow.Current?.CurrentPage?.Build(); });
            chips.Children.Add(chip);
        }

        var current = sets.First(s => s.Set == _set).List;
        var grid = new UniformGrid { Columns = 3 };
        foreach (var a in current.OrderByDescending(a => a.Done).ThenByDescending(a => a.Progress)) grid.Children.Add(AchCard(a));
        var doneHere = current.Count(a => a.Done);
        var head = Ui.Row(10, Ui.Text(Achievements.SetName(_set), "h2"),
            Gx.Tag($"{doneHere} / {current.Count}", Ui.Res("BrandSoft"), Ui.Res("Brand2")));
        if (_set != Achievements.Main) head.Children.Add(Ui.Text(I18n.T("v99.set.hint"), "small muted"));
        return Ui.Col(14, chips, head, grid);
    }

    static Control AchCard(Achievement a)
    {
        var color = ProfileKit.TierColor(a.Tier);
        var c = Color.Parse(color);
        var medal = new Border
        {
            Width = 52, Height = 52, CornerRadius = new CornerRadius(16),
            Background = a.Done ? new SolidColorBrush(Color.FromArgb(50, c.R, c.G, c.B)) : Ui.Res("Surface3"),
            BorderBrush = a.Done ? Ui.Hex(color) : Brushes.Transparent, BorderThickness = new Thickness(1.5),
            BoxShadow = a.Done && a.Tier is "epic" or "legend" ? BoxShadows.Parse($"0 0 18 0 {Gx.Alpha(c, 120)}") : default,
            Child = Ui.Icon(typeof(Icons).GetField(a.Icon)?.GetValue(null) as string ?? Icons.Star, 24, a.Done ? Ui.Hex(color) : Ui.Res("Faint")),
            VerticalAlignment = VerticalAlignment.Top,
        };
        var tier = new TextBlock { Text = I18n.T("v99.tier." + a.Tier) + " · " + a.Xp + " XP", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Ui.Hex(color) };
        var text = Ui.Col(4,
            Ui.Text(a.Title, "h3"),
            Ui.Text(a.Desc, "small muted", wrap: true),
            new ProgressBar { Minimum = 0, Maximum = 1, Value = a.Progress, Height = 4, Margin = new Thickness(0, 2) },
            Ui.Row(8, tier, Ui.Text(a.Done ? I18n.T("v98.ach.done") : $"{Math.Min(a.Have, a.Need)} / {a.Need}", "small muted")));
        if (a.Reward is { } r)
        {
            var gift = Ui.Row(6, Ui.Icon(Icons.Star, 12, a.Done ? Ui.Res("Good") : Ui.Res("Faint")),
                new TextBlock { Text = Cosmetics.KindName(r.Kind) + " «" + Cosmetics.Name(r) + "»", FontSize = 12, Foreground = a.Done ? Ui.Res("Good") : Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(gift);
        }
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 14 };
        grid.Children.Add(medal);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return new Border { Classes = { "card" }, Padding = new Thickness(16), Margin = new Thickness(0, 0, 12, 12), Opacity = a.Done ? 1 : 0.8, Child = grid };
    }

    // ---------------------------------------------------------------- награды

    static Control Rewards(List<Achievement> all, HashSet<string> unlocked)
    {
        var name = ProfileKit.PlayerName();
        var col = Ui.Col(18, Ui.Text(I18n.T("v99.rewards.text"), "muted", wrap: true));
        var catalog = Cosmetics.Catalog(all);
        foreach (var kind in Cosmetics.Kinds)
        {
            var items = catalog.Where(x => x.Reward.Kind == kind).OrderByDescending(x => unlocked.Contains(x.Reward.Key)).ToList();
            if (items.Count == 0) continue;
            var open = items.Count(x => unlocked.Contains(x.Reward.Key));
            var wrap = new WrapPanel();
            foreach (var (r, from) in items) wrap.Children.Add(RewardCard(r, from, unlocked, name));
            col.Children.Add(Ui.Col(10, Ui.Row(10, Ui.Text(I18n.T($"v99.kind.{kind}.many"), "h2"), Gx.Tag($"{open} / {items.Count}", Ui.Res("Surface3"))), wrap));
        }
        return col;
    }

    static Control RewardCard(Reward r, Achievement? from, HashSet<string> unlocked, string name)
    {
        var have = unlocked.Contains(r.Key);
        var on = have && Cosmetics.IsOn(r, unlocked);
        var preview = ProfileKit.Preview(r, name);
        preview.HorizontalAlignment = HorizontalAlignment.Center;
        if (!have) preview.Opacity = 0.4;
        var title = new TextBlock { Text = Cosmetics.Name(r), FontWeight = FontWeight.SemiBold, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center };
        var sub = new TextBlock
        {
            Text = have ? (from is null ? I18n.T("v99.rewards.starter") : from.Title) : I18n.T("v99.rewards.locked", ("ach", from?.Title ?? "")),
            FontSize = 11.5, Foreground = Ui.Res("Muted"), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Control action = have
            ? Ui.Button(on ? I18n.T(r.Kind == "badge" ? "v99.rewards.remove" : "v99.rewards.on") : I18n.T(r.Kind == "badge" ? "v99.rewards.show" : "v99.rewards.wear"),
                () => { if (on && r.Kind != "badge") return; Cosmetics.Toggle(r); MainWindow.Current?.CurrentPage?.Build(); MainWindow.Current?.RefreshAccount(); }, on ? "" : "primary", on ? Icons.Check : null)
            : Ui.Row(6, Ui.Icon(Icons.Lock, 13, Ui.Res("Faint")), Ui.Text(I18n.T("v99.rewards.closed"), "small muted"));
        action.HorizontalAlignment = HorizontalAlignment.Center;
        var body = Ui.Col(8, new Border { Height = 70, Child = preview }, title, sub, action);
        return new Border
        {
            Classes = { "card" }, Width = 196, Padding = new Thickness(14, 16), Margin = new Thickness(0, 0, 12, 12),
            BorderBrush = on ? Ui.Res("Brand") : Ui.Res("Line"), BorderThickness = new Thickness(on ? 2 : 1), Child = body,
        };
    }

    // ---------------------------------------------------------------- итоги

    static Control Replay()
    {
        var (hours, sessions, topGames) = Achievements.Replay();
        var replay = Ui.Col(12, Ui.Row(8, Ui.Icon(Icons.Chart, 18, Ui.Res("Brand2")), Ui.Text(I18n.T("v98.replay"), "h2")),
            Ui.Row(28, Gx.Stat(hours.ToString(), I18n.T("v98.replay.hours")), Gx.Stat(sessions.ToString(), I18n.T("v98.replay.sessions")), Gx.Stat(AiBuilds.All().Count.ToString(), I18n.T("v98.replay.builds"))));
        if (topGames.Count == 0) replay.Children.Add(Ui.Text(I18n.T("v98.replay.empty"), "muted", wrap: true));
        var max = Math.Max(1, topGames.Count > 0 ? topGames[0].Ms : 1);
        foreach (var (g, ms) in topGames)
        {
            var bar = new Border { Height = 8, CornerRadius = new CornerRadius(4), Background = Ui.Hex(g.Def.Accent), HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(8, 520.0 * ms / max) };
            var pic = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(9), ClipToBounds = true, Child = Ui.GameImage(g.Def, 120, art: Images.Art.Cover) };
            var words = Ui.Col(4, Ui.Row(8, Ui.Text(g.Def.Name), Ui.Text(PlayTime.Format(ms), "small muted")), bar);
            words.VerticalAlignment = VerticalAlignment.Center;
            replay.Children.Add(Ui.Row(12, pic, words));
        }
        return Ui.Card(replay, 24);
    }

    // ---------------------------------------------------------------- уведомления

    /// <summary>Показать, что открылось с прошлого раза: «Достижение открыто: … · Награда: …».</summary>
    public static void AnnounceNew()
    {
        var fresh = Achievements.TakeNew();
        if (fresh.Count == 0 || MainWindow.Current is not { } w) return;
        foreach (var a in fresh.Take(2))
            w.Toast(I18n.T("v99.unlocked", ("name", a.Title)) + (a.Reward is { } r ? " · " + Cosmetics.KindName(r.Kind) + " «" + Cosmetics.Name(r) + "»" : ""));
        if (fresh.Count > 2) w.Toast(I18n.T("v99.unlocked.more", ("n", fresh.Count - 2)));
    }
}
