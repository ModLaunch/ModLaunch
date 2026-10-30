using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Creator;
using MX = ModScriptX;

namespace ModLaunch.Views;

/// <summary>
/// 21.0: «Блоки» и «Код» — два вида одного файла. В блоках слева палитра цветных кирпичиков и кусочков, в середине
/// холст (BlockCanvas); в коде — редактор, как раньше. Переключатель над редактором, выбор помнится.
/// </summary>
public sealed partial class XStudio
{
    /// <summary>Для снимков: забыть открытые файлы проекта (следующий вход прочитает их с диска).</summary>
    internal static void Forget(string projectId) => Sessions.Remove(projectId);

    /// <summary>Вид по умолчанию — блоки: с них начинает новичок; кто пишет кодом, переключит один раз.</summary>
    static string DefaultView => Settings.Data.Str("xView") == "code" ? "code" : "blocks";

    bool BlocksOn => _s.View == "blocks" && _s.Active is { Modx: true };

    void SetView(string view)
    {
        if (_s.View == view) return;
        _s.View = view;
        Settings.Data["xView"] = view;
        Settings.Save();
        if (view == "blocks" && _s.Side is "" or "files") _s.Side = "blocks";
        Build();
    }

    /// <summary>Переключатель «Блоки · Код» справа от вкладок файлов.</summary>
    Control ViewSwitch()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(8, 5), VerticalAlignment = VerticalAlignment.Center };
        foreach (var (id, icon, key) in new[] { ("blocks", Icons.Grid, "v21.view.blocks"), ("code", Icons.Code, "v21.view.code") })
        {
            var view = id;
            var on = _s.View == id;
            var b = new Button
            {
                Classes = { "ghost" }, Padding = new Thickness(10, 4), CornerRadius = new CornerRadius(6),
                Background = on ? HubLook.Raised : Brushes.Transparent, BorderBrush = on ? HubLook.Premium(160) : Brushes.Transparent, BorderThickness = new Thickness(1),
                Content = Ui.Row(6, Ui.Icon(icon, 13, on ? Color : Ui.Res("Muted")), new TextBlock { Text = I18n.T(key), FontSize = 12.5, FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center }),
            };
            ToolTip.SetTip(b, I18n.T(key + ".tip"));
            b.Click += (_, _) => SetView(view);
            row.Children.Add(b);
        }
        return new Border { Background = HubLook.Panel, BorderBrush = HubLook.Edge, BorderThickness = new Thickness(1, 0, 0, 0), Child = row, IsVisible = _s.Active is { Modx: true } };
    }

    /// <summary>Холст блоков файла: живёт вместе с документом, правки пишет в его редактор (Ctrl+Z их отменяет).</summary>
    BlockCanvas CanvasOf(Doc d)
    {
        if (d.Canvas is { } known) { known.Refresh(); return known; }
        var canvas = new BlockCanvas(() => d.Editor.Text ?? "", text => d.Editor.ReplaceText(text), () => TargetFor(ActiveDoc()).Events, () => _live?.SetView("code"))
        {
            Undo = redo => { if (redo) d.Editor.Redo(); else d.Editor.Undo(); },
        };
        d.Canvas = canvas;
        return canvas;
    }

    /// <summary>Ошибки проверки — и на блоках (если открыт холст).</summary>
    void MarkBlocks(Doc d)
    {
        if (d.Canvas is not { } c || !BlocksOn) return;
        c.Mark(d.Diags.Select(x => (x.Line, CreatorPage.LxMsg(x), x.Warning)).Concat(d.Lints.Select(x => (x.Line, x.Message, x.Warning))));
    }

    /// <summary>Палитра: события, кусочки, переменные, условия, повторы, сообщения и свои команды — мини-кирпичики.</summary>
    Control PaletteSide()
    {
        var col = new StackPanel { Spacing = 4, Margin = new Thickness(10, 0, 10, 16) };
        col.Children.Add(new Border { Padding = new Thickness(4, 0, 4, 6), Child = Ui.Text(I18n.T("v21.p.lead"), "small muted", wrap: true) });
        var t = TargetFor(ActiveDoc());
        foreach (var (group, hue, items) in Palette())
        {
            col.Children.Add(PaletteCaption(I18n.T("v21.p.g." + group), hue));
            if (group == "pieces")
            {
                foreach (var p in Pieces.All().Take(12)) col.Children.Add(PieceItem(p, t));
                var market = Ui.Button(I18n.T("v21.piece.market"), () => MainWindow.Current?.Navigate(MarketPage.CodePieces), "ghost", Icons.Bag);
                market.HorizontalAlignment = HorizontalAlignment.Stretch;
                col.Children.Add(market);
                continue;
            }
            foreach (var id in items)
            {
                var can = !id.StartsWith("hook:") || t.Events.Contains(id[5..]);
                col.Children.Add(BrickItem(id, hue, can, t));
            }
        }
        return col;
    }

    internal static IEnumerable<(string Group, string Hue, string[] Items)> Palette() =>
    [
        ("events", BlockCanvas.Hue("hook"), ["hook:start", "hook:load", "hook:tick", "hook:ready"]),
        ("pieces", "#F5B94A", []),
        ("talk", BlockCanvas.Hue("say"), ["say", "log"]),
        ("vars", BlockCanvas.Hue("set"), ["set", "add", "op", "let"]),
        ("logic", BlockCanvas.Hue("if"), ["if", "ifelse", "chance"]),
        ("loops", BlockCanvas.Hue("for"), ["repeat", "while", "each", "break"]),
        ("fn", BlockCanvas.Hue("fn"), ["fn", "call", "return"]),
        ("other", BlockCanvas.Hue("note"), ["note"]),
    ];

    static Control PaletteCaption(string text, string hue) => new Border
    {
        Padding = new Thickness(4, 12, 4, 4),
        Child = Ui.Row(7, Ui.Dot(new SolidColorBrush(Avalonia.Media.Color.Parse(hue)), 8), HubLook.MonoText(text.ToUpper(I18n.Culture), 10, Ui.Res("Muted"))),
    };

    /// <summary>Кирпичик палитры: нажатие — в холст к курсору, перетаскивание — куда угодно.</summary>
    Control BrickItem(string id, string hue, bool enabled, MX.Target t)
    {
        var kind = id.StartsWith("hook:") ? "hook" : BlockCanvas.Template(id)?.Kind ?? "raw";
        var c = Avalonia.Media.Color.Parse(hue);
        var label = id.StartsWith("hook:") ? I18n.T("v21.b.when") + " " + I18n.T("v21.ev." + id[5..]) : I18n.T("v21.p." + id);
        var box = new Border
        {
            CornerRadius = new CornerRadius(kind == "hook" ? 10 : 8), Padding = new Thickness(12, 7, 10, 7), Cursor = new Cursor(StandardCursorType.Hand),
            Background = kind == "hook" ? new SolidColorBrush(Look.Mix(c, Colors.Black, 0.25)) : new SolidColorBrush(Avalonia.Media.Color.FromArgb(Look.IsLight ? (byte)0x22 : (byte)0x26, c.R, c.G, c.B)),
            BorderBrush = new SolidColorBrush(Avalonia.Media.Color.FromArgb(0x70, c.R, c.G, c.B)), BorderThickness = new Thickness(1), Opacity = enabled ? 1 : 0.45,
            Child = Ui.Row(8, Ui.Icon(BlockCanvas.IconOf(kind), 14, kind == "hook" ? Brushes.White : new SolidColorBrush(Look.IsLight ? Look.Mix(c, Colors.Black, 0.45) : c)),
                new TextBlock { Text = label, FontSize = 12.5, FontWeight = FontWeight.SemiBold, Foreground = kind == "hook" ? Brushes.White : Ui.Res("Text"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }),
        };
        var bar = new Border { Width = 4, CornerRadius = new CornerRadius(8, 0, 0, 8), Background = new SolidColorBrush(c), HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false, IsVisible = kind != "hook" };
        var item = new Panel { Children = { box, bar } };
        ToolTip.SetTip(item, enabled ? I18n.T("v21.p." + (id.StartsWith("hook:") ? "hook" : id) + ".tip") : I18n.T("v16.event.missing", ("engine", t.Title)));
        if (!enabled) return item;
        item.PointerEntered += (_, _) => box.BorderBrush = new SolidColorBrush(c);
        item.PointerExited += (_, _) => box.BorderBrush = new SolidColorBrush(Avalonia.Media.Color.FromArgb(0x70, c.R, c.G, c.B));
        item.Tapped += (_, _) => PlaceBlock(id);
        BlockCanvas.StartDrag(item, () => BlockCanvas.DragTemplate = id);
        return item;
    }

    /// <summary>Кусочек в палитре: золотая плашка с названием и автором (купленный — «куплено у …»).</summary>
    Control PieceItem(Piece p, MX.Target t)
    {
        var id = "piece:" + p.Title;
        var c = Avalonia.Media.Color.Parse(p.Color);
        var tile = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(7),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(c, 0), new GradientStop(Look.Mix(c, Colors.Black, 0.45), 1) },
            },
            Child = Ui.Icon(p.Icon, 14, Brushes.White),
        };
        var used = _s.Active is { } d && Pieces.Used(d.Editor.Text ?? "", p.Name);
        var sub = used ? I18n.T("v21.piece.inProject") : p.Free ? I18n.T("v21.piece.free") : I18n.T("v21.piece.bought", ("seller", p.Seller));
        var text = Ui.Col(1, new TextBlock { Text = p.Title, FontSize = 12.5, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
            Ui.Text(sub, "small", color: used ? Ui.Res("Good") : Ui.Res("Muted")));
        text.VerticalAlignment = VerticalAlignment.Center;
        var gold = Avalonia.Media.Color.Parse("#F5B94A");
        var box = new Border
        {
            CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 6), Cursor = new Cursor(StandardCursorType.Hand),
            Background = new SolidColorBrush(Avalonia.Media.Color.FromArgb(Look.IsLight ? (byte)0x26 : (byte)0x1C, gold.R, gold.G, gold.B)),
            BorderBrush = new SolidColorBrush(Avalonia.Media.Color.FromArgb(0x66, gold.R, gold.G, gold.B)), BorderThickness = new Thickness(1),
            Child = Ui.Row(10, tile, text),
        };
        var missing = p.Events.Where(e => !t.Events.Contains(e)).ToList();
        ToolTip.SetTip(box, (p.About == "" ? "" : p.About + "\n") + I18n.T("v21.piece.tip", ("line", Pieces.Line(p.Title)))
            + (missing.Count > 0 ? "\n" + I18n.T("v16.event.missing", ("engine", t.Title)) : ""));
        box.PointerEntered += (_, _) => box.BorderBrush = new SolidColorBrush(gold);
        box.PointerExited += (_, _) => box.BorderBrush = new SolidColorBrush(Avalonia.Media.Color.FromArgb(0x66, gold.R, gold.G, gold.B));
        box.Tapped += (_, _) => PlaceBlock(id);
        BlockCanvas.StartDrag(box, () => BlockCanvas.DragTemplate = id);
        return box;
    }

    /// <summary>Лот Рынка, который ставится в проект кусочком: часть кода или скрипт файлом .modx.</summary>
    internal static bool IsPiece(Listing l) => l.Kind is "code" or "script" && l.FileName.EndsWith(MX.ModxDoc.Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// На странице лота: как кусочек выглядит в ModScript X (золотая карточка и строка use) и кнопка «В проект»,
    /// когда он уже в библиотеке.
    /// </summary>
    internal static Control PieceNote(Listing l)
    {
        var gold = Avalonia.Media.Color.Parse("#F5B94A");
        var tile = new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(10),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Avalonia.Media.Color.Parse("#C9A8FF"), 0), new GradientStop(Look.Mix(Avalonia.Media.Color.Parse("#C9A8FF"), Colors.Black, 0.45), 1) },
            },
            Child = Ui.Icon(Icons.Package, 19, Brushes.White),
        };
        var card = new Border
        {
            CornerRadius = new CornerRadius(12), Padding = new Thickness(12), MinWidth = 240, HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Avalonia.Media.Color.FromArgb(0x22, gold.R, gold.G, gold.B)), BorderBrush = new SolidColorBrush(Avalonia.Media.Color.FromArgb(0x90, gold.R, gold.G, gold.B)), BorderThickness = new Thickness(1),
            Child = Ui.Row(10, tile, Ui.Col(2, Ui.Text(l.Title, "strong"), Ui.Text(I18n.T("v21.piece.bought", ("seller", l.Author)), "small muted"))),
        };
        var line = new Border
        {
            Background = HubLook.Raised, CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8), HorizontalAlignment = HorizontalAlignment.Left,
            Child = new SelectableTextBlock { Text = Pieces.Line(l.Title), FontFamily = HubLook.Mono, FontSize = 12.5, Foreground = new SolidColorBrush(Avalonia.Media.Color.Parse("#FFD58A")) },
        };
        var look = Ui.Row(14, card, line);
        foreach (var c in look.Children) c.VerticalAlignment = VerticalAlignment.Center;
        var col = Ui.Col(10, Ui.Row(8, Ui.Icon(Icons.Grid, 16, HubLook.Cyan), Ui.Text(I18n.T("v21.mk.piece.title"), "h3")),
            Ui.Text(I18n.T("v21.mk.piece.text"), "small muted", wrap: true), look);
        if (Features.Inject.Get("mk_" + l.Id) is not null)
            col.Children.Add(Ui.Button(I18n.T("v21.mk.piece.use"), () => UsePiece(l.Title), "primary", Icons.Plus));
        return new Border { Classes = { "card" }, Padding = new Thickness(16, 14), Child = col };
    }

    /// <summary>Кусочек — в последний открытый проект ModScript X (блоками); проектов нет — анкета нового.</summary>
    internal static void UsePiece(string name)
    {
        if (Settings.Data.Str("xLast") is not { } id || XProjects.Get(id) is null)
        {
            MainWindow.Current?.Toast(I18n.T("v21.mk.piece.noProject"));
            CreatorPage.OpenForm();
            return;
        }
        var page = new XStudio(id, side: "blocks");
        MainWindow.Current?.Navigate(() => page);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => page.PlaceBlock("piece:" + name), Avalonia.Threading.DispatcherPriority.Background);
    }

    /// <summary>Блок из палитры — в холст; если открыт код, кусочек встаёт строкой use, остальное — как раньше, кодом.</summary>
    void PlaceBlock(string id)
    {
        if (_s.Active is not { Modx: true } d) { NewFile(); d = _s.Active!; }
        if (!BlocksOn) SetView("blocks");
        CanvasOf(d).Insert(id);
        if (_s.Side == "blocks") RenderSide();
    }
}
