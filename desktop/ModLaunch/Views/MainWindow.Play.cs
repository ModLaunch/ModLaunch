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
        // 25.0: Premium куплен или найден при запуске — звёздочка на аватаре сразу.
        Features.Premium.Changed += () => Dispatcher.UIThread.Post(RenderAccount);
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
        if (p.SignedIn)
        {
            var name = p.Name ?? p.Email ?? "?";
            var online = new Border
            {
                Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = Ui.Res("Good"),
                BorderBrush = Ui.Res("Rail"), BorderThickness = new Thickness(2),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            };
            // 9.9: аватар в надетой рамке (награда за достижения).
            var ring = ProfileKit.Avatar(name, 40, ring: 3);
            var avatar = new Panel { Width = 40, Height = 40, Children = { ring, online } };
            // 25.0: золотая звёздочка Premium в углу аватара.
            if (Features.Premium.Active)
                avatar.Children.Add(new Border
                {
                    Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = Ui.Hex("#F0B429"),
                    BorderBrush = Ui.Res("Rail"), BorderThickness = new Thickness(2),
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(-3, -3, 0, 0),
                    Child = Ui.Icon(Icons.Sparkles, 8, Brushes.White),
                });
            _accountButton.Content = avatar;
            ToolTip.SetTip(_accountButton, Features.Premium.Active ? name + " · Premium" : name);
        }
        else
        {
            _accountButton.Content = new Border
            {
                Width = 38, Height = 38, CornerRadius = new CornerRadius(19), Background = Ui.Res("Surface2"), BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1),
                Child = Ui.Icon(Icons.User, 18, Ui.Res("Muted")),
            };
            ToolTip.SetTip(_accountButton, I18n.T("acc.menu.signin"));
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
        // 25.0: ModLaunch Premium — золотой пункт; у кого он есть — «Premium ✓».
        var premium = MenuRow(Features.Premium.Active ? "Premium ✓" : I18n.T("v25.prem.menu"), Icons.Sparkles, () => Navigate(() => new AccountPage("premium")));
        premium.Foreground = Ui.Hex("#F0B429");
        col.Children.Add(premium);
        col.Children.Add(MenuRow(I18n.T("v25.bug.title"), Icons.Flag, () => BugReportUi.Show()));
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

    // 24.0: разбор вылета и окно «Почему игра упала» — в MainWindow.Crash.cs.

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
}
