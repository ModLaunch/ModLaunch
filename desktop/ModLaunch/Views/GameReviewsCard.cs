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
/// 28.0: «Отзывы игроков» на странице игры во «Всех играх». Читать может любой, оценить — только тот, кто наиграл
/// час и больше (часы Steam или учёт ModLaunch). Общая база отзывов (Social/GameReviews.cs).
/// </summary>
public sealed class GameReviewsCard : ContentControl
{
    const int Shown = 5;
    readonly LibGame _g;
    int _stars;
    string _text = "";
    bool _sending, _all;

    public GameReviewsCard(LibGame g)
    {
        _g = g;
        var mine = GameReviews.Mine(g);
        if (mine is not null) { _stars = mine.Stars; _text = mine.Text; }
        Render();
        if (Reviews.Configured && !Program.Screenshot && !Program.Demo)
            _ = Task.Run(async () =>
            {
                try { await Reviews.Sync(); } catch { /* нет сети — показываем то, что уже скачано */ }
                Dispatcher.UIThread.Post(() =>
                {
                    if (_stars == 0 && GameReviews.Mine(_g) is { } m) { _stars = m.Stars; _text = m.Text; }
                    Render();
                });
            });
    }

    void Render()
    {
        var col = Ui.Col(14);
        var stat = GameReviews.Stat(_g);
        var head = Ui.Row(12, Ui.Text(I18n.T("v28.grev.title"), "h2"));
        if (stat is not null) head.Children.Add(Ui.Row(6, Stars(stat.Avg, 16), Ui.Text($"{stat.Avg:0.0}", "h3"), Ui.Text($"({stat.Count})", "small muted")));
        foreach (var c in head.Children) c.VerticalAlignment = VerticalAlignment.Center;
        col.Children.Add(head);
        col.Children.Add(Ui.Text(I18n.T("v28.grev.lead", ("h", GameReviews.MinMinutes / 60)), "small muted", wrap: true));

        if (!Reviews.Configured)
        {
            col.Children.Add(Ui.Col(4, Ui.Text(I18n.T("rev.off.title"), "h3"), Ui.Text(I18n.T("rev.off.text"), "muted", wrap: true)));
            Content = Ui.Card(col, 22);
            return;
        }
        if (Reviews.LastError == "BUSY") col.Children.Add(Ui.Col(4, Ui.Text(I18n.T("rev.down.title"), "h3"), Ui.Text(I18n.T("rev.down.text"), "muted", wrap: true)));

        col.Children.Add(Form());

        var list = GameReviews.For(_g);
        if (list.Count == 0) col.Children.Add(Ui.Text(I18n.T("v28.grev.empty"), "muted", wrap: true));
        foreach (var r in _all ? list : list.Take(Shown))
        {
            var title = Ui.Row(8, Ui.Text(r.Name, "h3"), Stars(r.Stars, 13));
            if (r.Admin) title.Children.Add(ModRow.Tag("👑 " + I18n.T("rev.admin"), Ui.Soft("#3A2E10", "#F2B84B"), Ui.Ink("#F2B84B")));
            if (r.Mine) title.Children.Add(ModRow.Tag(I18n.T("rev.mine"), Ui.Res("BrandSoft"), Ui.Res("Brand2")));
            var when = Ui.Ago(r.Updated ?? r.Created) + (r.Updated is not null && r.Created is not null && r.Updated - r.Created > TimeSpan.FromMinutes(1) ? " · " + I18n.T("rev.edited") : "");
            title.Children.Add(Ui.Text(when, "small muted"));
            foreach (var c in title.Children) c.VerticalAlignment = VerticalAlignment.Center;
            var body = Ui.Col(6, title);
            if (r.Text != "") body.Children.Add(new SelectableTextBlock { Text = r.Text, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Res("Muted") });
            col.Children.Add(new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 12), Child = body });
        }
        if (!_all && list.Count > Shown)
        {
            var more = Ui.Button(I18n.T("v28.grev.more", ("n", list.Count)), () => { _all = true; Render(); }, "ghost", Icons.ChevronDown);
            more.HorizontalAlignment = HorizontalAlignment.Left;
            col.Children.Add(more);
        }
        Content = Ui.Card(col, 22);
    }

    Control Form()
    {
        var (ok, minutes) = GameReviews.Gate(_g);
        if (!ok)
            return new Border
            {
                Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(16),
                Child = Ui.Row(12, Ui.Icon(Icons.Lock, 18, Ui.Res("Muted")), Ui.Col(4,
                    Ui.Text(I18n.T("v28.grev.gate", ("h", GameReviews.MinMinutes / 60)), "h3"),
                    Ui.Text(I18n.T("v28.grev.gate.text", ("played", AllGamesPage.Hours(minutes))), "small muted", wrap: true))),
            };

        var mine = GameReviews.Mine(_g);
        var starRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        for (var i = 1; i <= 5; i++)
        {
            var n = i;
            var b = new Button { Classes = { "ghost" }, Padding = new Thickness(2), Content = Star(n <= _stars, 26) };
            ToolTip.SetTip(b, I18n.T($"rev.star.{n}"));
            b.Click += (_, _) => { _stars = n; Render(); };
            starRow.Children.Add(b);
        }
        if (_stars > 0) starRow.Children.Add(new TextBlock { Text = I18n.T($"rev.star.{_stars}"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0), Foreground = Ui.Res("Muted") });

        var profile = Account.Get();
        var name = new TextBox { Text = profile.SignedIn ? profile.Name : Settings.Data.Str("reviewName") ?? "", Watermark = I18n.T("rev.name"), IsReadOnly = profile.SignedIn, MaxLength = 32 };
        var text = new TextBox { Text = _text, Watermark = I18n.T("v28.grev.text"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80, MaxLength = 1000 };
        text.TextChanged += (_, _) => _text = text.Text ?? "";

        var publish = Ui.Button(_sending ? I18n.T("rev.sending") : mine is null ? I18n.T("rev.publish") : I18n.T("rev.update"), async () =>
        {
            var w = MainWindow.Current!;
            if (_stars == 0) { w.Toast(I18n.T("rev.pickStars"), bad: true); return; }
            var who = profile.SignedIn && profile.Name is { Length: > 0 } pn ? pn : (name.Text ?? "").Trim();
            if (who == "") { w.Toast(I18n.T("rev.needName"), bad: true); return; }
            if (!profile.SignedIn) { Settings.Data["reviewName"] = who; Settings.Save(); }
            _sending = true;
            Render();
            try
            {
                await GameReviews.Submit(_g, _stars, _text, who);
                w.Toast(mine is null ? I18n.T("rev.published") : I18n.T("rev.updatedToast"));
            }
            catch (ServiceError e) when (e.Code == "NOT_PLAYED") { w.Toast(I18n.T("v28.grev.gate", ("h", GameReviews.MinMinutes / 60)), bad: true); }
            catch (Exception e) { w.Toast(Reviews.Explain(e), bad: true); }
            _sending = false;
            Render();
        }, "primary", Icons.Check);
        publish.IsEnabled = !_sending;

        var buttons = Ui.Row(8, publish);
        if (mine is not null) buttons.Children.Add(Ui.Button(I18n.T("rev.delete"), DeleteMine, "ghost", Icons.Trash));

        var form = Ui.Col(10, Ui.Text(mine is null ? I18n.T("v28.grev.write", ("played", AllGamesPage.Hours(minutes))) : I18n.T("rev.yours"), "h3"), starRow);
        if (profile.SignedIn) form.Children.Add(Ui.Text(I18n.T("rev.as", ("name", profile.Name ?? "")), "small muted"));
        else
        {
            form.Children.Add(name);
            var login = Ui.Button(I18n.T("rev.loginHint"), () => MainWindow.Current?.Navigate(() => new SettingsPage("accounts")), "ghost", Icons.Key);
            login.HorizontalAlignment = HorizontalAlignment.Left;
            form.Children.Add(login);
        }
        form.Children.Add(text);
        form.Children.Add(buttons);
        return new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Child = form };
    }

    void DeleteMine()
    {
        var w = MainWindow.Current!;
        w.Dialog(I18n.T("rev.deleteConfirm"), Ui.Text(I18n.T("rev.deleteConfirm.text"), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("rev.delete"), async () =>
            {
                w.CloseDialog();
                try { await GameReviews.Remove(_g); _stars = 0; _text = ""; w.Toast(I18n.T("rev.deleted")); }
                catch (Exception e) { w.Toast(Reviews.Explain(e), bad: true); }
                Render();
            }, "primary", Icons.Trash));
    }

    static Control Star(bool on, double size)
    {
        if (Ui.Icon(Icons.Star, size, null, fill: false) is not IconView v) return new Panel();
        v.Stroke = on ? Ui.Hex("#F2B84B") : Ui.Res("Faint");
        v.Fill = on ? Ui.Hex("#F2B84B") : null;
        return v;
    }

    static Control Stars(double value, double size)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        for (var i = 1; i <= 5; i++) row.Children.Add(Star(i <= Math.Round(value), size));
        return row;
    }
}
