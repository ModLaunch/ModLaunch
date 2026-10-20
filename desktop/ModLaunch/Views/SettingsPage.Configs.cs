using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>
/// 10.0: «Конфиги и стиль» — как конфиги в CS2. Сверху готовые и свои конфиги карточками с мини-экраном
/// (как будет выглядеть программа); «Загрузить», «Сохранить текущий», «Папка конфигов». Ниже — полная
/// кастомизация: каждый цвет темы, акцент, шрифт, углы, эффекты экрана, сцена главной, виджеты и анимации.
/// </summary>
public sealed partial class SettingsPage
{
    Control ConfigsTab()
    {
        var col = Ui.Col(16, ConfigsSection(), CustomSection(), MotionSection());
        // Без «кирпичиков»: карточкам конфигов нужна вся ширина.
        return new Border { Child = col };
    }

    Control ConfigsSection()
    {
        var w = MainWindow.Current!;
        var wrap = new WrapPanel();
        foreach (var c in Configs.All()) wrap.Children.Add(ConfigCard(c));
        var load = Ui.Button(I18n.T("v10.set.load"), async () =>
        {
            var file = await w.PickAny(I18n.T("v10.set.load"), ["*" + Configs.Ext], I18n.T("v10.set.filetype"));
            if (file is null) return;
            var c = Configs.Import(file);
            w.Toast(c is null ? I18n.T("v10.cfg.bad") : I18n.T("v10.cfg.applied", ("name", c.Name)), bad: c is null);
            Build();
        }, "primary", Icons.Upload);
        var save = Ui.Button(I18n.T("v10.set.save"), SaveDialog, "", Icons.Save);
        var export = Ui.Button(I18n.T("v10.set.export"), async () =>
        {
            var file = await w.PickSaveAny(I18n.T("v10.set.export"), "ModLaunch" + Configs.Ext, Configs.Ext.TrimStart('.'), I18n.T("v10.set.filetype"));
            if (file is null) return;
            try { File.WriteAllText(file, Configs.Capture(Path.GetFileNameWithoutExtension(file), Social.Account.SignedIn ? Social.Account.Get().Name : null)); w.Toast(I18n.T("v10.set.exported")); }
            catch (Exception e) { w.Toast(Jobs.Explain(e), bad: true); }
        }, "", Icons.Download);
        var folder = Ui.Button(I18n.T("v10.set.folder"), () => Actions.OpenFolder(Configs.Dir), "ghost", Icons.Folder);
        var copy = Ui.Button(I18n.T("v10.set.copy"), () => { _ = TopLevel.GetTopLevel(w)?.Clipboard?.SetTextAsync(Configs.Capture("ModLaunch")); w.Toast(I18n.T("v10.set.copied")); }, "ghost", Icons.Copy);
        var tools = new WrapPanel { Children = { load, save, export, folder, copy } };
        foreach (var b in tools.Children.OfType<Control>()) b.Margin = new Thickness(0, 0, 8, 8);
        return Section(I18n.T("v10.set.configs"), I18n.T("v10.set.configs.hint"), tools, wrap);
    }

