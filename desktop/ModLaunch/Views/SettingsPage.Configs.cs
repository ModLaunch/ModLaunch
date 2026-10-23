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
    Control ConfigsSection()
    {
        var w = MainWindow.Current!;
        var wrap = new WrapPanel();
        // 17.0: первые карточки сразу, остальные — следующими порциями (страница открывается быстрее).
        Ui.Fill(wrap, Configs.All().Select(c => (Func<Control>)(() => ConfigCard(c))).ToList(), first: 4, step: 4);
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
        // 17.0: личные сборки создателей — открываются на пути уровней, закрытые видны с замком и уровнем.
        var (level, _, _) = RoadKit.Now();
        var builds = new WrapPanel();
        Ui.Fill(builds, CreatorBuilds.All.Select(b => (Func<Control>)(() => ConfigCard(CreatorBuilds.Config(b), CreatorBuilds.Open(b, level) ? null : b.Level))).ToList(), first: 0, step: 4);
        var open = CreatorBuilds.All.Count(b => CreatorBuilds.Open(b, level));
        var head = Ui.Row(10, Ui.Text(I18n.T("v99.kind.config.many"), "h3"), Gx.Tag($"{open} / {CreatorBuilds.All.Length}", Ui.Res("Surface3")));
        var creators = Ui.Col(8, head, Ui.Text(I18n.T("v17.cb.hint"), "muted small", wrap: true), builds);
        return Section(I18n.T("v10.set.configs"), I18n.T("v10.set.configs.hint"), tools, wrap, creators);
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

    /// <summary>
    /// Мини-экран конфига 228×132: боковая панель, сцена главной и виджеты в его цветах.
    /// 17.0: общий для карточек конфигов, наград «Сборки создателей» и пути уровней.
    /// </summary>
    internal static Border MiniScreen(UiConfig c)
    {
        var (p, accent, scan, radius) = Configs.Preview(c);
        IBrush B(string key) => Ui.Hex(p.TryGetValue(key, out var v) ? v : "#000000");
        var r = radius == "sharp" ? 1 : radius == "round" ? 7 : 4;
        // 17.0: мини-экран рисуется одним элементом, а не двумя десятками рамок — в «Внешнем виде» таких карточек 20+.
        var screen = new Sketch { Width = 228, Height = 132, Back = B("Bg"), ClipToBounds = true };
        void Box(double x, double y, double wd, double ht, IBrush fill, double rad = -1) => screen.Boxes.Add((new Rect(x, y, wd, ht), fill, rad < 0 ? r : rad));
        Box(0, 0, 26, 132, B("Rail"), 0);
        for (var i = 0; i < 4; i++) Box(8, 12 + i * 16, 10, 10, i == 0 ? Ui.Hex(accent) : B("Surface3"), 3);
        Box(26, 0, 202, 132, B("Layer"), 0);
        var hero = c.Get("home_hero") ?? "stage";
        var y = 8.0;
        if (hero != "off")
        {
            var hh = hero == "compact" ? 30 : 52;
            Box(33, y, 188, hh, new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse(p["Surface2"]), 0), new GradientStop(Look.Mix(Color.Parse(p["Surface2"]), Color.Parse(accent), 0.35), 1) },
            });
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
        Control child = screen;
        if (scan) child = new Panel { Children = { screen, new Border { Width = 228, Height = 132, Child = Gx.Scanlines(), Opacity = 0.9 } } };
        return new Border { CornerRadius = new CornerRadius(12), ClipToBounds = true, Child = child, BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1) };
    }

    /// <summary>Прямоугольники мини-экрана, нарисованные за один проход.</summary>
    sealed class Sketch : Control
    {
        public readonly List<(Rect R, IBrush Fill, double Radius)> Boxes = [];
        public IBrush? Back;

        public override void Render(DrawingContext dc)
        {
            if (Back is not null) dc.FillRectangle(Back, new Rect(Bounds.Size));
            foreach (var (rect, fill, radius) in Boxes) dc.DrawRectangle(fill, null, new RoundedRect(rect, radius));
        }
    }

    /// <summary>Карточка конфига: мини-экран (боковая панель, сцена, виджеты), название, «Применить».
    /// 17.0: lockedAt — сборка создателя ещё не открыта (уровень, на котором откроется).</summary>
    Control ConfigCard(UiConfig c, int? lockedAt = null)
    {
        var on = Configs.CurrentId == c.Id;
        var shot = MiniScreen(c);
        if (lockedAt is not null) shot.Opacity = 0.45;
        var title = Ui.Row(8, new TextBlock { Text = c.Name, FontWeight = FontWeight.SemiBold, FontSize = 14.5, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 160 });
        if (on) title.Children.Add(Gx.Tag(I18n.T("v10.set.on"), Ui.Res("BrandSoft"), Ui.Res("Brand2"), 11));
        var about = new TextBlock
        {
            Text = c.About != "" ? c.About : c.Author != "" ? I18n.T("v10.set.by", ("name", c.Author)) : I18n.T("v10.set.own"),
            FontSize = 12, Foreground = Ui.Res("Muted"), TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, Height = 32,
        };
        var actions = Ui.Row(6);
        if (lockedAt is int lv)
            actions.Children.Add(Ui.Row(6, Ui.Icon(Icons.Lock, 13, Ui.Res("Faint")), Ui.Text(I18n.T("v16.road.locked", ("n", lv)), "small muted")));
        else if (CreatorBuilds.Find(c.Id) is { } build)
            actions.Children.Add(Ui.Button(on ? I18n.T("v10.set.reapply") : I18n.T("v10.set.apply"), () => { CreatorBuilds.Apply(build); MainWindow.Current?.Toast(I18n.T("v17.cb.applied", ("name", c.Name))); }, on ? "" : "primary", on ? Icons.Refresh : Icons.Check));
        else
            actions.Children.Add(Ui.Button(on ? I18n.T("v10.set.reapply") : I18n.T("v10.set.apply"), () => { Configs.Apply(c); MainWindow.Current?.Toast(I18n.T("v10.cfg.applied", ("name", c.Name))); }, on ? "" : "primary", on ? Icons.Refresh : Icons.Check));
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
