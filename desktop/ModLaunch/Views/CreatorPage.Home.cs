using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Games;

namespace ModLaunch.Views;

public sealed partial class CreatorPage
{
    // ---------------------------------------------------------------- главная

    /// <summary>
    /// Обзор Creator Hub (12.1 — пульт мастерской): слева «Верстак» с недавними проектами, справа «Путь автора»;
    /// ниже — ящики с инструментами, популярное в Hub, новое в маркете и шаблоны.
    /// Цифры автора и «Новый проект» — в шапке Hub (HubNav.Frame).
    /// </summary>
    Control Home()
    {
        if (_hub is null && !_hubLoading && !Program.Screenshot) _ = LoadHub();
        if (_market is null && !_marketLoading && !Program.Screenshot) _ = LoadMarket();
        var col = new StackPanel { Spacing = 26 };
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,330"), ColumnSpacing = 16 };
        var bench = Bench();
        var path = AuthorPath();
        top.Children.Add(bench);
        Grid.SetColumn(path, 1);
        top.Children.Add(path);
        var tools = Tools();
        col.Children.Add(top);
        col.Children.Add(tools);
        if (!Shown) { Animate.Rise(top, 0); Animate.Rise(tools, 1); }

        Control popular;
        if (_hubLoading && _hub is null)
        {
            var skeleton = new UniformGrid { Columns = 3 };
            for (var i = 0; i < 3; i++) skeleton.Children.Add(Skeleton());
            popular = skeleton;
        }
        else if (_hubError is not null) popular = Ui.Text(I18n.T("hub.error"), "muted");
        else if ((_hub ?? []).Count == 0) popular = Ui.Text(I18n.T("cr.home.hubEmpty"), "muted");
        else popular = Grid3(Hub.Sort(_hub!, "trending").Take(6));
        col.Children.Add(Section(I18n.T("cr.home.popular"), () => Go("hub"), popular, eyebrow: "Hub"));

        var newest = Market.Sort(_market ?? [], "new").Take(3).ToList();
        if (newest.Count > 0)
        {
            var shop = new UniformGrid { Columns = 3 };
            foreach (var l in newest) shop.Children.Add(MarketViews.Card(l));
            col.Children.Add(Section(I18n.T("mk.home.fresh"), () => Go("market"), shop, eyebrow: I18n.T("v95.hub.group.shop")));
        }

        var templates = new UniformGrid { Columns = 3 };
        foreach (var t in Templates.All.Take(3)) templates.Children.Add(TemplateCard(t));
        col.Children.Add(Section(I18n.T("cr.home.templates"), () => Go("examples"), templates, eyebrow: I18n.T("v91.cr.tab.templates")));
        return col;
    }

    /// <summary>«Верстак»: недавние проекты строками — точка статуса, имя и игра, версия, когда менялся.</summary>
    Control Bench()
    {
        var recent = Projects.List().OrderByDescending(p => p.Updated).Take(5).ToList();
        var publish = Ui.Button(I18n.T("v91.cr.publish"), PublishArchive, "ghost", Icons.Upload);
        publish.Padding = new Thickness(10, 4);
        var all = Ui.Button(I18n.T("home.all"), () => Go("mine"), "ghost");
        all.Padding = new Thickness(10, 4);
        var body = Ui.Col(2);
        if (recent.Count == 0)
        {
            body.Children.Add(Ui.Text(I18n.T("v91.cr.myMods.empty"), "muted", wrap: true));
            var start = Ui.Row(8, Ui.Button(I18n.T("v95.hub.newProject"), CreateNew, "primary", Icons.Plus), Ui.Button(I18n.T("cr.tab.examples"), () => Go("examples"), "", Icons.Wand));
            start.Margin = new Thickness(0, 12, 0, 0);
            body.Children.Add(start);
        }
        foreach (var p in recent)
        {
            var project = p;
            var game = GameCatalog.ById(p.Game);
            var status = p.Meta().Status(p.Updated);
            var name = new TextBlock { Text = p.Name, FontWeight = FontWeight.SemiBold, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis };
            var words = Ui.Col(1, name, new TextBlock { Text = game?.Name ?? p.Game, FontSize = 12, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis });
            words.VerticalAlignment = VerticalAlignment.Center;
            var dot = Ui.Dot(StatusBrush(status), 8);
            ToolTip.SetTip(dot, I18n.T($"v12.pj.st.{status}"));
            var version = HubLook.MonoText(p.Version == "" ? "—" : "v" + p.Version);
            var when = HubLook.MonoText(Ui.Ago(p.Updated), 11);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,90,110,Auto"), ColumnSpacing = 12 };
            row.Children.Add(dot);
            Grid.SetColumn(words, 1);
            row.Children.Add(words);
            Grid.SetColumn(version, 2);
            row.Children.Add(version);
            Grid.SetColumn(when, 3);
            row.Children.Add(when);
            var go = Ui.Icon(Icons.ChevronRight, 14, Ui.Res("Faint"));
            go.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(go, 4);
            row.Children.Add(go);
            var b = new Button { Classes = { "hub-row" }, Content = row };
            b.Click += (_, _) => OpenProject(project);
            Ctx.Attach(b, () => ProjectMenu(project));
            body.Children.Add(b);
        }
        return HubLook.Frame(Ui.Col(12, HubLook.Head(I18n.T("v12d.home.bench"), I18n.T("v12d.home.recent"), publish, all), body), new Thickness(18, 16));
    }

