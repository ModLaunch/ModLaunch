using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>
/// 20.0: главная по эскизам. Сверху — баннер последней игры: арт, логотип, «Играть», «Моды», «Автопилот»,
/// а справа — подсказка: модов нет — собрать ИИ-сборку, моды есть — сводка сборки и обновления.
/// Ниже — одна строка «Ваш прогресс» (уровень, серия, часы, награда) вместо плиток-виджетов,
/// «Рекомендуем» — четыре мода с обложками, и «Подборки ModLaunch» плитками разного размера:
/// мод недели, ИИ-сборки, Creator Hub и реклама (обычной плиткой с пометкой, а не колонкой справа).
/// Виджеты остаются по кнопке «Виджеты» — для тех, кто их собирал.
/// </summary>
public sealed partial class HomePage
{
    static bool _weeklyLoading;

    /// <summary>Свои виджеты на главной — включаются кнопкой «Виджеты» (по умолчанию в 20.0 их нет).</summary>
    static bool ShowWidgets => _editWidgets || Settings.Data.Bool("homeWidgets20");

    void BuildTwenty()
    {
        _stageHost = null;
        if (!_widgetsHooked)
        {
            _widgetsHooked = true;
            Widgets.Changed += () => { if (MainWindow.Current?.CurrentPage is HomePage hp) hp.Build(); };
        }
        var mine = MyGames();
        if (_featured is null && !_featuredLoading && mine.Count > 0) { _featuredLoading = true; Dispatcher.UIThread.Post(() => _ = LoadFeatured(mine)); }
        if (AiBuildsPage.WeeklyPick is null && !_weeklyLoading && mine.Count > 0)
        {
            _weeklyLoading = true;
            _ = AiBuildsPage.EnsureWeekly(() => Dispatcher.UIThread.Post(RebuildCurrent));
        }
        CheckHealthLater(mine);
        if (!Shown) Dispatcher.UIThread.Post(ProfilePage.AnnounceNew, DispatcherPriority.Background);
        Widgets.EnsureRoad();

        var col = new StackPanel { Spacing = 32, Margin = new Thickness(32, 24, 32, 48), MaxWidth = 1680, HorizontalAlignment = HorizontalAlignment.Stretch };
        var n = 0;
        if (Starter(mine) is { } starter) col.Children.Add(Intro(starter, n++));
        var hero = HeroGame(mine);
        if (hero is not null && Settings.Data.Str("homeHero") != "off") col.Children.Add(Intro(Hero20(hero), n++));
        col.Children.Add(Intro(Progress20(), n++));
        if (Recommend20(mine, hero) is { } rec) col.Children.Add(Intro(rec, n++));
        col.Children.Add(Intro(Picks20(mine, hero), n++));
        if (ShowWidgets) col.Children.Add(Intro(WidgetsSection(mine), n++));
        col.Background = Brushes.Transparent;
        col.ContextFlyout = AreaMenu();
        Content = new ScrollViewer { Content = col, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    /// <summary>Игра для баннера: новая (один раз за сеанс) или та, во что играли последней.</summary>
    static GameState? HeroGame(List<GameState> mine)
    {
        if (mine.Count == 0) return null;
        if (mine.FirstOrDefault(x => NewGames.IsNew(x) && !Presented.Contains(x.Def.Id)) is { } newest)
        {
            Presented.Add(newest.Def.Id);
            _stage = newest.Def.Id;
        }
        return mine.FirstOrDefault(x => x.Def.Id == _stage) ?? mine[0];
    }

    // ---------------------------------------------------------------- баннер

    Control Hero20(GameState g)
    {
        var id = g.Def.Id;
        _stage = id;
        var played = PlayTime.Get(id);
        var running = Launcher.IsRunning(id);
        var art = new Border { Child = Ui.GameImage(g.Def, 1600, art: Images.Art.Hero), ClipToBounds = true };
        var logo = Images.GameAsset(g.Def, Images.Art.Logo, 900);
        Control title = logo is null
            ? new TextBlock { Text = g.Def.Name, FontFamily = Look.Display, FontSize = 46, FontWeight = FontWeight.Bold, LetterSpacing = -1.2, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, MaxWidth = 600, LineHeight = 50 }
            : new Image { Source = logo, MaxHeight = 104, MaxWidth = 440, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
        if (logo is not null) ToolTip.SetTip(title, g.Def.Name);

        var when = running ? I18n.T("time.running")
            : played.LastPlayed is not null ? I18n.T("v20.home.continue", ("when", Ui.Ago(played.LastPlayed)))
            : NewGames.IsNew(g) ? I18n.T("v20.game.new") : I18n.T("v93.home.notPlayed");
        var chip = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)), CornerRadius = new CornerRadius(999), Padding = new Thickness(12, 5),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = when, FontSize = 12.5, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White },
        };
        var ready = g.LoaderInstalled || g.Def.Loader is Games.LoaderKind.None or Games.LoaderKind.Minecraft;
        var state = !ready ? I18n.T("v20.game.needs", ("loader", g.Def.LoaderName))
            : g.ModCount > 0 ? StoreKit.Mods(g.ModCount) + (played.TotalMs >= 3_600_000 ? " · " + I18n.T("v93.home.hours", ("n", played.TotalMs / 3_600_000)) : "")
            : I18n.T("v20.home.noMods");
        var stateRow = Ui.Row(8, Ui.Dot(!ready ? Ui.Res("Warn") : g.ModCount > 0 ? Ui.Res("Good") : Ui.Hex("#B3AEC2")), new TextBlock { Text = state, FontSize = 15, Foreground = Ui.Hex("#DCD8E6"), VerticalAlignment = VerticalAlignment.Center });

        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        void Add(Control c) { c.Margin = new Thickness(0, 0, 10, 10); buttons.Children.Add(c); }
        if (running)
        {
            Add(PlayControls.RunningPill(id));
            Add(Ui.Button(I18n.T("v4.stop"), () => Launcher.Stop(id), "hero-ghost", Icons.Stop));
        }
        else if (ready) Add(PlayControls.PlayButton(g));
        else
        {
            var install = Ui.Button(I18n.T("games.installLoader", ("loader", g.Def.LoaderName)), () => Actions.InstallLoader(g), "primary", Icons.Download);
            install.Padding = new Thickness(24, 13);
            Add(install);
        }
        var mods = Ui.Button(I18n.T("tab.mods"), () => MainWindow.Current?.Navigate(() => new GamePage(id, "installed")), "hero-ghost", Icons.Package);
        mods.Padding = new Thickness(18, 13);
        Add(mods);
        if (!g.Def.IsMinecraft && g.Registry is not null)
        {
            var score = Autopilot.Plan(g, quick: true).Score;
            var pilot = Ui.Button(I18n.T("v10.ap.short"), () => MainWindow.Current?.Navigate(() => new AutopilotPage(id)), "hero-ghost", Icons.Wand,
                I18n.T("v20.home.pilot", ("score", score)) + " · " + I18n.T("v10.ap.grade." + Autopilot.Grade(score)));
            pilot.Padding = new Thickness(18, 13);
            Add(pilot);
        }

