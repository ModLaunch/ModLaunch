using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>
/// Библиотека, как в Steam: вертикальные обложки. Сначала игры, которые есть
/// на компьютере, ниже — остальные поддерживаемые (приглушённые), поиск — сверху.
/// </summary>
public sealed class LibraryPage : Page
{
    string _query = "";
    bool _showOther = Settings.Data.Bool("libraryShowOther", true);

    public override string Title => I18n.T("lib.title");
    public override string SearchHint => I18n.T("lib.search");
    public override void Search(string text) { _query = text.Trim(); Build(); }

    public override void Build()
    {
        var content = new StackPanel { Spacing = 22, Margin = new Thickness(32, 26, 32, 32), MaxWidth = 1320 };
        bool Match(GameState g) => _query == "" || g.Def.Name.Contains(_query, StringComparison.OrdinalIgnoreCase);
        var all = MainWindow.OrderedGames().ToList();
        var installed = all.Where(g => g.Status == Detect.Found && Match(g)).ToList();
        var other = all.Where(g => g.Status != Detect.Found && Match(g)).ToList();

        var head = new DockPanel();
        var add = Ui.Button(I18n.T("add.title"), () => MainWindow.Current?.Navigate(() => new AddGamePage()), "", Icons.Plus);
        DockPanel.SetDock(add, Dock.Right);
        head.Children.Add(add);
        var title = Ui.Row(10, Ui.Text(I18n.T("lib.found"), "h2"), Ui.Text(installed.Count.ToString(), "h2", color: Ui.Res("Faint")));
        title.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(title);
        content.Children.Add(head);

        var grid = new WrapPanel();
        foreach (var g in installed) grid.Children.Add(GameCard.Cover(g));
        grid.Children.Add(GameCard.AddCover());
        content.Children.Add(grid);

        if (other.Count > 0)
        {
            var toggle = Ui.Button(_showOther ? I18n.T("lib.hideOther") : I18n.T("lib.showOther"), () =>
            {
                _showOther = !_showOther;
                Settings.Data["libraryShowOther"] = _showOther;
                Settings.Save();
                Build();
            }, "ghost");
            var otherHead = new DockPanel();
            DockPanel.SetDock(toggle, Dock.Right);
            otherHead.Children.Add(toggle);
            otherHead.Children.Add(Ui.Col(2,
                Ui.Row(10, Ui.Text(I18n.T("lib.other"), "h2"), Ui.Text(other.Count.ToString(), "h2", color: Ui.Res("Faint"))),
                Ui.Text(I18n.T("lib.other.hint"), "small muted")));
            content.Children.Add(otherHead);
            if (_showOther)
            {
                var more = new WrapPanel();
                foreach (var g in other) more.Children.Add(GameCard.Cover(g, 128));
                content.Children.Add(more);
            }
        }
        if (installed.Count + other.Count == 0) content.Children.Add(Ui.Text(I18n.T("lib.empty"), "muted"));
        Content = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }
}
