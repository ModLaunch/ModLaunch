using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Social;

namespace ModLaunch.Views;

/// <summary>
/// «Друзья» внизу окна, как «Друзья и чат» в Steam: кнопка в правом нижнем углу
/// с аватарками тех, кто в сети, и панель над ней — кто во что играет, заявки,
/// добавление по коду. Всегда под рукой, на любой странице.
/// </summary>
public sealed class FriendsDock : Panel
{
    readonly Button _pill = new() { Classes = { "friends-pill" } };
    readonly Border _panel;
    readonly StackPanel _list = new() { Spacing = 2 };
    readonly TextBox _add = new() { Watermark = I18n.T("friends.add.placeholder") };
    string _filter = "";
    bool _open;

    public FriendsDock()
    {
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Bottom;
        Margin = new Thickness(0, 0, 18, 16);

        _pill.Click += (_, _) => Toggle();
        var search = new TextBox { Watermark = I18n.T("dock.search"), Height = 36 };
        search.InnerLeftContent = new Border { Padding = new Thickness(10, 0, 0, 0), Child = Ui.Icon(Icons.Search, 14, Ui.Res("Muted")) };
        search.TextChanged += (_, _) => { _filter = search.Text?.Trim() ?? ""; RenderList(); };
        _add.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) _ = AddFriend(); };
        var addButton = Ui.Button("", () => _ = AddFriend(), "icon primary", Icons.Plus, I18n.T("friends.add"));
        var addRow = new DockPanel();
        DockPanel.SetDock(addButton, Dock.Right);
        addButton.Margin = new Thickness(8, 0, 0, 0);
        addRow.Children.Add(addButton);
        addRow.Children.Add(_add);

        var all = Ui.Button(I18n.T("dock.all"), () => { Toggle(false); MainWindow.Current?.Navigate(() => new FriendsPage()); }, "ghost", Icons.External);
        var head = new DockPanel();
        DockPanel.SetDock(all, Dock.Right);
        head.Children.Add(all);
        head.Children.Add(Ui.Row(8, Ui.Icon(Icons.Users, 16, Ui.Res("Brand2")), Ui.Text(I18n.T("friends.title"), "h3")));

        _panel = new Border
        {
            Classes = { "card" }, Width = 360, Height = 520, Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 56), IsVisible = false,
            BoxShadow = BoxShadows.Parse("0 24 60 0 #90000000"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Child = new DockPanel
            {
                Children =
                {
                    Top(head), Top(search), Bottom(addRow),
                    new ScrollViewer { Content = _list, Margin = new Thickness(-6, 8, -6, 8), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
                },
            },
        };
        Children.Add(_panel);
        var pillHost = new Panel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Children = { _pill } };
        Children.Add(pillHost);

        Friends.Changed += () => Dispatcher.UIThread.Post(Render);
        Account.Changed += () => Dispatcher.UIThread.Post(Render);
        Render();
    }

    static Control Top(Control c) { DockPanel.SetDock(c, Dock.Top); c.Margin = new Thickness(0, 0, 0, 10); return c; }
    static Control Bottom(Control c) { DockPanel.SetDock(c, Dock.Bottom); return c; }

    public bool IsOpen => _open;

    public void Toggle(bool? open = null)
    {
        _open = open ?? !_open;
        _panel.IsVisible = _open;
        if (_open)
        {
            Animate.Pop(_panel);
            RenderList();
            if (Account.SignedIn && !Program.Demo) _ = Task.Run(async () => { try { await Friends.Refresh(); } catch { } });
        }
    }

    public void Render()
    {
        IsVisible = Settings.Data.Bool("friendsDock", true);
        var view = Friends.View();
        var online = view.Friends.Where(f => f.State != "offline").ToList();
        var faces = new Panel { Width = Math.Max(1, Math.Min(3, online.Count)) * 16 + 10, Height = 26 };
        for (var i = 0; i < Math.Min(3, online.Count); i++)
        {
            var t = Ui.Thumb(null, online[i].Name, 26, 13);
            t.Margin = new Thickness(i * 16, 0, 0, 0);
            t.HorizontalAlignment = HorizontalAlignment.Left;
            faces.Children.Add(new Border { Child = t, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(2), BorderBrush = Ui.Res("Surface"), Margin = t.Margin, HorizontalAlignment = HorizontalAlignment.Left });
            t.Margin = new Thickness(0);
        }
        var label = !view.SignedIn ? I18n.T("friends.title")
            : online.Count > 0 ? I18n.T("dock.online", ("n", online.Count)) : I18n.T("friends.title");
        var content = Ui.Row(10);
        content.Children.Add(online.Count > 0 ? faces : Ui.Icon(Icons.Users, 16));
        content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold });
        if (view.Incoming.Count > 0)
            content.Children.Add(new Border { Background = Ui.Res("Bad"), CornerRadius = new CornerRadius(999), Padding = new Thickness(7, 1), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = view.Incoming.Count.ToString(), FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Brushes.White } });
        _pill.Content = content;
        if (_open) RenderList();
    }

    void RenderList()
    {
        _list.Children.Clear();
        var view = Friends.View();
        if (!view.SignedIn)
        {
            _list.Children.Add(Ui.Col(12,
                Ui.Text(I18n.T("friends.signin.text"), "muted", wrap: true),
                Ui.Button(I18n.T("aside.login"), () => { Toggle(false); MainWindow.Current?.Navigate(() => new SettingsPage("accounts")); }, "primary", Icons.User)));
            _add.IsEnabled = false;
            return;
        }
        _add.IsEnabled = true;

        foreach (var r in view.Incoming)
        {
            var uid = r.Uid;
            var accept = Ui.Button("", () => _ = Do(() => Friends.Accept(uid)), "icon primary", Icons.Check, I18n.T("friends.accept"));
            var decline = Ui.Button("", () => _ = Do(() => Friends.Remove(uid)), "icon", Icons.Close, I18n.T("friends.decline"));
            _list.Children.Add(Row(r.Name, I18n.T("friends.incoming"), Ui.Res("Warn"), Ui.Row(4, accept, decline)));
        }

        bool Match(Friend f) => _filter == "" || f.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase) || f.GameName.Contains(_filter, StringComparison.OrdinalIgnoreCase);
        void Group(string title, IEnumerable<Friend> friends, Func<Friend, (string, IBrush)> line)
        {
            var list = friends.Where(Match).ToList();
            if (list.Count == 0) return;
            _list.Children.Add(new TextBlock { Text = $"{title} — {list.Count}", FontSize = 11.5, FontWeight = FontWeight.SemiBold, Foreground = Ui.Res("Faint"), Margin = new Thickness(8, 10, 0, 4) });
            foreach (var f in list)
            {
                var (text, color) = line(f);
                _list.Children.Add(Row(f.Name, text, color, null));
            }
        }
        Group(I18n.T("dock.inGame"), view.Friends.Where(f => f.State == "playing"), f => (f.GameName == "" ? I18n.T("aside.online") : f.GameName, Ui.Res("Good")));
        Group(I18n.T("friends.online"), view.Friends.Where(f => f.State is not ("playing" or "offline")), _ => (I18n.T("aside.online"), Ui.Res("Brand2")));
        Group(I18n.T("friends.offline"), view.Friends.Where(f => f.State == "offline"), f => (f.Seen is null ? I18n.T("friends.offline") : I18n.T("friends.ago", ("time", Ui.Ago(f.Seen).TrimEnd())), Ui.Res("Faint")));
        if (view.Friends.Count == 0 && view.Incoming.Count == 0)
            _list.Children.Add(Ui.Text(I18n.T("friends.empty"), "small muted", wrap: true));
    }

    static Control Row(string name, string line, IBrush color, Control? right)
    {
        var dot = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = color, BorderBrush = Ui.Res("Surface"), BorderThickness = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
        var face = new Panel { Width = 36, Height = 36, Children = { Ui.Thumb(null, name, 36, 18), dot } };
        var info = Ui.Col(1, new TextBlock { Text = name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
            new TextBlock { Text = line, FontSize = 12, Foreground = color, TextTrimming = TextTrimming.CharacterEllipsis });
        info.VerticalAlignment = VerticalAlignment.Center;
        var dock = new DockPanel();
        if (right is not null) { DockPanel.SetDock(right, Dock.Right); dock.Children.Add(right); }
        dock.Children.Add(Ui.Row(10, face, info));
        return new Border { Classes = { "friend-row" }, Padding = new Thickness(8, 6), CornerRadius = new CornerRadius(10), Child = dock };
    }

    async Task AddFriend()
    {
        var code = _add.Text ?? "";
        if (code.Trim() == "") return;
        try
        {
            var (status, name) = await Friends.Add(code);
            MainWindow.Current?.Toast(I18n.T("friends.added." + status, ("name", name)));
            _add.Text = "";
        }
        catch (Exception e) { MainWindow.Current?.Toast(Friends.Explain(e), bad: true); }
        Render();
    }

    async Task Do(Func<Task> action)
    {
        try { await action(); } catch (Exception e) { MainWindow.Current?.Toast(Friends.Explain(e), bad: true); }
        Render();
    }
}