        var left = Ui.Col(12, chip, title, stateRow, buttons);
        left.VerticalAlignment = VerticalAlignment.Bottom;
        var side = HeroSide(g);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 24, Margin = new Thickness(36, 28, 28, 26) };
        grid.Children.Add(left);
        if (side is not null)
        {
            side.VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetColumn(side, 1);
            grid.Children.Add(side);
        }
        var stage = new Panel
        {
            MinHeight = 380, ClipToBounds = true,
            Children =
            {
                art,
                new Border { Background = Gx.ShadeLeft(0.8, 235) },
                new Border { Background = Gx.ShadeUp(0.45, 210) },
                grid,
            },
        };
        var frame = new Border { CornerRadius = new CornerRadius(Look.Radius == "sharp" ? 6 : 20), ClipToBounds = true, Child = stage, ContextFlyout = GameCard.Menu(g) };
        // Узкое окно: подсказка справа уходит, чтобы не наезжать на кнопки.
        if (side is not null) frame.SizeChanged += (_, e) => side.IsVisible = e.NewSize.Width >= 900;
        return frame;
    }

    /// <summary>Справа на баннере: «модов пока нет — соберём» или «ваша сборка» с обновлениями.</summary>
    static Control? HeroSide(GameState g)
    {
        var id = g.Def.Id;
        var soft = Ui.Hex("#CFCADB");
        Control Eyebrow(string icon, string text) => Ui.Row(8, Ui.Icon(icon, 15, Ui.Res("Brand2")),
            new TextBlock { Text = text.ToUpper(I18n.Culture), FontSize = 12, FontWeight = FontWeight.Bold, LetterSpacing = 1, Foreground = Ui.Res("Brand2"), VerticalAlignment = VerticalAlignment.Center });
        Border Panel(Control child) => new()
        {
            Width = 300, Padding = new Thickness(20), CornerRadius = new CornerRadius(16), Child = child,
            Background = new SolidColorBrush(Color.FromArgb(205, 13, 12, 17)), BorderBrush = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)), BorderThickness = new Thickness(1),
        };
        if (g.ModCount == 0 && AiBuildsPage.Supports(g))
        {
            var chips = new WrapPanel();
            foreach (var sid in new[] { "beauty", "hardcore", "content" })
            {
                var style = sid;
                var c = new Button
                {
                    Classes = { "chip20" }, Padding = new Thickness(10, 4), CornerRadius = new CornerRadius(999), Margin = new Thickness(0, 0, 6, 6),
                    Content = new TextBlock { Text = I18n.T("v98.ai.style." + sid), FontSize = 12.5 },
                };
                c.Click += (_, _) => MainWindow.Current?.Navigate(() => AiBuildsPage.Start(id, style));
                chips.Children.Add(c);
            }
            var go = Ui.Button(I18n.T("v98.go"), () => MainWindow.Current?.Navigate(() => AiBuildsPage.Start(id)), "hero-light", Icons.Forward);
            go.HorizontalAlignment = HorizontalAlignment.Stretch;
            go.HorizontalContentAlignment = HorizontalAlignment.Center;
            return Panel(Ui.Col(10,
                Eyebrow(Icons.Sparkles, I18n.T("v20.home.ai.eyebrow")),
                new TextBlock { Text = I18n.T("v20.home.ai.title"), FontSize = 17, FontWeight = FontWeight.Bold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = I18n.T("v20.home.ai.text", ("game", g.Def.Name)), FontSize = 13.5, LineHeight = 19.5, Foreground = soft, TextWrapping = TextWrapping.Wrap },
                chips, go));
        }
        if (g.ModCount > 0)
        {
            var updates = ModUpdates.Found.TryGetValue(id, out var u) ? u : [];
            var col = Ui.Col(10,
                Eyebrow(Icons.Package, I18n.T("v20.home.build.eyebrow")),
                new TextBlock { Text = StoreKit.Mods(g.ModCount), FontSize = 17, FontWeight = FontWeight.Bold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap });
            if (updates.Count > 0)
            {
                col.Children.Add(Ui.Row(8, Ui.Dot(Ui.Res("Warn")), new TextBlock { Text = I18n.T("v20.home.build.updates", ("n", updates.Count)), FontSize = 13.5, Foreground = soft, VerticalAlignment = VerticalAlignment.Center }));
                var all = updates.ToList();
                var update = Ui.Button(I18n.T("v20.home.build.updateAll"), () => { _ = Actions.UpdateMods(g, all); RebuildCurrent(); }, "hero-light", Icons.ArrowUp);
                update.HorizontalAlignment = HorizontalAlignment.Stretch;
                update.HorizontalContentAlignment = HorizontalAlignment.Center;
                col.Children.Add(update);
            }
            else
            {
                col.Children.Add(Ui.Row(8, Ui.Dot(Ui.Res("Good")), new TextBlock { Text = I18n.T("v20.home.build.fresh"), FontSize = 13.5, Foreground = soft, VerticalAlignment = VerticalAlignment.Center }));
                var open = Ui.Button(I18n.T("v20.home.build.open"), () => MainWindow.Current?.Navigate(() => new GamePage(id, "installed")), "hero-ghost", Icons.Package);
                open.HorizontalAlignment = HorizontalAlignment.Stretch;
                open.HorizontalContentAlignment = HorizontalAlignment.Center;
                col.Children.Add(open);
            }
            if (g.Def.HasCatalog)
            {
                var catalog = Kit20.Link(I18n.T("v20.nav.catalog"), () => MainWindow.Current?.Navigate(() => new GamePage(id, "catalog")), 13.5);
                catalog.HorizontalAlignment = HorizontalAlignment.Center;
                col.Children.Add(catalog);
            }
            return Panel(col);
        }
        return null;
    }

    // ---------------------------------------------------------------- ваш прогресс

    Control Progress20()
    {
        var (level, _, next) = RoadKit.Now();
        var fresh = LevelRoad.Fresh(level).Count;
        var ring = Kit20.Ring((300.0 - next) / 300.0, level.ToString());
        var levelWords = Ui.Col(2,
            new TextBlock { Text = I18n.T("v20.acc.level", ("n", level)), FontSize = 15, FontWeight = FontWeight.Bold },
            new TextBlock { Text = I18n.T("v20.home.next." + I18n.Plural(next, "one", "few", "many"), ("n", next), ("lv", level + 1)), FontSize = 13, Foreground = Ui.Res("Faint") });
        levelWords.VerticalAlignment = VerticalAlignment.Center;
        var levelBlock = Ui.Row(14, ring, levelWords);
        StoreKit.OnClick(levelBlock, () => MainWindow.Current?.Navigate(() => new ProfilePage("rewards")));

        Control Fact(string icon, IBrush tint, string label, string value)
        {
            var row = Ui.Row(10, Ui.Icon(icon, 18, tint),
                new TextBlock { Text = label, FontSize = 14, Foreground = Ui.Res("Muted"), VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = value, FontSize = 14, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center });
            row.VerticalAlignment = VerticalAlignment.Center;
            return row;
        }
        var streak = PlayStreak.Current();
        var total = AppState.Games.Sum(g => PlayTime.Get(g.Def.Id).TotalMs);
        var facts = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        void AddFact(Control c) { c.Margin = new Thickness(0, 6, 32, 6); facts.Children.Add(c); }
        AddFact(levelBlock);
        AddFact(new Border { Width = 1, Height = 32, Background = Ui.Res("Line"), VerticalAlignment = VerticalAlignment.Center });
        AddFact(Fact(Icons.Flame, Ui.Res("Warn"), I18n.T("v20.home.streak"), I18n.T("v20.days." + I18n.Plural(streak, "one", "few", "many"), ("n", streak))));
        AddFact(Fact(Icons.Clock, Ui.Res("Muted"), I18n.T("v20.home.hours"), total > 0 ? PlayTime.Format(total) : I18n.T("time.h", ("h", 0))));

        var right = Ui.Row(8);
        right.VerticalAlignment = VerticalAlignment.Center;
        if (fresh > 0)
        {
            var claim = new Button
            {
                Classes = { "soft" }, Height = 40, Padding = new Thickness(16, 0), CornerRadius = new CornerRadius(10), VerticalContentAlignment = VerticalAlignment.Center,
                Content = Ui.Row(8, Ui.Icon(Icons.Trophy, 16), new TextBlock { Text = I18n.T("v20.home.claim", ("n", fresh)), FontWeight = FontWeight.Bold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center }),
            };
            claim.Click += (_, _) => RoadKit.Claim();
            right.Children.Add(claim);
        }
        else right.Children.Add(Ui.Button(I18n.T("v20.home.road"), () => MainWindow.Current?.Navigate(() => new ProfilePage("rewards")), "ghost", Icons.Trophy));
        var widgets = Ui.Button(I18n.T("v20.home.widgets"), () =>
        {
            _editWidgets = !ShowWidgets;
            if (_editWidgets) Settings.Data["homeWidgets20"] = true;
            Settings.Save();
            Build();
        }, "ghost", Icons.Grid, I18n.T("v20.home.widgets.tip"));
        right.Children.Add(widgets);
        var dock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(right);
        dock.Children.Add(facts);
        return Kit20.Card(dock, new Thickness(18, 8, 14, 8));
    }

    // ---------------------------------------------------------------- рекомендуем

    Control? Recommend20(List<GameState> mine, GameState? hero)
    {
        if (mine.Count == 0) return null;
        var featured = _featured ?? [];
        var focus = hero is not null && featured.Any(x => x.Item1.Def.Id == hero.Def.Id) ? hero
            : featured.Select(x => x.Item1).FirstOrDefault();
        var cards = new List<Control>();
        bool mixed = false;
        if (focus is not null)
        {
            var own = featured.Where(x => x.Item1.Def.Id == focus.Def.Id && !Actions.IsInstalled(x.Item1, x.Item2.Id)).Take(4).ToList();
            var rest = featured.Where(x => x.Item1.Def.Id != focus.Def.Id && !Actions.IsInstalled(x.Item1, x.Item2.Id)).Take(4 - own.Count).ToList();
            mixed = rest.Count > 0;
            foreach (var (g, m) in own.Concat(rest)) cards.Add(Kit20.ModTile(g, m, showGame: mixed));
        }
        if (cards.Count == 0)
        {
            // Подборки ещё грузятся — серые заготовки карточек, чтобы страница не прыгала.
            if (_featured is not null) return null;
            for (var i = 0; i < 4; i++) cards.Add(new Border { Classes = { "skeleton" }, Height = 300, CornerRadius = new CornerRadius(16) });
        }
        var title = focus is null ? I18n.T("v20.home.rec.any") : mixed ? I18n.T("v20.home.rec.any") : I18n.T("v20.home.rec", ("game", focus.Def.Name));
        var head = Kit20.SectionHead(title, I18n.T("v20.home.rec.sub"),
            focus is not null && focus.Def.HasCatalog ? I18n.T("v20.home.rec.all") : null,
            focus is null ? null : () => MainWindow.Current?.Navigate(() => new GamePage(focus.Def.Id, "catalog")));
        return Ui.Col(16, head, Kit20.Grid(cards, 240, 4));
    }

    // ---------------------------------------------------------------- подборки

    Control Picks20(List<GameState> mine, GameState? hero)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), RowDefinitions = new RowDefinitions("200,200"),
            ColumnSpacing = 20, RowSpacing = 20,
        };
        void Put(Control c, int col, int row, int colSpan = 1, int rowSpan = 1)
        {
            Grid.SetColumn(c, col); Grid.SetRow(c, row); Grid.SetColumnSpan(c, colSpan); Grid.SetRowSpan(c, rowSpan);
            grid.Children.Add(c);
        }
        Put(WeeklyTile(hero), 0, 0, 2, 2);
        Put(AiTile(), 2, 0, 2);
        Put(SmallTile(Icons.Code, I18n.T("v20.home.creator.title"), I18n.T("v20.home.creator.text"), I18n.T("v20.home.creator.go"), () => MainWindow.Current?.Navigate(() => new CreatorPage())), 2, 1);
        Put(Ads.Enabled ? AdSlot.Tile() : SmallTile(Icons.Users, I18n.T("v20.home.friends.title"), I18n.T("v20.home.friends.text"), I18n.T("v20.home.friends.go"), () => MainWindow.Current?.Navigate(() => new CommunityPage())), 3, 1);
        return Ui.Col(16, Kit20.SectionHead(I18n.T("v20.home.picks")), grid);
    }

    /// <summary>Большая плитка «Мод недели» (как в «ИИ-сборках»); пока не загрузился — каталог игры с баннера.</summary>
    static Control WeeklyTile(GameState? hero)
    {
        var white = Brushes.White;
        var soft = Ui.Hex("#DCD8E6");
        Control Frame(Control art, Control words, Action open)
        {
            var panel = new Panel
            {
                ClipToBounds = true,
                Children = { art, new Border { Background = Gx.ShadeUp(0.05, 238) }, new Border { Padding = new Thickness(28), Child = words } },
            };
            var b = new Border { Classes = { "tile20" }, CornerRadius = new CornerRadius(20), ClipToBounds = true, Child = panel };
            StoreKit.OnClick(b, open);
            return b;
        }
        if (AiBuildsPage.WeeklyPick is { } wk)
        {
            var (g, m, days) = wk;
            var id = g.Def.Id;
            var badge = new Border
            {
                Background = Ui.Res("Brand"), CornerRadius = new CornerRadius(999), Padding = new Thickness(12, 5), HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock { Text = I18n.T("v20.home.weekly", ("n", days)), FontSize = 12.5, FontWeight = FontWeight.Bold, Foreground = Ui.Res("OnBrand") },
            };
            var desc = new TextBlock { Text = m.Description, FontSize = 14, LineHeight = 20, Foreground = soft, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 520 };
            var install = Kit20.InstallButton(g, m);
            var more = Ui.Button(I18n.T("v20.home.more"), () => MainWindow.Current?.Navigate(() => new ModPage(id, m)), "hero-ghost");
            more.Height = 36;
            more.Padding = new Thickness(16, 0);
            more.VerticalContentAlignment = VerticalAlignment.Center;
            var words = Ui.Col(10, badge,
                new TextBlock { Text = m.Name, FontFamily = Look.Display, FontSize = 26, FontWeight = FontWeight.Bold, LetterSpacing = -0.5, Foreground = white, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = g.Def.Name, FontSize = 14, Foreground = soft },
                desc, Ui.Row(10, install, more));
            words.VerticalAlignment = VerticalAlignment.Bottom;
            return Frame(ModCover.Create(g, m, 1200, big: true, iconH: HorizontalAlignment.Right, iconV: VerticalAlignment.Top, iconShare: 0.42), words, () => MainWindow.Current?.Navigate(() => new ModPage(id, m)));
        }
        if (hero is not null && hero.Def.HasCatalog)
        {
            var id = hero.Def.Id;
            var words = Ui.Col(10,
                new TextBlock { Text = I18n.T("v20.nav.catalog"), FontFamily = Look.Display, FontSize = 26, FontWeight = FontWeight.Bold, Foreground = white },
                new TextBlock { Text = I18n.T("v20.home.catalog.text", ("game", hero.Def.Name)), FontSize = 14, Foreground = soft, TextWrapping = TextWrapping.Wrap });
            words.VerticalAlignment = VerticalAlignment.Bottom;
            return Frame(Ui.GameImage(hero.Def, 1200, art: Images.Art.Hero), words, () => MainWindow.Current?.Navigate(() => new GamePage(id, "catalog")));
        }
        return SmallTile(Icons.Compass, I18n.T("v20.nav.library"), I18n.T("v20.home.catalog.none"), I18n.T("v20.home.more"), () => MainWindow.Current?.Navigate(() => new LibraryPage()));
    }

    /// <summary>Широкая плитка «ИИ-сборки»: значок, заголовок, текст и стили-«пилюли».</summary>
    static Control AiTile()
    {
        var chips = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var sid in new[] { "vanilla", "beauty", "hardcore", "coop" })
        {
            if (AiBuilds.Styles.All(s => s.Id != sid)) continue;
            var style = sid;
            var c = new Button
            {
                Classes = { "chip20", "on-card" }, Padding = new Thickness(10, 4), CornerRadius = new CornerRadius(999), Margin = new Thickness(0, 0, 6, 6),
                Content = new TextBlock { Text = I18n.T("v98.ai.style." + sid), FontSize = 12.5 },
            };
            c.Click += (_, _) => MainWindow.Current?.Navigate(() => AiBuildsPage.Start(null, style));
            chips.Children.Add(c);
        }
        var icon = new Border
        {
            Width = 48, Height = 48, CornerRadius = new CornerRadius(14), Background = Ui.Res("BrandSoft"), VerticalAlignment = VerticalAlignment.Top,
            Child = Ui.Icon(Icons.Sparkles, 24, Ui.Res("Brand2")),
        };
        var words = Ui.Col(8,
            new TextBlock { Text = I18n.T("v20.home.ai2.title"), FontSize = 18, FontWeight = FontWeight.Bold },
            new TextBlock { Text = I18n.T("v20.home.ai2.text"), FontSize = 14, LineHeight = 20, Foreground = Ui.Res("Muted"), TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis },
            chips);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 18 };
        grid.Children.Add(icon);
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);
        var card = Kit20.TileCard(grid, new Thickness(24), 20);
        StoreKit.OnClick(card, () => MainWindow.Current?.Navigate(() => new AiBuildsPage()));
        return card;
    }

    /// <summary>Малая плитка: значок, заголовок, строка текста и ссылка внизу.</summary>
    static Control SmallTile(string icon, string title, string text, string link, Action open)
    {
        var glyph = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(12), Background = Ui.Res("Surface2"), HorizontalAlignment = HorizontalAlignment.Left, Child = Ui.Icon(icon, 20, Ui.Res("Text")) };
        var go = new TextBlock { Text = link + " →", FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Ui.Res("Brand2"), VerticalAlignment = VerticalAlignment.Bottom };
        var dock = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(go, Dock.Bottom);
        dock.Children.Add(go);
        var top = Ui.Col(8, glyph,
            new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis },
            new TextBlock { Text = text, FontSize = 13.5, LineHeight = 19, Foreground = Ui.Res("Muted"), TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis });
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        var card = Kit20.TileCard(dock, new Thickness(20), 20);
        StoreKit.OnClick(card, open);
        return card;
    }

    /// <summary>Свои виджеты — по кнопке «Виджеты»: заголовок, «Готово»/«Скрыть» и сама сетка виджетов.</summary>
    Control WidgetsSection(List<GameState> mine)
    {
        var done = Ui.Button(_editWidgets ? I18n.T("v10.w.done") : I18n.T("v10.w.edit"), () => { _editWidgets = !_editWidgets; Build(); }, _editWidgets ? "primary" : "ghost", _editWidgets ? Icons.Check : Icons.Edit);
        var hide = Ui.Button(I18n.T("v20.home.widgets.hide"), () => { _editWidgets = false; Settings.Data["homeWidgets20"] = false; Settings.Save(); Build(); }, "ghost", Icons.EyeOff);
        var right = Ui.Row(6, done, hide);
        right.VerticalAlignment = VerticalAlignment.Bottom;
        var head = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        head.Children.Add(right);
        head.Children.Add(Kit20.SectionHead(I18n.T("v20.home.widgets.title")));
        return Ui.Col(16, head, WidgetsArea(mine));
    }
}
