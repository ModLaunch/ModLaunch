using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Social;

namespace ModLaunch.Views;

/// <summary>
/// 28.0: разговор с другом в панели «Друзья»: сообщения, «Позвать в игру» (приглашение с кнопкой «Играть» у друга),
/// поле ввода (Enter — отправить, Shift+Enter — новая строка). Пока разговор открыт, ящик проверяется каждые 4 секунды.
/// </summary>
public sealed class ChatPanel : DockPanel
{
    readonly Friend _f;
    readonly Action _back;
    readonly StackPanel _messages = new() { Spacing = 6 };
    readonly ScrollViewer _scroll;
    readonly TextBox _input;
    readonly Border _banner = new() { IsVisible = false, CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 8), Margin = new Thickness(0, 0, 0, 8) };
    readonly Button _send;
    Button? _inviteButton;
    int _shown = -1;

    public ChatPanel(Friend f, Action back)
    {
        _f = f;
        _back = back;
        var backButton = Ui.Button("", Close, "icon ghost", Icons.ChevronLeft, I18n.T("v28.chat.back"));
        var invite = _inviteButton = Ui.Button("", ShowInviteMenu, "icon ghost", Icons.Gamepad, I18n.T("v28.chat.invite"));
        var color = f.State switch { "playing" => Ui.Res("Good"), "online" => Ui.Res("Brand2"), _ => Ui.Res("Faint") };
        var status = f.State switch
        {
            "playing" => f.GameName == "" ? I18n.T("aside.online") : f.GameName,
            "online" => I18n.T("aside.online"),
            _ => f.Seen is null ? I18n.T("friends.offline") : I18n.T("friends.ago", ("time", Ui.Ago(f.Seen).TrimEnd())),
        };
        var who = Ui.Col(1, new TextBlock { Text = f.Name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
            new TextBlock { Text = status, FontSize = 12, Foreground = color, TextTrimming = TextTrimming.CharacterEllipsis });
        who.VerticalAlignment = VerticalAlignment.Center;
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(backButton, Dock.Left);
        DockPanel.SetDock(invite, Dock.Right);
        head.Children.Add(backButton);
        head.Children.Add(invite);
        head.Children.Add(Ui.Row(8, Ui.Thumb(null, f.Name, 32, 16, person: true), who));

        _input = new TextBox { Watermark = I18n.T("v28.chat.placeholder"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 110, MaxLength = 1000 };
        _input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None) { e.Handled = true; _ = Send(); }
        };
        _send = Ui.Button("", () => _ = Send(), "icon primary", Icons.Forward, I18n.T("v28.chat.send"));
        _send.VerticalAlignment = VerticalAlignment.Bottom;
        _send.Margin = new Thickness(8, 0, 0, 0);
        var inputRow = new DockPanel();
        DockPanel.SetDock(_send, Dock.Right);
        inputRow.Children.Add(_send);
        inputRow.Children.Add(_input);

        _scroll = new ScrollViewer { Content = _messages, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(-6, 0, -6, 8), Padding = new Thickness(6, 0) };
        SetDock(head, Dock.Top);
        SetDock(_banner, Dock.Top);
        SetDock(inputRow, Dock.Bottom);
        Children.Add(head);
        Children.Add(_banner);
        Children.Add(inputRow);
        Children.Add(_scroll);

        void Changed(string uid) => Dispatcher.UIThread.Post(() => { if (uid == "" || uid == _f.Uid) Render(); });
        AttachedToVisualTree += (_, _) =>
        {
            Friends.Chat.Changed += Changed;
            Friends.Chat.OpenWith = _f.Uid;
            Friends.Chat.MarkRead(_f.Uid);
            Render();
            _ = Friends.Chat.Poll(_f.Uid);
            Dispatcher.UIThread.Post(() => _input.Focus(), DispatcherPriority.Background);
        };
        DetachedFromVisualTree += (_, _) =>
        {
            Friends.Chat.Changed -= Changed;
            if (Friends.Chat.OpenWith == _f.Uid) Friends.Chat.OpenWith = null;
        };
    }

    void Close()
    {
        if (Friends.Chat.OpenWith == _f.Uid) Friends.Chat.OpenWith = null;
        _back();
    }

    void Render()
    {
        var error = Friends.Chat.Error;
        _banner.IsVisible = error is "DENIED" or "BUSY";
        if (_banner.IsVisible)
        {
            _banner.Background = Ui.Soft("#3A2A12", "#F2B84B");
            _banner.Child = Ui.Text(I18n.T(error == "DENIED" ? "v28.chat.unavailable" : "v28.chat.busy"), "small", wrap: true);
        }
        _input.IsEnabled = _send.IsEnabled = error != "DENIED";

        var list = Friends.Chat.Messages(_f.Uid);
        if (list.Count == _shown && list.All(m => !m.Pending)) return;
        _shown = list.Count;
        _messages.Children.Clear();
        if (list.Count == 0)
        {
            _messages.Children.Add(new Border
            {
                Padding = new Thickness(10, 30), Child = Ui.Text(I18n.T("v28.chat.empty", ("name", _f.Name)), "small muted", wrap: true),
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            return;
        }
        DateTime? day = null;
        foreach (var m in list)
        {
            var local = m.At.ToLocalTime();
            if (day != local.Date)
            {
                day = local.Date;
                var label = local.Date == DateTime.Today ? I18n.T("v28.chat.today") : local.Date == DateTime.Today.AddDays(-1) ? I18n.T("v28.chat.yesterday") : local.ToString("d MMMM", I18n.Culture);
                _messages.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = Ui.Res("Faint"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 2) });
            }
            _messages.Children.Add(Bubble(m, local));
        }
        Dispatcher.UIThread.Post(() => _scroll.ScrollToEnd(), DispatcherPriority.Background);
    }

    Control Bubble(ChatMessage m, DateTime local)
    {
        var time = new TextBlock { Text = m.Pending ? I18n.T("v28.chat.sending") : local.ToString("t", I18n.Culture), FontSize = 10.5, Foreground = m.Mine ? Ui.Hex("#D6E4FF") : Ui.Res("Faint"), HorizontalAlignment = HorizontalAlignment.Right };
        Control body;
        if (m.Kind == "invite")
        {
            var title = m.Mine ? I18n.T("v28.chat.invite.mine", ("game", m.GameName)) : I18n.T("v28.chat.invite.theirs", ("name", _f.Name), ("game", m.GameName));
            var col = Ui.Col(8, Ui.Row(8, Ui.Icon(Icons.Gamepad, 16, m.Mine ? Brushes.White : Ui.Res("Good")), new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold, Foreground = m.Mine ? Brushes.White : Ui.Res("Text"), MaxWidth = 210 }));
            if (!m.Mine) col.Children.Add(JoinButton(m));
            col.Children.Add(time);
            body = col;
        }
        else
            body = Ui.Col(2, new SelectableTextBlock { Text = m.Text, TextWrapping = TextWrapping.Wrap, Foreground = m.Mine ? Brushes.White : Ui.Res("Text"), MaxWidth = 230 }, time);
        return new Border
        {
            Background = m.Mine ? Ui.Res("Brand") : Ui.Res("Surface2"),
            CornerRadius = m.Mine ? new CornerRadius(14, 14, 4, 14) : new CornerRadius(14, 14, 14, 4),
            Padding = new Thickness(11, 7), MaxWidth = 260, Opacity = m.Pending ? 0.6 : 1,
            HorizontalAlignment = m.Mine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Child = body,
        };
    }

    /// <summary>«Играть» у приглашения: игра есть на этом ПК — запуск, нет — страница в Steam (если это игра Steam).</summary>
    static Control JoinButton(ChatMessage m)
    {
        var mine = (AllGames.Current ?? []).FirstOrDefault(g => GameReviews.Key(g) == m.Game && g.Installed);
        if (mine is not null)
            return Ui.Button(I18n.T("games.play"), () => AllGamesPage.Play(mine), "primary", Icons.Play);
        if (m.Game.StartsWith("steam:") && int.TryParse(m.Game[6..], out var app))
            return Ui.Button(I18n.T("v28.chat.invite.getSteam"), () => SteamLibrary.StorePage(app), "", Icons.Globe);
        return Ui.Text(I18n.T("v28.chat.invite.notHere"), "small muted", wrap: true);
    }

    async Task Send()
    {
        var text = (_input.Text ?? "").Trim();
        if (text == "") return;
        _input.Text = "";
        try { await Friends.Chat.Send(_f.Uid, text); }
        catch (Exception e)
        {
            if (Friends.Chat.Error != "DENIED") MainWindow.Current?.Toast(Friends.Explain(e), bad: true);
            if ((_input.Text ?? "") == "") _input.Text = text; // не отправилось — текст не теряем
        }
        Render();
    }

    /// <summary>Чем позвать: сначала игра, в которую играю сейчас, потом недавние из всех лаунчеров.</summary>
    void ShowInviteMenu()
    {
        var games = (AllGames.Current ?? []).Where(g => g.Installed)
            .OrderByDescending(g => AllGames.IsRunning(g)).ThenByDescending(g => AllGames.Played(g).Last).Take(8).ToList();
        if (games.Count == 0)
        {
            MainWindow.Current?.Toast(I18n.T("v28.chat.invite.noGames"), bad: true);
            if (AllGames.Current is null) _ = AllGames.Refresh();
            return;
        }
        var items = games.Select(g => (object?)Ctx.Item(g.Name, AllGames.IsRunning(g) ? Icons.Play : Icons.Gamepad, () => _ = Invite(g))).ToArray();
        var menu = Ctx.Menu(items);
        menu.ShowAt(_inviteButton ?? (Control)this);
    }

    async Task Invite(LibGame g)
    {
        try
        {
            await Friends.Chat.Invite(_f.Uid, GameReviews.Key(g), g.Name);
            MainWindow.Current?.Toast(I18n.T("v28.chat.invite.sent", ("name", _f.Name), ("game", g.Name)));
        }
        catch (Exception e) { if (Friends.Chat.Error != "DENIED") MainWindow.Current?.Toast(Friends.Explain(e), bad: true); }
        Render();
    }
}
