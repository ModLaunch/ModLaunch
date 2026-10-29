using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>
/// Главное окно, часть «игра и аккаунт»: экран запуска поверх всего окна, кнопка
/// аккаунта внизу боковой панели с меню и помощник, когда игра вылетела.
/// </summary>
public sealed partial class MainWindow
{
    readonly Panel _launchLayer = new() { IsVisible = false };
    readonly Button _accountButton = new()
    {
        Classes = { "rail" }, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
    };
    readonly Border _accountPanel = new()
    {
        Classes = { "card" }, Width = 300, Padding = new Thickness(18), Margin = new Thickness(88, 0, 0, 16),
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, IsVisible = false,
        BoxShadow = BoxShadows.Parse("0 18 50 0 #80000000"),
    };

    void InitPlay()
    {
        _accountButton.Click += (_, _) => ToggleAccountPanel();
        Social.Account.Changed += () => Dispatcher.UIThread.Post(() => { RenderAccount(); if (_accountPanel.IsVisible) RenderAccountPanel(); });
        Features.Cosmetics.Changed += () => Dispatcher.UIThread.Post(RenderAccount);
    }

    // ---------------------------------------------------------------- экран запуска

    public LaunchScreen ShowLaunch(GameState g)
    {
        var screen = new LaunchScreen(g);
        screen.Closed += HideLaunch;
        // 12.0: прежний экран запуска останавливаем, а не просто убираем — иначе он доведёт свой запуск до конца.
        foreach (var old in _launchLayer.Children.OfType<LaunchScreen>().ToList()) { old.Closed -= HideLaunch; old.Close(); }
        _launchLayer.Children.Clear();
        _launchLayer.Children.Add(screen);
        _launchLayer.IsVisible = true;
        return screen;
    }

    public void HideLaunch()
    {
        _launchLayer.IsVisible = false;
        _launchLayer.Children.Clear();
    }

    // ---------------------------------------------------------------- аккаунт

    /// <summary>Кнопка аккаунта на боковой панели: аватар с зелёной точкой или значок «войти».</summary>
    public void RefreshAccount() => RenderAccount();

