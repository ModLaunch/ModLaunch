using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>
/// «Выбор редакции» — большая карточка главной, как истории во вкладке
/// «Сегодня» App Store: фоном — арт игры, поверх — надзаголовок, крупное
/// название мода, его значок и белая капсула «Установить». Листается сама
/// раз в 8 секунд; точки внизу — где мы и сколько всего.
/// </summary>
public sealed class Featured : UserControl
{
    readonly List<(GameState Game, ModInfo Mod)> _items;
    readonly ContentControl _card = new();
    readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 26, 28) };
    readonly DispatcherTimer _timer;
    int _index;

    public Featured(List<(GameState Game, ModInfo Mod)> items)
    {
        _items = items;
        var prev = Arrow(Icons.Back, () => Show(_index - 1, true));
        var next = Arrow(Icons.Forward, () => Show(_index + 1, true));
        prev.HorizontalAlignment = HorizontalAlignment.Left;
        next.HorizontalAlignment = HorizontalAlignment.Right;
        prev.Margin = new Thickness(12, 0, 0, 0);
        next.Margin = new Thickness(0, 0, 12, 0);
        var arrows = new Panel { Children = { prev, next }, Opacity = 0, IsVisible = items.Count > 1 };
        arrows.Transitions = [new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(160) }];
        Content = new Border { CornerRadius = new CornerRadius(22), ClipToBounds = true, Child = new Panel { Children = { _card, _dots, arrows } } };
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(8), DispatcherPriority.Background, (_, _) => Show(_index + 1, false));
        AttachedToVisualTree += (_, _) => { if (_items.Count > 1 && !Program.Screenshot) _timer.Start(); };
        DetachedFromVisualTree += (_, _) => _timer.Stop();
        // Пока мышь над карточкой — не листаем, а стрелки проявляются.
        PointerEntered += (_, _) => { _timer.Stop(); arrows.Opacity = 1; };
        PointerExited += (_, _) => { arrows.Opacity = 0; if (_items.Count > 1 && !Program.Screenshot) _timer.Start(); };
        Show(0, false, animate: false);
    }

    static Button Arrow(string icon, Action go)
    {
        var b = new Button
        {
            Classes = { "icon" }, Width = 40, Height = 40, CornerRadius = new CornerRadius(20), VerticalAlignment = VerticalAlignment.Center,
            Background = Ui.Hex("#99000000"), BorderThickness = new Thickness(0), Foreground = Brushes.White, Content = Ui.Icon(icon, 16, Brushes.White),
        };
        b.Click += (_, _) => go();
        return b;
    }

    void Show(int index, bool user, bool animate = true)
    {
        if (_items.Count == 0) return;
        _index = (index % _items.Count + _items.Count) % _items.Count;
        if (user && _timer.IsEnabled) { _timer.Stop(); _timer.Start(); }
        var card = Card(_items[_index]);
        _card.Content = card;
        if (animate) Animate.From(card, "scale(1.03)", 520, 0, new Avalonia.Animation.Easings.CubicEaseOut(), 0.3);
        _dots.Children.Clear();
        for (var i = 0; i < _items.Count; i++)
        {
            var at = i;
            var dot = new Border
            {
                Width = i == _index ? 22 : 7, Height = 7, CornerRadius = new CornerRadius(4), Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Background = i == _index ? Brushes.White : Ui.Hex("#66FFFFFF"),
            };
            dot.PointerPressed += (_, _) => Show(at, true);
            _dots.Children.Add(dot);
        }
    }

    Control Card((GameState Game, ModInfo Mod) item)
    {
        var (g, mod) = item;
        var installed = Actions.IsInstalled(g, mod.Id);
        var busy = Actions.IsBusy(g, mod.Id);

        var eyebrow = new TextBlock
        {
            Text = $"{I18n.T("feat.eyebrow")} · {g.Def.Name}".ToUpperInvariant(),
            FontSize = 12, FontWeight = FontWeight.Bold, LetterSpacing = 1.2, Foreground = Ui.Hex("#D9FFFFFF"),
        };
        var title = new TextBlock { Text = mod.Name, FontSize = 34, FontWeight = FontWeight.Bold, LetterSpacing = -0.8, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis };
        var by = new TextBlock { Text = mod.Author == "" ? g.Def.Name : I18n.T("mod.by", ("author", mod.Author)), FontSize = 14, Foreground = Ui.Hex("#CCFFFFFF") };
        var icon = new Border { CornerRadius = new CornerRadius(16), ClipToBounds = true, BorderBrush = Ui.Hex("#40FFFFFF"), BorderThickness = new Thickness(1), Child = Ui.Thumb(mod.Icon, mod.Name, 64, 16, 200) };
        var heading = Ui.Row(16, icon, Ui.Col(2, title, by));
        heading.Children[1].VerticalAlignment = VerticalAlignment.Center;
        var description = new TextBlock { Text = mod.Description, FontSize = 15, Foreground = Ui.Hex("#E0FFFFFF"), TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, LineHeight = 22, MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };

        // Белая капсула на арте — как «Получить» на обложках App Store.
        Button action;
        if (installed) action = Ui.Button(I18n.T("mod.installed"), () => MainWindow.Current?.Navigate(() => new GamePage(g.Def.Id, "installed")), "", Icons.Check);
        else action = Ui.Button(busy ? I18n.T("tile.installing") : I18n.T("mod.install"), async () => { await Actions.Install(g, mod); Show(_index, false, false); }, "", Icons.Download);
        action.IsEnabled = !busy && g.Status == Detect.Found;
        action.Background = Brushes.White;
        action.Foreground = Ui.Hex("#111113");
        action.BorderThickness = new Thickness(0);
        action.CornerRadius = new CornerRadius(999);
        action.Padding = new Thickness(22, 11);
        var more = Ui.Button(I18n.T("feat.more"), () => MainWindow.Current?.Navigate(() => new ModPage(g.Def.Id, mod)), "ghost");
        more.Foreground = Brushes.White;
        more.Padding = new Thickness(16, 11);
        var text = Ui.Col(14, eyebrow, heading, description, Ui.Row(8, action, more));
        text.Margin = new Thickness(32, 0, 32, 30);
        text.VerticalAlignment = VerticalAlignment.Bottom;

        var art = Ui.GameImage(g.Def, 1600, art: Images.Art.Hero);
        return new Panel
        {
            Children =
            {
                art,
                // Затемнение снизу и слева — под текстом, сверху арт остаётся ярким.
                new Border { Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse("#000B0B0D"), 0.25), new GradientStop(Color.Parse("#B00B0B0D"), 0.7), new GradientStop(Color.Parse("#F00B0B0D"), 1) },
                } },
                new Border { Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse("#990B0B0D"), 0), new GradientStop(Color.Parse("#000B0B0D"), 0.65) },
                } },
                text,
            },
        };
    }
}
