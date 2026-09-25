using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>
/// Компактная карточка игры, как у сборок в Modrinth App: квадратная обложка,
/// название и одна строка состояния. Кнопка «Играть» — справа, только у найденных игр.
/// </summary>
public static class GameCard
{
    public const double Width = 280;

    public static string Status(GameState g) => g.Status switch
    {
        Detect.Searching => I18n.T("games.searching"),
        Detect.Found when !g.LoaderInstalled => I18n.T("home.loaderNeeded", ("loader", g.Def.LoaderName)),
        Detect.Found when g.ModCount > 0 => I18n.T("aside.mods." + I18n.Plural(g.ModCount, "one", "few", "many"), ("n", g.ModCount)),
        Detect.Found => I18n.T("games.loaderReady", ("loader", g.Def.LoaderName)),
        Detect.NotFound => I18n.T("games.notDetected"),
        _ => I18n.T("games.notSearched"),
    };

    public static Control Create(GameState g)
    {
        var found = g.Status == Detect.Found;
        var cover = new Border
        {
            Width = 56, Height = 56, CornerRadius = new CornerRadius(12), ClipToBounds = true,
            Child = Ui.GameImage(g.Def, 160),
            Opacity = found ? 1 : 0.55,
        };
        var info = Ui.Col(2,
            Ui.Text(g.Def.Name, "h3"),
            Ui.Text(Status(g), "small", color: found ? Ui.Res("Muted") : Ui.Res("Faint")));
        info.VerticalAlignment = VerticalAlignment.Center;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
        grid.Children.Add(cover);
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);
        if (found && g.LoaderInstalled)
        {
            var gs = g;
            var running = Features.Launcher.IsRunning(g.Def.Id);
            var play = running
                ? Ui.Button("", () => Features.Launcher.Stop(gs.Def.Id), "icon", Icons.Stop, I18n.T("v4.stop"))
                : Ui.Button("", () => Actions.Play(gs), "icon primary", Icons.Play, I18n.T("games.play"));
            play.Width = 36;
            play.Height = 36;
            play.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(play, 2);
            grid.Children.Add(play);
        }

        var card = new Button
        {
            Classes = { "card-btn" },
            Width = Width,
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 12, 12),
            Content = grid,
        };
        var id = g.Def.Id;
        card.Click += (_, _) => MainWindow.Current?.Navigate(() => new GamePage(id));
        return card;
    }

    /// <summary>Карточка «Добавить игру» того же размера.</summary>
    public static Control Add()
    {
        var content = Ui.Row(12,
            new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(12), Background = Ui.Res("Surface2"), Child = Ui.Icon(Icons.Plus, 22, Ui.Res("Muted")) },
            new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { Ui.Text(I18n.T("add.tile"), "h3"), Ui.Text(I18n.T("add.tile.text"), "small muted") } });
        var card = new Button { Classes = { "card-btn", "dashed" }, Width = Width, Padding = new Thickness(10), Margin = new Thickness(0, 0, 12, 12), Content = content };
        card.Click += (_, _) => MainWindow.Current?.Navigate(() => new AddGamePage());
        return card;
    }
}
