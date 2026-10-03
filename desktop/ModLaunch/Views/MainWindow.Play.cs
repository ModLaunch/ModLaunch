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
    }

    // ---------------------------------------------------------------- экран запуска

    public LaunchScreen ShowLaunch(GameState g)
    {
        var screen = new LaunchScreen(g);
        screen.Closed += HideLaunch;
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
            _accountButton.Content = new Panel { Width = 36, Height = 36, Children = { Ui.Thumb(null, name, 36, 18, person: true), online } };
            ToolTip.SetTip(_accountButton, name);
        }
        else
        {
            _accountButton.Content = Ui.Icon(Icons.User, 20);
            ToolTip.SetTip(_accountButton, I18n.T("acc.menu.signin"));
        }
        _accountButton.Foreground = Ui.Res("Muted");
        _accountButton.Classes.Set("active", _accountPanel.IsVisible);
    }

    void RenderAccountPanel()
    {
        var p = Social.Account.Get();
        var col = Ui.Col(12);
        if (p.SignedIn)
        {
            var who = Ui.Col(2, Ui.Text(p.Name ?? "", "h3"), Ui.Text(p.Email ?? "", "small muted"));
            who.VerticalAlignment = VerticalAlignment.Center;
            col.Children.Add(Ui.Row(12, Ui.Thumb(null, p.Name ?? p.Email ?? "?", 48, 24, person: true), who));
            col.Children.Add(new Border { Height = 1, Background = Ui.Res("Line"), Margin = new Thickness(0, 2) });
            col.Children.Add(MenuRow(I18n.T("acc.menu.account"), Icons.User, () => Navigate(() => new AccountPage())));
            col.Children.Add(MenuRow(I18n.T("acc.s.wallet"), Icons.Bag, () => Navigate(() => new AccountPage("wallet"))));
            col.Children.Add(MenuRow(I18n.T("acc.s.purchases"), Icons.Download, () => Navigate(() => new AccountPage("purchases"))));
            col.Children.Add(MenuRow(I18n.T("st.tab"), Icons.Chart, () => Navigate(() => new CreatorPage("studio"))));
            col.Children.Add(MenuRow(I18n.T("acc.menu.friends"), Icons.Users, () => Navigate(() => new FriendsPage())));
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
            var face = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(24), Background = Ui.Res("Surface3"), Child = Ui.Icon(Icons.User, 22) };
            var who = Ui.Text(I18n.T("acc.menu.guest"), "h3");
            who.VerticalAlignment = VerticalAlignment.Center;
            col.Children.Add(Ui.Row(12, face, who));
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
