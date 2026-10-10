using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ModLaunch.Core;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>
/// Строительные блоки дизайна 9.2 «Store» (как в Microsoft Store): заголовок раздела со стрелкой,
/// полка с прокруткой и кнопками «‹ ›», обложка игры с подписью поверх, строка мода
/// (значок, название, игра, кнопка) и широкая карточка «Выбор ModLaunch».
/// Все экраны Store собираются только из них — поэтому отступы и размеры везде одинаковые.
/// </summary>
public static class StoreKit
{
    /// <summary>Поля страницы и ширина колонки: одна сетка для главной, библиотеки и страницы игры.</summary>
    public const double Gutter = 36;
    public const double MaxWidth = 1480;
    public const double Gap = 12;

    /// <summary>Колонка страницы: по центру, с одинаковыми полями слева и справа.</summary>
    public static StackPanel Column(double spacing = 34, double top = 24) => new()
    {
        Spacing = spacing, Margin = new Thickness(Gutter, top, Gutter, 40), MaxWidth = MaxWidth, HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    public static IBrush White => Brushes.White;

    /// <summary>«Ваши игры ›» — заголовок раздела; со ссылкой — весь заголовок кликабельный.</summary>
    public static Control Header(string title, Action? open = null, Control? right = null, string? count = null)
    {
        var text = new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeight.SemiBold, LetterSpacing = -0.3, VerticalAlignment = VerticalAlignment.Center };
        var words = Ui.Row(8, text);
        if (count is not null) words.Children.Add(new TextBlock { Text = count, FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = Ui.Res("Faint"), VerticalAlignment = VerticalAlignment.Center });
        Control head = words;
        if (open is not null)
        {
            words.Children.Add(Ui.Icon(Icons.ChevronRight, 18, Ui.Res("Muted")));
            var b = new Button { Classes = { "shelf-link" }, Content = words, Padding = new Thickness(0, 2, 6, 2), Margin = new Thickness(-2, 0, 0, 0) };
            b.Click += (_, _) => open();
            head = b;
        }
        var row = new DockPanel { LastChildFill = false };
        if (right is not null)
        {
            DockPanel.SetDock(right, Dock.Right);
            right.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(right);
        }
        DockPanel.SetDock(head, Dock.Left);
        row.Children.Add(head);
        return row;
    }

