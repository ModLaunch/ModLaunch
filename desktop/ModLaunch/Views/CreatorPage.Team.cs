using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Games;
using ModLaunch.Social;

namespace ModLaunch.Views;

/// <summary>Цвета групп: тег, полоса и точка участника.</summary>
public static class TeamLook
{
    public static IBrush Brush(string color) => Ui.Hex(color switch
    {
        "blue" => "#4C8DFF", "green" => "#2FB673", "orange" => "#F08A3C", "pink" => "#E8609E",
        "cyan" => "#22B8CF", "red" => "#E5484D", "yellow" => "#D9A520", _ => "#8B6CFF",
    });

    /// <summary>Тег группы плашкой: [NITE] на цвете группы.</summary>
    public static Control Tag(Team t, double size = 10.5) => new Border
    {
        Background = Brush(t.Color), CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 2), VerticalAlignment = VerticalAlignment.Center,
        Child = HubLook.MonoText(t.Tag, size, Brushes.White),
    };
}

/// <summary>
/// 13.0: раздел «Команда» — группы разработчиков на аккаунте. Нет группы — «Создать группу» и «Войти по коду»;
/// есть — карточка группы (тег, код входа, участники, приглашения) и общие проекты: выложить свой, взять себе, обновить.
/// </summary>
public sealed partial class CreatorPage
{
    static string? _teamId;
    static readonly Dictionary<string, List<TeamProject>> _teamProjects = [];
    static string? _teamError;
    static bool _teamLoading, _teamAsked;

    /// <summary>Главная кнопка раздела: «Создать группу» (если вошли).</summary>
    Control? TeamAction() => Account.SignedIn || Program.Demo && Teams.DemoMine is not null
        ? Ui.Button(I18n.T("v13.team.create"), () => TeamDialog(null), "primary", Icons.Plus)
        : null;

    Control Team()
    {
        var demo = Program.Demo && Teams.DemoMine is not null;
        if (!demo && !Account.SignedIn) return TeamSignIn();
        if (!demo && !Teams.Loaded && !_teamAsked) { _teamAsked = true; _ = LoadTeams(); }
        Teams.Changed -= OnTeams;
        Teams.Changed += OnTeams;

        var col = Ui.Col(18);
        if (_teamError is not null) col.Children.Add(Ui.Card(Ui.Row(10, Ui.Icon(Icons.Alert, 16, Ui.Res("Warn")), Ui.Text(_teamError, "small", wrap: true)), 14));
        foreach (var inv in Teams.Invites) col.Children.Add(InviteCard(inv));
        var mine = Teams.Mine;
        if (mine.Count == 0)
        {
            col.Children.Add(_teamLoading ? Ui.Text(I18n.T("common.loading"), "muted") : TeamEmpty());
            return col;
        }
        var team = mine.FirstOrDefault(t => t.Id == _teamId) ?? mine[0];
        _teamId = team.Id;
        if (mine.Count > 1)
        {
            var chips = new WrapPanel();
            foreach (var t in mine)
            {
                var one = t;
                var chip = Ui.Button("", () => { _teamId = one.Id; Build(); }, "chip");
                chip.Content = Ui.Row(8, TeamLook.Tag(one, 9.5), new TextBlock { Text = one.Name, VerticalAlignment = VerticalAlignment.Center });
                chip.Margin = new Thickness(0, 0, 8, 8);
                if (one.Id == team.Id) chip.Classes.Add("active");
                chips.Children.Add(chip);
            }
            col.Children.Add(chips);
        }
        col.Children.Add(TeamBanner(team));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,340"), ColumnSpacing = 16 };
        var projects = TeamProjects(team);
        var people = TeamPeople(team);
        grid.Children.Add(projects);
        Grid.SetColumn(people, 1);
        grid.Children.Add(people);
        col.Children.Add(grid);
        col.Children.Add(JoinRow());
        return col;
    }