    void SaveDialog()
    {
        var w = MainWindow.Current!;
        var name = new TextBox { Watermark = I18n.T("v10.set.save.name"), Width = 360, Text = Configs.All().FirstOrDefault(c => c.Id == Configs.CurrentId) is { BuiltIn: false } own ? own.Name : "" };
        void Go()
        {
            var text = name.Text?.Trim() ?? "";
            if (text == "") return;
            var path = Configs.SaveCurrent(text);
            w.CloseDialog();
            w.Toast(I18n.T("v10.set.saved", ("file", Path.GetFileName(path))));
            Build();
        }
        name.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Go(); };
        w.Dialog(I18n.T("v10.set.save"), Ui.Col(10, Ui.Text(I18n.T("v10.set.save.text"), "muted", wrap: true), name),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog), Ui.Button(I18n.T("v10.set.save.go"), Go, "primary", Icons.Save));
        Avalonia.Threading.Dispatcher.UIThread.Post(() => name.Focus());
    }

    /// <summary>Золотой акцент (в 10.0.0 был наградой из кейса) — теперь у всех.</summary>
    const string GoldAccent = "#E8B84A";

    /// <summary>Карточка конфига: мини-экран (боковая панель, сцена, виджеты), название, «Применить».</summary>
    Control ConfigCard(UiConfig c)
    {
        var (p, accent, scan, radius) = Configs.Preview(c);
        var on = Configs.CurrentId == c.Id;
        IBrush B(string key) => Ui.Hex(p.TryGetValue(key, out var v) ? v : "#000000");
        var r = radius == "sharp" ? 1 : radius == "round" ? 7 : 4;
        var screen = new Canvas { Width = 228, Height = 132, Background = B("Bg"), ClipToBounds = true };
        void Box(double x, double y, double wd, double ht, IBrush fill, double rad = -1)
        {
            var b = new Border { Width = wd, Height = ht, Background = fill, CornerRadius = new CornerRadius(rad < 0 ? r : rad) };
            Canvas.SetLeft(b, x);
            Canvas.SetTop(b, y);
            screen.Children.Add(b);
        }
        Box(0, 0, 26, 132, B("Rail"), 0);
        for (var i = 0; i < 4; i++) Box(8, 12 + i * 16, 10, 10, i == 0 ? Ui.Hex(accent) : B("Surface3"), 3);
        Box(26, 0, 202, 132, B("Layer"), 0);
        var hero = c.Get("home_hero") ?? "stage";
        var y = 8.0;
        if (hero != "off")
        {
            var hh = hero == "compact" ? 30 : 52;
            var art = new Border { Width = 188, Height = hh, CornerRadius = new CornerRadius(r), ClipToBounds = true, Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse(p["Surface2"]), 0), new GradientStop(Look.Mix(Color.Parse(p["Surface2"]), Color.Parse(accent), 0.35), 1) },
            } };
            Canvas.SetLeft(art, 33);
            Canvas.SetTop(art, y);
            screen.Children.Add(art);
            Box(39, y + hh - 14, 26, 8, Ui.Hex(accent), 3);
            y += hh + 6;
        }
        // Виджеты конфига (или стандартные) крошечными плитками.
        var widgets = c.Widgets();
        if (widgets.Count == 0) widgets = [.. Widgets.Default];
        var at = Widgets.Pack(widgets.Select(x => x.Size).ToList(), 4);
        const double cell = 43.5, ch = 22, gap = 4;
        for (var i = 0; i < at.Count; i++)
        {
            var (cw, chh) = Widgets.Cells(widgets[i].Size);
            var top = y + at[i].Y * (ch + gap);
            if (top > 128) break;
            Box(33 + at[i].X * (cell + gap), top, cw * cell + (cw - 1) * gap, chh * ch + (chh - 1) * gap, B("Surface"));
        }
        if (scan) screen.Children.Add(new Border { Width = 228, Height = 132, Child = Gx.Scanlines(), Opacity = 0.9 });

        var shot = new Border { CornerRadius = new CornerRadius(12), ClipToBounds = true, Child = screen, BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1) };
        var title = Ui.Row(8, new TextBlock { Text = c.Name, FontWeight = FontWeight.SemiBold, FontSize = 14.5, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 160 });
        if (on) title.Children.Add(Gx.Tag(I18n.T("v10.set.on"), Ui.Res("BrandSoft"), Ui.Res("Brand2"), 11));
        var about = new TextBlock
        {
            Text = c.About != "" ? c.About : c.Author != "" ? I18n.T("v10.set.by", ("name", c.Author)) : I18n.T("v10.set.own"),
            FontSize = 12, Foreground = Ui.Res("Muted"), TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, Height = 32,
        };
        var actions = Ui.Row(6);
        actions.Children.Add(Ui.Button(on ? I18n.T("v10.set.reapply") : I18n.T("v10.set.apply"), () => { Configs.Apply(c); MainWindow.Current?.Toast(I18n.T("v10.cfg.applied", ("name", c.Name))); Build(); }, on ? "" : "primary", on ? Icons.Refresh : Icons.Check));
        if (c.File is { } file)
        {
            actions.Children.Add(Ui.Button("", () => Ui.OpenUrl(file), "icon ghost", Icons.Edit, I18n.T("v10.set.edit")));
            actions.Children.Add(Ui.Button("", () => { Configs.Delete(c); Build(); }, "icon ghost", Icons.Trash, I18n.T("v10.set.delete")));
        }
        var card = new Border
        {
            Classes = { "card" }, Width = 254, Padding = new Thickness(12), Margin = new Thickness(0, 0, 12, 12),
            BorderBrush = on ? Ui.Res("Brand") : Ui.Res("Line"), BorderThickness = new Thickness(on ? 2 : 1),
            Child = Ui.Col(10, shot, Ui.Col(3, title, about), actions),
        };
        return card;
    }

    /// <summary>Полная кастомизация: каждый цвет, акцент, шрифт, углы, эффекты и главная.</summary>
    Control CustomSection()
    {
        var colors = new WrapPanel();
        var now = Look.Current;
        foreach (var key in Look.ColorKeys)
        {
            var k = key;
            var swatch = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(8), Background = Ui.Hex(now[key]), BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1) };
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

        var accents = new WrapPanel();
        var list = Look.Accents.Take(20).ToList();
        list.Insert(0, GoldAccent);
        foreach (var hex in list.Distinct())
        {
            var h = hex;
            var on = Look.Accent.Equals(hex, StringComparison.OrdinalIgnoreCase);
            var dot = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = Ui.Hex(hex), Margin = new Thickness(0, 0, 8, 8),
                BorderBrush = on ? Ui.Res("Text") : Brushes.Transparent, BorderThickness = new Thickness(on ? 3 : 0), Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            };
            if (h == GoldAccent) ToolTip.SetTip(dot, I18n.T("v10.accent.gold"));
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

        var font = new ComboBox { Width = 240, ItemsSource = Look.Fonts, SelectedItem = Look.Font };
        font.SelectionChanged += (_, _) => { if (font.SelectedItem is string f && f != Look.Font) { Look.SetFont(f); Settings.Data.Remove("config"); Settings.Save(); } };
        var radius = Segments(Look.Radii.Select(x => (x, I18n.T("v10.radius." + x))).ToArray(), Look.Radius, v => { Look.SetRadius(v); Settings.Data.Remove("config"); Settings.Save(); Build(); });
        var hero = Segments([("stage", I18n.T("v10.hero.stage")), ("compact", I18n.T("v10.hero.compact")), ("off", I18n.T("v10.hero.off"))], Settings.Data.Str("homeHero") ?? "stage",
            v => { Settings.Data["homeHero"] = v; Settings.Data.Remove("config"); Settings.Save(); Build(); });
        var widgets = Ui.Button(I18n.T("v10.set.widgets"), () => { HomePage.EditWidgets(); MainWindow.Current?.Navigate(() => new HomePage()); }, "", Icons.Grid);
        var reset = Ui.Button(I18n.T("look.reset"), () => { Look.Reset(); Widgets.Reset(); Build(); }, "ghost", Icons.Undo);

        Control Labeled(string title, Control c) => Ui.Col(8, Ui.Text(title, "h3"), c);
        return Section(I18n.T("v10.set.custom"), I18n.T("v10.set.custom.hint"),
            Labeled(I18n.T("v10.set.colors"), colors),
            Labeled(I18n.T("look.accent"), accents),
            Ui.Row(28, Labeled(I18n.T("look.font"), font), Labeled(I18n.T("look.radius"), radius)),
            Toggle(I18n.T("v10.fx.scan"), I18n.T("v10.fx.scan.hint"), Look.Scanlines, v => { Look.SetEffect("fxScanlines", v); Settings.Data.Remove("config"); }),
            Toggle(I18n.T("v10.fx.vignette"), I18n.T("v10.fx.vignette.hint"), Look.Vignette, v => { Look.SetEffect("fxVignette", v); Settings.Data.Remove("config"); }),
            Labeled(I18n.T("v10.set.hero"), hero),
            Ui.Row(8, widgets, reset));
    }

    /// <summary>Анимации открытия и запуска игр.</summary>
    Control MotionSection()
    {
        var open = Segments([("calm", I18n.T("v10.motion.calm")), ("classic", I18n.T("v10.motion.classic")), ("off", I18n.T("v10.motion.off"))], GameOpenFx.Style,
            v => { Settings.Data["gameOpenStyle"] = v; Settings.Data["gameOpenFx"] = v != "off"; Settings.Save(); Build(); });
        var launch = Segments([("calm", I18n.T("v10.motion.calm")), ("off", I18n.T("v10.motion.off"))], Settings.Data.Str("launchFx") ?? "calm",
            v => { Settings.Data["launchFx"] = v; Settings.Save(); Build(); });
        return Section(I18n.T("v10.set.motion"), I18n.T("v10.set.motion.hint"),
            Ui.Col(8, Ui.Text(I18n.T("v10.motion.open"), "h3"), Ui.Text(I18n.T("v10.motion.open.hint"), "small muted", wrap: true), open),
            Ui.Col(8, Ui.Text(I18n.T("v10.motion.launch"), "h3"), Ui.Text(I18n.T("v10.motion.launch.hint"), "small muted", wrap: true), launch));
    }

    /// <summary>Переключатель-сегменты: выбранный — акцентом.</summary>
    static Control Segments((string Id, string Text)[] items, string current, Action<string> pick)
    {
        var row = new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Left };
        var inner = Ui.Row(4);
        foreach (var (id, text) in items)
        {
            var v = id;
            var b = Ui.Button(text, () => { if (v != current) pick(v); }, v == current ? "primary" : "ghost");
            b.Padding = new Thickness(14, 7);
            inner.Children.Add(b);
        }
        row.Child = inner;
        return row;
    }
}
