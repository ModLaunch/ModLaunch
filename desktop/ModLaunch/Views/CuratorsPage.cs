using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Social;

namespace ModLaunch.Views;

/// <summary>
/// 30.0: «Кураторы» — люди, которые пишут об играх. Популярные, свои подписки и своя страница куратора
/// (имя, пара слов о себе, мнения об играх из своей библиотеки). Страница одного куратора — new CuratorsPage(uid).
/// </summary>
public sealed class CuratorsPage : Page
{
    public override string Title => I18n.T("v30.cur.title");

    readonly string? _uid;
    string _tab = "popular";
    List<Curator>? _popular;
    Curator? _curator;
    List<CuratorPick>? _picks;
    Curator? _mine;
    List<CuratorPick>? _myPicks;
    bool _mineLoaded, _busy;
    string? _error;

    public CuratorsPage(string? uid = null, string tab = "popular")
    {
        _uid = uid;
        _tab = tab;
        void Changed() => Dispatcher.UIThread.Post(() => { if (MainWindow.Current?.CurrentPage == this) Build(); });
        AttachedToVisualTree += (_, _) => Curators.Changed += Changed;
        DetachedFromVisualTree += (_, _) => Curators.Changed -= Changed;
    }

    public override void Build()
    {
        var body = _uid is null ? Main() : Detail(_uid);
        body.Margin = new Thickness(34, 28);
        body.MaxWidth = 980;
        body.HorizontalAlignment = HorizontalAlignment.Left;
        Content = new ScrollViewer { Content = body };
    }

    /// <summary>Загрузить в фоне и перерисовать; ошибка — строкой на странице.</summary>
    void Load(Func<Task> work)
    {
        if (_busy) return;
        _busy = true;
        _ = Task.Run(async () =>
        {
            string? error = null;
            try { await work(); }
            catch (Exception e) { error = Curators.Explain(e); }
            Dispatcher.UIThread.Post(() => { _busy = false; _error = error; Build(); });
        });
    }

    async void Run(Func<Task> work, string? ok = null)
    {
        try
        {
            await work();
            if (ok is not null) MainWindow.Current?.Toast(ok);
        }
        catch (Exception e) { MainWindow.Current?.Toast(Curators.Explain(e), bad: true); }
        Build();
    }

    // ---------------------------------------------------------------- главная: популярные, подписки, своя страница

    StackPanel Main()
    {
        var tabs = Ui.Row(6);
        foreach (var (id, key, icon) in new[] { ("popular", "v30.cur.popular", Icons.Star), ("following", "v30.cur.following", Icons.Heart), ("mine", "v30.cur.mine", Icons.User) })
        {
            var b = Ui.Button(I18n.T(key), () => { _tab = id; _error = null; Build(); }, _tab == id ? "primary" : "ghost", icon);
            tabs.Children.Add(b);
        }
        var body = Ui.Col(16,
            Ui.Col(4, Ui.Text(I18n.T("v30.cur.title"), "h1"), Ui.Text(I18n.T("v30.cur.lead"), "muted", wrap: true)),
            tabs);
        if (_error is not null) body.Children.Add(Ui.Card(Ui.Text(_error, "muted", wrap: true), 18));
        switch (_tab)
        {
            case "following": body.Children.Add(FollowingList()); break;
            case "mine": body.Children.Add(Mine()); break;
            default: body.Children.Add(PopularList()); break;
        }
        return body;
    }

    Control PopularList()
    {
        if (_popular is null)
        {
            if (_error is null) Load(async () => _popular = await Curators.Popular());
            return Ui.Text(I18n.T("v30.cur.loading"), "muted");
        }
        if (_popular.Count == 0) return Ui.Card(Ui.Text(I18n.T("v30.cur.none"), "muted", wrap: true), 18);
        var list = new StackPanel { Spacing = 8 };
        foreach (var c in _popular) list.Children.Add(CuratorRow(c));
        return list;
    }

    Control FollowingList()
    {
        var follow = Curators.Following;
        if (follow.Count == 0)
            return Ui.Card(Ui.Col(10, Ui.Text(I18n.T("v30.cur.following.empty"), "muted", wrap: true),
                Ui.Button(I18n.T("v30.cur.popular"), () => { _tab = "popular"; Build(); }, "primary", Icons.Star)), 18);
        var list = new StackPanel { Spacing = 8 };
        foreach (var (uid, name) in follow)
        {
            var known = _popular?.FirstOrDefault(c => c.Uid == uid);
            list.Children.Add(CuratorRow(known ?? new Curator(uid, name, "", -1, -1, null)));
        }
        return list;
    }

