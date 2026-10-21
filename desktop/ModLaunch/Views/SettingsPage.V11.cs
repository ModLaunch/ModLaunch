using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>
/// 11.0: настройки пересобраны. Было 13 разделов, где оформление жило в трёх местах («Конфиги»,
/// «Оформление», «Интерфейс»), а анимации — в двух; стало 9: «Основное» (язык, запуск программы,
/// сеть и офлайн, уведомления, перенос), «Оформление» (конфиги, тема, размер, движение, главная,
/// тонкая настройка свёрнута), «Игры» (папки, порядок, скрытое), «Запуск и инжектор», «Графика»,
/// «Бэкапы», «Загрузки и обновления», «Аккаунты», «О программе». Убраны устаревшие: выбор дизайна
/// 8.x, наборы оформления ML-LOOK (их заменили конфиги), «перемешать плитки», классическая главная 9.x.
/// </summary>
public sealed partial class SettingsPage
{
    /// <summary>Старые адреса разделов (ссылки из «Что нового», меню и страниц) → новые.</summary>
    static string MapTab(string tab) => tab switch
    {
        "configs" or "interface" => "look",
        "system" => "general",
        "updates" => "downloads",
        "services" when !Social.Account.Owner => "general",
        _ => tab,
    };

    /// <summary>
    /// Подпись раздела: у пересобранных в 11.0 — новая (v11.set.d.*), у остальных — прежняя.
    /// Новой нет на языке интерфейса — прежняя на нём же, а если нет и её — новая по-английски (не ключ «set.d.general»).
    /// </summary>
    static string Desc(string id, string suffix = "")
    {
        string key = $"v11.set.d.{id}{suffix}", old = $"set.d.{id}{suffix}";
        var v11 = I18n.Has(key) || !I18n.Has(old) && (I18n.Raw("en", key) ?? I18n.Raw("ru", key)) is not null;
        return I18n.T(v11 ? key : old);
    }

    /// <summary>Строка «название, пояснение — элемент справа».</summary>
    static DockPanel Line(string title, string hint, Control right)
    {
        right.VerticalAlignment = VerticalAlignment.Center;
        var row = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        row.Children.Add(right);
        row.Children.Add(Ui.Col(3, Ui.Text(title, "h3"), Ui.Text(hint, "small muted", wrap: true)));
        return row;
    }

    // ---------------------------------------------------------------- основное