    /// <summary>Полка: ряд карточек с прокруткой вбок и круглыми кнопками «‹ ›» по краям.</summary>
    public static Control Shelf(IEnumerable<Control> items, double spacing = Gap)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
        foreach (var c in items) row.Children.Add(c);
        var scroll = new ScrollViewer
        {
            Content = row, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            // Запас сверху и снизу — под подъём и тень карточки при наведении.
            Padding = new Thickness(0, 6, 0, 14), Margin = new Thickness(0, -6, 0, -14),
        };
        Button Arrow(string icon, int dir)
        {
            var b = new Button
            {
                Classes = { "icon", "shelf-arrow" }, Width = 38, Height = 38, CornerRadius = new CornerRadius(19), Content = Ui.Icon(icon, 16),
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = dir < 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                Margin = new Thickness(dir < 0 ? -14 : 0, 0, dir > 0 ? -14 : 0, 0), IsVisible = false,
            };
            b.Click += (_, _) =>
            {
                var max = Math.Max(0, scroll.Extent.Width - scroll.Viewport.Width);
                scroll.Offset = new Vector(Math.Clamp(scroll.Offset.X + dir * scroll.Viewport.Width * 0.8, 0, max), 0);
            };
            return b;
        }
        var left = Arrow(Icons.ChevronLeft, -1);
        var right = Arrow(Icons.ChevronRight, 1);
        void Update()
        {
            left.IsVisible = scroll.Offset.X > 1;
            right.IsVisible = scroll.Offset.X < scroll.Extent.Width - scroll.Viewport.Width - 1;
        }
        scroll.ScrollChanged += (_, _) => Update();
        return new Panel { Children = { scroll, left, right } };
    }

    /// <summary>Мягкая «пилюля» поверх картинки: состояние игры, число модов.</summary>
    public static Border Pill(string text, IBrush? dot = null, bool onArt = true)
    {
        var words = new TextBlock { Text = text, FontSize = 11.5, FontWeight = FontWeight.SemiBold, Foreground = onArt ? Brushes.White : Ui.Res("Text"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        return new Border
        {
            Background = onArt ? new SolidColorBrush(Color.FromArgb(150, 10, 10, 12)) : Ui.Res("Surface3"),
            CornerRadius = new CornerRadius(5), Padding = new Thickness(7, 3), HorizontalAlignment = HorizontalAlignment.Left,
            Child = dot is null ? words : Ui.Row(6, Ui.Dot(dot, 7), words),
        };
    }

    static LinearGradientBrush Fade(double from, byte alpha = 235) => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.FromArgb(0, 8, 8, 10), from),
            new GradientStop(Color.FromArgb((byte)(alpha * 0.82), 8, 8, 10), from + (1 - from) * 0.62),
            new GradientStop(Color.FromArgb(alpha, 8, 8, 10), 1),
        },
    };

    /// <summary>Клик левой кнопкой по карточке (кнопки внутри обрабатывают свои нажатия сами).</summary>
    public static void OnClick(Control c, Action open)
    {
        c.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            if (e.Source is Visual v && Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<Button>(v, true) is not null) return;
            var p = e.GetPosition(c);
            if (p.X < 0 || p.Y < 0 || p.X > c.Bounds.Width || p.Y > c.Bounds.Height) return;
            open();
        };
    }

    /// <summary>Состояние игры для обложки: цвет точки и короткая подпись.</summary>
    public static (IBrush Dot, string Text) GameState(GameState g)
    {
        if (Features.Launcher.IsRunning(g.Def.Id)) return (Ui.Res("Good"), I18n.T("run.running"));
        return g.Status switch
        {
            Detect.Found when g.Def.IsMinecraft => (Ui.Res("Good"), Minecraft.Mc.CardLine()),
            Detect.Found when g.Def.Loader == Games.LoaderKind.None => (Ui.Res("Good"), g.ModCount > 0 ? Mods(g.ModCount) : I18n.T("v92.game.ready")),
            Detect.Found when !g.LoaderInstalled => (Ui.Res("Warn"), I18n.T("home.loaderNeeded", ("loader", g.Def.LoaderName))),
            Detect.Found when g.ModCount > 0 => (Ui.Res("Good"), Mods(g.ModCount)),
            Detect.Found => (Ui.Res("Good"), I18n.T("v92.game.ready")),
            Detect.Searching => (Ui.Res("Muted"), I18n.T("games.searching")),
            _ => (Ui.Res("Faint"), I18n.T("games.notDetected")),
        };
    }

    public static string Mods(int n) => I18n.T("aside.mods." + I18n.Plural(n, "one", "few", "many"), ("n", n));

    /// <summary>
    /// Обложка игры 2:3 как на полках Microsoft Store: картинка во всю карточку, внизу — название
    /// и состояние; при наведении — подъём и круглая кнопка «Играть».
    /// </summary>
    public static Control Poster(GameState g, double width = 156)
    {
        var height = Math.Round(width * 1.5);
        var found = g.Status == Detect.Found;
        var (dot, state) = GameState(g);
        var name = new TextBlock
        {
            Text = g.Def.Name, Foreground = White, FontWeight = FontWeight.SemiBold, FontSize = 13.5, TextWrapping = TextWrapping.Wrap,
            MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, LineHeight = 17,
        };
        var bottom = new StackPanel { Spacing = 7, Margin = new Thickness(10, 0, 10, 10), VerticalAlignment = VerticalAlignment.Bottom, Children = { name, Pill(state, dot) } };
        var layers = new Panel { Children = { Ui.GameImage(g.Def, (int)(width * 2), art: Images.Art.Cover), new Border { Background = Fade(0.48, 248) }, bottom } };
        if (found && (g.LoaderInstalled || g.Def.Loader is Games.LoaderKind.None or Games.LoaderKind.Minecraft) && !Features.Launcher.IsRunning(g.Def.Id))
        {
            var gs = g;
            var play = Ui.Button("", () => Actions.Play(gs), "icon primary tile-play", Icons.Play, I18n.T("games.play"));
            play.Width = play.Height = 38;
            play.CornerRadius = new CornerRadius(19);
            play.HorizontalAlignment = HorizontalAlignment.Right;
            play.VerticalAlignment = VerticalAlignment.Top;
            play.Margin = new Thickness(8);
            layers.Children.Add(play);
        }
        if (g.Def.Custom)
            layers.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(190, 10, 10, 12)), CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 2),
                Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock { Text = I18n.T("lib.custom"), FontSize = 11, Foreground = White },
            });
        if (Features.GameCollections.IsFavorite(g.Def.Id))
            layers.Children.Add(new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Margin = new Thickness(8), Background = new SolidColorBrush(Color.FromArgb(190, 10, 10, 12)),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Child = Ui.Icon(Icons.Star, 12, Ui.Hex("#F2C25C"), fill: true),
            });
        var tile = new Border { Classes = { "store-tile", "poster" }, Width = width, Height = height, Child = layers, Opacity = found ? 1 : 0.55 };
        ToolTip.SetTip(tile, g.Def.Name);
        var id = g.Def.Id;
        OnClick(tile, () => MainWindow.Current?.Navigate(() => new GamePage(id)));
        tile.ContextFlyout = GameCard.Menu(g);
        return tile;
    }

    /// <summary>Карточка «Добавить игру» в размер обложки.</summary>
    public static Control AddPoster(double width = 156)
    {
        var tile = new Border
        {
            Classes = { "store-tile", "add" }, Width = width, Height = Math.Round(width * 1.5), Background = Brushes.Transparent,
            BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1.5),
            Child = Ui.Col(10,
                new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(23), Background = Ui.Res("Surface2"), HorizontalAlignment = HorizontalAlignment.Center, Child = Ui.Icon(Icons.Plus, 20, Ui.Res("Text")) },
                new TextBlock { Text = I18n.T("add.title"), FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = I18n.T("v92.add.hint"), FontSize = 12, Foreground = Ui.Res("Muted"), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = width - 24 }),
        };
        if (tile.Child is StackPanel sp) sp.VerticalAlignment = VerticalAlignment.Center;
        OnClick(tile, () => MainWindow.Current?.Navigate(() => new AddGamePage()));
        return tile;
    }

    /// <summary>
    /// Строка мода как в списках Microsoft Store («Популярные приложения»): значок, название,
    /// игра и раздел, справа — «Установить» или «Установлен».
    /// </summary>
    public static Control ModItem(GameState g, ModInfo m, bool showGame = true)
    {
        var installed = Actions.IsInstalled(g, m.Id);
        var busy = Actions.IsBusy(g, m.Id);
        var sub = new List<string>();
        if (showGame) sub.Add(g.Def.ShortName);
        if (m.Categories.FirstOrDefault() is { } cat) sub.Add(cat);
        else if (m.Author != "") sub.Add(m.Author);
        var words = Ui.Col(3,
            new TextBlock { Text = m.Name, FontWeight = FontWeight.SemiBold, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis },
            new TextBlock { Text = string.Join(" · ", sub), FontSize = 12, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis },
            new TextBlock { Text = m.Downloads > 0 ? "↓ " + I18n.Compact(m.Downloads) : " ", FontSize = 11.5, Foreground = Ui.Res("Faint") });
        words.VerticalAlignment = VerticalAlignment.Center;
        Control action;
        if (installed)
            action = Ui.Row(5, Ui.Icon(Icons.Check, 13, Ui.Res("Good")), new TextBlock { Text = I18n.T("mod.installed"), FontSize = 12, Foreground = Ui.Res("Muted"), VerticalAlignment = VerticalAlignment.Center });
        else
        {
            var gs = g; var mm = m;
            var b = Ui.Button(busy ? "…" : I18n.T("mod.install"), () => _ = Actions.Install(gs, mm), "", null);
            b.Padding = new Thickness(12, 5);
            b.FontSize = 12.5;
            b.IsEnabled = !busy && g.Status == Detect.Found;
            action = b;
        }
        action.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
        grid.Children.Add(Ui.Thumb(m.Icon, m.Name, 56, 10, 140));
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);
        Grid.SetColumn(action, 2);
        grid.Children.Add(action);
        var row = new Border { Classes = { "store-row" }, Padding = new Thickness(8), Child = grid };
        var id = g.Def.Id;
        OnClick(row, () => MainWindow.Current?.Navigate(() => new ModPage(id, m)));
        Ctx.Attach(row, () => ModRow.Menu(g.Def, m, installed, busy, () => _ = Actions.Install(g, m), () => MainWindow.Current?.Navigate(() => new ModPage(id, m))));
        return row;
    }

    /// <summary>Сетка строк модов: столбцы одинаковой ширины (как «Популярные игры» в Store).</summary>
    public static Control ModGrid(IEnumerable<Control> rows, int columns = 3)
    {
        var grid = new Grid { ColumnSpacing = 18, RowSpacing = 4 };
        for (var i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        var list = rows.ToList();
        var count = (list.Count + columns - 1) / columns;
        for (var r = 0; r < count; r++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        // Заполняем по столбцам: сначала первый столбец сверху вниз — так читается рейтинг.
        for (var i = 0; i < list.Count; i++)
        {
            var c = list[i];
            Grid.SetColumn(c, i / Math.Max(1, count));
            Grid.SetRow(c, i % Math.Max(1, count));
            grid.Children.Add(c);
        }
        return grid;
    }

    /// <summary>
    /// Широкая карточка мода («Выбор ModLaunch»): сверху — арт игры с крупным значком мода,
    /// снизу — название, игра и кнопка.
    /// </summary>
    public static Control ModCard(GameState g, ModInfo m, double width = 292)
    {
        var artHeight = Math.Round(width * 9 / 16);
        var art = Ui.GameImage(g.Def, (int)(width * 2), art: Images.Art.Hero);
        var icon = new Border
        {
            Width = 92, Height = 92, CornerRadius = new CornerRadius(18), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), BorderThickness = new Thickness(1),
            Child = Ui.Thumb(m.Icon, m.Name, 92, 0, 220),
        };
        var top = new Panel
        {
            Height = artHeight, ClipToBounds = true,
            Children = { art, new Border { Background = new SolidColorBrush(Color.FromArgb(120, 8, 8, 10)) }, new Border { Child = icon, BoxShadow = BoxShadows.Parse("0 12 30 0 #90000000"), CornerRadius = new CornerRadius(18), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } },
        };
        var installed = Actions.IsInstalled(g, m.Id);
        var gs = g; var mm = m;
        var action = installed
            ? Ui.Button("", () => { }, "icon", Icons.Check, I18n.T("mod.installed"))
            : Ui.Button("", () => _ = Actions.Install(gs, mm), "icon primary", Icons.Download, I18n.T("mod.install"));
        action.IsEnabled = !installed && !Actions.IsBusy(g, m.Id) && g.Status == Detect.Found;
        action.Width = action.Height = 34;
        action.VerticalAlignment = VerticalAlignment.Center;
        var foot = new DockPanel { Margin = new Thickness(12, 10, 10, 12) };
        DockPanel.SetDock(action, Dock.Right);
        foot.Children.Add(action);
        foot.Children.Add(Ui.Col(2,
            new TextBlock { Text = m.Name, FontWeight = FontWeight.SemiBold, FontSize = 14.5, TextTrimming = TextTrimming.CharacterEllipsis },
            new TextBlock { Text = m.Author == "" ? g.Def.Name : $"{g.Def.ShortName} · {m.Author}", FontSize = 12, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis }));
        var tile = new Border { Classes = { "store-tile" }, Width = width, Child = Ui.Col(0, top, foot) };
        var id = g.Def.Id;
        OnClick(tile, () => MainWindow.Current?.Navigate(() => new ModPage(id, m)));
        Ctx.Attach(tile, () => ModRow.Menu(g.Def, m, installed, false, () => _ = Actions.Install(g, m), () => MainWindow.Current?.Navigate(() => new ModPage(id, m))));
        return tile;
    }

    /// <summary>Плитка-ссылка с картинкой или градиентом и подписью внизу (правый столбец героя на главной).</summary>
    public static Control LinkTile(Control background, string eyebrow, string title, string? text, Action open, string? icon = null)
    {
        var words = Ui.Col(3);
        if (eyebrow != "") words.Children.Add(new TextBlock { Text = eyebrow.ToUpper(I18n.Culture), FontSize = 11, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.7, Foreground = Ui.Hex("#D9DBE3") });
        words.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeight.Bold, Foreground = White, TextTrimming = TextTrimming.CharacterEllipsis });
        if (text is not null) words.Children.Add(new TextBlock { Text = text, FontSize = 12.5, Foreground = Ui.Hex("#C9CCD6"), TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis });
        words.VerticalAlignment = VerticalAlignment.Bottom;
        words.Margin = new Thickness(16, 0, 16, 14);
        var layers = new Panel { Children = { background, new Border { Background = Fade(0.25, 225) }, words } };
        if (icon is not null)
            layers.Children.Add(new Border
            {
                Width = 40, Height = 40, CornerRadius = new CornerRadius(10), Margin = new Thickness(14), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Background = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), Child = Ui.Icon(icon, 20, White),
            });
        var tile = new Border { Classes = { "store-tile" }, Child = layers };
        OnClick(tile, open);
        return tile;
    }

    /// <summary>Фон плитки: градиент из двух цветов.</summary>
    public static Control Gradient(Color a, Color b) => new Border
    {
        Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(a, 0), new GradientStop(b, 1) },
        },
    };

    /// <summary>Картинка из ресурсов программы во всю плитку.</summary>
    public static Control AssetImage(string name, int width) => new Image { Classes = { "zoom" }, Source = Images.Asset(name, width), Stretch = Stretch.UniformToFill };

    /// <summary>Картинка по ссылке во всю плитку (пока грузится — фон поверхности).</summary>
    public static Control UrlImage(string? url, int width)
    {
        var image = new Image { Classes = { "zoom" }, Stretch = Stretch.UniformToFill };
        if (!string.IsNullOrEmpty(url))
            _ = Images.FromUrl(url, width).ContinueWith(t => { if (t.Result is Bitmap b) Avalonia.Threading.Dispatcher.UIThread.Post(() => image.Source = b); });
        return new Panel { Children = { new Border { Background = Ui.Res("Surface2") }, image } };
    }

    /// <summary>
    /// 9.3: сетка карточек 16:9 на всю ширину — столбцов столько, сколько влезает (не уже minWidth),
    /// высота карточек подстраивается, справа не остаётся пустой полосы.
    /// </summary>
    public static Control Tiles(IEnumerable<Control> items, double minWidth = 250, double extra = 62)
    {
        var list = items.ToList();
        var grid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -Gap, 0) };
        foreach (var c in list)
        {
            c.Width = double.NaN;
            c.Height = Math.Round(minWidth * 9 / 16) + extra;
            c.Margin = new Thickness(0, 0, Gap, Gap);
            grid.Children.Add(c);
        }
        grid.SizeChanged += (_, e) =>
        {
            var cols = Math.Max(1, (int)(e.NewSize.Width / (minWidth + Gap)));
            if (grid.Columns != cols) grid.Columns = cols;
            var w = e.NewSize.Width / cols - Gap;
            foreach (var c in list) c.Height = Math.Round(w * 9 / 16) + extra;
        };
        return grid;
    }

    /// <summary>Заготовка карточки, пока данные грузятся.</summary>
    public static Control Placeholder(double width, double height) => new Border
    {
        Classes = { "shimmer" }, Width = width, Height = height, CornerRadius = new CornerRadius(10), Background = Ui.Res("Surface"),
    };
}

/// <summary>
/// Слой-подложка: заполняет место, которое дали соседи, но сам места не просит. Нужен, чтобы
/// арт игры в шапке не растягивал её по своей высоте: высоту задаёт содержимое шапки.
/// </summary>
public sealed class FillLayer : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var w = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        foreach (var c in Children) c.Measure(new Size(w, 0));
        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var c in Children)
        {
            c.Measure(finalSize);
            c.Arrange(new Rect(finalSize));
        }
        return finalSize;
    }
}