    void OnTeams()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (MainWindow.Current?.CurrentPage == this && _tab == "team") Build(); });
    }

    async Task LoadTeams()
    {
        _teamLoading = true;
        try { await Teams.Refresh(); _teamError = null; }
        catch (Exception e) { _teamError = Teams.Explain(e); }
        finally { _teamLoading = false; }
        if (MainWindow.Current?.CurrentPage == this && _tab == "team") Build();
    }

    async Task LoadTeamProjects(Team t)
    {
        try { _teamProjects[t.Id] = await Teams.Projects(t); }
        catch (Exception e) { _teamProjects[t.Id] = []; _teamError = Teams.Explain(e); }
        if (MainWindow.Current?.CurrentPage == this && _tab == "team") Build();
    }

    /// <summary>Выполнить действие с группой: ошибка — тостом, потом перестроить экран.</summary>
    async void Do(Func<Task> work, string? done = null)
    {
        try
        {
            await work();
            _teamError = null;
            if (done is not null) MainWindow.Current?.Toast(done);
        }
        catch (Exception e) { MainWindow.Current?.Toast(Teams.Explain(e), bad: true); }
        if (MainWindow.Current?.CurrentPage == this) Build();
    }

    static Control TeamSignIn()
    {
        var icon = new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(14), Background = Ui.Res("BrandSoft"), Child = Ui.Icon(Icons.Users, 24, Ui.Res("Brand2")), VerticalAlignment = VerticalAlignment.Top };
        var words = Ui.Col(6, Ui.Text(I18n.T("v13.team.signin.title"), "h3"), Ui.Text(I18n.T("v13.team.signin.text"), "muted", wrap: true),
            Ui.Row(10, Ui.Button(I18n.T("v11.cm.signin.go"), () => MainWindow.Current?.Navigate(() => new AccountPage()), "primary", Icons.User)));
        var row = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        icon.Margin = new Thickness(0, 0, 16, 0);
        row.Children.Add(icon);
        row.Children.Add(words);
        return HubLook.Frame(row, new Thickness(22));
    }

    /// <summary>Группы ещё нет: три шага словами и два пути — создать свою или войти по коду.</summary>
    Control TeamEmpty()
    {
        var steps = Ui.Col(10);
        for (var i = 1; i <= 3; i++)
            steps.Children.Add(Ui.Row(12, new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(3), BorderBrush = HubLook.Edge, BorderThickness = new Thickness(1),
                Child = HubLook.MonoText("0" + i, 11, HubLook.Violet), VerticalAlignment = VerticalAlignment.Center,
            }, new TextBlock { Text = I18n.T($"v13.team.step{i}"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }));
        steps.Children[0].HorizontalAlignment = HorizontalAlignment.Left;
        var left = Ui.Col(14,
            HubLook.Head(I18n.T("v13.team.eyebrow"), I18n.T("v13.team.empty.title")),
            Ui.Text(I18n.T("v13.team.empty.text"), "muted", wrap: true),
            steps,
            Ui.Row(8, Ui.Button(I18n.T("v13.team.create"), () => TeamDialog(null), "primary", Icons.Plus)));
        var right = Ui.Col(10, HubLook.Eyebrow(I18n.T("v13.team.join.eyebrow")), Ui.Text(I18n.T("v13.team.join.text"), "small muted", wrap: true), JoinBox());
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,340"), ColumnSpacing = 16 };
        grid.Children.Add(HubLook.Frame(left, new Thickness(22, 20)));
        var r = HubLook.Frame(right, new Thickness(18, 16));
        Grid.SetColumn(r, 1);
        grid.Children.Add(r);
        return grid;
    }

    Control JoinBox()
    {
        var box = new TextBox { Watermark = "ABCD-EFGH", MaxLength = 12, FontFamily = HubLook.Mono, Width = 170 };
        var go = Ui.Button(I18n.T("v13.team.join"), () =>
        {
            var code = box.Text;
            Do(async () => { var t = await Teams.Join(code ?? ""); _teamId = t.Id; }, I18n.T("v13.team.joined"));
        }, "", Icons.ArrowUp);
        return Ui.Row(8, box, go);
    }

    /// <summary>Внизу экрана группы: «Есть код другой группы?» — тихой строкой.</summary>
    Control JoinRow()
    {
        var row = Ui.Row(12, HubLook.MonoText(I18n.T("v13.team.join.more"), 11), JoinBox());
        row.Margin = new Thickness(0, 6, 0, 0);
        return row;
    }

    Control InviteCard(Team t)
    {
        var words = Ui.Row(10, TeamLook.Tag(t), new TextBlock { Text = I18n.T("v13.team.invited", ("name", t.Name), ("by", t.NameOf(t.Owner))), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        var actions = Ui.Row(8,
            Ui.Button(I18n.T("v13.team.decline"), () => Do(() => Teams.Decline(t)), "ghost"),
            Ui.Button(I18n.T("v13.team.accept"), () => Do(async () => { var j = await Teams.Accept(t); _teamId = j.Id; }, I18n.T("v13.team.joined")), "primary", Icons.Check));
        var dock = new DockPanel();
        DockPanel.SetDock(actions, Dock.Right);
        dock.Children.Add(actions);
        words.VerticalAlignment = VerticalAlignment.Center;
        dock.Children.Add(words);
        return HubLook.Frame(dock, new Thickness(16, 12));
    }

    /// <summary>Карточка группы: полоса цвета, тег и название, описание, код входа; справа — меню владельца или «Выйти».</summary>
    Control TeamBanner(Team t)
    {
        var me = Teams.Me;
        var owner = me == t.Owner;
        var title = Ui.Row(12, TeamLook.Tag(t, 12), new TextBlock { Text = t.Name, FontFamily = Look.Display, FontSize = 24, FontWeight = FontWeight.Bold, LetterSpacing = -0.5, VerticalAlignment = VerticalAlignment.Center });
        var facts = HubLook.MonoText(I18n.T("v13.team.facts", ("n", t.Members.Count), ("max", Teams.MaxMembers), ("owner", t.NameOf(t.Owner))), 11);
        var words = Ui.Col(6, title, facts);
        if (t.About != "") words.Children.Add(Ui.Text(t.About, "muted", wrap: true));

        var code = Ui.Row(8, HubLook.Eyebrow(I18n.T("v13.team.code")), new SelectableTextBlock { Text = Teams.ShowCode(t.Code), FontFamily = HubLook.Mono, FontSize = 16, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center },
            Ui.Button("", () => { MainWindow.Current?.Clipboard?.SetTextAsync(Teams.ShowCode(t.Code)); MainWindow.Current?.Toast(I18n.T("ctx.copied")); }, "icon ghost", Icons.Copy, I18n.T("v13.team.code.copy")));
        ToolTip.SetTip(code, I18n.T("v13.team.code.tip"));
        var menu = owner
            ? Ctx.Menu(
                Ctx.Item(I18n.T("v13.team.edit"), Icons.Edit, () => TeamDialog(t)),
                Ctx.Item(I18n.T("v13.team.newCode"), Icons.Refresh, () => Do(() => Teams.NewCode(t), I18n.T("v13.team.newCode.done"))),
                "-",
                Ctx.Item(I18n.T("v13.team.delete"), Icons.Trash, () => Confirm(I18n.T("v13.team.delete"), I18n.T("v13.team.delete.text", ("name", t.Name)), () => Do(() => Teams.Leave(t)))))
            : Ctx.Menu(Ctx.Item(I18n.T("v13.team.leave"), Icons.Back, () => Confirm(I18n.T("v13.team.leave"), I18n.T("v13.team.leave.text", ("name", t.Name)), () => Do(() => Teams.Leave(t)))));
        var more = Ui.Button("", () => { }, "icon ghost", Icons.More, I18n.T("v13.team.more"));
        more.Flyout = menu;
        var right = Ui.Row(10, code, more);
        right.VerticalAlignment = VerticalAlignment.Top;
        var dock = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(right);
        dock.Children.Add(words);
        var stripe = new Border { Height = 3, Background = TeamLook.Brush(t.Color), Margin = new Thickness(-22, -20, -22, 16) };
        return HubLook.Frame(Ui.Col(0, stripe, dock), new Thickness(22, 20));
    }

    static void Confirm(string title, string text, Action yes)
    {
        var w = MainWindow.Current!;
        w.Dialog(title, Ui.Text(text, "muted", wrap: true), Ui.Button(I18n.T("common.cancel"), w.CloseDialog), Ui.Button(title, () => { w.CloseDialog(); yes(); }, "primary"));
    }

    /// <summary>Проекты команды: выложенные участниками; «Выложить проект» — свой проект в команду.</summary>
    Control TeamProjects(Team t)
    {
        if (!_teamProjects.ContainsKey(t.Id)) { _teamProjects[t.Id] = []; _ = LoadTeamProjects(t); }
        var list = _teamProjects[t.Id];
        var share = Ui.Button(I18n.T("v13.team.share"), () => { }, "", Icons.Upload);
        var pick = new MenuFlyout();
        var own = Projects.List().OrderByDescending(p => p.Updated).Take(20).ToList();
        if (own.Count == 0) pick.Items.Add(new MenuItem { Header = I18n.T("v13.team.share.none"), IsEnabled = false });
        foreach (var p in own)
        {
            var project = p;
            var item = new MenuItem { Header = $"{p.Name}  ·  {GameCatalog.ById(p.Game)?.ShortName ?? p.Game}" };
            item.Click += (_, _) => Do(async () => { await Teams.Share(t, project); _teamProjects.Remove(t.Id); }, I18n.T("v13.team.shared", ("name", project.Name)));
            pick.Items.Add(item);
        }
        share.Flyout = pick;
        var body = Ui.Col(2);
        if (list.Count == 0) body.Children.Add(Ui.Text(I18n.T("v13.team.projects.empty"), "muted", wrap: true));
        var me = Teams.Me;
        var mineIds = Projects.List().Select(Teams.ProjectId).ToHashSet();
        foreach (var p in list)
        {
            var project = p;
            var game = GameCatalog.ById(p.Game);
            var words = Ui.Col(1, new TextBlock { Text = p.Name, FontWeight = FontWeight.SemiBold, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = $"{game?.Name ?? p.Game} · {I18n.T("v13.team.by", ("name", p.ByName))}", FontSize = 12, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis });
            words.VerticalAlignment = VerticalAlignment.Center;
            var have = mineIds.Contains(p.Id);
            var take = Ui.Button(I18n.T(have ? "v13.team.pull" : "v13.team.take"), () =>
            {
                var local = Teams.Take(project);
                MainWindow.Current?.Toast(I18n.T(have ? "v13.team.pulled" : "v13.team.taken", ("name", project.Name)));
                OpenProject(local);
            }, have ? "ghost" : "", have ? Icons.Refresh : Icons.Download);
            ToolTip.SetTip(take, I18n.T(have ? "v13.team.pull.tip" : "v13.team.take.tip"));
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,80,100,Auto"), ColumnSpacing = 12, Margin = new Thickness(0, 6) };
            row.Children.Add(words);
            var version = HubLook.MonoText(p.Version == "" ? "—" : "v" + p.Version);
            version.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(version, 1);
            row.Children.Add(version);
            var when = HubLook.MonoText(p.Updated is { } u ? Ui.Ago(u.ToLocalTime()) : "", 11);
            when.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(when, 2);
            row.Children.Add(when);
            Grid.SetColumn(take, 3);
            row.Children.Add(take);
            if (p.By == me || t.Owner == me)
                Ctx.Attach(row, () => Ctx.Menu(Ctx.Item(I18n.T("v13.team.unshare"), Icons.Trash, () => Do(async () => { await Teams.Unshare(t, project); _teamProjects.Remove(t.Id); }))));
            body.Children.Add(row);
        }
        return HubLook.Frame(Ui.Col(12, HubLook.Head(I18n.T("v13.team.projects.eyebrow"), I18n.T("v13.team.projects"), share), body), new Thickness(18, 16));
    }

    /// <summary>Участники: владелец первым, у владельца — «убрать»; ниже — приглашённые и «Пригласить друга».</summary>
    Control TeamPeople(Team t)
    {
        var me = Teams.Me;
        var owner = me == t.Owner;
        var body = Ui.Col(4);
        foreach (var uid in t.Members.OrderBy(u => u == t.Owner ? 0 : 1))
        {
            var who = uid;
            var name = t.NameOf(uid);
            var badge = uid == t.Owner ? HubLook.MonoText(I18n.T("v13.team.role.owner"), 10, HubLook.Violet) : uid == me ? HubLook.MonoText(I18n.T("v13.team.role.you"), 10) : null;
            var line = Ui.Row(10, ProfileKit.Avatar(name, 28), new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            if (badge is not null) { badge.VerticalAlignment = VerticalAlignment.Center; line.Children.Add(badge); }
            var dock = new DockPanel { Margin = new Thickness(0, 3) };
            if (owner && uid != t.Owner)
            {
                var kick = Ui.Button("", () => Confirm(I18n.T("v13.team.kick"), I18n.T("v13.team.kick.text", ("name", name)), () => Do(() => Teams.Kick(t, who))), "icon ghost", Icons.Close, I18n.T("v13.team.kick"));
                DockPanel.SetDock(kick, Dock.Right);
                dock.Children.Add(kick);
            }
            dock.Children.Add(line);
            body.Children.Add(dock);
        }
        var friends = Friends.View().Friends;
        foreach (var uid in t.Invites)
        {
            var who = uid;
            var name = friends.FirstOrDefault(f => f.Uid == uid)?.Name ?? "…";
            var line = Ui.Row(10, ProfileKit.Avatar(name, 28), new TextBlock { Text = name, Foreground = Ui.Res("Muted"), VerticalAlignment = VerticalAlignment.Center }, HubLook.MonoText(I18n.T("v13.team.role.invited"), 10));
            line.Opacity = 0.8;
            var dock = new DockPanel { Margin = new Thickness(0, 3) };
            if (owner)
            {
                var cancel = Ui.Button("", () => Do(() => Teams.Kick(t, who)), "icon ghost", Icons.Close, I18n.T("v13.team.uninvite"));
                DockPanel.SetDock(cancel, Dock.Right);
                dock.Children.Add(cancel);
            }
            dock.Children.Add(line);
            body.Children.Add(dock);
        }
        Control? invite = null;
        if (owner)
        {
            var b = Ui.Button(I18n.T("v13.team.invite"), () => { }, "", Icons.Plus);
            var pick = new MenuFlyout();
            var candidates = friends.Where(f => !t.Members.Contains(f.Uid) && !t.Invites.Contains(f.Uid)).ToList();
            if (candidates.Count == 0) pick.Items.Add(new MenuItem { Header = I18n.T("v13.team.invite.none"), IsEnabled = false });
            foreach (var f in candidates)
            {
                var friend = f;
                var item = new MenuItem { Header = f.Name };
                item.Click += (_, _) => Do(() => Teams.Invite(t, friend.Uid), I18n.T("v13.team.invite.sent", ("name", friend.Name)));
                pick.Items.Add(item);
            }
            b.Flyout = pick;
            invite = b;
        }
        var head = invite is null ? HubLook.Head(I18n.T("v13.team.people.eyebrow"), I18n.T("v13.team.people")) : HubLook.Head(I18n.T("v13.team.people.eyebrow"), I18n.T("v13.team.people"), invite);
        var col = Ui.Col(12, head, body);
        if (owner) col.Children.Add(Ui.Text(I18n.T("v13.team.people.hint"), "small muted", wrap: true));
        return HubLook.Frame(col, new Thickness(18, 16));
    }

    /// <summary>«Создать группу» / «Изменить группу»: название, тег (сам из названия, пока его не трогали), цвет и пара слов.</summary>
    void TeamDialog(Team? edit)
    {
        var w = MainWindow.Current;
        if (w is null) return;
        var name = new TextBox { Text = edit?.Name ?? "", MaxLength = Teams.MaxName, Watermark = I18n.T("v13.team.d.name.hint") };
        var tag = new TextBox { Text = edit?.Tag ?? "", MaxLength = 5, FontFamily = HubLook.Mono, Width = 120, HorizontalAlignment = HorizontalAlignment.Left, Watermark = "NITE" };
        var tagTouched = edit is not null;
        name.TextChanged += (_, _) => { if (!tagTouched) tag.Text = Teams.SuggestTag(name.Text ?? ""); };
        tag.TextChanged += (_, _) => { if (tag.IsFocused) tagTouched = true; };
        var about = new TextBox { Text = edit?.About ?? "", MaxLength = Teams.MaxAbout, AcceptsReturn = false, Watermark = I18n.T("v13.team.d.about.hint") };
        var color = edit?.Color ?? Teams.Colors[0];
        var dots = new WrapPanel();
        void Dots()
        {
            dots.Children.Clear();
            foreach (var c in Teams.Colors)
            {
                var one = c;
                var dot = new Border
                {
                    Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Margin = new Thickness(0, 0, 8, 0), Background = TeamLook.Brush(one),
                    BorderBrush = one == color ? Ui.Res("Text") : Brushes.Transparent, BorderThickness = new Thickness(2.5),
                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                };
                StoreKit.OnClick(dot, () => { color = one; Dots(); });
                dots.Children.Add(dot);
            }
        }
        Dots();
        var error = Ui.Text("", "small", wrap: true, color: Ui.Res("Bad"));
        var body = Ui.Col(14,
            Ui.Col(6, Ui.Text(I18n.T("v13.team.d.name"), "small muted"), name),
            Ui.Col(6, Ui.Text(I18n.T("v13.team.d.tag"), "small muted"), tag),
            Ui.Col(6, Ui.Text(I18n.T("v13.team.d.color"), "small muted"), dots),
            Ui.Col(6, Ui.Text(I18n.T("v13.team.d.about"), "small muted"), about),
            error);
        Button? save = null;
        save = Ui.Button(edit is null ? I18n.T("v13.team.create") : I18n.T("common.save"), async () =>
        {
            var draft = new TeamDraft(name.Text ?? "", tag.Text ?? "", color, about.Text ?? "");
            if (Teams.Check(draft) is [var first, ..]) { error.Text = I18n.T(first, ("n", Teams.MaxName)); return; }
            save!.IsEnabled = false;
            try
            {
                var t = edit is null ? await Teams.Create(draft) : await Teams.Edit(edit, draft);
                _teamId = t.Id;
                w.CloseDialog();
                if (edit is null) w.Toast(I18n.T("v13.team.created", ("name", t.Name)));
                _tab = "team";
                Build();
            }
            catch (Exception e) { error.Text = Teams.Explain(e); save.IsEnabled = true; }
        }, "primary", edit is null ? Icons.Plus : Icons.Check);
        w.Dialog(edit is null ? I18n.T("v13.team.create") : I18n.T("v13.team.edit"), body, 480, Ui.Button(I18n.T("common.cancel"), w.CloseDialog), save);
    }
}