    void RenderAccount()
    {
        var p = Social.Account.Get();
        Control avatar;
        string name;
        if (p.SignedIn)
        {
            name = p.Name ?? p.Email ?? "?";
            var online = new Border
            {
                Width = 11, Height = 11, CornerRadius = new CornerRadius(6), Background = Ui.Res("Good"),
                BorderBrush = Ui.Res("Rail"), BorderThickness = new Thickness(2),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            };
            // 9.9: аватар в надетой рамке (награда за достижения).
            var size = _sideNarrow ? 38 : 30;
            avatar = new Panel { Width = size, Height = size, Children = { ProfileKit.Avatar(name, size, ring: _sideNarrow ? 3 : 2), online } };
        }
        else
        {
            name = I18n.T("acc.menu.signin");
            var size = _sideNarrow ? 36 : 30;
            avatar = new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(size / 2.0), Background = Ui.Res("Surface2"), BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1),
                Child = Ui.Icon(Icons.User, 16, Ui.Res("Muted")),
            };
        }
        if (_sideNarrow)
        {
            _accountButton.Content = avatar;
            ToolTip.SetTip(_accountButton, name);
        }
        else
        {
            // 2.0: на широкой панели — аватар, имя и уровень одной строкой (уровень — золотом, как на пути наград).
            var (level, _, _) = RoadKit.Now();
            var words = Ui.Col(0,
                new TextBlock { Text = p.SignedIn ? name : ProfileKit.PlayerName(), FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Ui.Res("Text"), TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = p.SignedIn ? I18n.T("v16.road.lv", ("n", level)) : name, FontSize = Phi.Caption, Foreground = p.SignedIn ? Ui.Res("Gold") : Ui.Res("Faint"), TextTrimming = TextTrimming.CharacterEllipsis });
            words.VerticalAlignment = VerticalAlignment.Center;
            avatar.Margin = new Thickness(0, 0, Phi.S13 - 3, 0);
            var row = new DockPanel { LastChildFill = true };
            var more = Ui.Icon(Icons.More, 15, Ui.Res("Faint"));
            DockPanel.SetDock(more, Dock.Right);
            DockPanel.SetDock(avatar, Dock.Left);
            row.Children.Add(more);
            row.Children.Add(avatar);
            row.Children.Add(words);
            _accountButton.Content = row;
            ToolTip.SetTip(_accountButton, null);
        }
        _accountButton.Foreground = Ui.Res("Muted");
        _accountButton.Classes.Set("active", _accountPanel.IsVisible);
    }

    void RenderAccountPanel()
    {
        var p = Social.Account.Get();
        var col = Ui.Col(12);
        // 9.9: сверху меню — карточка игрока: баннер, аватар в рамке, титул и уровень (и у гостя тоже).
        var all = Features.Achievements.All();
        var (level, _, next) = Features.Achievements.Level(all);
        var name = p.SignedIn ? p.Name ?? p.Email ?? "?" : I18n.T("acc.menu.guest");
        var words = Ui.Col(3,
            new TextBlock { Text = name, FontWeight = FontWeight.Bold, FontSize = 16, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis },
            ProfileKit.Title(size: 11),
            new TextBlock { Text = p.SignedIn ? p.Email ?? "" : I18n.T("v99.acc.level", ("n", level)), FontSize = 11.5, Foreground = Ui.Hex("#D6D9E3"), TextTrimming = TextTrimming.CharacterEllipsis });
        words.VerticalAlignment = VerticalAlignment.Center;
        var bar = new ProgressBar { Minimum = 0, Maximum = 300, Value = 300 - next, Height = 4, Margin = new Thickness(14, 0, 14, 12), VerticalAlignment = VerticalAlignment.Bottom };
        var cardGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12, Margin = new Thickness(14, 16, 14, 22) };
        cardGrid.Children.Add(ProfileKit.Avatar(p.SignedIn ? name : "?", 54, ring: 3));
        Grid.SetColumn(words, 1);
        cardGrid.Children.Add(words);
        var card = new Border { CornerRadius = new CornerRadius(14), ClipToBounds = true, Height = 104, Margin = new Thickness(-4, -4, -4, 0), Child = new Panel { Children = { ProfileKit.Banner(), cardGrid, bar } }, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
        StoreKit.OnClick(card, () => { CloseAccountPanel(); Navigate(() => new ProfilePage()); });
        col.Children.Add(card);
        col.Children.Add(MenuRow(I18n.T("v99.home.profile"), Icons.Trophy, () => Navigate(() => new ProfilePage())));
        if (p.SignedIn)
        {
            col.Children.Add(MenuRow(I18n.T("acc.menu.account"), Icons.User, () => Navigate(() => new AccountPage())));
            col.Children.Add(MenuRow(I18n.T("acc.s.wallet"), Icons.Bag, () => Navigate(() => new AccountPage("wallet"))));
            col.Children.Add(MenuRow(I18n.T("acc.s.purchases"), Icons.Download, () => Navigate(() => new AccountPage("purchases"))));
            col.Children.Add(MenuRow(I18n.T("v20.acc.earnings"), Icons.Chart, () => Navigate(() => new AccountPage("earnings"))));
            col.Children.Add(MenuRow(I18n.T("acc.menu.friends"), Icons.Users, () => Navigate(() => new CommunityPage())));
            var signOut = MenuRow(I18n.T("acc.menu.signout"), Icons.Power, () =>
            {
                Social.Account.SignOut();
                RenderAccount();
                Toast(I18n.T("acc.menu.signedOut"));
            });
            signOut.Foreground = Ui.Res("Bad");
            col.Children.Add(signOut);
        }
        else
        {
            col.Children.Add(Ui.Text(I18n.T("acc.menu.guest.text"), "small muted", wrap: true));
            var signIn = Ui.Button(I18n.T("acc.menu.signin"), () => { CloseAccountPanel(); Navigate(() => new AccountPage()); }, "primary", Icons.Lock);
            var signUp = Ui.Button(I18n.T("acc.menu.signup"), () => { CloseAccountPanel(); Navigate(() => new AccountPage()); }, "", Icons.User);
            foreach (var b in new[] { signIn, signUp }) { b.HorizontalAlignment = HorizontalAlignment.Stretch; b.HorizontalContentAlignment = HorizontalAlignment.Center; }
            col.Children.Add(signIn);
            col.Children.Add(signUp);
        }
        _accountPanel.Child = col;
    }

    Button MenuRow(string text, string icon, Action action)
    {
        var b = Ui.Button(text, () => { CloseAccountPanel(); action(); }, "ghost", icon);
        b.HorizontalAlignment = HorizontalAlignment.Stretch;
        b.HorizontalContentAlignment = HorizontalAlignment.Left;
        return b;
    }

    void ToggleAccountPanel()
    {
        if (_accountPanel.IsVisible) { CloseAccountPanel(); return; }
        OpenAccountPanel();
    }

    public void OpenAccountPanel()
    {
        RenderAccountPanel();
        _accountPanel.IsVisible = true;
        _bellPanel.IsVisible = false;
        _downloadsPanel.IsVisible = false;
        RenderAccount();
    }

    public void CloseAccountPanel()
    {
        if (!_accountPanel.IsVisible) return;
        _accountPanel.IsVisible = false;
        RenderAccount();
    }

    // ---------------------------------------------------------------- помощник при вылете

    /// <summary>Мод, на который ругается лог: имя, первая строка ошибки и id в реестре (чтобы выключить).</summary>
    public sealed record Culprit(string Name, string Message, string? ModId);

    /// <summary>
    /// Игра закрылась сама в первые полторы минуты — смотрим свежий лог загрузчика.
    /// Нашлись моды с ошибками — предлагаем выключить их и запустить снова.
    /// </summary>
    async Task CheckCrash(GameState g, Launcher.ExitInfo exit)
    {
        if (g.Path is null) return;
        await Task.Delay(700);
        var path = g.Path;
        var report = await Task.Run(() => Logs.Read(g.Def, path));
        var fresh = report.Available && report.Modified is DateTime m && m >= exit.Started.ToLocalTime().AddSeconds(-5);
        var culprits = fresh ? Culprits(g, report.Issues) : [];
        // Безобидные ошибки в логе бывают всегда: без кода ошибки или совсем короткого сеанса
        // это обычный выход из игры, а не вылет.
        var crashed = exit.Code is int code && code != 0;
        var suspicious = culprits.Count > 0 ? crashed || exit.Ms < 30_000 : crashed && exit.Ms < 30_000;
        if (!suspicious) return;
        ShowCrash(g, exit.Ms, exit.Code, culprits);
    }

    public static List<Culprit> Culprits(GameState g, List<LogIssue> issues)
    {
        static string Norm(string? s) => new string((s ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        var mods = g.Registry?.List().Where(m => m.Bool("enabled", true) && !m.Bool("missing") && m.Str("kind") != "preset").ToList() ?? [];
        var result = new List<Culprit>();
        foreach (var issue in issues)
        {
            var n = Norm(issue.Mod);
            if (n.Length < 3) continue;
            var mod = mods.FirstOrDefault(m =>
            {
                var name = Norm(m.Str("name"));
                var tail = Norm((m.Str("id") ?? "").Split('-').Last());
                return name == n || tail == n || (name.Length >= 4 && (n.Contains(name) || name.Contains(n)));
            });
            if (mod is null) continue;
            var id = mod.Str("id");
            if (result.Any(c => c.ModId == id)) continue;
            var message = issue.Message.Length > 160 ? issue.Message[..160] + "…" : issue.Message;
            result.Add(new Culprit(mod.Str("name") ?? issue.Mod, message, id));
        }
        return result.Take(5).ToList();
    }

    public void ShowCrash(GameState g, long ms, int? code, List<Culprit> culprits)
    {
        var body = Ui.Col(12, Ui.Text(I18n.T("crash.time", ("time", PlayControls.Clock(TimeSpan.FromMilliseconds(ms)))), "muted", wrap: true));
        if (culprits.Count > 0)
        {
            body.Children.Add(Ui.Text(I18n.T("crash.mods"), "h3"));
            var list = Ui.Col(8);
            foreach (var c in culprits)
                list.Children.Add(new Border
                {
                    Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10),
                    Child = Ui.Col(4,
                        Ui.Row(8, Ui.Icon(Icons.Alert, 15, Ui.Res("Warn")), Ui.Text(c.Name, "h3")),
                        Ui.Text(c.Message, "small muted", wrap: true)),
                });
            body.Children.Add(list);
        }
        else body.Children.Add(Ui.Text(I18n.T("crash.code", ("code", code?.ToString() ?? "?")), "small muted", wrap: true));

        var actions = new List<Control>();
        if (culprits.Count == 0) actions.Add(Ui.Button(I18n.T("common.close"), CloseDialog));
        actions.Add(Ui.Button(I18n.T("crash.log"), () => { CloseDialog(); Navigate(() => new GamePage(g.Def.Id, "log")); }, "", Icons.Alert));
        if (culprits.Count > 0 && g.Registry is not null)
            actions.Add(Ui.Button(I18n.T("crash.disable"), () =>
            {
                CloseDialog();
                var off = 0;
                foreach (var c in culprits)
                    if (c.ModId is not null) try { g.Registry!.SetEnabled(c.ModId, false); off++; } catch { }
                Toast(I18n.T("crash.disabled", ("n", off)));
                AppState.Notify();
                Actions.Play(g);
            }, "primary", Icons.Play));
        Dialog(I18n.T("crash.title", ("game", g.Def.Name)), body, actions.ToArray());
    }
}
