using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>
/// «Состояние сервисов» (8.4): живая проверка каждой службы и кнопка «Подключить»
/// с готовыми правилами сервера, лентой рекламы и файлом обновлений.
/// </summary>
public sealed partial class SettingsPage
{
    static bool _svcStarted;

    static string SvcIcon(string id) => id switch
    {
        "reviews" => Icons.Star,
        "accounts" => Icons.User,
        "friends" => Icons.Users,
        "hub" => Icons.Globe,
        "market" => Icons.Bag,
        "ads" => Icons.Megaphone,
        "updates" => Icons.ArrowUp,
        _ => Icons.Server,
    };

    Control ServicesTab()
    {
        if (!_svcStarted && !Program.Screenshot)
        {
            _svcStarted = true;
            _ = Services.CheckAll();
        }
        var rows = new Dictionary<string, Border>();
        var list = Ui.Col(10);
        foreach (var id in Services.Ids)
        {
            var row = new Border { Classes = { "cfg-row" } };
            rows[id] = row;
            Fill(row, id);
            list.Children.Add(row);
        }
        void OnChanged(ServiceStatus s) => Dispatcher.UIThread.Post(() => { if (rows.TryGetValue(s.Id, out var r)) { Fill(r, s.Id); Animate.From(r, "translateX(10px)", 260); } });
        list.AttachedToVisualTree += (_, _) =>
        {
            Services.Changed += OnChanged;
            // Быстрые службы (аккаунт с живым токеном) успевают ответить раньше, чем экран подписался, —
            // перерисовываем строки по последним итогам, чтобы ни одна не осталась на «Проверяю…».
            foreach (var (id, row) in rows) Fill(row, id);
        };
        list.DetachedFromVisualTree += (_, _) => Services.Changed -= OnChanged;

        var again = Ui.Button(I18n.T("svc.again"), () => _ = Services.CheckAll(), "primary", Icons.Refresh);
        var head = new DockPanel();
        DockPanel.SetDock(again, Dock.Right);
        head.Children.Add(again);
        var hint = Ui.Text(I18n.T("svc.hint"), "muted", wrap: true);
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.Margin = new Thickness(0, 0, 16, 0);
        head.Children.Add(hint);

        var server = Ui.Col(10,
            Ui.Text(I18n.T("svc.server"), "h2"),
            Ui.Text(I18n.T("svc.server.text", ("project", Social.Firebase.ProjectId)), "muted", wrap: true),
            Ui.Row(10,
                Ui.Button(I18n.T("svc.rules.copy"), CopyRules, "", Icons.Copy),
                Ui.Button(I18n.T("svc.rules.console"), () => Ui.OpenUrl(ConsoleUrl), "", Icons.External)));

        var col = new StackPanel { Spacing = 16 };
        col.Children.Add(Ui.Card(Ui.Col(16, head, list), 22));
        col.Children.Add(Ui.Card(server, 22));
        return col;
    }

    static string ConsoleUrl => $"https://console.firebase.google.com/project/{Social.Firebase.ProjectId}/firestore/rules";

