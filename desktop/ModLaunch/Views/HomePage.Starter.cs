using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>Карточка «Начало работы» для новичка: найти игру → поставить первый мод → нажать «Играть». Шаги отмечаются сами.</summary>
public sealed partial class HomePage
{
    [DemoShots]
    static void StarterShots(Shots s)
    {
        Settings.Data["onboardingDone"] = false;
        _starterPreview = true;
        s.Window.Navigate(() => new HomePage());
        s.Pump(900);
        s.Save("starter-1-home");
    }

    static bool _starterPreview; // только для снимков экрана: показать шаги невыполненными

    Control? Starter(List<GameState> mine)
    {
        if (Settings.Data.Bool("onboardingDone")) return null;
        var hasGame = mine.Count > 0;
        var hasMod = mine.Any(g => g.ModCount > 0);
        var played = mine.Any(g => Features.PlayTime.Get(g.Def.Id).LastPlayed is not null);
        if (_starterPreview) { hasMod = false; played = false; }
        // Всё сделано — карточка больше не нужна.
        if (hasGame && hasMod && played) { Settings.Data["onboardingDone"] = true; Settings.Save(); return null; }

        var first = mine.FirstOrDefault(g => g.ModCount == 0) ?? mine.FirstOrDefault();
        Control Step(int n, string key, bool done, Control? action)
        {
            var mark = new Border
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(14),
                Background = done ? Ui.Res("Good") : Ui.Res("Surface3"),
                Child = done ? Ui.Icon(Icons.Check, 16, Ui.Res("Bg")) : new TextBlock { Text = n.ToString(), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = Avalonia.Media.FontWeight.SemiBold },
            };
            var text = Ui.Col(2, Ui.Text(I18n.T("starter." + key), done ? "muted" : "h3"), Ui.Text(I18n.T("starter." + key + ".text"), "small muted", wrap: true));
            text.VerticalAlignment = VerticalAlignment.Center;
            var row = new DockPanel { LastChildFill = true };
            row.Children.Add(mark);
            if (action is not null && !done) { action.MinWidth = 180; if (action is Button ab) ab.HorizontalContentAlignment = HorizontalAlignment.Center; DockPanel.SetDock(action, Dock.Right); row.Children.Add(action); }
            text.Margin = new Thickness(14, 0, 12, 0);
            row.Children.Add(text);
            return row;
        }

        var add = Ui.Button(I18n.T("starter.1.go"), () => MainWindow.Current?.Navigate(() => new AddGamePage()), "primary", Icons.Plus);
        var mods = first is null ? null : Ui.Button(I18n.T("starter.2.go"), () => MainWindow.Current?.Navigate(() => new GamePage(first.Def.Id, "catalog")), "primary", Icons.Package);
        var play = first is null ? null : Ui.Button(I18n.T("starter.3.go"), () => MainWindow.Current?.Navigate(() => new GamePage(first.Def.Id)), "primary", Icons.Gamepad);
        var close = Ui.Button("", () => { Settings.Data["onboardingDone"] = true; Settings.Save(); Build(); }, "icon ghost", Icons.Close, I18n.T("starter.hide"));
        DockPanel.SetDock(close, Dock.Right);
        var head = new DockPanel();
        head.Children.Add(close);
        head.Children.Add(Ui.Text(I18n.T("starter.title"), "h2"));

        return new Border
        {
            Background = Ui.Res("BrandSoft"), CornerRadius = new CornerRadius(16), Padding = new Thickness(20, 16),
            Child = Ui.Col(14, head, Step(1, "1", hasGame, add), Step(2, "2", hasMod, mods), Step(3, "3", played, play)),
        };
    }
}
