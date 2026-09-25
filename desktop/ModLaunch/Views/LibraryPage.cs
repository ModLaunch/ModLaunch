using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>
/// Библиотека, как в Modrinth App: все игры компактными карточками,
/// фильтр «Установленные / Не найдены» и поиск по названию.
/// </summary>
public sealed class LibraryPage : Page
{
    string _filter = Settings.Data.Str("libraryFilter") ?? "all";
    string _query = "";

    public override string Title => I18n.T("lib.title");
    public override string SearchHint => I18n.T("lib.search");
    public override void Search(string text) { _query = text.Trim(); Build(); }

    public override void Build()
    {
        var content = new StackPanel { Spacing = 18, Margin = new Thickness(32, 26, 32, 32), MaxWidth = 1240 };
        var all = MainWindow.OrderedGames().ToList();
        var found = all.Count(g => g.Status == Detect.Found);

        var chips = Ui.Row(6);
        foreach (var (id, label) in new[]
        {
            ("all", $"{I18n.T("lib.all")} · {all.Count}"),
            ("found", $"{I18n.T("lib.found")} · {found}"),
            ("missing", $"{I18n.T("lib.missing")} · {all.Count - found}"),
        })
        {
            var b = Ui.Button(label, () => { _filter = id; Settings.Data["libraryFilter"] = id; Settings.Save(); Build(); }, "chip");
            if (_filter == id) b.Classes.Add("active");
            chips.Children.Add(b);
        }
        var head = new DockPanel();
        var add = Ui.Button(I18n.T("add.title"), () => MainWindow.Current?.Navigate(() => new AddGamePage()), "", Icons.Plus);
        DockPanel.SetDock(add, Dock.Right);
        head.Children.Add(add);
        chips.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(chips);
        content.Children.Add(head);

        var list = all.Where(g => _filter switch
        {
            "found" => g.Status == Detect.Found,
            "missing" => g.Status != Detect.Found,
            _ => true,
        }).Where(g => _query == "" || g.Def.Name.Contains(_query, StringComparison.OrdinalIgnoreCase)).ToList();

        var grid = new WrapPanel();
        foreach (var g in list) grid.Children.Add(GameCard.Create(g));
        if (_filter != "found") grid.Children.Add(GameCard.Add());
        if (list.Count == 0) content.Children.Add(Ui.Text(I18n.T("lib.empty"), "muted"));
        content.Children.Add(grid);
        Content = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }
}
