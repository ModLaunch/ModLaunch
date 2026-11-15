using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Social;

namespace ModLaunch.Views;

/// <summary>30.0: «Кураторы об игре» на странице игры во «Всех играх» — мнения тех, на кого подписан.</summary>
public sealed class CuratorPicksCard : ContentControl
{
    readonly LibGame _g;
    List<(string Name, CuratorPick Pick)>? _picks;
    string? _error;

    public CuratorPicksCard(LibGame g)
    {
        _g = g;
        Render();
        if (Curators.Following.Count == 0 && !Program.Screenshot) return;
        var key = GameReviews.Key(g);
        _ = Task.Run(async () =>
        {
            List<(string, CuratorPick)>? found = null;
            string? error = null;
            try { found = await Curators.ForGame(key); }
            catch (Exception e) { error = Curators.Explain(e); }
            Dispatcher.UIThread.Post(() => { _picks = found; _error = error; Render(); });
        });
    }

    void Render()
    {
        var all = Ui.Button(I18n.T("v30.cur.all"), () => MainWindow.Current?.Navigate(() => new CuratorsPage()), "ghost", Icons.Users);
        all.VerticalAlignment = VerticalAlignment.Center;
        var head = new DockPanel();
        DockPanel.SetDock(all, Dock.Right);
        head.Children.Add(all);
        head.Children.Add(Ui.Text(I18n.T("v30.cur.aboutGame"), "h2"));
        var col = Ui.Col(12, head);

        if (Curators.Following.Count == 0 && !Program.Screenshot)
            col.Children.Add(Ui.Text(I18n.T("v30.cur.card.empty"), "muted", wrap: true));
        else if (_error is not null)
            col.Children.Add(Ui.Text(_error, "muted", wrap: true));
        else if (_picks is null)
            col.Children.Add(Ui.Text(I18n.T("v30.cur.loading"), "small muted"));
        else if (_picks.Count == 0)
            col.Children.Add(Ui.Text(I18n.T("v30.cur.card.none"), "muted", wrap: true));
        else
            foreach (var (name, p) in _picks)
            {
                var title = Ui.Row(8, Ui.Text(name, "h3"), CuratorsPage.Verdict(p.Verdict), Ui.Text(Ui.Ago(p.Updated), "small muted"));
                foreach (var c in title.Children) c.VerticalAlignment = VerticalAlignment.Center;
                var body = Ui.Col(6, title);
                if (p.Text != "") body.Children.Add(new SelectableTextBlock { Text = p.Text, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Res("Muted") });
                var row = new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 12), Child = body, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
                var curator = p.Curator;
                row.PointerReleased += (_, _) => MainWindow.Current?.Navigate(() => new CuratorsPage(curator));
                col.Children.Add(row);
            }
        Content = Ui.Card(col, 22);
    }
}