    Control GeneralTab()
    {
        var col = new StackPanel { Spacing = 16 };

        // Язык: «как в системе» и все найденные (свои переводы из папки languages тоже).
        var languages = I18n.Available();
        var lang = new ComboBox { Width = 280 };
        lang.Items.Add(I18n.T("lang.auto", ("name", languages.FirstOrDefault(l => l.Code == I18n.Detect())?.Native ?? "English")));
        foreach (var l in languages) lang.Items.Add(l.Coverage < 100 ? $"{l.Native} · {l.Coverage}%" : l.Native);
        var saved = Settings.Language;
        lang.SelectedIndex = saved == "auto" ? 0 : Math.Max(0, languages.FindIndex(l => l.Code.Equals(saved, StringComparison.OrdinalIgnoreCase)) + 1);
        lang.SelectionChanged += (_, _) =>
        {
            if (lang.SelectedIndex < 0) return;
            var value = lang.SelectedIndex == 0 ? "auto" : languages[lang.SelectedIndex - 1].Code;
            if (value == Settings.Language) return;
            Settings.Language = value;
            I18n.Set(value);
        };
        col.Children.Add(Section(I18n.T("settings.language"), null, lang, Ui.Text(I18n.T("lang.hint"), "small muted", wrap: true),
            Ui.Button(I18n.T("lang.folder"), () => Actions.OpenFolder(Directory.CreateDirectory(Path.Combine(Paths.DataDir, "languages")).FullName), "ghost", Icons.Folder)));

        // Запуск программы.
        var start = new ComboBox { Width = 220 };
        var starts = new[] { "home", "lastGame", "creator", "add" };
        foreach (var st in starts) start.Items.Add(I18n.T("look.start." + st));
        start.SelectedIndex = Math.Max(0, Array.IndexOf(starts, Settings.Data.Str("startPage") ?? "home"));
        start.SelectionChanged += (_, _) => { Settings.Data["startPage"] = starts[Math.Max(0, start.SelectedIndex)]; Settings.Save(); };
        col.Children.Add(Section(I18n.T("v11.set.startup"), null,
            Toggle(I18n.T("sys.autostart"), I18n.T("sys.autostart.hint"), Autostart.Enabled, v => Autostart.Set(v)),
            Toggle(I18n.T("sys.minimized"), I18n.T("sys.minimized.hint"), Settings.Data.Bool("startMinimized"), v => Settings.Data["startMinimized"] = v),
            Toggle(I18n.T("sys.tray"), I18n.T("sys.tray.hint"), Settings.Data.Bool("closeToTray"), v => { Settings.Data["closeToTray"] = v; Settings.Save(); MainWindow.Current?.UpdateTray(); }),
            Line(I18n.T("look.start"), I18n.T("look.start.hint"), start)));

        // Сеть: офлайн сам, когда интернета нет, и вручную.
        var state = Offline.On
            ? Offline.Manual ? I18n.T("v11.set.net.manual") : I18n.T("v11.set.net.auto", ("ago", Offline.Since is { } since ? Ui.Ago(since) : "—"))
            : I18n.T("v11.set.net.online");
        col.Children.Add(Section(I18n.T("v11.set.net"), I18n.T("v11.set.net.hint"),
            Toggle(I18n.T("v11.set.net.autoOn"), I18n.T("v11.set.net.autoOn.hint"), Offline.Auto, v => Offline.SetAuto(v)),
            Toggle(I18n.T("offline.title"), I18n.T("offline.hint"), Offline.Manual, v => { Offline.Set(v); MainWindow.Current?.Toast(I18n.T(v ? "offline.on" : "offline.off")); }),
            Ui.Row(8, Ui.Icon(Offline.On ? Icons.WifiOff : Icons.Wifi, 14, Offline.On ? Ui.Res("Warn") : Ui.Res("Good")), Ui.Text(state, "small muted")),
            Ui.Button(I18n.T("v11.set.net.check"), async () => { await Offline.Probe(); Build(); }, "ghost", Icons.Refresh)));

        // Уведомления и подтверждения.
        var toast = new NumericUpDown { Minimum = 2, Maximum = 30, Value = (decimal)Math.Clamp(Settings.Data.Long("toastSeconds") is var t && t > 0 ? t : 4, 2, 30), Width = 140, FormatString = "0" };
        toast.ValueChanged += (_, _) => { Settings.Data["toastSeconds"] = (int)(toast.Value ?? 4); Settings.Save(); };
        col.Children.Add(Section(I18n.T("sys.notify"), null,
            Line(I18n.T("sys.toast"), I18n.T("sys.toast.hint"), toast),
            Toggle(I18n.T("sys.toastDone"), I18n.T("sys.toastDone.hint"), Settings.Data.Bool("toastOnDone", true), v => Settings.Data["toastOnDone"] = v),
            Toggle(I18n.T("sys.confirm"), I18n.T("sys.confirm.hint"), Settings.Data.Bool("confirmRemove", true), v => Settings.Data["confirmRemove"] = v),
            Toggle(I18n.T("v11.set.statusBar"), I18n.T("v11.set.statusBar.hint"), Settings.Data.Bool("statusBar", true), v => { Settings.Data["statusBar"] = v; Settings.Save(); AppState.Notify(); })));

        // Перенос настроек на другой компьютер.
        col.Children.Add(Section(I18n.T("sys.transfer"), I18n.T("sys.transfer.hint"),
            Ui.Row(10,
                Ui.Button(I18n.T("sys.export"), async () =>
                {
                    var file = await MainWindow.Current!.PickSaveFile(I18n.T("sys.export"), "modlaunch-settings.json");
                    if (file is null) return;
                    File.WriteAllText(file, SettingsTransfer.Export());
                    MainWindow.Current.Toast(I18n.T("sys.exported"));
                }, "", Icons.Upload),
                Ui.Button(I18n.T("sys.import"), async () =>
                {
                    var file = await MainWindow.Current!.PickFile(I18n.T("sys.import"), json: true);
                    if (file is null) return;
                    try
                    {
                        var n = SettingsTransfer.Import(File.ReadAllText(file));
                        Look.Apply();
                        MainWindow.Current.Toast(I18n.T("sys.imported", ("n", n)));
                        Build();
                    }
                    catch (Exception e) { MainWindow.Current.Toast(Jobs.Explain(e), bad: true); }
                }, "", Icons.Download),
                Ui.Button(I18n.T("keys.title"), () => MainWindow.Current?.Shortcuts(), "ghost", Icons.Keyboard))));
        return col;
    }

