using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ModLaunch.Core;
using ModLaunch.Creator;
using MX = ModScriptX;

namespace ModLaunch.Views;

/// <summary>
/// 21.0: ModScript X блоками — как в Scratch, только это тот же файл .modx. Каждое событие («Когда игра началась»)
/// — стопка, в ней цветные кирпичики с полями: «Сказать [Привет]», «Если [здоровье] [меньше] [20]», «Повторить
/// для [i] от [1] до [3]». Кирпичики перетаскиваются из палитры и между собой, нажатие на кирпичик палитры кладёт
/// его туда, где мигает курсор. Купленные на Рынке кусочки кода — золотые карточки с названием (в коде — строка
/// use "Название"). Любая правка сразу пишется в код (её можно отменить Ctrl+Z), проверка ошибок — та же, что в коде,
/// а ошибка показывается прямо на кирпичике понятными словами.
/// </summary>
public sealed class BlockCanvas : UserControl
{
    readonly Func<string> _read;
    readonly Action<string> _write;
    readonly Func<IReadOnlyCollection<string>> _events;
    readonly Action _showCode;
    XBlockDoc _doc;
    string _last;
    XBlock? _selected;
    (List<XBlock> List, int Index)? _cursor;
    readonly Dictionary<XBlock, (Border Box, TextBlock Error)> _views = [];
    readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

    /// <summary>Что тащат: шаблон из палитры или блок с холста (данные перетаскивания — только метка формата).</summary>
    internal static string? DragTemplate;
    static XBlock? _dragBlock;
    internal const string DragFormat = "modlaunch/modx-block";