    void Fill(Border row, string id)
    {
        var s = Services.Last.TryGetValue(id, out var v) ? v : new ServiceStatus(id, ServiceState.Checking, I18n.T(Program.Screenshot ? "svc.notChecked" : "svc.checking"));
        var dot = new Border
        {
            Classes = { "svc-dot", s.State switch { ServiceState.Ok => "ok", ServiceState.Warn => "warn", ServiceState.Bad => "bad", _ => "wait" } },
            Width = 10, Height = 10, CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center,
        };
        var words = Ui.Col(3, Ui.Row(8, Ui.Text(I18n.T("svc." + id), "h3"), dot), Ui.Text(s.Summary, "small", color: s.State switch
        {
            ServiceState.Ok => Ui.Res("Good"),
            ServiceState.Bad => Ui.Res("Bad"),
            ServiceState.Warn => Ui.Res("Warn"),
            _ => Ui.Res("Muted"),
        }));
        if (s.Fix is not null) words.Children.Add(Ui.Text(s.Fix, "small muted", wrap: true));
        if (s.Detail is not null)
        {
            var detail = Ui.Text(s.Detail, "tiny muted", wrap: true);
            detail.MaxHeight = 48;
            words.Children.Add(detail);
        }
        words.VerticalAlignment = VerticalAlignment.Center;

        var actions = Ui.Row(6);
        actions.VerticalAlignment = VerticalAlignment.Center;
        if (s.State == ServiceState.Bad && id is "friends" or "hub" or "market" or "reviews")
            actions.Children.Add(Ui.Button(I18n.T("svc.connect"), ConnectServer, "primary", Icons.Zap));
        if (id == "ads" && s.State != ServiceState.Checking) actions.Children.Add(Ui.Button(I18n.T("svc.how"), ConnectAds, "", Icons.Info));
        if (id == "updates" && s.State != ServiceState.Checking) actions.Children.Add(Ui.Button(I18n.T("svc.how"), ConnectUpdates, "", Icons.Info));
        if (id is "friends" or "accounts" && s.State != ServiceState.Checking && !Social.Account.SignedIn) actions.Children.Add(Ui.Button(I18n.T("acc.menu.signin"), () => MainWindow.Current?.Navigate(() => new AccountPage()), "", Icons.User));
        if (s.State != ServiceState.Checking)
            actions.Children.Add(Ui.Button("", () => _ = Services.CheckOne(id), "icon ghost", Icons.Refresh, I18n.T("svc.again")));

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 14 };
        grid.Children.Add(new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(12), Background = Ui.Res("Surface3"), Child = Ui.Icon(SvcIcon(id), 18, Ui.Res("Text")), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);
        row.Child = grid;
        row.Classes.Set("changed", s.State == ServiceState.Bad);
    }

    static string Rules()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://ModLaunch/Assets/server-rules.txt"));
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch { return ""; }
    }

    static async void CopyRules()
    {
        var w = MainWindow.Current;
        if (w?.Clipboard is null) return;
        await w.Clipboard.SetTextAsync(Rules());
        w.Toast(I18n.T("svc.rules.copied"));
    }

    /// <summary>Подключить друзей, Hub и маркет: три шага в консоли Firebase.</summary>
    static void ConnectServer()
    {
        var w = MainWindow.Current!;
        var steps = Ui.Col(10);
        var n = 1;
        foreach (var key in new[] { "svc.step1", "svc.step2", "svc.step3", "svc.step4" })
        {
            var num = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Ui.Res("Brand"), Child = Ui.Text((n++).ToString(), "small", color: Avalonia.Media.Brushes.White), VerticalAlignment = VerticalAlignment.Top };
            ((TextBlock)num.Child!).HorizontalAlignment = HorizontalAlignment.Center;
            ((TextBlock)num.Child!).VerticalAlignment = VerticalAlignment.Center;
            var text = Ui.Text(I18n.T(key, ("project", Social.Firebase.ProjectId)), "", wrap: true);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
            row.Children.Add(num);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            steps.Children.Add(row);
        }
        w.Dialog(I18n.T("svc.connect.title"), Ui.Col(14, Ui.Text(I18n.T("svc.connect.text"), "muted", wrap: true), steps), 600,
            Ui.Button(I18n.T("common.close"), w.CloseDialog),
            Ui.Button(I18n.T("svc.rules.copy"), CopyRules, "", Icons.Copy),
            Ui.Button(I18n.T("svc.rules.console"), () => Ui.OpenUrl(ConsoleUrl), "primary", Icons.External));
    }

    static void ConnectAds()
    {
        var w = MainWindow.Current!;
        const string sample = """
            {
              "rotateSeconds": 15,
              "items": [
                { "id": "my-first-ad", "title": "Название", "text": "Короткий текст до 80 знаков",
                  "image": "https://modlaunchapp.com/ads/logo.png", "url": "https://example.com",
                  "until": "2026-12-31", "lang": "ru" }
              ]
            }
            """;
        var code = new SelectableTextBlock { Text = sample, FontFamily = new Avalonia.Media.FontFamily("Cascadia Mono, Consolas, monospace"), FontSize = 12 };
        w.Dialog(I18n.T("svc.ads.title"),
            Ui.Col(12, Ui.Text(I18n.T("svc.ads.text", ("url", Ads.FeedUrl ?? "—")), "muted", wrap: true),
                new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Child = code }), 620,
            Ui.Button(I18n.T("common.close"), w.CloseDialog, "primary"));
    }

    static void ConnectUpdates()
    {
        var w = MainWindow.Current!;
        w.Dialog(I18n.T("svc.upd.title"), Ui.Text(I18n.T("svc.upd.text"), "muted", wrap: true), 600,
            Ui.Button(I18n.T("common.close"), w.CloseDialog, "primary"));
    }
}
