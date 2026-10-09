using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Transformation;
using ModLaunch.Core;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>
/// 9.1: главная из плиток (по эскизу «главное меню» и «случайное расположение плиток»).
/// Раскладка выбирается наугад при каждом запуске (A — герой с веером обложек, B — постеры
/// и баннеры, C — герой и колонка справа); кнопка «Перемешать плитки» выбирает другую.
/// </summary>
public sealed partial class HomePage
{
    static readonly string[] Layouts = ["a", "b", "c"];
    static string? _layout;
    static readonly int Seed = Environment.TickCount;

    /// <summary>Раскладка этого запуска.</summary>
    public static string Layout => _layout ??= PickLayout(null);

    /// <summary>Для снимков: задать раскладку вручную.</summary>
    public static void DemoLayout(string layout) => _layout = layout;

    static string PickLayout(string? not)
    {
        if (Program.Screenshot) return "a";
        if (!Settings.Data.Bool("homeShuffle", true)) return Settings.Data.Str("homeLayout") is string fixedLayout && Layouts.Contains(fixedLayout) ? fixedLayout : "a";
        var options = Layouts.Where(l => l != not).ToArray();
        return options[Random.Shared.Next(options.Length)];
    }

    void Shuffle()
    {
        _layout = PickLayout(_layout);
        Shown = false;
        Build();
        Shown = true;
    }

    /// <summary>Моды для плиток: «Выбор ModLaunch» и популярные — в случайном, но постоянном за сеанс порядке.</summary>
    static List<(GameState Game, ModInfo Mod)> TileMods(List<GameState> mine)
    {
        var list = new List<(GameState, ModInfo)>();
        if (_featured is not null) list.AddRange(_featured);
        foreach (var g in mine)
            foreach (var m in (Views.Aside.Popular(g) ?? []).Take(6))
                if (!list.Any(x => x.Item1.Def.Id == g.Def.Id && x.Item2.Id == m.Id)) list.Add((g, m));
        var rng = new Random(Seed);
        return list.Where(x => !x.Item2.Adult).OrderBy(_ => rng.Next()).Take(12).ToList();
    }