    // ---------------------------------------------------------------- оформление

    Control LookTab()
    {
        // Карточкам конфигов нужна вся ширина — без «кирпичиков».
        return new Border { Child = Ui.Col(16, ConfigsSection(), ThemeSection(), SizeSection(), MotionSection(), HomeSection(), FineTuneSection()) };
    }

    /// <summary>Тема (карточки-образцы) и цвет акцента.</summary>
    Control ThemeSection()
    {
        var themes = new WrapPanel();
        foreach (var theme in Look.Themes)
        {
            var (bg, surface, text) = Look.Sample(theme);
            var sample = new Border
            {
                Width = 132, Height = 76, CornerRadius = new CornerRadius(10), Background = Ui.Hex(bg), Padding = new Thickness(10), Margin = new Thickness(0, 0, 10, 10),
                BorderThickness = new Thickness(2), BorderBrush = Look.Theme == theme ? Ui.Res("Brand") : Ui.Res("Line"),
                Child = Ui.Col(6,
                    new Border { Height = 8, Width = 56, CornerRadius = new CornerRadius(4), Background = Ui.Hex(Look.Accent), HorizontalAlignment = HorizontalAlignment.Left },
                    new Border { Height = 18, CornerRadius = new CornerRadius(5), Background = Ui.Hex(surface) },
                    new TextBlock { Text = I18n.T("look.theme." + theme), Foreground = Ui.Hex(text), FontSize = 11.5, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis }),
            };
            var th = theme;
            var b = new Button { Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Content = sample };
            b.Click += (_, _) => { Look.SetTheme(th); Settings.Data.Remove("config"); Settings.Save(); Build(); };
            themes.Children.Add(b);
        }

        var accents = new WrapPanel();
        var list = Look.Accents.Take(20).ToList();
        list.Insert(0, GoldAccent);
        foreach (var hex in list.Distinct())
        {
            var h = hex;
            var on = Look.Accent.Equals(hex, StringComparison.OrdinalIgnoreCase);
            var dot = new Border
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = Ui.Hex(hex), Margin = new Thickness(0, 0, 8, 8),
                BorderBrush = on ? Ui.Res("Text") : Brushes.Transparent, BorderThickness = new Thickness(on ? 3 : 0), Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            };
            StoreKit.OnClick(dot, () => { Look.SetAccent(h); Settings.Data.Remove("config"); Settings.Save(); Build(); });
            accents.Children.Add(dot);
        }
        var accentBox = new TextBox { Text = Look.Accent, Width = 100, FontFamily = new FontFamily("Consolas, monospace"), VerticalAlignment = VerticalAlignment.Top };
        accentBox.LostFocus += (_, _) =>
        {
            var hex = (accentBox.Text ?? "").Trim();
            if (!hex.StartsWith('#')) hex = "#" + hex;
            if (hex.Length == 7 && Color.TryParse(hex, out _) && !hex.Equals(Look.Accent, StringComparison.OrdinalIgnoreCase)) { Look.SetAccent(hex.ToUpperInvariant()); Settings.Data.Remove("config"); Settings.Save(); Build(); }
        };
        accents.Children.Add(accentBox);
        return Section(I18n.T("v11.set.theme"), I18n.T("look.theme.hint"), themes, Ui.Col(8, Ui.Text(I18n.T("look.accent"), "h3"), accents));
    }

    /// <summary>Масштаб, шрифт и заголовки.</summary>
    Control SizeSection()
    {
        var scales = new WrapPanel();
        var auto = Ui.Button(I18n.T("look.scale.auto"), () => { Look.SetAutoScale(); Build(); }, Look.AutoScale ? "chip active" : "chip", Icons.Sparkles);
        auto.Margin = new Thickness(0, 0, 6, 6);
        ToolTip.SetTip(auto, I18n.T("look.scale.auto.hint"));
        scales.Children.Add(auto);
        foreach (var k in Look.Scales.Concat([1.4]))
        {
            var kk = k;
            var chip = Ui.Button($"{k * 100:0}%", () => { Look.SetScale(kk); Build(); }, "chip");
            if (!Look.AutoScale && Math.Abs(Look.Scale - k) < 0.001) chip.Classes.Add("active");
            chip.Margin = new Thickness(0, 0, 6, 6);
            scales.Children.Add(chip);
        }
        var font = new ComboBox { Width = 240, ItemsSource = Look.Fonts, SelectedItem = Look.Font };
        font.SelectionChanged += (_, _) => { if (font.SelectedItem is string f && f != Look.Font) { Look.SetFont(f); Settings.Data.Remove("config"); Settings.Save(); } };
        var headings = Segments([("ui", I18n.T("v11.set.headings.ui")), ("wide", I18n.T("v11.set.headings.wide"))], Look.WideHeadings ? "wide" : "ui",
            v => { Settings.Data["headings"] = v; Settings.Data.Remove("config"); Settings.Save(); Look.Apply(); Build(); });
        return Section(I18n.T("v11.set.size"), I18n.T("look.scale.hint"), scales,
            Ui.Row(28, Ui.Col(8, Ui.Text(I18n.T("look.font"), "h3"), font), Ui.Col(8, Ui.Text(I18n.T("v11.set.headings"), "h3"), headings)));
    }

    /// <summary>
    /// Движение одним выбором: «Спокойно» (короткие переходы), «Ярко» (анимации 9.x–10.0) или «Без анимаций».
    /// Раньше это были шесть переключателей в двух разделах.
    /// </summary>
    Control MotionSection()
    {
        // Смесь (например, своя заставка из 10.0) — ничего не выбрано, и нажатие на любой вариант его применит.
        var motion = Segments([("calm", I18n.T("v11.set.motion.calm")), ("bright", I18n.T("v11.set.motion.bright")), ("off", I18n.T("v11.set.motion.off"))], Motion() ?? "", v =>
        {
            SetMotion(v);
            MainWindow.Current?.Refresh();
            Build();
        });
        return Section(I18n.T("v10.set.motion"), I18n.T("v11.set.motion.hint"), motion,
            Toggle(I18n.T("look.smooth"), I18n.T("look.smooth.hint"), SmoothScroll.On, v => Settings.Data["smoothScroll"] = v));
    }

    static readonly string[] MotionModes = ["calm", "bright", "off"];

    /// <summary>Записать режим движения: все шесть ключей анимаций.</summary>
    static void SetMotion(string v)
    {
        var d = Settings.Data;
        var on = v != "off";
        d["animations"] = on;
        d["gameOpenStyle"] = v == "bright" ? "classic" : on ? "calm" : "off";
        d["gameOpenFx"] = on;
        d["launchFx"] = v == "bright" ? "calm" : "off";
        d["installFx"] = on;
        d["modOpenFx"] = v == "bright";
        d["splash"] = v == "bright";
        d.Remove("config");
        Settings.Save();
    }

    /// <summary>
    /// Какой режим движения сейчас — по тому, что на деле играет (заставка, открытие игры и мода, запуск, установка);
    /// null — не совпадает ни с одним.
    /// </summary>
    static string? Motion()
    {
        var d = Settings.Data;
        var anim = Look.Animations;
        var now = (anim, Open: anim ? GameOpenFx.Style : "off", Launch: anim && d.Str("launchFx") != "off", Install: anim && d.Bool("installFx", true),
            ModOpen: anim && d.Bool("modOpenFx", true), Splash: d.Bool("splash", true));
        foreach (var v in MotionModes)
        {
            var on = v != "off";
            if (now == (on, v == "bright" ? "classic" : on ? "calm" : "off", v == "bright", on, v == "bright", v == "bright")) return v;
        }
        return null;
    }

    [SelfTest]
    static string MotionModeMatchesWhatPlays()
    {
        var keys = new[] { "animations", "gameOpenStyle", "gameOpenFx", "launchFx", "installFx", "modOpenFx", "splash", "config" };
        var was = keys.ToDictionary(k => k, k => Settings.Data[k]?.DeepClone());
        try
        {
            foreach (var v in MotionModes)
            {
                SetMotion(v);
                if (Motion() != v) throw new Exception($"{v} reads back as {Motion() ?? "none"}");
            }
            // «Спокойно», но заставка своя (включена) — это уже не «Спокойно».
            SetMotion("calm");
            Settings.Data["splash"] = true;
            if (Motion() is not null) throw new Exception("calm with splash reads as " + Motion());
            return "motion mode read from all six keys";
        }
        finally
        {
            foreach (var (k, v) in was)
                if (v is null) Settings.Data.Remove(k); else Settings.Data[k] = v;
            Settings.Save();
        }
    }

    /// <summary>Главная и боковая панель: что показывать.</summary>
    Control HomeSection()
    {
        void Chrome() => MainWindow.Current?.Refresh();
        var hero = Segments([("stage", I18n.T("v10.hero.stage")), ("compact", I18n.T("v10.hero.compact")), ("off", I18n.T("v10.hero.off"))], Settings.Data.Str("homeHero") ?? "stage",
            v => { Settings.Data["homeHero"] = v; Settings.Data.Remove("config"); Settings.Save(); Build(); });
        var widgets = Ui.Button(I18n.T("v10.set.widgets"), () => { HomePage.EditWidgets(); MainWindow.Current?.Navigate(() => new HomePage()); }, "", Icons.Grid);
        return Section(I18n.T("v11.set.home"), I18n.T("look.home.hint"),
            Ui.Col(8, Ui.Text(I18n.T("v10.set.hero"), "h3"), hero), widgets,
            Toggle(I18n.T("v4.continue"), I18n.T("v4.continue.text"), Settings.Data.Bool("homeContinue", true), v => Settings.Data["homeContinue"] = v),
            Toggle(I18n.T("home.favorites"), I18n.T("home.favorites.text"), Settings.Data.Bool("homeFavorites", true), v => Settings.Data["homeFavorites"] = v),
            Toggle(I18n.T("look.home.popular"), I18n.T("look.home.popular.hint"), Settings.Data.Bool("homePopular", true), v => Settings.Data["homePopular"] = v),
            Toggle(I18n.T("look.rail"), I18n.T("look.rail.hint"), !Settings.Data.Bool("railHidden"), v => { Settings.Data["railHidden"] = !v; Chrome(); }),
            Toggle(I18n.T("look.railFriends"), I18n.T("look.railFriends.hint"), Settings.Data.Bool("railFriends", true), v => { Settings.Data["railFriends"] = v; Chrome(); MainWindow.Current?.RefreshFriendsDock(); }),
            Toggle(I18n.T("v93.set.railSections"), I18n.T("v93.set.railSections.hint"), Settings.Data.Bool("railGamesMods"), v => { Settings.Data["railGamesMods"] = v; Chrome(); }),
            Toggle(I18n.T("look.friendsDock"), I18n.T("look.friendsDock.hint"), Settings.Data.Bool("friendsDock", true), v => { Settings.Data["friendsDock"] = v; MainWindow.Current?.RefreshFriendsDock(); }),
            Toggle(I18n.T("look.compact"), I18n.T("look.compact.hint"), Settings.Data.Bool("compactLists"), v => Settings.Data["compactLists"] = v));
    }

    static bool _fineOpen;

    /// <summary>Тонкая настройка (свёрнута): каждый цвет темы, углы, фон, свечение и эффекты экрана.</summary>
    Control FineTuneSection()
    {
        var colors = new WrapPanel();
        var now = Look.Current;
        foreach (var key in Look.ColorKeys)
        {
            var k = key;
            var swatch = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(6), Background = Ui.Hex(now[key]), BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1) };
            var box = new TextBox { Text = now[key], Width = 92, FontFamily = new FontFamily("Consolas, monospace"), FontSize = 12.5 };
            void Commit()
            {
                var hex = (box.Text ?? "").Trim();
                if (!hex.StartsWith('#')) hex = "#" + hex;
                if (hex.Length != 7 || !Color.TryParse(hex, out _)) { box.Text = Look.Current[k]; return; }
                if (hex.Equals(Look.Current[k], StringComparison.OrdinalIgnoreCase)) return;
                Look.SetColor(k, hex.ToUpperInvariant());
                Settings.Data.Remove("config");
                Build();
            }
            box.LostFocus += (_, _) => Commit();
            box.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Commit(); };
            var cell = Ui.Row(8, swatch, Ui.Col(2, new TextBlock { Text = I18n.T("v10.color." + key.ToLowerInvariant()), FontSize = 12, Foreground = Ui.Res("Muted") }, box));
            cell.Margin = new Thickness(0, 0, 18, 12);
            cell.Width = 196;
            colors.Children.Add(cell);
        }
        var radius = Segments(Look.Radii.Select(x => (x, I18n.T("v10.radius." + x))).ToArray(), Look.Radius, v => { Look.SetRadius(v); Settings.Data.Remove("config"); Settings.Save(); Build(); });
        var backdrops = new WrapPanel();
        foreach (var bd in Look.Backdrops)
        {
            var v = bd;
            var chip = Ui.Button(I18n.T("look.bg." + bd), () => { Look.SetBackdrop(v); Build(); }, Look.Backdrop == bd ? "chip active" : "chip");
            chip.Margin = new Thickness(0, 0, 6, 6);
            backdrops.Children.Add(chip);
        }
        var glow = new Slider { Minimum = 0, Maximum = 1, Value = Look.Glow, Width = 240, TickFrequency = 0.1, IsSnapToTickEnabled = true };
        glow.ValueChanged += (_, e) => { Look.SetGlow(e.NewValue); Settings.Data.Remove("config"); };
        var body = Ui.Col(14,
            Ui.Col(8, Ui.Text(I18n.T("v10.set.colors"), "h3"), colors),
            Ui.Row(28, Ui.Col(8, Ui.Text(I18n.T("look.radius"), "h3"), radius), Ui.Col(8, Ui.Text(I18n.T("look.glow"), "h3"), glow)),
            Ui.Col(8, Ui.Text(I18n.T("look.bg"), "h3"), backdrops),
            Toggle(I18n.T("v10.fx.scan"), I18n.T("v10.fx.scan.hint"), Look.Scanlines, v => { Look.SetEffect("fxScanlines", v); Settings.Data.Remove("config"); }),
            Toggle(I18n.T("v10.fx.vignette"), I18n.T("v10.fx.vignette.hint"), Look.Vignette, v => { Look.SetEffect("fxVignette", v); Settings.Data.Remove("config"); }),
            // Только оформление: раскладка главной, картинка на ней и анимации остаются свои.
            Ui.Button(I18n.T("look.reset"), () => { Configs.Apply(Look.Pro("widget", "home_hero", "anim_open", "anim_launch")); Build(); }, "ghost", Icons.Undo));
        var expander = new Expander
        {
            Header = Ui.Col(2, Ui.Text(I18n.T("v11.set.fine"), "h3"), Ui.Text(I18n.T("v11.set.fine.hint"), "small muted", wrap: true)),
            Content = body, IsExpanded = _fineOpen, HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        expander.Expanded += (_, _) => _fineOpen = true;
        expander.Collapsed += (_, _) => _fineOpen = false;
        return Ui.Card(expander, 16);
    }

    // ---------------------------------------------------------------- игры: порядок и скрытое

    Control GamesOrderSection()
    {
        void Chrome() => MainWindow.Current?.Refresh();
        var list = new StackPanel { Spacing = 6 };
        var order = MainWindow.OrderedGames().ToList();
        var hidden = Settings.Data.Arr("hiddenGames").Select(x => x?.ToString()).ToHashSet();
        for (var i = 0; i < order.Count; i++)
        {
            var g = order[i];
            var id = g.Def.Id;
            var index = i;
            var sw = new ToggleSwitch { IsChecked = !hidden.Contains(id), OnContent = "", OffContent = "", MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
            sw.IsCheckedChanged += (_, _) =>
            {
                var set = Settings.Data.Arr("hiddenGames").Select(x => x?.ToString()).OfType<string>().ToHashSet();
                if (sw.IsChecked == true) set.Remove(id); else set.Add(id);
                Settings.Data["hiddenGames"] = new System.Text.Json.Nodes.JsonArray(set.Select(x => (System.Text.Json.Nodes.JsonNode)x).ToArray());
                Settings.Save();
                Chrome();
            };
            var move = Ui.Row(2,
                Ui.Button("", () => { MainWindow.MoveGame(id, -1); Chrome(); Build(); }, "icon ghost", Icons.ArrowUp, I18n.T("look.up")),
                Ui.Button("", () => { MainWindow.MoveGame(id, 1); Chrome(); Build(); }, "icon ghost", Icons.Download, I18n.T("look.down")));
            move.Children[0].IsEnabled = index > 0;
            move.Children[1].IsEnabled = index < order.Count - 1;
            var right = Ui.Row(8, move, sw);
            var row = new DockPanel();
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right);
            var name = Ui.Row(10, Ui.Thumb(g.Def.ArtUrl, g.Def.Name, 28, 7), Ui.Text(g.Def.Name + (g.Status == Detect.Found ? "" : "  · " + I18n.T("games.notDetected")), g.Status == Detect.Found ? "" : "muted"));
            name.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(name);
            list.Children.Add(row);
        }
        return Section(I18n.T("look.games"), I18n.T("look.games.hint"), list,
            Toggle(I18n.T("v94.set.officialArt"), I18n.T("v94.set.officialArt.hint"), Settings.Data.Bool("officialArt", true), v => { Settings.Data["officialArt"] = v; if (v) OfficialArt.Refresh(); }),
            Toggle(I18n.T("feed.setting"), I18n.T("feed.setting.hint"), Settings.Data.Bool("catalogFeeds", true), v => Settings.Data["catalogFeeds"] = v),
            Ui.Button(I18n.T("add.title"), () => MainWindow.Current?.Navigate(() => new AddGamePage()), "", Icons.Plus));
    }

    /// <summary>Скрытые моды и авторы (как блок-лист на Nexus).</summary>
    Control HiddenSection()
    {
        var hiddenList = new StackPanel { Spacing = 6 };
        var blocked = Blocklist.All();
        if (blocked.Count == 0) hiddenList.Children.Add(Ui.Text(I18n.T("nx.hidden.none"), "muted"));
        foreach (var (key, title, author) in blocked)
        {
            var k = key;
            var a = author;
            var back = Ui.Button(I18n.T("nx.unhide"), () => { Blocklist.Unhide(k, a); Build(); }, "ghost", Icons.Eye);
            var row = new DockPanel();
            DockPanel.SetDock(back, Dock.Right);
            row.Children.Add(back);
            var label = Ui.Row(8, Ui.Icon(author ? Icons.User : Icons.Package, 15, Ui.Res("Muted")), Ui.Text(title));
            label.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(label);
            hiddenList.Children.Add(row);
        }
        return Section(I18n.T("nx.hidden.title"), I18n.T("nx.hidden.hint"), hiddenList);
    }

    // ---------------------------------------------------------------- инжектор

    /// <summary>Режим инжектора и уборка после игры (бывшая «Чистая загрузка» 10.0) — одним разделом.</summary>
    Control InjectorSection()
    {
        var w = MainWindow.Current!;
        var games = AppState.Games.Where(g => g.Status == Detect.Found && !g.Def.IsMinecraft && g.Path is not null).ToList();
        var count = Inject.All().Count(i => i.Enabled);
        var tools = Ui.Row(8,
            Ui.Button(I18n.T("v11.set.inj.open", ("n", count)), () => w.Navigate(() => new CreatorPage("injects")), "", Icons.Zap),
            Ui.Button(I18n.T("v10.clean.now"), () =>
            {
                int files = 0, q = 0;
                long bytes = 0;
                foreach (var g in games)
                {
                    if (Launcher.IsRunning(g.Def.Id)) continue;
                    try { var r = CleanBoot.Run(g); files += r.Files; q += r.Quarantined; bytes += r.Bytes; } catch { }
                }
                w.Toast(files + q == 0 ? I18n.T("v10.clean.nothing") : I18n.T("v10.clean.done", ("n", files + q), ("size", GamePage.Size(bytes))));
                Build();
            }, "ghost", Icons.Sparkles));
        var held = games.Sum(g => CleanBoot.Quarantined(g.Def.Id).Count);
        if (held > 0)
            tools.Children.Add(Ui.Button(I18n.T("v10.clean.restore", ("n", held)), () =>
            {
                var n = games.Sum(CleanBoot.Restore);
                w.Toast(I18n.T("v10.clean.restored", ("n", n)));
                AppState.Notify();
                Build();
            }, "ghost", Icons.Undo));
        return Section(I18n.T("v11.inj.mode"), I18n.T("v11.set.inj.hint"),
            Toggle(I18n.T("v11.set.inj.on"), I18n.T("v11.inj.mode.hint"), Inject.On, v => { Inject.SetOn(v); Build(); }),
            Toggle(I18n.T("v10.clean.on"), I18n.T("v11.set.clean.hint"), CleanBoot.On, v => Settings.Data["cleanBoot"] = v),
            Toggle(I18n.T("v10.clean.unknown"), I18n.T("v10.clean.unknown.hint"), CleanBoot.Quarantine, v => Settings.Data["cleanBootUnknown"] = v),
            tools);
    }

    // ---------------------------------------------------------------- обновления (в «Загрузках»)

    Control UpdatesSection()
    {
        var latest = Setup.Updater.Latest;
        var status = !Setup.Updater.Configured ? I18n.T("upd.app.off")
            : latest is null ? I18n.T("upd.never")
            : Setup.Updater.Available ? I18n.T("upd.app.available", ("version", latest.Version))
            : I18n.T("upd.app.latest", ("version", Http.Version));
        var list = new StackPanel { Spacing = 8 };
        var total = 0;
        foreach (var g in AppState.Games)
        {
            if (!ModUpdates.Found.TryGetValue(g.Def.Id, out var ups) || ups.Count == 0) continue;
            total += ups.Count;
            var id = g.Def.Id;
            var open = Ui.Button(I18n.T("upd.found." + I18n.Plural(ups.Count, "one", "few", "many"), ("n", ups.Count)), () => MainWindow.Current?.Navigate(() => new GamePage(id, "installed")), "", Icons.ArrowUp);
            list.Children.Add(Line(g.Def.Name, string.Join(", ", ups.Select(u => $"{u.Name} {u.Current} → {u.Latest}").Take(4)), open));
        }
        if (total == 0) list.Children.Add(Ui.Text(ModUpdates.Found.IsEmpty ? I18n.T("upd.never") : I18n.T("upd.none"), "small muted"));
        return Section(I18n.T("settings.tab.updates"), null,
            Ui.Row(10, Ui.Icon(Setup.Updater.Available ? Icons.Sparkles : Icons.Check, 15, Setup.Updater.Available ? Ui.Res("Brand2") : Ui.Res("Good")), Ui.Text(status, "h3")),
            Ui.Row(10,
                Ui.Button(I18n.T("upd.check"), async () =>
                {
                    try { await Setup.Updater.Check(); } catch (Exception e) { MainWindow.Current?.Toast(Jobs.Explain(e), bad: true); }
                    Build();
                }, "", Icons.Refresh),
                Setup.Updater.Available ? Ui.Button(I18n.T("upd.app.open"), () => MainWindow.Current?.ShowUpdate(), "primary", Icons.Sparkles) : new Panel()),
            Toggle(I18n.T("upd.app.auto"), I18n.T("upd.app.auto.hint"), Setup.Updater.AutoDownload, v => Settings.Data["autoDownloadUpdates"] = v),
            Toggle(I18n.T("upd.onStart"), I18n.T("upd.onStart.hint"), ModUpdates.OnStart, v => Settings.Data["checkModUpdates"] = v),
            Toggle(I18n.T("upd.auto"), I18n.T("upd.auto.hint"), ModUpdates.Auto, v => Settings.Data["autoUpdateMods"] = v),
            Toggle(I18n.T("upd.beforePlay"), I18n.T("upd.beforePlay.hint"), ModUpdates.BeforePlay, v => Settings.Data["updateBeforePlay"] = v),
            Ui.Button(I18n.T("upd.checkAll"), CheckAll, "", Icons.Refresh),
            list);
    }
}