    public BlockCanvas(Func<string> read, Action<string> write, Func<IReadOnlyCollection<string>> events, Action showCode)
    {
        _read = read;
        _write = write;
        _events = events;
        _showCode = showCode;
        _last = read();
        _doc = XBlockDoc.Parse(_last);
        Focusable = true;
        Content = new Panel { Children = { new HubLook.Blueprint(), _scroll } };
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Bubble);
        Render();
    }

    /// <summary>Код поменяли не блоками (в редакторе, отменой, из истории) — разобрать заново.</summary>
    public void Refresh()
    {
        var code = _read();
        if (code == _last) return;
        _last = code;
        _doc = XBlockDoc.Parse(code);
        _selected = null;
        _cursor = null;
        Render();
    }

    /// <summary>Записать блоки в код; rebuild — ещё и перерисовать холст (новый, убранный или перенесённый блок).</summary>
    void Commit(bool rebuild)
    {
        var code = _doc.Code();
        _last = code;
        _write(code);
        if (rebuild) Render();
    }

    // ---------------------------------------------------------------- цвета и подписи

    internal static string Hue(string kind) => kind switch
    {
        "hook" => "#F5B94A",
        "let" or "set" or "add" or "op" => "#FF8C42",
        "if" => "#36C2E0",
        "while" or "for" or "break" or "continue" => "#4CC38A",
        "say" or "log" => "#9B7BFF",
        "fn" or "call" or "return" => "#FF6FB1",
        "use" => "#F5B94A",
        "note" => "#8A93A6",
        _ => "#6B7385",
    };

    internal static string IconOf(string kind) => kind switch
    {
        "hook" => Icons.Zap, "let" => Icons.Package, "set" => Icons.Edit, "add" => Icons.Plus, "op" => Icons.Sliders, "if" => Icons.Shuffle,
        "while" or "for" => Icons.Refresh, "say" => Icons.Chat, "log" => Icons.List, "call" => Icons.Play, "return" => Icons.Undo,
        "break" => Icons.Stop, "continue" => Icons.Forward, "fn" => Icons.Code, "note" => Icons.Info, "use" => Icons.Package, "native" => Icons.Layers, _ => Icons.Code,
    };

    static Color C(string hex) => Color.Parse(hex);
    static IBrush Fill(string hex, byte a) { var c = C(hex); return new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B)); }
    static IBrush Solid(string hex) => new SolidColorBrush(C(hex));
    static IBrush Ink(string hex) => Look.IsLight ? new SolidColorBrush(Look.Mix(C(hex), Colors.Black, 0.45)) : Solid(hex);

    /// <summary>Подпись блока: «Прибавить {value} к {target}» — текст и места полей по порядку.</summary>
    static IEnumerable<(bool Field, string Text)> Format(string key)
    {
        foreach (var part in Regex.Split(I18n.T(key), @"(\{\w+\})"))
            if (part.StartsWith('{') && part.EndsWith('}')) yield return (true, part[1..^1]);
            else if (part.Trim() != "") yield return (false, part.Trim());
    }

    // ---------------------------------------------------------------- поля

    static TextBox Field(string text, string watermark, bool mono = true, double min = 38)
    {
        var box = new TextBox
        {
            Text = text, Watermark = watermark, MinWidth = min, MaxWidth = 420, MinHeight = 26, Padding = new Thickness(9, 3), FontSize = 12.5,
            FontFamily = mono ? HubLook.Mono : FontFamily.Default, VerticalAlignment = VerticalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(13), BorderThickness = new Thickness(1), AcceptsReturn = false,
            Background = Look.IsLight ? new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(150, 6, 8, 14)),
            BorderBrush = Look.IsLight ? new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)) : new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
        };
        return box;
    }

    TextBox Bind(XBlock b, string key, string watermark, bool mono = true, Action<string>? also = null)
    {
        var box = Field(b[key], watermark, mono);
        box.TextChanged += (_, _) =>
        {
            if (box.Text == b[key]) return;
            b[key] = box.Text ?? "";
            also?.Invoke(box.Text ?? "");
            Commit(false);
        };
        return box;
    }

    static ComboBox Pick(IEnumerable<(string Value, string Text)> items, string value, Action<string> changed)
    {
        var combo = new ComboBox { MinHeight = 26, Padding = new Thickness(10, 2), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(13) };
        foreach (var (v, t) in items) combo.Items.Add(new ComboBoxItem { Content = t, Tag = v });
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag! == value) ?? combo.Items.OfType<ComboBoxItem>().FirstOrDefault();
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is ComboBoxItem { Tag: string v } && v != value) { value = v; changed(v); } };
        return combo;
    }

    static TextBlock Word(string text, IBrush? color = null, bool bold = true) => new()
    {
        Text = text, FontSize = 13, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center,
        Foreground = color ?? Ui.Res("Text"), Margin = new Thickness(0, 0, 2, 0),
    };

    static readonly (string Op, string Key)[] Compare = [("<", "v21.cmp.lt"), (">", "v21.cmp.gt"), ("==", "v21.cmp.eq"), ("!=", "v21.cmp.ne"), ("<=", "v21.cmp.le"), (">=", "v21.cmp.ge")];

    /// <summary>
    /// Условие: «шанс N %», «[a] [меньше] [b]» тремя полями, иначе одно поле с выражением. Правка любой части
    /// собирает условие заново.
    /// </summary>
    IEnumerable<Control> Condition(Func<string> get, Action<string> set)
    {
        var cond = get().Trim();
        var chance = Regex.Match(cond, @"^chance\((.+)\)$");
        if (chance.Success)
        {
            var n = Field(chance.Groups[1].Value, "10", min: 34);
            n.TextChanged += (_, _) => Change($"chance({(n.Text ?? "").Trim()})");
            return [Word(I18n.T("v21.b.chance")), n, Word("%", bold: false)];
        }
        var m = Regex.Match(cond, @"^([^""<>=!]+?)\s*(<=|>=|==|!=|<|>)\s*([^<>=!]+)$");
        if (m.Success)
        {
            var a = Field(m.Groups[1].Value.Trim(), "a");
            var op = m.Groups[2].Value;
            var c = Field(m.Groups[3].Value.Trim(), "b");
            void Update() => Change($"{(a.Text ?? "").Trim()} {op} {(c.Text ?? "").Trim()}");
            a.TextChanged += (_, _) => Update();
            c.TextChanged += (_, _) => Update();
            var pick = Pick(Compare.Select(x => (x.Op, I18n.T(x.Key))), op, v => { op = v; Update(); });
            return [a, pick, c];
        }
        var box = Field(cond, "health < 20");
        box.TextChanged += (_, _) => Change(box.Text ?? "");
        return [box];

        // Поле могло «измениться» без человека (при показе) — пишем в код только настоящую правку.
        void Change(string next)
        {
            if (next.Trim() == get().Trim()) return;
            set(next);
            Commit(false);
        }
    }

    /// <summary>Содержимое строки блока: значок, слова подписи и поля на своих местах.</summary>
    Control Line(XBlock b)
    {
        var hue = Hue(b.Kind);
        var row = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        void Add(Control c) { c.Margin = new Thickness(0, 2, 6, 2); row.Children.Add(c); }
        Add(Ui.Icon(IconOf(b.Kind), 15, Ink(hue)));
        string? key = b.Kind switch
        {
            "let" => "v21.b.let", "set" => "v21.b.set", "add" => "v21.b.add", "op" => "v21.b.op", "say" => "v21.b.say", "log" => "v21.b.log",
            "call" => "v21.b.call", "return" => "v21.b.return", "while" => "v21.b.while", "if" => "v21.b.if",
            "for" => b.F.ContainsKey("from") ? "v21.b.repeat" : "v21.b.each", "break" => "v21.b.break", "continue" => "v21.b.continue",
            "fn" => "v21.b.fn", _ => null,
        };
        if (key is null)
        {
            // Заметка и непонятная строка — одним полем как есть.
            var note = b.Kind == "note";
            Add(Word(I18n.T(note ? "v21.b.note" : "v21.b.raw"), Ink(hue)));
            var box = Bind(b, "text", note ? I18n.T("v21.b.note.hint") : "code", mono: !note);
            box.MinWidth = 220;
            if (note) box.FontStyle = FontStyle.Italic;
            Add(box);
            return row;
        }
        foreach (var (field, text) in Format(key))
        {
            if (!field) { Add(Word(text)); continue; }
            switch (text)
            {
                case "cond":
                    foreach (var c in Condition(() => b["cond"], v => b["cond"] = v)) Add(c);
                    break;
                case "op":
                    Add(Pick([("-=", I18n.T("v21.op.sub")), ("*=", I18n.T("v21.op.mul")), ("/=", I18n.T("v21.op.div"))], b["op"], v => { b["op"] = v; Commit(false); }));
                    break;
                case "message":
                    // Простой текст — без кавычек; выражение (переменная, склейка) — как код.
                    Add(b.PlainText ? Bind(b, "text", I18n.T("v21.b.say.hint"), mono: false) : Bind(b, "expr", "\"text\""));
                    break;
                default:
                    var hint = text switch { "name" => "gold", "target" => "gold", "value" => "10", "var" => "i", "from" => "1", "to" => "3", "source" => "items", "expr" => "my_command()", "params" => "a, b", _ => "" };
                    Add(Bind(b, text, hint));
                    break;
            }
        }
        return row;
    }

    // ---------------------------------------------------------------- блоки

    /// <summary>Строка-заголовок блока: ручка для перетаскивания, содержимое и действия при наведении.</summary>
    Control Header(XBlock b, Control content, bool hat = false)
    {
        var grip = new Border
        {
            Width = 16, Cursor = new Cursor(StandardCursorType.SizeAll), Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Stretch,
            Child = new TextBlock { Text = "⋮⋮", FontSize = 12, LetterSpacing = -3, Foreground = hat ? new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)) : Ui.Res("Faint"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center },
        };
        ToolTip.SetTip(grip, I18n.T("v21.b.drag"));
        StartDrag(grip, () => { _dragBlock = b; DragTemplate = null; });

        var acts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Opacity = 0, VerticalAlignment = VerticalAlignment.Center };
        Button Act(string icon, string tip, Action run)
        {
            var x = new Button { Classes = { "icon", "ghost" }, Width = 24, Height = 24, Padding = new Thickness(0), Content = Ui.Icon(icon, 12, hat ? Brushes.White : Ui.Res("Muted")) };
            ToolTip.SetTip(x, I18n.T(tip));
            x.Click += (_, e) => { e.Handled = true; run(); };
            return x;
        }
        if (!hat)
        {
            acts.Children.Add(Act(Icons.ArrowUp, "v21.b.up", () => Move(b, -1)));
            acts.Children.Add(Act(Icons.ChevronDown, "v21.b.down", () => Move(b, 1)));
        }
        if (b.Kind != "hook") acts.Children.Add(Act(Icons.Copy, "v21.b.copy", () => Duplicate(b)));
        acts.Children.Add(Act(Icons.Trash, "v21.b.delete", () => Delete(b)));

        var dock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(grip, Dock.Left);
        DockPanel.SetDock(acts, Dock.Right);
        dock.Children.Add(grip);
        dock.Children.Add(acts);
        dock.Children.Add(content);
        var host = new Border { Background = Brushes.Transparent, Child = dock };
        host.PointerEntered += (_, _) => acts.Opacity = 1;
        host.PointerExited += (_, _) => acts.Opacity = 0;
        host.PointerPressed += (_, e) =>
        {
            if (InField(e.Source)) return;
            Select(b);
        };
        return host;
    }

    static bool InField(object? source) => source is Visual v && (v is TextBox or ComboBox || v.FindAncestorOfType<TextBox>() is not null || v.FindAncestorOfType<ComboBox>() is not null);

    /// <summary>Перетаскивание с порога в 4 точки: короткий щелчок остаётся щелчком.</summary>
    internal static void StartDrag(Control handle, Action begin)
    {
        Point? down = null;
        handle.PointerPressed += (_, e) => { if (e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) down = e.GetPosition(handle); };
        handle.PointerReleased += (_, _) => down = null;
        handle.PointerMoved += async (_, e) =>
        {
            if (down is not { } d || !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) return;
            var p = e.GetPosition(handle);
            if (Math.Abs(p.X - d.X) + Math.Abs(p.Y - d.Y) < 4) return;
            down = null;
            begin();
            var data = new DataObject();
            data.Set(DragFormat, "block");
            try { await DragDrop.DoDragDrop(e, data, DragDropEffects.Move | DragDropEffects.Copy); }
            finally { DragTemplate = null; _dragBlock = null; }
        };
    }

    Control View(XBlock b)
    {
        var hue = Hue(b.Kind);
        var error = new TextBlock { FontSize = 12, Foreground = Ui.Res("Bad"), TextWrapping = TextWrapping.Wrap, IsVisible = false, Margin = new Thickness(22, 2, 8, 4) };
        Border box;
        if (b.Kind is "if" or "while" or "for")
        {
            var col = new StackPanel();
            col.Children.Add(new Border { Padding = new Thickness(4, 5, 6, 5), Child = Header(b, Line(b)) });
            col.Children.Add(Arm(b.Body, hue));
            foreach (var e in b.Elifs)
            {
                var branch = e;
                var row = new WrapPanel();
                row.Children.Add(new Border { Width = 16 });
                row.Children.Add(Ui.Icon(Icons.Shuffle, 14, Ink(hue)));
                row.Children.Add(Word(" " + I18n.T("v21.b.elif") + " "));
                foreach (var c in Condition(() => branch.Cond, v => branch.Cond = v)) { c.Margin = new Thickness(0, 2, 6, 2); row.Children.Add(c); }
                col.Children.Add(new Border { Padding = new Thickness(4, 3, 6, 3), Child = row });
                col.Children.Add(Arm(e.Body, hue));
            }
            if (b.Else is { } other)
            {
                col.Children.Add(new Border { Padding = new Thickness(20, 4, 6, 4), Child = Ui.Row(6, Ui.Icon(Icons.Shuffle, 14, Ink(hue)), Word(I18n.T("v21.b.else"))) });
                col.Children.Add(Arm(other, hue));
            }
            col.Children.Add(error);
            col.Children.Add(new Border { Height = 10 });
            box = new Border { Child = col };
        }
        else
        {
            box = new Border { Padding = new Thickness(4, 5, 6, 5), Child = new StackPanel { Children = { Header(b, Line(b)), error } } };
        }
        box.CornerRadius = new CornerRadius(9);
        box.Background = Fill(hue, Look.IsLight ? (byte)0x22 : (byte)0x26);
        box.BorderBrush = Fill(hue, 0x70);
        box.BorderThickness = new Thickness(1);
        box.HorizontalAlignment = HorizontalAlignment.Stretch;
        // Полоса цвета слева — как у кирпичика: видно, какого он рода, даже мельком.
        var bar = new Border { Width = 4, CornerRadius = new CornerRadius(9, 0, 0, 9), Background = Solid(hue), HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false };
        var wrap = new Panel { Children = { box, bar } };
        _views[b] = (box, error);
        if (b == _selected) Highlight(b, true);
        return wrap;
    }

    /// <summary>Внутренность if/while/for: цветная «рука» слева и блоки с местами для вставки.</summary>
    Control Arm(List<XBlock> list, string hue)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("14,*"), Margin = new Thickness(4, 0, 8, 0) };
        grid.Children.Add(new Border { Background = Fill(hue, 0x55), CornerRadius = new CornerRadius(3), Margin = new Thickness(4, 0, 6, 0) });
        var inner = List(list);
        Grid.SetColumn(inner, 1);
        grid.Children.Add(inner);
        return grid;
    }

    /// <summary>Блоки списка с местами для вставки между ними; пустой список — место «Перетащите сюда».</summary>
    StackPanel List(List<XBlock> list)
    {
        var col = new StackPanel();
        for (var i = 0; i < list.Count; i++)
        {
            col.Children.Add(Slot(list, i, false));
            col.Children.Add(View(list[i]));
        }
        col.Children.Add(Slot(list, list.Count, list.Count == 0));
        return col;
    }

    /// <summary>Место между блоками: подсвечивается, когда над ним тащат блок; щелчок — сюда встанет следующий блок палитры.</summary>
    Control Slot(List<XBlock> list, int index, bool empty)
    {
        var on = _cursor is { } c && c.List == list && c.Index == index;
        var line = new Border { Height = 3, CornerRadius = new CornerRadius(2), Background = on ? HubLook.Premium() : Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0) };
        Control body = line;
        if (empty)
        {
            body = new Border
            {
                Height = 34, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 2), BorderThickness = new Thickness(1),
                BorderBrush = on ? HubLook.Premium() : Ui.Res("Faint"), Background = on ? Fill("#F5B94A", 0x18) : Brushes.Transparent,
                Child = Ui.Text(I18n.T(list == _doc.Items ? "v21.b.emptyTop" : "v21.b.empty"), "small muted"),
            };
            ((Control)((Border)body).Child!).HorizontalAlignment = HorizontalAlignment.Center;
            ((Control)((Border)body).Child!).VerticalAlignment = VerticalAlignment.Center;
        }
        var slot = new Border { MinHeight = empty ? 38 : 10, Background = Brushes.Transparent, Child = body, Cursor = new Cursor(StandardCursorType.Hand) };
        DragDrop.SetAllowDrop(slot, true);
        slot.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = e.Data.Contains(DragFormat) && Accepts(list, DragTemplate, _dragBlock) ? DragDropEffects.Move : DragDropEffects.None;
            if (e.DragEffects != DragDropEffects.None) { if (body is Border bb && empty) bb.BorderBrush = HubLook.Premium(); else line.Background = HubLook.Premium(); }
        });
        slot.AddHandler(DragDrop.DragLeaveEvent, (_, _) => { if (body is Border bb && empty) bb.BorderBrush = Ui.Res("Faint"); else if (!on) line.Background = Brushes.Transparent; });
        slot.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (!e.Data.Contains(DragFormat)) return;
            var moving = _dragBlock;
            var template = DragTemplate;
            Dispatcher.UIThread.Post(() => Drop(list, index, template, moving));
        });
        slot.PointerPressed += (_, _) => { _cursor = (list, index); _selected = null; Render(); };
        return slot;
    }

    /// <summary>Что можно положить в список: наверх файла — переменные, заметки и код; в тело — команды, не события.</summary>
    bool Accepts(List<XBlock> list, string? template, XBlock? moving)
    {
        var kind = moving?.Kind ?? (template is null ? null : Template(template)?.Kind);
        if (kind is null) return template?.StartsWith("hook:") == true || template?.StartsWith("piece:") == true;
        if (moving is not null && Inside(moving, list)) return false;
        return list == _doc.Items ? kind is "let" or "note" or "raw" or "hook" or "fn" : kind is not ("hook" or "fn" or "use" or "native");
    }

    static bool Inside(XBlock b, List<XBlock> list) => b.Body == list || b.Elifs.Any(e => e.Body == list) || b.Else == list
        || b.Body.Concat(b.Elifs.SelectMany(e => e.Body)).Concat(b.Else ?? []).Any(c => Inside(c, list));

    void Drop(List<XBlock> list, int index, string? template, XBlock? moving)
    {
        if (moving is not null)
        {
            if (!Accepts(list, null, moving) || _doc.Owner(moving) is not { } owner) return;
            var old = owner.IndexOf(moving);
            owner.RemoveAt(old);
            if (owner == list && old < index) index--;
            list.Insert(Math.Clamp(index, 0, list.Count), moving);
            _selected = moving;
            _cursor = (list, list.IndexOf(moving) + 1);
            Commit(true);
            return;
        }
        if (template is not null) Place(template, (list, index));
    }

    // ---------------------------------------------------------------- шаблоны палитры

    /// <summary>
    /// Новый блок по имени из палитры: say, set, add, let, if, ifelse, chance, repeat, while, each, fn, call, return,
    /// break, note. hook:&lt;событие&gt; и piece:&lt;название&gt; — не блоки тела, их кладёт Place.
    /// </summary>
    internal static XBlock? Template(string id) => id switch
    {
        "say" => XBlock.New("say", ("text", I18n.T("v21.t.say"))),
        "log" => XBlock.New("log", ("text", "debug: {gold}")),
        "set" => XBlock.New("set", ("target", "gold"), ("value", "100")),
        "add" => XBlock.New("add", ("target", "gold"), ("value", "10")),
        "op" => XBlock.New("op", ("target", "enemy_damage"), ("op", "*="), ("value", "2")),
        "let" => XBlock.New("let", ("name", "bonus"), ("value", "50")),
        "if" => XBlock.New("if", ("cond", "health < 20")),
        "ifelse" => new Func<XBlock>(() => { var b = XBlock.New("if", ("cond", "gold > 100")); b.Else = []; return b; })(),
        "chance" => XBlock.New("if", ("cond", "chance(10)")),
        "repeat" => XBlock.New("for", ("var", "i"), ("from", "1"), ("to", "3")),
        "while" => XBlock.New("while", ("cond", "gold < 100")),
        "each" => XBlock.New("for", ("var", "item"), ("source", "[1, 2, 3]")),
        "fn" => XBlock.New("fn", ("name", "my_command"), ("params", "")),
        "call" => XBlock.New("call", ("expr", "my_command()")),
        "return" => XBlock.New("return", ("value", "")),
        "break" => XBlock.New("break"),
        "continue" => XBlock.New("continue"),
        "note" => XBlock.New("note", ("text", I18n.T("v21.t.note"))),
        _ => null,
    };

    /// <summary>Положить блок из палитры по щелчку: туда, где курсор, иначе после выбранного, иначе в конец первого события.</summary>
    public void Insert(string template)
    {
        if (template.StartsWith("hook:") || template.StartsWith("piece:")) { Place(template, null); return; }
        var b = Template(template);
        if (b is null) return;
        (List<XBlock>, int)? at = _cursor is { } c && Valid(c.List) ? c : null;
        if (at is null && _selected is { } sel && _doc.Owner(sel) is { } owner) at = (owner, owner.IndexOf(sel) + 1);
        if (at is { } a && !Accepts(a.Item1, template, null)) at = null;
        Place(template, at);
    }

    bool Valid(List<XBlock> list) => list == _doc.Items || _doc.All().Any(b => b.Body == list || b.Else == list || b.Elifs.Any(e => e.Body == list));

    void Place(string template, (List<XBlock> List, int Index)? at)
    {
        if (template.StartsWith("hook:")) { AddHook(template[5..]); return; }
        if (template.StartsWith("piece:")) { AddPiece(template[6..]); return; }
        var b = Template(template)!;
        if (b.Kind == "fn")
        {
            var name = "my_command";
            for (var n = 2; _doc.Items.Any(x => x.Kind == "fn" && x["name"] == name); n++) name = $"my_command_{n}";
            b["name"] = name;
            _doc.Items.Add(b);
        }
        else
        {
            if (at is null)
            {
                // Некуда — в конец первого события (если событий нет, появится «Когда игра началась»).
                var hook = _doc.Items.FirstOrDefault(x => x.Kind == "hook") ?? _doc.AddHook(_events().Contains("start") ? "start" : _events().FirstOrDefault() ?? "ready");
                at = (hook.Body, hook.Body.Count);
            }
            var (list, index) = at.Value;
            list.Insert(Math.Clamp(index, 0, list.Count), b);
        }
        _selected = b;
        // Если, повтор и своя команда — следующий блок палитры встанет внутрь них.
        _cursor = b.Container ? (b.Body, 0) : _doc.Owner(b) is { } o ? (o, o.IndexOf(b) + 1) : null;
        Commit(true);
        Reveal(b);
    }

    public void AddHook(string ev)
    {
        var hook = _doc.Hook(ev) ?? _doc.AddHook(ev);
        _selected = hook;
        _cursor = (hook.Body, hook.Body.Count);
        Commit(true);
        Reveal(hook);
    }

    /// <summary>Кусочек в проект: строка use "Название" наверху (после других кусочков). Второй раз тот же не ставится.</summary>
    public void AddPiece(string name)
    {
        if (_doc.Items.FirstOrDefault(x => x.Kind == "use" && Same(x["name"], name)) is { } has) { _selected = has; Render(); Reveal(has); return; }
        var use = XBlock.New("use", ("name", name), ("settings", ""));
        var last = _doc.Items.FindLastIndex(x => x.Kind == "use");
        var first = _doc.Items.FindIndex(x => x.Kind is "hook" or "fn" or "native");
        _doc.Items.Insert(last >= 0 ? last + 1 : first >= 0 ? first : _doc.Items.Count, use);
        _selected = use;
        Commit(true);
        MainWindow.Current?.Toast(I18n.T("v21.piece.added", ("name", name)));
    }

    static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase) || Pieces.Find(a) is { } p && Pieces.Find(b) is { } q && p.Name == q.Name;

    void Reveal(XBlock b) => Dispatcher.UIThread.Post(() => { if (_views.TryGetValue(b, out var v)) v.Box.BringIntoView(); }, DispatcherPriority.Background);

    // ---------------------------------------------------------------- правка

    void Select(XBlock b)
    {
        if (_selected is { } old && old != b) Highlight(old, false);
        _selected = b;
        Highlight(b, true);
        if (_doc.Owner(b) is { } o) _cursor = (o, o.IndexOf(b) + 1);
        Focus();
    }

    void Highlight(XBlock b, bool on)
    {
        if (!_views.TryGetValue(b, out var v)) return;
        v.Box.BorderBrush = on ? (Look.IsLight ? Brushes.Black : Brushes.White) : v.Error.IsVisible ? Ui.Res("Bad") : Fill(Hue(b.Kind), 0x70);
        v.Box.BorderThickness = new Thickness(on ? 1.5 : 1);
    }

    void Move(XBlock b, int by)
    {
        if (_doc.Owner(b) is not { } list) return;
        var i = list.IndexOf(b);
        var j = i + by;
        if (j < 0 || j >= list.Count) return;
        (list[i], list[j]) = (list[j], list[i]);
        _selected = b;
        Commit(true);
    }

    void Duplicate(XBlock b)
    {
        if (_doc.Owner(b) is not { } list) return;
        var copy = b.Clone();
        copy.Dirty = true;
        if (copy.Kind == "fn") copy["name"] = b["name"] + "_2";
        list.Insert(list.IndexOf(b) + 1, copy);
        _selected = copy;
        Commit(true);
    }

    void Delete(XBlock b)
    {
        if (_doc.Owner(b) is not { } list) return;
        var i = list.IndexOf(b);
        list.Remove(b);
        _selected = i < list.Count ? list[i] : i > 0 ? list[i - 1] : null;
        _cursor = (list, Math.Min(i, list.Count));
        Commit(true);
    }

    void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox || e.Handled) return;
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (_selected is { } b)
        {
            if (e.Key is Key.Delete or Key.Back) { Delete(b); e.Handled = true; return; }
            if (ctrl && e.Key == Key.D) { Duplicate(b); e.Handled = true; return; }
            if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.Key is Key.Up or Key.Down) { Move(b, e.Key == Key.Up ? -1 : 1); e.Handled = true; return; }
        }
        if (ctrl && e.Key is Key.Z or Key.Y) { Undo?.Invoke(e.Key == Key.Y || e.KeyModifiers.HasFlag(KeyModifiers.Shift)); Refresh(); e.Handled = true; }
    }

    /// <summary>Отмена и повтор — правками кода (true — повтор).</summary>
    public Action<bool>? Undo;

    // ---------------------------------------------------------------- ошибки

    /// <summary>Ошибки проверки — на блоках: красная рамка и текст под строкой. Ошибки кусочка — на его карточке.</summary>
    public void Mark(IEnumerable<(int Line, string Text, bool Warning)> issues)
    {
        foreach (var (b, v) in _views)
        {
            v.Error.IsVisible = false;
            Highlight(b, b == _selected);
        }
        foreach (var (line, text, warning) in issues.Where(i => i.Line > 0).OrderBy(i => i.Warning))
        {
            if (_doc.At(line) is not { } b || !_views.TryGetValue(b, out var v) || v.Error.IsVisible) continue;
            v.Error.Text = (warning ? "⚠ " : "✕ ") + text;
            v.Error.Foreground = warning ? Ui.Res("Warn") : Ui.Res("Bad");
            v.Error.IsVisible = true;
            if (!warning && b != _selected) v.Box.BorderBrush = Ui.Res("Bad");
        }
    }

    // ---------------------------------------------------------------- холст

    void Render()
    {
        _views.Clear();
        var root = new StackPanel { Spacing = 18, Margin = new Thickness(28, 22, 28, 40), MaxWidth = 980, HorizontalAlignment = HorizontalAlignment.Left };
        if (_doc.Error is { } err)
        {
            root.Children.Add(Broken(err));
            _scroll.Content = root;
            return;
        }
        root.Children.Add(Top());
        root.Children.Add(Shelf());
        var top = _doc.Items.Where(b => b.Kind is not ("hook" or "fn" or "native" or "use")).ToList();
        if (top.Count > 0) root.Children.Add(Stack(I18n.T("v21.b.vars"), "#FF8C42", Icons.Package, TopList()));
        var hats = _doc.Items.Where(b => b.Kind is "hook" or "fn" or "native").ToList();
        if (hats.Count == 0) root.Children.Add(Start());
        foreach (var h in hats) root.Children.Add(Hat(h));
        root.Children.Add(More());
        _scroll.Content = root;
    }

    /// <summary>Скобки не сходятся — блоками не показать; ведём к строке с ошибкой в коде.</summary>
    Control Broken(string error)
    {
        var text = CreatorPage.LxMsg(new Diag(_doc.ErrorLine, error));
        return new Border
        {
            Classes = { "card" }, Padding = new Thickness(22, 18), MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Left,
            Child = Ui.Col(10, Ui.Row(10, Ui.Icon(Icons.Alert, 18, Ui.Res("Warn")), Ui.Text(I18n.T("v21.b.broken"), "h3")),
                Ui.Text(I18n.T("v21.b.broken.text", ("line", _doc.ErrorLine), ("error", text)), "muted", wrap: true),
                Ui.Button(I18n.T("v21.view.code"), _showCode, "primary", Icons.Code)),
        };
    }

    /// <summary>Шапка холста: как собирать и сколько собрано (имя мода — в шапке среды, здесь не повторяем).</summary>
    Control Top()
    {
        var count = _doc.All().Count(b => b.Kind is not ("hook" or "fn" or "use"));
        var chips = Ui.Row(8, Chip(Icons.Grid, I18n.T("v21.b.count", ("n", count))), Chip(Icons.Package, I18n.T("v21.b.pieces", ("n", _doc.Items.Count(x => x.Kind == "use")))));
        var lead = Ui.Text(I18n.T("v21.b.lead"), "small muted", wrap: true);
        lead.VerticalAlignment = VerticalAlignment.Center;
        var dock = new DockPanel();
        DockPanel.SetDock(chips, Dock.Right);
        chips.Margin = new Thickness(16, 0, 0, 0);
        dock.Children.Add(chips);
        dock.Children.Add(lead);
        return dock;
    }

    static Control Chip(string icon, string text) => new Border
    {
        BorderBrush = HubLook.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(9, 3), Background = HubLook.Raised,
        Child = Ui.Row(6, Ui.Icon(icon, 12, Ui.Res("Muted")), HubLook.MonoText(text, 11, Ui.Res("Text"))),
    };

    /// <summary>Стопка: подписанная карточка с блоками.</summary>
    static Control Stack(string title, string hue, string icon, Control body) => new Border
    {
        Background = HubLook.Panel, BorderBrush = HubLook.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10, 12, 8),
        BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 6, Blur = 22, Color = Color.FromArgb(Look.IsLight ? (byte)18 : (byte)60, 0, 0, 0) }),
        Child = Ui.Col(6, Ui.Row(8, Ui.Icon(icon, 14, Ink(hue)), HubLook.Eyebrow(title, Ink(hue))), body),
    };

    /// <summary>Верх файла (кроме кусочков): места вставки — позиции в списке верха, между хуками их нет.</summary>
    Control TopList()
    {
        var col = new StackPanel();
        var items = _doc.Items;
        var last = -1;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Kind is "hook" or "fn" or "native" or "use") continue;
            col.Children.Add(Slot(items, i, false));
            col.Children.Add(View(items[i]));
            last = i;
        }
        col.Children.Add(Slot(items, last + 1, false));
        return col;
    }

    static readonly string[] EventOrder = ["start", "load", "tick", "ready"];

    /// <summary>Шапка события или своей команды — крупная, залитая цветом, тело — со стопкой блоков.</summary>
    Control Hat(XBlock h)
    {
        if (h.Kind == "native")
        {
            var raw = Field(h["text"], "", min: 300);
            raw.AcceptsReturn = true;
            raw.CornerRadius = new CornerRadius(6);
            raw.TextChanged += (_, _) => { if (raw.Text != h["text"]) { h["text"] = raw.Text ?? ""; Commit(false); } };
            var view = Stack(I18n.T("v21.b.native", ("lang", XStudio.Lang(MX.ModxDoc.NormalizeLang(h["lang"]) ?? h["lang"]))), "#6B7385", Icons.Layers, raw);
            _views[h] = (new Border(), new TextBlock());
            return view;
        }
        var hue = Hue(h.Kind);
        var row = new WrapPanel();
        void Add(Control c) { c.Margin = new Thickness(0, 2, 8, 2); row.Children.Add(c); }
        Add(Ui.Icon(h.Kind == "hook" ? Icons.Zap : Icons.Code, 17, Brushes.White));
        if (h.Kind == "hook")
        {
            Add(new TextBlock { Text = I18n.T("v21.b.when"), FontSize = 15, FontWeight = FontWeight.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
            var events = _events();
            Add(Pick(MX.ModxDoc.Events.OrderBy(e => Array.IndexOf(EventOrder, e)).Select(e => (e, I18n.T($"v21.ev.{e}") + (events.Contains(e) ? "" : "  ·  " + I18n.T("v21.ev.none")))), h["event"],
                v => { h["event"] = v; Commit(true); }));
        }
        else
        {
            Add(new TextBlock { Text = I18n.T("v21.b.fnTitle"), FontSize = 15, FontWeight = FontWeight.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
            Add(Bind(h, "name", "my_command"));
            Add(new TextBlock { Text = "(", FontSize = 15, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
            Add(Bind(h, "params", "a, b"));
            Add(new TextBlock { Text = ")", FontSize = 15, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
        }
        var c1 = C(hue);
        var head = new Border
        {
            CornerRadius = new CornerRadius(12, 12, 4, 4), Padding = new Thickness(8, 8, 8, 8),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Look.Mix(c1, Colors.Black, 0.18), 0), new GradientStop(Look.Mix(c1, Colors.Black, 0.42), 1) },
            },
            Child = Header(h, row, hat: true),
        };
        var error = new TextBlock { FontSize = 12, Foreground = Ui.Res("Bad"), TextWrapping = TextWrapping.Wrap, IsVisible = false, Margin = new Thickness(12, 4, 8, 0) };
        var card = new Border
        {
            Background = HubLook.Panel, BorderBrush = Fill(hue, 0x60), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 8, Blur = 26, Color = Color.FromArgb(Look.IsLight ? (byte)22 : (byte)70, 0, 0, 0) }),
            Child = Ui.Col(0, head, error, new Border { Padding = new Thickness(4, 6, 4, 8), Child = Arm(h.Body, hue) }),
        };
        _views[h] = (card, error);
        if (h == _selected) Highlight(h, true);
        return card;
    }

    /// <summary>Событий ещё нет — большие кнопки «Когда…»: с чего начинается любой мод.</summary>
    Control Start()
    {
        var grid = new WrapPanel();
        var events = EventOrder.Where(_events().Contains).ToList();
        foreach (var e in events.Count > 0 ? events : [.. EventOrder])
        {
            var ev = e;
            var b = new Button
            {
                Classes = { "tile" }, Width = 210, Margin = new Thickness(0, 0, 12, 12), Padding = new Thickness(14, 12), HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = Ui.Col(6, Ui.Row(8, Ui.Icon(Icons.Zap, 16, Ink("#F5B94A")), Ui.Text(I18n.T("v21.b.when") + " " + I18n.T($"v21.ev.{e}"), "strong")),
                    Ui.Text(I18n.T($"v21.ev.{e}.text"), "small muted", wrap: true)),
            };
            b.Click += (_, _) => AddHook(ev);
            grid.Children.Add(b);
        }
        return Ui.Col(10, Ui.Text(I18n.T("v21.b.start"), "h3"), grid);
    }

    /// <summary>Снизу: новое событие (которых ещё нет) и своя команда.</summary>
    Control More()
    {
        var row = new WrapPanel();
        var used = _doc.Items.Where(x => x.Kind == "hook").Select(x => x["event"]).ToHashSet();
        foreach (var e in EventOrder.Where(e => _events().Contains(e) && !used.Contains(e)))
        {
            var ev = e;
            var b = Ui.Button(I18n.T("v21.b.when") + " " + I18n.T($"v21.ev.{e}"), () => AddHook(ev), "ghost", Icons.Plus);
            b.Margin = new Thickness(0, 0, 8, 8);
            row.Children.Add(b);
        }
        var fn = Ui.Button(I18n.T("v21.p.fn"), () => Place("fn", null), "ghost", Icons.Code);
        fn.Margin = new Thickness(0, 0, 8, 8);
        row.Children.Add(fn);
        return row;
    }

    // ---------------------------------------------------------------- кусочки

    /// <summary>
    /// Полка кусочков: каждый — золотая карточка с названием, автором и настройками (у бесплатных). Код купленного
    /// кусочка не показывается нигде — только название, как у предмета в инвентаре.
    /// </summary>
    Control Shelf()
    {
        var wrap = new WrapPanel();
        foreach (var use in _doc.Items.Where(x => x.Kind == "use")) wrap.Children.Add(Card(use));
        var add = new Button
        {
            Classes = { "ghost" }, Width = 150, MinHeight = 92, Margin = new Thickness(0, 0, 12, 12), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            BorderBrush = Fill("#F5B94A", 0x80), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            Content = Ui.Col(4, Ui.Icon(Icons.Plus, 18, Ink("#F5B94A")), Ui.Text(I18n.T("v21.piece.add"), "small strong")),
        };
        foreach (var c in ((StackPanel)add.Content).Children) c.HorizontalAlignment = HorizontalAlignment.Center;
        add.Flyout = PieceMenu(AddPiece);
        wrap.Children.Add(add);
        return Ui.Col(8, Ui.Row(8, Ui.Icon(Icons.Package, 14, Ink("#F5B94A")), HubLook.Eyebrow(I18n.T("v21.piece.shelf"), Ink("#F5B94A")),
            Ui.Text(I18n.T("v21.piece.shelf.hint"), "small muted")), wrap);
    }

    /// <summary>Меню «Добавить кусочек»: купленные, бесплатные и путь на Рынок.</summary>
    internal static MenuFlyout PieceMenu(Action<string> add)
    {
        var all = Pieces.All();
        var items = new List<object?>();
        foreach (var p in all.Where(p => !p.Free)) { var n = p.Title; items.Add(Ctx.Item(p.Title + "  ·  " + p.Seller, Icons.Package, () => add(n))); }
        if (items.Count > 0) items.Add("-");
        foreach (var p in all.Where(p => p.Free)) { var n = p.Title; items.Add(Ctx.Item(p.Title, p.Icon, () => add(n))); }
        items.Add("-");
        items.Add(Ctx.Item(I18n.T("v21.piece.market"), Icons.Bag, () => MainWindow.Current?.Navigate(MarketPage.CodePieces)));
        return Ctx.Menu([.. items]);
    }

    Control Card(XBlock use)
    {
        var piece = Pieces.Find(use["name"]);
        var hex = piece?.Color ?? "#FF5A5F";
        var tile = new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(10), VerticalAlignment = VerticalAlignment.Top,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(C(hex), 0), new GradientStop(Look.Mix(C(hex), Colors.Black, 0.45), 1) },
            },
            Child = Ui.Icon(piece?.Icon ?? Icons.Alert, 19, Brushes.White),
        };
        var who = piece is null ? I18n.T("v21.piece.missing") : piece.Free ? I18n.T("v21.piece.byFree") : I18n.T("v21.piece.bought", ("seller", piece.Seller));
        var text = Ui.Col(2, new TextBlock { Text = use["name"], FontSize = 14, FontWeight = FontWeight.Bold, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 170 },
            Ui.Text(who, "small", color: piece is null ? Ui.Res("Bad") : Ui.Res("Muted")));
        var remove = new Button { Classes = { "icon", "ghost" }, Width = 24, Height = 24, Padding = new Thickness(0), Content = Ui.Icon(Icons.Close, 11, Ui.Res("Faint")), VerticalAlignment = VerticalAlignment.Top };
        ToolTip.SetTip(remove, I18n.T("v21.piece.remove"));
        remove.Click += (_, _) => Delete(use);
        var top = new DockPanel();
        DockPanel.SetDock(tile, Dock.Left);
        DockPanel.SetDock(remove, Dock.Right);
        tile.Margin = new Thickness(0, 0, 10, 0);
        top.Children.Add(tile);
        top.Children.Add(remove);
        top.Children.Add(text);
        var col = Ui.Col(8, top);
        if (piece is { About.Length: > 0 }) col.Children.Add(Ui.Text(piece.About, "small muted", wrap: true));
        // Настройки бесплатного кусочка: своё значение пишется в строку use, пустое — значение кусочка.
        if (piece is { Settings.Count: > 0 })
        {
            var mine = XBlock.ParseSettings(use["settings"]).ToDictionary(s => s.Name, s => s.Value);
            foreach (var (name, value) in piece.Settings)
            {
                var key = name;
                var box = Field(mine.GetValueOrDefault(name) ?? "", value, min: 60);
                box.TextChanged += (_, _) =>
                {
                    var now = XBlock.ParseSettings(use["settings"]).Where(s => s.Name != key).ToList();
                    if ((box.Text ?? "").Trim() != "") now.Add((key, box.Text!.Trim()));
                    var next = string.Join(", ", now.Select(s => $"{s.Name} = {s.Value}"));
                    if (next == use["settings"]) return;
                    use["settings"] = next;
                    Commit(false);
                };
                var label = I18n.T($"v21.set.{name}") is var t && t != $"v21.set.{name}" ? t : name;
                var line = new DockPanel();
                var l = Ui.Text(label, "small");
                l.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(box, Dock.Right);
                line.Children.Add(box);
                line.Children.Add(l);
                col.Children.Add(line);
            }
        }
        var error = new TextBlock { FontSize = 12, Foreground = Ui.Res("Bad"), TextWrapping = TextWrapping.Wrap, IsVisible = false };
        col.Children.Add(error);
        var card = new Border
        {
            Width = 260, Margin = new Thickness(0, 0, 12, 12), Padding = new Thickness(12, 12, 8, 12), CornerRadius = new CornerRadius(12),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(Look.IsLight ? (byte)0x30 : (byte)0x26, 0xF5, 0xB9, 0x4A), 0), new GradientStop(Color.FromArgb(0x08, 0xF5, 0xB9, 0x4A), 1) },
            },
            BorderBrush = Fill("#F5B94A", 0x90), BorderThickness = new Thickness(1), Child = col,
        };
        ToolTip.SetTip(card, I18n.T("v21.piece.tip", ("line", Pieces.Line(use["name"]))));
        card.PointerPressed += (_, e) => { if (!InField(e.Source)) Select(use); };
        _views[use] = (card, error);
        if (use == _selected) Highlight(use, true);
        return card;
    }

    [SelfTest]
    static string BlocksBuildAMod()
    {
        var code = "mod \"Kid\"\nengine renpy\n";
        var canvas = new BlockCanvas(() => code, t => code = t, () => ["ready", "start", "load", "tick"], () => { });
        canvas.Insert("say");                 // событий нет — появится «Когда игра началась»
        canvas.Insert("chance");              // «если» — следующий блок встанет внутрь
        canvas.Insert("add");
        canvas.Insert("hook:tick");
        canvas.Insert("repeat");
        canvas.Insert("log");
        canvas.Insert("piece:Double gold");
        canvas.Insert("piece:Double gold");   // второй раз тот же кусочек не ставится
        canvas.Insert("let");                 // в повтор — локальная переменная
        var doc = MX.ModxDoc.Parse(code);
        if (!doc.Ok) throw new Exception("built mod has errors: " + doc.Diags[0].Message + "\n" + code);
        var start = doc.Hooks.Single(h => h.Event == "start");
        if (start.Body.Select(s => s.Kind).FirstOrDefault(k => k == "if") is null) throw new Exception("no if in start:\n" + code);
        var tick = doc.Hooks.Single(h => h.Event == "tick");
        if (!tick.Body.Any(s => s.Kind == "for") || !tick.Body.Any(s => s.Kind == "log")) throw new Exception("tick: " + string.Join(",", tick.Body.Select(s => s.Kind)));
        if (doc.Uses.Count != 1) throw new Exception("piece added twice:\n" + code);
        // Удаление и перенос — через выбранный блок.
        var blocks = XBlockDoc.Parse(code);
        if (blocks.Error is not null || blocks.Items.Count(b => b.Kind == "hook") != 2) throw new Exception("blocks: " + blocks.Error);
        canvas.Refresh();
        var say = canvas._doc.All().First(b => b.Kind == "say");
        canvas.Select(say);
        canvas.Delete(say);
        // Своя строка «Сказать» ушла (у кусочка своя «say» остаётся — она в коде кусочка, не в файле).
        if (XBlockDoc.Parse(code).Hook("start")!.Body.Any(b => b.Kind == "say") || !MX.ModxDoc.Parse(code).Ok) throw new Exception("delete:\n" + code);
        return "palette clicks build a valid mod: events, if, repeat, piece once, delete";
    }
}