    /// <summary>Большая кнопка «+ Создать» / «Опубликовать»: значок в кружке, название и пояснение.</summary>
    static Control BigButton(string icon, string title, string text, Action click, bool primary)
    {
        var circle = new Border
        {
            Width = 52, Height = 52, CornerRadius = new CornerRadius(26),
            Background = primary ? new SolidColorBrush(Color.FromArgb(48, 255, 255, 255)) : Ui.Res("Surface3"),
            Child = Ui.Icon(icon, 24, primary ? Brushes.White : Ui.Res("Text")),
        };
        var name = new TextBlock { Text = title, FontFamily = Look.Display, FontSize = 20, FontWeight = FontWeight.Bold };
        var sub = Ui.Text(text, "small", wrap: true);
        sub.Opacity = 0.8;
        var words = Ui.Col(3, name, sub);
        words.VerticalAlignment = VerticalAlignment.Center;
        var b = new Button
        {
            Padding = new Thickness(22, 20), CornerRadius = new CornerRadius(20), Margin = new Thickness(0, 0, 14, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = Ui.Row(16, circle, words),
        };
        b.Classes.Add(primary ? "primary" : "card-btn");
        if (!primary) { b.BorderBrush = Ui.Res("Line"); b.BorderThickness = new Thickness(1); }
        b.Click += (_, _) => click();
        return b;
    }

    const double TallW = 212, TallH = 300;

    /// <summary>Высокая карточка: картинка во всю высоту, ярлык сверху, название снизу.</summary>
    static Control Tall(Control art, string label, string title, string sub, string stats, Action open)
    {
        var chip = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(205, 12, 13, 18)), CornerRadius = new CornerRadius(999), Padding = new Thickness(10, 4),
            Margin = new Thickness(12), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Child = Ui.Row(6, Ui.Icon(Icons.Flame, 12, Ui.Hex("#FFB86B")), new TextBlock { Text = label, FontSize = 11.5, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White }),
        };
        var words = Ui.Col(3,
            new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeight.Bold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis },
            new TextBlock { Text = sub, FontSize = 12, Foreground = Ui.Hex("#C9CFDC"), TextTrimming = TextTrimming.CharacterEllipsis },
            new TextBlock { Text = stats, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Ui.Hex("#E9E3FF") });
        words.VerticalAlignment = VerticalAlignment.Bottom;
        words.Margin = new Thickness(16, 0, 16, 16);
        var shade = new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#20080A0E"), 0), new GradientStop(Color.Parse("#10080A0E"), 0.35), new GradientStop(Color.Parse("#F0080A0E"), 1) },
            },
        };
        var b = new Button
        {
            Classes = { "card-btn", "cover-btn" }, Width = TallW, Height = TallH, Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch, Background = Brushes.Transparent,
            Transitions = [new Avalonia.Animation.TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(280), Easing = new Avalonia.Animation.Easings.BackEaseOut() }],
            Content = new Border { CornerRadius = new CornerRadius(20), ClipToBounds = true, BorderThickness = new Thickness(2), Child = new Panel { Children = { art, shade, chip, words } } },
        };
        b.Click += (_, _) => open();
        return b;
    }

    static Control Gradient(string accent, string icon) => new Border
    {
        Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse(accent), 0), new GradientStop(Color.Parse("#141620"), 1) },
        },
        Child = new Border { Margin = new Thickness(0, 0, 0, 70), Child = Ui.Icon(icon, 54, new SolidColorBrush(Color.FromArgb(200, 255, 255, 255))) },
    };

    static Control TallHub(HubMod m)
    {
        var game = GameCatalog.ById(m.Game);
        Control art = m.Images.Count > 0
            ? new Panel { Children = { game is null ? Gradient("#7C5CFF", Icons.Code) : Ui.GameImage(game, 500, art: Images.Art.Cover), Pic(m.Images[0]) } }
            : game is not null ? Ui.GameImage(game, 500, art: Images.Art.Cover) : Gradient("#7C5CFF", m.IsPackage ? Icons.Package : Icons.Code);
        return Tall(art, I18n.T("v91.cr.hit"), m.Name, $"{m.Author} · {game?.ShortName ?? m.Game}", $"↓ {I18n.Compact(m.Downloads)}   ♥ {I18n.Compact(m.Likes)}", () => OpenMod(m));
    }

    static Control TallListing(Listing l)
    {
        var game = GameCatalog.ById(l.Game);
        Control art = l.Images.Count > 0
            ? new Panel { Children = { Gradient("#EC4899", MarketViews.KindIcon(l.Kind)), Pic(l.Images[0]) } }
            : game is not null ? Ui.GameImage(game, 500, art: Images.Art.Cover) : Gradient("#EC4899", MarketViews.KindIcon(l.Kind));
        return Tall(art, I18n.T("v91.cr.fresh"), l.Title, $"{l.Author} · {I18n.T("mk.kind." + l.Kind)}", MarketViews.PriceText(l),
            () => MarketViews.Open(l, () => _market = null));
    }

    static Control TallPromo(string icon, string title, string text, string accent, Action open) =>
        Tall(Gradient(accent, icon), "Creator Hub", title, text, "", open);

    static Control Pic(string url)
    {
        var image = new Image { Classes = { "zoom" }, Stretch = Stretch.UniformToFill };
        _ = Images.FromUrl(url, 500).ContinueWith(t =>
        {
            if (t.Result is Avalonia.Media.Imaging.Bitmap bmp) Avalonia.Threading.Dispatcher.UIThread.Post(() => image.Source = bmp);
        });
        return image;
    }

    /// <summary>Заголовок раздела в духе мастерской: «// ПОДПИСЬ» и название, справа — «Все» (если есть куда вести).</summary>
    static Control Section(string title, Action? all, Control body, Control? extra = null, string? eyebrow = null)
    {
        var actions = new List<Control>();
        if (extra is not null) actions.Add(extra);
        if (all is not null)
        {
            var more = Ui.Button(I18n.T("home.all"), all, "ghost");
            more.Padding = new Thickness(8, 4);
            actions.Add(more);
        }
        return Ui.Col(14, HubLook.Head(eyebrow ?? "Creator Hub", title, actions.ToArray()), body);
    }

    /// <summary>Большая карточка-действие; акцентная — только одна на экране.</summary>
    static Control BigAction(string icon, string title, string text, Action click, bool primary)
    {
        var circle = new Border
        {
            Width = 48, Height = 48, CornerRadius = new CornerRadius(24),
            Background = primary ? new SolidColorBrush(Color.FromArgb(48, 255, 255, 255)) : Ui.Res("Surface3"),
            Child = Ui.Icon(icon, 22, primary ? Brushes.White : Ui.Res("Text")),
        };
        var sub = Ui.Text(text, "small", wrap: true);
        sub.Opacity = 0.8;
        var words = Ui.Col(3, Ui.Text(title, "h3"), sub);
        words.VerticalAlignment = VerticalAlignment.Center;
        var b = new Button
        {
            Padding = new Thickness(22), CornerRadius = new CornerRadius(18), Margin = new Thickness(0, 0, 14, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = Ui.Row(16, circle, words),
        };
        b.Classes.Add(primary ? "primary" : "card-btn");
        b.Click += (_, _) => click();
        return b;
    }
}