    /// <summary>Верх главной: плитки в выбранной раскладке и кнопка «Перемешать».</summary>
    Control Tiles(List<(GameState Game, Features.Played Played)> recent, List<GameState> mine)
    {
        var mods = TileMods(mine);
        (GameState Game, Features.Played Played)? hero = null;
        if (recent.Count > 0) hero = recent[0];
        else if (mine.Count > 0) hero = (mine[0], Features.PlayTime.Get(mine[0].Def.Id));
        // Веер обложек: ваши игры, кроме той, что в герое; если своих мало — популярные из каталога.
        var fan = mine.Where(g => g.Def.Id != hero?.Game.Def.Id)
            .Concat(AppState.Games.Where(g => g.Status != Detect.Found && !g.Def.Custom && g.Def.Id != hero?.Game.Def.Id))
            .Take(3).ToList();

        var index = 0;
        Control T(Control c) { if (!Shown) Animate.Rise(c, index); index++; return c; }

        var col = new StackPanel { Spacing = 16 };
        var shuffle = Ui.Button(I18n.T("v91.home.shuffle"), Shuffle, "ghost", Icons.Shuffle);
        shuffle.Foreground = Ui.Res("Muted");
        shuffle.Padding = new Thickness(10, 5);
        shuffle.FontSize = 13;
        shuffle.HorizontalAlignment = HorizontalAlignment.Right;
        shuffle.Margin = new Thickness(0, -10, 0, -6);
        shuffle.IsVisible = Settings.Data.Bool("homeShuffle", true) || Program.Screenshot;
        col.Children.Add(shuffle);

        Control Hero(bool withFan) => hero is { } h ? ContinueHero(h.Game, h.Played, withFan ? fan : null) : StartTile(fan);

        switch (Layout)
        {
            case "b":
            {
                var posters = new UniformGrid { Columns = 3 };
                var games = mine.Concat(AppState.Games.Where(g => g.Status != Detect.Found && !g.Def.Custom)).Take(3).ToList();
                for (var i = 0; i < games.Count; i++) posters.Children.Add(T(Poster(games[i], i < games.Count - 1)));
                col.Children.Add(posters);
                if (hero is { } h) col.Children.Add(T(Banner(h.Game, h.Played)));
                col.Children.Add(T(mods.Count > 0 ? WideMod(mods[0], 176) : Placeholder(176)));
                var small = new UniformGrid { Columns = 4 };
                for (var i = 1; i <= 4; i++) small.Children.Add(T(i < mods.Count ? ModTile(mods[i], 176, i < 4) : Placeholder(176, i < 4)));
                col.Children.Add(small);
                break;
            }
            case "c":
            {
                var top = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*"), ColumnSpacing = 16 };
                top.Children.Add(T(Hero(false)));
                var side = new Grid { RowDefinitions = new RowDefinitions("*,*"), RowSpacing = 16 };
                var s1 = mods.Count > 0 ? ModTile(mods[0], 0, false) : Placeholder(0, false);
                var s2 = mods.Count > 1 ? ModTile(mods[1], 0, false) : Placeholder(0, false);
                Grid.SetRow(s2, 1);
                side.Children.Add(T(s1));
                side.Children.Add(T(s2));
                side.Height = 372;
                Grid.SetColumn(side, 1);
                top.Children.Add(side);
                col.Children.Add(top);
                col.Children.Add(T(CirclesRow()));
                col.Children.Add(SixTiles(mods, 2, T));
                break;
            }
            default:
            {
                col.Children.Add(T(Hero(true)));
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*,Auto"), ColumnSpacing = 16, Height = 236 };
                row.Children.Add(T(mods.Count > 0 ? WideMod(mods[0], 236) : Placeholder(236)));
                var square = mods.Count > 1 ? ModTile(mods[1], 236, false) : Placeholder(236, false);
                Grid.SetColumn(square, 1);
                row.Children.Add(T(square));
                var circles = T(CirclesColumn());
                Grid.SetColumn(circles, 2);
                row.Children.Add(circles);
                col.Children.Add(row);
                col.Children.Add(SixTiles(mods, 2, T));
                break;
            }
        }
        return col;
    }

    Control SixTiles(List<(GameState Game, ModInfo Mod)> mods, int from, Func<Control, Control> intro)
    {
        var grid = new UniformGrid { Columns = 6 };
        for (var i = 0; i < 6; i++)
        {
            var at = from + i;
            grid.Children.Add(intro(at < mods.Count ? ModTile(mods[at], 168, i < 5) : Placeholder(168, i < 5)));
        }
        return grid;
    }

    // ---------------------------------------------------------------- плитки

    static readonly Thickness Gap = new(0, 0, 16, 0);

    /// <summary>Картинка во всю плитку: пока грузится (или если её нет) — арт игры.</summary>
    static Control Fill(string? url, Games.GameDef game, Images.Art fallback = Images.Art.Header)
    {
        var back = Ui.GameImage(game, 600, art: fallback);
        if (string.IsNullOrEmpty(url)) return back;
        var image = new Image { Classes = { "zoom" }, Stretch = Stretch.UniformToFill };
        _ = Images.FromUrl(url, 480).ContinueWith(t =>
        {
            if (t.Result is Bitmap bmp) Avalonia.Threading.Dispatcher.UIThread.Post(() => image.Source = bmp);
        });
        return new Panel { Children = { back, image } };
    }

