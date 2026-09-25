using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>
/// «Выбор ModLaunch» — большая карусель лучших модов, как в ModLaunch 3:
/// крупная карточка со свечением цвета игры, стрелки, миниатюры снизу и
/// автопрокрутка раз в 8 секунд (перерисовывается только сама карусель).
/// </summary>
public sealed class Featured : UserControl
{
    readonly List<(GameState Game, ModInfo Mod)> _items;
    readonly ContentControl _card = new();
    readonly UniformGrid _thumbs = new() { Columns = 4, Margin = new Thickness(0, 12, 0, 0) };
    readonly DispatcherTimer _timer;
    int _index;

    public Featured(List<(GameState Game, ModInfo Mod)> items)
    {
        _items = items;
        var prev = Arrow(Icons.Back, () => Show(_index - 1, true));
        var next = Arrow(Icons.Forward, () => Show(_index + 1, true));
        prev.HorizontalAlignment = HorizontalAlignment.Left;
        next.HorizontalAlignment = HorizontalAlignment.Right;
        prev.Margin = new Thickness(10, 0, 0, 0);
        next.Margin = new Thickness(0, 0, 10, 0);
        var stage = new Panel { Children = { _card, prev, next } };
        Content = Ui.Col(0, stage, _thumbs);
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(8), DispatcherPriority.Background, (_, _) => Show(_index + 1, false));
        AttachedToVisualTree += (_, _) => { if (_items.Count > 1 && !Program.Screenshot) _timer.Start(); };
        DetachedFromVisualTree += (_, _) => _timer.Stop();
        PointerEntered += (_, _) => _timer.Stop(); // пока мышь над каруселью — не листаем
        PointerExited += (_, _) => { if (_items.Count > 1 && !Program.Screenshot) _timer.Start(); };
        Show(0, false, animate: false);
    }

    static Button Arrow(string icon, Action go)
    {
        var b = new Button
        {
            Classes = { "icon" }, Width = 44, Height = 44, CornerRadius = new CornerRadius(22), VerticalAlignment = VerticalAlignment.Center,
            Background = Ui.Hex("#CC12141A"), BorderBrush = Ui.Res("Line"), Content = Ui.Icon(icon, 18),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        };
        b.Click += (_, _) => go();
        return b;
    }

    void Show(int index, bool user, bool animate = true)
    {
        if (_items.Count == 0) return;
        var forward = index >= _index;
        _index = (index % _items.Count + _items.Count) % _items.Count;
        if (user && _timer.IsEnabled) { _timer.Stop(); _timer.Start(); }
        var card = Card(_items[_index]);
        _card.Content = card;
        if (animate) Animate.From(card, forward ? "translateX(36px)" : "translateX(-36px)", 380, 0, new Avalonia.Animation.Easings.CubicEaseOut(), 0.2);
        RenderThumbs();
    }

    Control Card((GameState Game, ModInfo Mod) item)
    {
        var (g, mod) = item;
        var accent = Color.Parse(g.Def.Accent);
        var installed = Actions.IsInstalled(g, mod.Id);
        var busy = Actions.IsBusy(g, mod.Id);

        var pick = new Border
        {
            Background = Ui.Res("BrandSoft"), BorderBrush = Ui.Res("Brand"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(999), Padding = new Thickness(10, 3),
            Child = Ui.Row(6, new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(4), ClipToBounds = true, Child = new Image { Source = Images.Asset("icon.png", 32) } },
                new TextBlock { Text = I18n.T("feat.pick"), FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Ui.Res("Brand2") }),
        };
        var game = new Border
        {
            Background = Ui.Hex("#99000000"), BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(999), Padding = new Thickness(4, 3, 10, 3),
            Child = Ui.Row(6, new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), ClipToBounds = true, Child = Ui.GameImage(g.Def, 40, art: Images.Art.Cover) },
                new TextBlock { Text = g.Def.Name, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White }),
        };

        Button action;
        if (installed) action = Ui.Button(I18n.T("mod.installed"), () => MainWindow.Current?.Navigate(() => new GamePage(g.Def.Id, "installed")), "", Icons.Check);
        else action = Ui.Button(busy ? I18n.T("aside.installing") : I18n.T("mod.install"), async () => { await Actions.Install(g, mod); Show(_index, false, false); }, "primary", Icons.Download);
        action.IsEnabled = !busy && g.Status == Detect.Found;
        action.Padding = new Thickness(26, 13);
        action.FontSize = 15;
        action.Background = installed ? null : new SolidColorBrush(accent);
        var more = Ui.Button(I18n.T("feat.more"), () => MainWindow.Current?.Navigate(() => new ModPage(g.Def.Id, mod)), "");
        more.Padding = new Thickness(22, 13);
        more.FontSize = 15;

        var facts = Ui.Row(16);
        if (mod.Downloads > 0) facts.Children.Add(Ui.Row(6, Ui.Icon(Icons.Download, 13, Ui.Hex("#C9CFDB")), Ui.Text(I18n.Compact(mod.Downloads), "small", color: Ui.Hex("#C9CFDB"))));
        if (mod.UpdatedAt is not null) facts.Children.Add(Ui.Row(6, Ui.Icon(Icons.Refresh, 13, Ui.Hex("#C9CFDB")), Ui.Text(Ui.Ago(mod.UpdatedAt), "small", color: Ui.Hex("#C9CFDB"))));

        var text = Ui.Col(12,
            Ui.Row(8, pick, game),
            new TextBlock { Text = mod.Name, FontSize = 34, FontWeight = FontWeight.Bold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis },
            new TextBlock { Text = mod.Author == "" ? g.Def.Name : I18n.T("mod.by", ("author", mod.Author)), FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Lighten(accent)) },
            new TextBlock { Text = mod.Description, FontSize = 15, Foreground = Ui.Hex("#D5DAE5"), TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, LineHeight = 22 },
            facts,
            Ui.Row(10, action, more));
        text.VerticalAlignment = VerticalAlignment.Center;

        var picture = new Border
        {
            Width = 300, Height = 188, CornerRadius = new CornerRadius(16), ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center,
            BorderBrush = Ui.Hex("#33FFFFFF"), BorderThickness = new Thickness(1), BoxShadow = BoxShadows.Parse("0 20 40 -12 #A0000000"),
            Child = Ui.Thumb(mod.Icon, mod.Name, 300, 0, 600),
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 30, Margin = new Thickness(66, 34) };
        grid.Children.Add(text);
        Grid.SetColumn(picture, 1);
        grid.Children.Add(picture);

        var bg = new Panel
        {
            Children =
            {
                new Border { Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.FromArgb(70, accent.R, accent.G, accent.B), 0), new GradientStop(Color.Parse("#14161D"), 0.55), new GradientStop(Color.FromArgb(55, accent.R, accent.G, accent.B), 1) },
                } },
                new Border { Background = new RadialGradientBrush
                {
                    Center = new RelativePoint(0.78, 0.5, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0.78, 0.5, RelativeUnit.Relative),
                    RadiusX = new RelativeScalar(0.45, RelativeUnit.Relative), RadiusY = new RelativeScalar(0.8, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.FromArgb(110, accent.R, accent.G, accent.B), 0), new GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), 1) },
                } },
                grid,
            },
        };
        return new Border { CornerRadius = new CornerRadius(22), ClipToBounds = true, BorderBrush = Ui.Hex("#2EFFFFFF"), BorderThickness = new Thickness(1), MinHeight = 300, Child = bg };
    }

    static Color Lighten(Color c) => Color.FromRgb((byte)(c.R + (255 - c.R) * 0.45), (byte)(c.G + (255 - c.G) * 0.45), (byte)(c.B + (255 - c.B) * 0.45));

    void RenderThumbs()
    {
        _thumbs.Children.Clear();
        var count = Math.Min(4, _items.Count);
        var start = Math.Clamp(_index - 1, 0, Math.Max(0, _items.Count - count));
        for (var i = start; i < start + count; i++)
        {
            var (g, mod) = _items[i];
            var index = i;
            var b = new Button
            {
                Classes = { "card-btn" }, Padding = new Thickness(8), Margin = new Thickness(0, 0, i < start + count - 1 ? 10 : 0, 0),
                Content = Ui.Row(10, new Border { Width = 64, Height = 40, CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = Ui.Thumb(mod.Icon, mod.Name, 64, 0, 160) },
                    Ui.Col(1, new TextBlock { Text = mod.Name, FontWeight = FontWeight.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 130 }, Ui.Text(g.Def.ShortName, "small muted"))),
            };
            if (i == _index) { b.BorderBrush = Ui.Res("Brand"); b.Background = Ui.Res("Surface2"); }
            b.Click += (_, _) => Show(index, true);
            _thumbs.Children.Add(b);
        }
    }
}