    static Control Avatar(string name, double size = 44) => new Border
    {
        Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = Ui.Res("BrandSoft"),
        Child = new TextBlock
        {
            Text = name.Length > 0 ? char.ToUpper(name[0], I18n.Culture).ToString() : "?", FontSize = size * 0.42, FontWeight = FontWeight.Bold,
            Foreground = Ui.Res("Brand2"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        },
    };

    static string Counts(Curator c) => c.Followers < 0 ? "" :
        I18n.T("v30.cur.counts", ("followers", c.Followers.ToString("N0", I18n.Culture)), ("games", c.Picks.ToString("N0", I18n.Culture)));

    Control FollowButton(Curator c)
    {
        if (c.Uid == Account.Uid) return Ui.Text(I18n.T("v30.cur.you"), "small muted");
        return Curators.Follows(c.Uid)
            ? Ui.Button(I18n.T("v30.cur.unfollow"), () => Run(() => Curators.Unfollow(c.Uid)), "ghost", Icons.Check)
            : Ui.Button(I18n.T("v30.cur.follow"), () => Run(() => Curators.Follow(c), I18n.T("v30.cur.followed", ("name", c.Name))), "primary", Icons.Plus);
    }

    Control CuratorRow(Curator c)
    {
        var buttons = Ui.Row(6, Ui.Button(I18n.T("v30.cur.open"), () => MainWindow.Current?.Navigate(() => new CuratorsPage(c.Uid)), "ghost", Icons.Eye), FollowButton(c));
        buttons.VerticalAlignment = VerticalAlignment.Center;
        var line = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right);
        line.Children.Add(buttons);
        var info = Ui.Col(2, Ui.Text(c.Name, "h3"));
        if (c.About != "") info.Children.Add(Ui.Text(c.About, "small muted", wrap: true));
        if (Counts(c) is { Length: > 0 } counts) info.Children.Add(Ui.Text(counts, "small muted"));
        line.Children.Add(Ui.Row(12, Avatar(c.Name), info));
        return Ui.Card(line, 14);
    }

    // ---------------------------------------------------------------- один куратор

    StackPanel Detail(string uid)
    {
        if (!_mineLoaded && _error is null)
        {
            _mineLoaded = true;
            Load(async () => { _curator = await Curators.Get(uid); _picks = await Curators.Picks(uid); });
        }
        var body = Ui.Col(16);
        if (_error is not null) { body.Children.Add(Ui.Card(Ui.Text(_error, "muted", wrap: true), 18)); return body; }
        if (_curator is null)
        {
            body.Children.Add(Ui.Text(_busy ? I18n.T("v30.cur.loading") : I18n.T("v30.cur.gone"), "muted"));
            return body;
        }
        var follow = FollowButton(_curator);
        follow.VerticalAlignment = VerticalAlignment.Center;
        follow.Margin = new Thickness(24, 0, 0, 0);
        var head = new DockPanel();
        DockPanel.SetDock(follow, Dock.Right);
        head.Children.Add(follow);
        var info = Ui.Col(4, Ui.Text(_curator.Name, "h1"));
        if (_curator.About != "") info.Children.Add(Ui.Text(_curator.About, "muted", wrap: true));
        info.Children.Add(Ui.Text(Counts(_curator), "small muted"));
        head.Children.Add(Ui.Row(16, Avatar(_curator.Name, 64), info));
        body.Children.Add(head);
        var picks = _picks ?? [];
        if (picks.Count == 0) body.Children.Add(Ui.Card(Ui.Text(I18n.T("v30.cur.noPicks"), "muted", wrap: true), 18));
        foreach (var p in picks) body.Children.Add(PickRow(p, null));
        return body;
    }

    /// <summary>Вердикт куратора цветной меткой.</summary>
    internal static Control Verdict(string verdict) => verdict switch
    {
        "yes" => ModRow.Tag("👍 " + I18n.T("v30.cur.v.yes"), Ui.Soft("#123524", "#43D17A"), Ui.Res("Good")),
        "no" => ModRow.Tag("👎 " + I18n.T("v30.cur.v.no"), Ui.Soft("#3A1616", "#F06A6A"), Ui.Res("Bad")),
        _ => ModRow.Tag("ℹ " + I18n.T("v30.cur.v.info"), Ui.Res("BrandSoft"), Ui.Res("Brand2")),
    };

    static LibGame? InLibrary(string gameKey) => AllGames.Current?.FirstOrDefault(g => GameReviews.Key(g) == gameKey);

    Control PickRow(CuratorPick p, Action? remove)
    {
        var title = Ui.Row(8, Ui.Text(p.Title, "h3"), Verdict(p.Verdict), Ui.Text(Ui.Ago(p.Updated), "small muted"));
        foreach (var c in title.Children) c.VerticalAlignment = VerticalAlignment.Center;
        var col = Ui.Col(6, title);
        if (p.Text != "") col.Children.Add(new SelectableTextBlock { Text = p.Text, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Res("Muted") });
        var line = new DockPanel();
        var buttons = Ui.Row(6);
        if (InLibrary(p.Game) is { } g) buttons.Children.Add(Ui.Button(I18n.T("v30.cur.inLibrary"), () => MainWindow.Current?.Navigate(() => new AllGamesPage(g.Key)), "ghost", Icons.Grid));
        if (remove is not null) buttons.Children.Add(Ui.Button("", remove, "icon ghost", Icons.Trash, I18n.T("v30.cur.remove")));
        buttons.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(buttons, Dock.Right);
        line.Children.Add(buttons);
        line.Children.Add(col);
        return Ui.Card(line, 14);
    }