    static IBrush Shade(double from = 0.35) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse("#00080A0E"), from), new GradientStop(Color.Parse("#E6080A0E"), 1) },
    };

    static Button InstallIcon(GameState g, ModInfo m)
    {
        var installed = Actions.IsInstalled(g, m.Id);
        var busy = Actions.IsBusy(g, m.Id);
        var b = installed
            ? Ui.Button("", () => MainWindow.Current?.Navigate(() => new ModPage(g.Def.Id, m)), "icon", Icons.Check, I18n.T("mod.installed"))
            : Ui.Button("", () => _ = Actions.Install(g, m), "icon primary", busy ? Icons.Clock : Icons.Download, busy ? I18n.T("aside.installing") : I18n.T("mod.install"));
        b.IsEnabled = !busy;
        if (installed) b.Foreground = Ui.Res("Good");
        b.HorizontalAlignment = HorizontalAlignment.Right;
        b.VerticalAlignment = VerticalAlignment.Top;
        b.Margin = new Thickness(10);
        b.Classes.Add("cover-play");
        return b;
    }

    /// <summary>
    /// Кнопка-плитка: подъём при наведении (card-btn), «Скачать»/«Играть» в углу появляется при наведении (cover-btn),
    /// контур цвета акцента. Содержимое растягивается на всю плитку.
    /// </summary>
    static Button TileButton(Control content, bool gap) => new()
    {
        Classes = { "card-btn", "cover-btn" }, Padding = new Thickness(0), Margin = gap ? Gap : default,
        HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
        Background = Brushes.Transparent,
        Transitions = [new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(280), Easing = new BackEaseOut() }],
        Content = content,
    };

    /// <summary>Плитка мода: картинка во всю плитку, название снизу, «Скачать» в углу.</summary>
    static Control ModTile((GameState Game, ModInfo Mod) item, double height, bool gap)
    {
        var (g, m) = item;
        var name = new TextBlock { Text = m.Name, FontSize = height is > 0 and < 200 ? 14 : 16, FontWeight = FontWeight.Bold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis };
        var game = new TextBlock { Text = g.Def.ShortName, FontSize = 12, Foreground = Ui.Hex("#C9CFDC"), TextTrimming = TextTrimming.CharacterEllipsis };
        var words = Ui.Col(1, name, game);
        words.VerticalAlignment = VerticalAlignment.Bottom;
        words.Margin = new Thickness(14, 0, 14, 12);
        var layers = new Panel { Children = { Fill(m.Icon, g.Def), new Border { Background = Shade(0.3) }, words, InstallIcon(g, m) } };
        var tile = TileButton(new Border { CornerRadius = new CornerRadius(18), ClipToBounds = true, BorderThickness = new Thickness(2), Child = layers }, gap);
        if (height > 0) tile.Height = height;
        tile.Click += (_, _) => MainWindow.Current?.Navigate(() => new ModPage(g.Def.Id, m));
        ToolTip.SetTip(tile, $"{m.Name} · {g.Def.Name}");
        return tile;
    }

    /// <summary>Широкая плитка «Выбор ModLaunch»: арт игры, большая картинка мода справа, кнопка «Скачать».</summary>
    static Control WideMod((GameState Game, ModInfo Mod) item, double height)
    {
        var (g, m) = item;
        var installed = Actions.IsInstalled(g, m.Id);
        var busy = Actions.IsBusy(g, m.Id);
        var pick = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), CornerRadius = new CornerRadius(999), Padding = new Thickness(10, 3), HorizontalAlignment = HorizontalAlignment.Left,
            Child = Ui.Row(6, Ui.Icon(Icons.Sparkles, 13, Ui.Hex("#FFE08A")), new TextBlock { Text = I18n.T("v91.home.pick"), FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White }),
        };
        var title = new TextBlock { Text = m.Name, FontFamily = Look.Display, FontSize = height < 200 ? 22 : 26, FontWeight = FontWeight.Bold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Left };
        var by = new TextBlock { Text = string.IsNullOrEmpty(m.Author) ? g.Def.Name : $"{m.Author} · {g.Def.Name}", FontSize = 13, Foreground = Ui.Hex("#C9CFDC") };
        Button action = installed
            ? Ui.Button(I18n.T("mod.installed"), () => MainWindow.Current?.Navigate(() => new ModPage(g.Def.Id, m)), "hero-ghost", Icons.Check)
            : Ui.Button(busy ? I18n.T("aside.installing") : I18n.T("mod.install"), () => _ = Actions.Install(g, m), "primary", Icons.Download);
        action.IsEnabled = !busy;
        action.HorizontalAlignment = HorizontalAlignment.Left;
        var left = Ui.Col(10, pick, Ui.Col(2, title, by), action);
        left.VerticalAlignment = VerticalAlignment.Center;
        left.Margin = new Thickness(28, 0, 0, 0);

        var icon = new Border
        {
            Width = height * 0.62, Height = height * 0.62, CornerRadius = new CornerRadius(22), ClipToBounds = true,
            Child = Fill(m.Icon, g.Def, Images.Art.Cover),
        };
        var iconHost = new Border
        {
            CornerRadius = new CornerRadius(22), BoxShadow = BoxShadows.Parse("0 18 40 0 #A0000000"),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 34, 0),
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative), RenderTransform = TransformOperations.Parse("rotate(5deg)"),
            Child = icon,
        };
        var layers = new Panel
        {
            Children =
            {
                Ui.GameImage(g.Def, 1200, art: Images.Art.Hero),
                new Border { Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse("#F0080A0E"), 0), new GradientStop(Color.Parse("#B0080A0E"), 0.5), new GradientStop(Color.Parse("#40080A0E"), 1) } } },
                iconHost,
                left,
            },
        };
        var card = new Border
        {
            Height = height, CornerRadius = new CornerRadius(20), ClipToBounds = true, Child = layers, Cursor = new Cursor(StandardCursorType.Hand),
        };
        card.PointerPressed += (_, e) =>
        {
            if (e.Source is Visual v && Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<Button>(v, true) is null)
                MainWindow.Current?.Navigate(() => new ModPage(g.Def.Id, m));
        };
        return card;
    }

    /// <summary>Пустая плитка, пока моды грузятся (или если игр ещё нет).</summary>
    static Control Placeholder(double height, bool gap = true)
    {
        var b = new Border
        {
            Classes = { "card", "skeleton" }, CornerRadius = new CornerRadius(18), Margin = gap ? Gap : default,
            Child = new TextBlock { Text = _featuredLoading ? I18n.T("v91.home.loading") : "", FontSize = 12, Foreground = Ui.Res("Faint"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        if (height > 0) b.Height = height;
        return b;
    }

    /// <summary>Постер игры (раскладка B): обложка во всю высоту, название и «Играть».</summary>
    static Control Poster(GameState g, bool gap)
    {
        var found = g.Status == Detect.Found;
        var words = Ui.Col(2,
            new TextBlock { Text = g.Def.Name, FontSize = 17, FontWeight = FontWeight.Bold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis },
            new TextBlock { Text = GameCard.Status(g), FontSize = 12, Foreground = Ui.Hex("#C9CFDC"), TextTrimming = TextTrimming.CharacterEllipsis });
        words.VerticalAlignment = VerticalAlignment.Bottom;
        words.Margin = new Thickness(16, 0, 70, 14);
        var layers = new Panel { Children = { Ui.GameImage(g.Def, 700, art: Images.Art.Cover), new Border { Background = Shade(0.45) }, words } };
        if (found && g.LoaderInstalled)
        {
            var gs = g;
            var play = Ui.Button("", () => Actions.Play(gs), "icon primary", Icons.Play, I18n.T("games.play"));
            play.HorizontalAlignment = HorizontalAlignment.Right;
            play.VerticalAlignment = VerticalAlignment.Bottom;
            play.Margin = new Thickness(12);
            layers.Children.Add(play);
        }
        var tile = TileButton(new Border { CornerRadius = new CornerRadius(20), ClipToBounds = true, BorderThickness = new Thickness(2), Child = layers }, gap);
        tile.Height = 300;
        tile.Opacity = found ? 1 : 0.6;
        var id = g.Def.Id;
        tile.Click += (_, _) => MainWindow.Current?.Navigate(() => new GamePage(id));
        tile.ContextFlyout = GameCard.Menu(g);
        return tile;
    }

    /// <summary>Баннер «Продолжить» (раскладка B): арт, логотип и «Играть» в одну строку.</summary>
    static Control Banner(GameState g, Features.Played played)
    {
        var id = g.Def.Id;
        var logo = Images.GameAsset(g.Def, Images.Art.Logo, 512);
        Control title = logo is null
            ? new TextBlock { Text = g.Def.Name, FontFamily = Look.Display, FontSize = 26, FontWeight = FontWeight.Bold, Foreground = Brushes.White }
            : new Image { Source = logo, MaxHeight = 70, MaxWidth = 300, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
        var when = played.LastPlayed is null ? I18n.T("home.yourGames") : $"{I18n.T("v4.continue")} · {I18n.T("time.last", ("when", Ui.Ago(played.LastPlayed)))}";
        var eyebrow = new TextBlock { Text = when.ToUpper(I18n.Culture), FontSize = 11.5, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.8, Foreground = Ui.Hex("#B8E3F0") };
        var left = Ui.Col(10, eyebrow, title);
        left.VerticalAlignment = VerticalAlignment.Center;
        left.Margin = new Thickness(30, 0, 0, 0);
        Control action = Features.Launcher.IsRunning(id)
            ? PlayControls.RunningPill(id)
            : g.LoaderInstalled ? PlayControls.PlayButton(g) : Ui.Button(I18n.T("games.installLoader", ("loader", g.Def.LoaderName)), () => Actions.InstallLoader(g), "primary", Icons.Download);
        action.HorizontalAlignment = HorizontalAlignment.Right;
        action.VerticalAlignment = VerticalAlignment.Center;
        action.Margin = new Thickness(0, 0, 28, 0);
        var layers = new Panel
        {
            Children =
            {
                Ui.GameImage(g.Def, 1400, art: Images.Art.Hero),
                new Border { Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse("#E8080A0E"), 0), new GradientStop(Color.Parse("#70080A0E"), 0.55), new GradientStop(Color.Parse("#C0080A0E"), 1) } } },
                left, action,
            },
        };
        var card = new Border { Height = 176, CornerRadius = new CornerRadius(20), ClipToBounds = true, Child = layers, Cursor = new Cursor(StandardCursorType.Hand) };
        card.PointerPressed += (_, e) =>
        {
            if (e.Source is Visual v && Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<Button>(v, true) is null)
                MainWindow.Current?.Navigate(() => new GamePage(id));
        };
        card.ContextFlyout = GameCard.Menu(g);
        return card;
    }

    /// <summary>Игр ещё нет: приглашение добавить и веер популярных игр.</summary>
    static Control StartTile(List<GameState> fan)
    {
        var left = Ui.Col(14,
            new TextBlock { Text = I18n.T("v91.home.start"), FontFamily = Look.Display, FontSize = 30, FontWeight = FontWeight.Bold, Foreground = Ui.Res("Text") },
            Ui.Text(I18n.T("v91.home.start.text"), "muted", wrap: true),
            Ui.Row(10,
                Ui.Button(I18n.T("add.title"), () => MainWindow.Current?.Navigate(() => new AddGamePage()), "primary", Icons.Plus),
                Ui.Button(I18n.T("lib.title"), () => MainWindow.Current?.Navigate(() => new LibraryPage()), "", Icons.Layers)));
        left.MaxWidth = 520;
        left.VerticalAlignment = VerticalAlignment.Center;
        left.HorizontalAlignment = HorizontalAlignment.Left;
        left.Margin = new Thickness(40, 0, 0, 0);
        var layers = new Panel { Children = { left } };
        if (fan.Count > 0) layers.Children.Add(Fan(fan));
        return new Border { Classes = { "card" }, Height = 372, CornerRadius = new CornerRadius(20), ClipToBounds = true, Child = layers };
    }

    /// <summary>Веер обложек (на эскизе — SV, HK, LC): чуть повёрнуты, при наведении поднимаются.</summary>
    static Control Fan(List<GameState> games)
    {
        const double w = 132, h = 198, step = 96;
        var canvas = new Canvas
        {
            Width = w + step * (games.Count - 1) + 30, Height = h + 40,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 44, 0),
        };
        double[] angles = games.Count switch { 1 => [0], 2 => [-6, 6], _ => [-9, -1, 8] };
        double[] lift = games.Count switch { 1 => [0], 2 => [6, 0], _ => [14, 0, 10] };
        for (var i = 0; i < games.Count; i++)
        {
            var g = games[i];
            var rest = $"translateY({lift[i]}px) rotate({angles[i]}deg)";
            var cover = new Border
            {
                Width = w, Height = h, CornerRadius = new CornerRadius(14), ClipToBounds = true,
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), BorderThickness = new Thickness(1.5),
                Child = Ui.GameImage(g.Def, 300, art: Images.Art.Cover),
            };
            var host = new Border
            {
                Width = w, Height = h, CornerRadius = new CornerRadius(14), BoxShadow = BoxShadows.Parse("0 18 36 0 #B0000000"), Child = cover,
                Cursor = new Cursor(StandardCursorType.Hand), Opacity = g.Status == Detect.Found ? 1 : 0.75,
                RenderTransformOrigin = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                RenderTransform = TransformOperations.Parse(rest),
                Transitions = [new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(240), Easing = new BackEaseOut() }],
            };
            Canvas.SetLeft(host, 10 + i * step);
            Canvas.SetTop(host, 16);
            var id = g.Def.Id;
            var z = i;
            host.PointerEntered += (_, _) => { host.ZIndex = 10; host.RenderTransform = TransformOperations.Parse("translateY(-12px) rotate(0deg) scale(1.05)"); };
            host.PointerExited += (_, _) => { host.ZIndex = z; host.RenderTransform = TransformOperations.Parse(rest); };
            host.PointerPressed += (_, e) => { e.Handled = true; MainWindow.Current?.Navigate(() => new GamePage(id)); };
            ToolTip.SetTip(host, g.Def.Name);
            host.ZIndex = z;
            canvas.Children.Add(host);
        }
        return canvas;
    }

    // ---------------------------------------------------------------- кружки быстрых кнопок

    static IEnumerable<(string Icon, string Text, int Badge, Func<Page> Open)> Quick()
    {
        var updates = Features.ModUpdates.Found.Values.Sum(l => l.Count);
        var view = Social.Friends.View();
        var online = view.Friends.Count(f => f.State != "offline") + view.Incoming.Count;
        yield return (Icons.Layers, I18n.T("lib.title"), 0, () => new LibraryPage());
        yield return (Icons.Package, I18n.T("mc.title"), updates, () => new ModsCenterPage());
        yield return (Icons.Users, I18n.T("v91.home.friends"), online, () => new FriendsPage());
    }

    static Control Circle(string icon, string text, int badge, Func<Page> open, double size = 58)
    {
        var b = new Button
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Padding = new Thickness(0),
            Classes = { "card-btn" }, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1),
            Content = Ui.Icon(icon, 22, Ui.Res("Text")),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        b.Click += (_, _) => MainWindow.Current?.Navigate(open);
        ToolTip.SetTip(b, text);
        var face = new Panel { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center, Children = { b } };
        if (badge > 0)
            face.Children.Add(new Border
            {
                MinWidth = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = Ui.Res("Brand"), Padding = new Thickness(5, 0),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -3, -3, 0), IsHitTestVisible = false,
                Child = new TextBlock { Text = badge > 99 ? "99+" : badge.ToString(I18n.Culture), FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
        var label = new TextBlock { Text = text, FontSize = 11.5, Foreground = Ui.Res("Muted"), HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 92 };
        return Ui.Col(6, face, label);
    }

    /// <summary>Раскладка A: три кружка столбиком справа от плиток.</summary>
    static Control CirclesColumn()
    {
        var col = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Width = 92 };
        foreach (var (icon, text, badge, open) in Quick()) col.Children.Add(Circle(icon, text, badge, open, 54));
        return col;
    }

    /// <summary>Раскладка C: кружки одной строкой, плюс «Добавить игру».</summary>
    static Control CirclesRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 28, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 6) };
        foreach (var (icon, text, badge, open) in Quick()) row.Children.Add(Circle(icon, text, badge, open));
        row.Children.Add(Circle(Icons.Plus, I18n.T("add.title"), 0, () => new AddGamePage()));
        row.Children.Add(Circle(Icons.Settings, I18n.T("nav.settings"), 0, () => new SettingsPage()));
        return row;
    }
}
