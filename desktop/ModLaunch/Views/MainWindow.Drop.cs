using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ModLaunch.Core;
using ModLaunch.Games;

namespace ModLaunch.Views;

/// <summary>
/// Перетащить мод в окно: архив, папку или .dll. На странице игры — ставится в эту игру,
/// в других местах — спрашиваем, в какую.
/// </summary>
public sealed partial class MainWindow
{
    Border? _dropHint;

    void InitDrop()
    {
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => ShowDropHint(false));
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    static List<string> Dropped(DragEventArgs e) =>
        e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList() ?? [];

    /// <summary>Игра страницы, на которой стоим (если она найдена).</summary>
    GameState? PageGame() => _current?.GameId is { } id && AppState.Game(id) is { Status: Detect.Found } g ? g : null;

    void OnDragOver(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.Contains(DataFormat.File);
        e.DragEffects = files ? DragDropEffects.Copy : DragDropEffects.None;
        ShowDropHint(files);
    }

    void OnDrop(object? sender, DragEventArgs e)
    {
        ShowDropHint(false);
        var paths = Dropped(e);
        if (paths.Count == 0) return;
        if (PageGame() is { } g) Actions.InstallLocal(g, paths);
        else AskGameFor(paths);
    }

    /// <summary>«В какую игру поставить?» — если бросили не на странице игры.</summary>
    void AskGameFor(List<string> paths)
    {
        var games = AppState.Games.Where(g => g.Status == Detect.Found && g.Registry is not null).ToList();
        if (games.Count == 0) { Toast(I18n.T("local.noGames"), bad: true); return; }
        if (games.Count == 1) { Actions.InstallLocal(games[0], paths); return; }
        var list = new WrapPanel();
        foreach (var g in games)
        {
            var game = g;
            var b = Ui.Button(g.Def.Name, () => { CloseDialog(); Actions.InstallLocal(game, paths); }, "chip");
            b.Margin = new Thickness(0, 0, 8, 8);
            list.Children.Add(b);
        }
        Dialog(I18n.T("local.which"), Ui.Col(12, Ui.Text(string.Join(", ", paths.Select(p => Path.GetFileName(p.TrimEnd('\\', '/')))), "small muted", wrap: true), list),
            Ui.Button(I18n.T("common.cancel"), CloseDialog));
    }

    /// <summary>Рамка «Отпусти, чтобы поставить» поверх окна, пока тащат файлы.</summary>
    void ShowDropHint(bool show)
    {
        if (!show)
        {
            if (_dropHint is not null) _dropHint.IsVisible = false;
            return;
        }
        if (_dropHint is null)
        {
            _dropHint = new Border
            {
                Margin = new Thickness(24),
                CornerRadius = new CornerRadius(20),
                BorderThickness = new Thickness(2),
                BorderBrush = Ui.Res("Brand"),
                Background = new SolidColorBrush(Color.FromArgb(238, 14, 14, 20)),
                IsHitTestVisible = false,
            };
            _layers?.Children.Add(_dropHint);
        }
        var game = PageGame();
        var icon = Ui.Icon(Icons.Download, 40, Ui.Res("Brand2"));
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        var title = Ui.Text(game is null ? I18n.T("local.drop") : I18n.T("local.dropInto", ("game", game.Def.Name)), "h2");
        var hint = Ui.Text(I18n.T("local.drop.hint"), "muted");
        title.HorizontalAlignment = hint.HorizontalAlignment = HorizontalAlignment.Center;
        _dropHint.Child = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { icon, title, hint } };
        _dropHint.IsVisible = true;
    }

    /// <summary>Для снимка: как выглядит окно, когда над ним держат файл.</summary>
    public void PreviewDrop(bool show) => ShowDropHint(show);
}