    // ---------------------------------------------------------------- своя страница куратора

    Control Mine()
    {
        if (!Account.SignedIn)
            return Ui.Card(Ui.Col(10, Ui.Text(I18n.T("v30.cur.signIn"), "muted", wrap: true),
                Ui.Button(I18n.T("acc.signin.go"), () => MainWindow.Current?.Navigate(() => new AccountPage()), "primary", Icons.User)), 18);
        if (!_mineLoaded)
        {
            _mineLoaded = true;
            Load(async () =>
            {
                var uid = Account.Uid!;
                _mine = await Curators.Get(uid);
                _myPicks = _mine is null ? [] : await Curators.Picks(uid);
            });
            return Ui.Text(I18n.T("v30.cur.loading"), "muted");
        }
        if (_busy) return Ui.Text(I18n.T("v30.cur.loading"), "muted");

        var col = Ui.Col(16);
        var name = new TextBox { Text = _mine?.Name ?? Account.Get().Name ?? "", MaxLength = 32, Watermark = I18n.T("v30.cur.name"), Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
        var about = new TextBox { Text = _mine?.About ?? "", MaxLength = Curators.MaxAbout, Watermark = I18n.T("v30.cur.about"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 72, VerticalContentAlignment = VerticalAlignment.Top };
        var save = Ui.Button(_mine is null ? I18n.T("v30.cur.create") : I18n.T("v30.cur.save"), () => Run(async () =>
        {
            _mine = await Curators.SaveProfile(name.Text ?? "", about.Text ?? "", _mine);
            _myPicks ??= [];
        }, I18n.T("v30.cur.saved")), "primary", Icons.Check);
        save.HorizontalAlignment = HorizontalAlignment.Left;
        var profile = Ui.Col(10, Ui.Text(I18n.T("v30.cur.mine"), "h2"), Ui.Text(I18n.T("v30.cur.mine.lead"), "muted", wrap: true), name, about, save);
        if (_mine is not null) profile.Children.Insert(1, Ui.Text(Counts(_mine), "small muted"));
        col.Children.Add(Ui.Card(profile, 22));
        if (_mine is null) return col;

        col.Children.Add(AddPick());
        foreach (var p in _myPicks ?? [])
        {
            var pick = p;
            col.Children.Add(PickRow(pick, () => Run(async () =>
            {
                await Curators.RemovePick(pick.Game);
                _myPicks?.Remove(pick);
            }, I18n.T("v30.cur.removed"))));
        }
        return col;
    }

    /// <summary>Написать об игре из своей библиотеки: игра, вердикт и пара фраз.</summary>
    Control AddPick()
    {
        var games = (AllGames.Current ?? []).Where(g => !CustomGames.IsTool(g.Name))
            .OrderByDescending(g => AllGames.Played(g).Minutes).ThenBy(g => g.Name).Take(400).ToList();
        if (games.Count == 0)
            return Ui.Card(Ui.Text(I18n.T("v30.cur.noGames"), "muted", wrap: true), 18);
        var pick = new ComboBox { ItemsSource = games.Select(g => g.Name).ToList(), SelectedIndex = 0, MinWidth = 320, MaxDropDownHeight = 360 };
        var verdict = "yes";
        var verdicts = Ui.Row(6);
        void RenderVerdicts()
        {
            verdicts.Children.Clear();
            foreach (var v in Curators.Verdicts)
                verdicts.Children.Add(Ui.Button(I18n.T("v30.cur.v." + v), () => { verdict = v; RenderVerdicts(); }, verdict == v ? "primary" : "ghost"));
        }
        RenderVerdicts();
        var text = new TextBox { MaxLength = Curators.MaxText, Watermark = I18n.T("v30.cur.text"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 72, VerticalContentAlignment = VerticalAlignment.Top };
        void Fill()
        {
            if (pick.SelectedIndex < 0) return;
            var existing = _myPicks?.FirstOrDefault(p => p.Game == GameReviews.Key(games[pick.SelectedIndex]));
            text.Text = existing?.Text ?? "";
            verdict = existing?.Verdict ?? "yes";
            RenderVerdicts();
        }
        pick.SelectionChanged += (_, _) => Fill();
        var publish = Ui.Button(I18n.T("v30.cur.publish"), () =>
        {
            if (pick.SelectedIndex < 0) return;
            var g = games[pick.SelectedIndex];
            var key = GameReviews.Key(g);
            var isNew = _myPicks?.Any(p => p.Game == key) != true;
            var v = verdict;
            var t = text.Text ?? "";
            Run(async () =>
            {
                await Curators.SavePick(g, v, t, isNew);
                _myPicks = await Curators.Picks(Account.Uid!);
            }, I18n.T("v30.cur.published"));
        }, "primary", Icons.Upload);
        publish.HorizontalAlignment = HorizontalAlignment.Left;
        return Ui.Card(Ui.Col(10, Ui.Text(I18n.T("v30.cur.add"), "h2"), pick, verdicts, text, publish), 22);
    }

    /// <summary>Снимки: страница одного куратора (демо-данные).</summary>
    internal static CuratorsPage Demo(string uid) => new(uid);
}
