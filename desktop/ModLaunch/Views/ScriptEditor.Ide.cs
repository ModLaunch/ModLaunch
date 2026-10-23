using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Indentation;
using AvaloniaEdit.Rendering;
using ModLaunch.Core;
using ModLaunch.Creator;
using Insight = ModLaunch.Creator.ModxInsight;

namespace ModLaunch.Views;

/// <summary>
/// 19.0: редактор ModScript X как в настоящей среде разработки. Подсказки по смыслу места (события после hook,
/// переменные хука, свои функции с подписью, заготовки блоков), подсказка аргументов, справка под курсором,
/// Ctrl+клик — к определению, подсветка всех мест того же имени и парной скобки, волнистые линии под
/// ошибками, сворачивание блоков, автоскобки и отступы ({ + Enter закрывает блок), перенос строк Alt+↑/↓,
/// точки останова на полях (F9) и строка отладчика со значениями переменных прямо в коде.
/// </summary>
public sealed partial class ScriptEditor
{
    bool _modx;
    Insight.Model? _model;
    string? _modelText;
    FoldingManager? _folds;
    readonly IdeLayer _layer = new();
    BreakpointMargin? _margin;
    readonly List<TextAnchor> _breakpoints = [];
    OverloadInsightWindow? _insight;
    int? _debugLine;

    /// <summary>Команда среды из меню или клавиш редактора: definition, references, rename, format, breakpoint.</summary>
    public event Action<string>? IdeCommand;
    /// <summary>Ctrl+клик по имени: смещение в тексте.</summary>
    public event Action<int>? DefinitionRequested;
    public event Action? BreakpointsChanged;
    /// <summary>Значение переменной в текущем шаге отладчика (для справки под курсором).</summary>
    public Func<string, string?>? DebugValue;

    /// <summary>Разбор текущего текста (кэш до следующей правки).</summary>
    internal Insight.Model Model
    {
        get
        {
            var text = Text;
            if (_model is null || _modelText != text) { _model = Insight.Read(text); _modelText = text; }
            return _model;
        }
    }

    void SetupIde()
    {
        var area = _editor.TextArea;
        _editor.Options.IndentationSize = 4;
        area.IndentationStrategy = new DefaultIndentationStrategy();
        _folds = FoldingManager.Install(area);
        foreach (var fm in area.LeftMargins.OfType<FoldingMargin>())
        {
            fm.FoldingMarkerBrush = Ui.Res("Faint");
            fm.FoldingMarkerBackgroundBrush = Brushes.Transparent;
            fm.SelectedFoldingMarkerBrush = Ui.Res("Text");
            fm.SelectedFoldingMarkerBackgroundBrush = Ui.Res("Hover");
        }
        area.TextView.BackgroundRenderers.Add(_layer);
        _margin = new BreakpointMargin(this);
        area.LeftMargins.Insert(0, _margin);
        area.TextEntering += IdeEntering;
        area.Caret.PositionChanged += (_, _) => { UpdateMarks(); if (_insight is not null) ShowSignature(); };
        area.TextView.PointerHover += OnHover;
        area.TextView.PointerHoverStopped += (_, _) => HideTip();
        area.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || !e.GetCurrentPoint(area).Properties.IsLeftButtonPressed) return;
            if (OffsetAt(e.GetPosition(area.TextView)) is not { } at) return;
            e.Handled = true;
            DefinitionRequested?.Invoke(at);
        }, RoutingStrategies.Tunnel);
        area.AddHandler(KeyDownEvent, IdeKey, RoutingStrategies.Tunnel);
        _editor.ContextFlyout = Ctx.Menu(
            Ctx.Item(I18n.T("v19.cmd.definition"), Icons.Forward, () => IdeCommand?.Invoke("definition"), gesture: "F12"),
            Ctx.Item(I18n.T("v19.cmd.references"), Icons.Search, () => IdeCommand?.Invoke("references"), gesture: "Shift+F12"),
            Ctx.Item(I18n.T("v19.cmd.rename"), Icons.Edit, () => IdeCommand?.Invoke("rename"), gesture: "F2"),
            Ctx.Item(I18n.T("v19.cmd.format"), Icons.Wand, () => IdeCommand?.Invoke("format"), gesture: "Shift+Alt+F"),
            Ctx.Item(I18n.T("v19.cmd.breakpoint"), Icons.Bug, () => ToggleBreakpoint(CaretLine), gesture: "F9"),
            "-",
            Ctx.Item(I18n.T("ctx.cut"), Icons.Edit, () => _editor.Cut(), gesture: "Ctrl+X"),
            Ctx.Item(I18n.T("ctx.copy"), Icons.List, () => _editor.Copy(), gesture: "Ctrl+C"),
            Ctx.Item(I18n.T("ctx.paste"), Icons.FilePlus, () => _editor.Paste(), gesture: "Ctrl+V"),
            "-",
            Ctx.Item(I18n.T("ctx.suggest"), Icons.Sparkles, Suggest, gesture: "Ctrl+Space"),
            Ctx.Item(I18n.T("ctx.comment"), Icons.Code, ToggleComment, gesture: "Ctrl+OemQuestion"),
            Ctx.Item(I18n.T("ctx.duplicate"), Icons.Layers, DuplicateLine, gesture: "Ctrl+D"),
            Ctx.Item(I18n.T("v19.cmd.moveUp"), Icons.ArrowUp, () => MoveLines(-1), gesture: "Alt+Up"),
            Ctx.Item(I18n.T("v19.cmd.deleteLine"), Icons.Trash, DeleteLine, gesture: "Ctrl+Shift+K"),
            "-",
            Ctx.Item(I18n.T("cr.save"), Icons.Save, () => SaveRequested?.Invoke(), gesture: "Ctrl+S"));
    }

    // ---------------------------------------------------------------- место в тексте

    public int CaretColumn => _editor.TextArea.Caret.Column - 1;

    /// <summary>Закрыть подсказки набора и аргументов (для снимков и смены файла).</summary>
    public void CloseHints()
    {
        _completion?.Close();
        _insight?.Close();
        _insight = null;
    }

    public int OffsetOf(int line, int col)
    {
        var doc = _editor.Document;
        var l = doc.GetLineByNumber(Math.Clamp(line, 1, doc.LineCount));
        return l.Offset + Math.Clamp(col, 0, l.Length);
    }

    public (int Line, int Col) PositionOf(int offset)
    {
        var loc = _editor.Document.GetLocation(Math.Clamp(offset, 0, _editor.Document.TextLength));
        return (loc.Line, loc.Column - 1);
    }

    int? OffsetAt(Point inView)
    {
        var view = _editor.TextArea.TextView;
        var pos = view.GetPositionFloor(inView + view.ScrollOffset);
        if (pos is not { } p) return null;
        return OffsetOf(p.Line, p.Column - 1);
    }

    /// <summary>Выделить место (строка, столбец, длина) и показать его.</summary>
    public void Select(int line, int col, int length)
    {
        var at = OffsetOf(line, col);
        _editor.Select(at, Math.Min(length, _editor.Document.TextLength - at));
        _editor.ScrollToLine(line);
        _editor.TextArea.Caret.Offset = at + length;
        _editor.Focus();
    }

    /// <summary>Заменить текст одной правкой (её отменяет Ctrl+Z): меняется только середина, свёртки и курсор остаются.</summary>
    public void Apply(string text)
    {
        var old = Text;
        if (old == text) return;
        var head = 0;
        while (head < old.Length && head < text.Length && old[head] == text[head]) head++;
        var tail = 0;
        while (tail < old.Length - head && tail < text.Length - head && old[^(tail + 1)] == text[^(tail + 1)]) tail++;
        _editor.Document.Replace(head, old.Length - head - tail, text[head..^tail]);
    }

    /// <summary>Выровнять код (Shift+Alt+F). false — не вышло сохранить смысл, текст не тронут.</summary>
    public bool Format()
    {
        var (text, safe) = Insight.Format(Text);
        if (safe) Apply(text);
        return safe;
    }

    // ---------------------------------------------------------------- набор: скобки, отступы, подсказки

    static readonly Dictionary<char, char> Pairs = new() { ['('] = ')', ['['] = ']', ['{'] = '}', ['"'] = '"' };

    char Next() => _editor.CaretOffset < _editor.Document.TextLength ? _editor.Document.GetCharAt(_editor.CaretOffset) : '\n';

    void IdeEntering(object? sender, TextInputEventArgs e)
    {
        if (e.Text is not { Length: 1 } t || _editor.SelectionLength > 0) return;
        // Закрывающую скобку или кавычку, которая уже стоит, — перешагнуть, а не печатать вторую.
        if (t[0] is ')' or ']' or '}' or '"' && Next() == t[0])
        {
            var line = _editor.Document.GetLineByOffset(_editor.CaretOffset);
            var before = _editor.Document.GetText(line.Offset, _editor.CaretOffset - line.Offset);
            if (t[0] != '"' || before.Count(c => c == '"') % 2 == 1)
            {
                _editor.CaretOffset++;
                e.Handled = true;
            }
        }
    }

    void IdeEntered(string typed, string before)
    {
        if (typed.Length != 1) return;
        var c = typed[0];
        var next = Next();
        var free = next is '\n' or '\r' or ' ' or ')' or ']' or ',' or '}';
        var inString = Insight.Code(before).Length < before.Length || before.Count(x => x == '"') % 2 == 1;
        if (c is '(' or '[' && free && !inString)
            Close(Pairs[c]);
        else if (c == '"' && free && before.Count(x => x == '"') % 2 == 1)
            Close('"');
        else if (c == '{' && inString && free)
            Close('}');
        else if (c == '}' && before.Trim() == "}")
            Outdent();
        if (c is '(' or ',') ShowSignature();
        else if (c == ')') { _insight?.Close(); _insight = null; }
        if (c == ' ' && Regex.IsMatch(before, @"^\s*(hook|native|engine)\s$")) IdeSuggest(force: true);
        else if (c == '{' && inString) IdeSuggest(force: true);
        else if (char.IsLetter(c) || c == '_') IdeSuggest(force: false);
    }

    void Close(char closing)
    {
        var at = _editor.CaretOffset;
        _editor.Document.Insert(at, closing.ToString());
        _editor.CaretOffset = at;
    }

    /// <summary>Набрали «}» на пустой строке — отступ как у строки, что открыла блок.</summary>
    void Outdent()
    {
        var doc = _editor.Document;
        var line = doc.GetLineByOffset(_editor.CaretOffset);
        var depth = 0;
        for (var n = line.LineNumber - 1; n >= 1; n--)
        {
            var code = Insight.Code(doc.GetText(doc.GetLineByNumber(n))).Trim();
            if (code.StartsWith('}')) depth++;
            if (!code.EndsWith('{')) continue;
            if (depth-- > 0) continue;
            var text = doc.GetText(doc.GetLineByNumber(n));
            var indent = text[..(text.Length - text.TrimStart().Length)];
            var current = doc.GetText(line);
            doc.Replace(line.Offset, current.Length, indent + current.TrimStart());
            _editor.CaretOffset = line.Offset + indent.Length + current.TrimStart().Length;
            return;
        }
    }

    void IdeKey(object? sender, KeyEventArgs e)
    {
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && _completion is null && _editor.SelectionLength == 0 && EnterBlock()) e.Handled = true;
        else if (alt && !ctrl && e.Key is Key.Up or Key.Down) { MoveLines(e.Key == Key.Up ? -1 : 1); e.Handled = true; }
        else if (ctrl && shift && e.Key == Key.K) { DeleteLine(); e.Handled = true; }
        else if (alt && shift && e.Key == Key.F) { IdeCommand?.Invoke("format"); e.Handled = true; }
        else if (e.Key == Key.Escape && _insight is not null) { _insight.Close(); _insight = null; e.Handled = true; }
    }

    /// <summary>Enter после «{»: тело с отступом, и если блок ещё не закрыт — «}» на своей строке.</summary>
    bool EnterBlock()
    {
        var doc = _editor.Document;
        var at = _editor.CaretOffset;
        var line = doc.GetLineByOffset(at);
        var before = doc.GetText(line.Offset, at - line.Offset);
        var after = doc.GetText(at, line.EndOffset - at);
        if (!Insight.Code(before).TrimEnd().EndsWith('{')) return false;
        var indent = before[..(before.Length - before.TrimStart().Length)];
        var inner = indent + "    ";
        var insert = "\n" + inner;
        var close = after.Trim().StartsWith('}');
        if (close) insert += "\n" + indent;
        else if (after.Trim() == "" && Unclosed()) insert += "\n" + indent + "}";
        doc.Replace(at, close ? after.Length - after.TrimStart().Length : 0, insert);
        _editor.CaretOffset = at + 1 + inner.Length;
        return true;
    }

    /// <summary>Открытых блоков больше, чем закрытых.</summary>
    bool Unclosed()
    {
        var depth = 0;
        foreach (var raw in Text.Split('\n'))
        {
            var code = Insight.Code(raw).Trim();
            if (code.StartsWith('}')) depth--;
            if (code.EndsWith('{')) depth++;
        }
        return depth > 0;
    }

    /// <summary>Перенести строку (или выделенные) выше или ниже — Alt+↑ / Alt+↓.</summary>
    public void MoveLines(int dir)
    {
        var doc = _editor.Document;
        var first = doc.GetLineByOffset(_editor.SelectionLength > 0 ? _editor.SelectionStart : _editor.CaretOffset);
        var last = doc.GetLineByOffset(_editor.SelectionLength > 0 ? _editor.SelectionStart + _editor.SelectionLength : _editor.CaretOffset);
        var neighbour = dir < 0 ? first.PreviousLine : last.NextLine;
        if (neighbour is null) return;
        var block = doc.GetText(first.Offset, last.EndOffset - first.Offset);
        var other = doc.GetText(neighbour);
        var caret = _editor.CaretOffset - first.Offset;
        doc.BeginUpdate();
        if (dir < 0)
        {
            doc.Replace(neighbour.Offset, last.EndOffset - neighbour.Offset, block + "\n" + other);
            _editor.CaretOffset = neighbour.Offset + caret;
        }
        else
        {
            doc.Replace(first.Offset, neighbour.EndOffset - first.Offset, other + "\n" + block);
            _editor.CaretOffset = first.Offset + other.Length + 1 + caret;
        }
        doc.EndUpdate();
    }

    public void DeleteLine()
    {
        var doc = _editor.Document;
        var line = doc.GetLineByOffset(_editor.CaretOffset);
        doc.Remove(line.Offset, line.TotalLength > 0 ? line.TotalLength : line.Length);
    }

    void IdeSuggest(bool force)
    {
        if (_completion is not null) return;
        var (line, col) = (CaretLine, CaretColumn);
        var text = _editor.Document.GetText(_editor.Document.GetLineByNumber(line));
        var typed = Regex.Match(text[..Math.Min(col, text.Length)], @"[\p{L}_][\p{L}\w]*$").Value;
        if (!force && typed.Length < (Regex.IsMatch(text[..Math.Min(col, text.Length)], @"^\s*[\p{L}_]+$") ? 1 : 2)) return;
        var items = Insight.Complete(Model, line, col)
            .Where(i => !(i.Kind == Insight.Kind.Game && i.Label == typed))
            .Where(i => typed == "" || i.Label.StartsWith(typed, StringComparison.OrdinalIgnoreCase) || typed.Length >= 3 && i.Label.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(i => i.Label.StartsWith(typed, StringComparison.Ordinal)).ThenByDescending(i => i.Priority).ThenBy(i => i.Label.Length).Take(60).ToList();
        if (items.Count == 0 || items.Count == 1 && items[0].Label == typed && !items[0].Snippet) return;
        Open(items.Select(i => (ICompletionData)new XItem(i, this)), typed.Length);
    }

    /// <summary>Подсказка набора в .modx: значок вида, имя и пояснение; заготовки вставляются с отступом строки.</summary>
    sealed class XItem(Insight.Item item, ScriptEditor owner) : ICompletionData
    {
        public Avalonia.Media.IImage? Image => null;
        public string Text => item.Label;
        public double Priority => item.Priority;
        public object Description => item.Detail;
        public object Content => Ui.Row(8,
            new Border
            {
                Width = 30, CornerRadius = new CornerRadius(3), Padding = new Thickness(0, 1), Background = new SolidColorBrush(KindColor(item), 0.18),
                Child = new TextBlock { Text = KindTag(item), FontSize = 9.5, FontFamily = HubLook.Mono, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Foreground = new SolidColorBrush(KindColor(item)) },
            },
            new TextBlock { Text = item.Label, FontFamily = HubLook.Mono, FontSize = 12.5 });

        public void Complete(TextArea area, ISegment segment, EventArgs e)
        {
            var doc = area.Document;
            var line = doc.GetLineByOffset(segment.Offset);
            var text = doc.GetText(line);
            var indent = text[..(text.Length - text.TrimStart().Length)];
            var insert = item.Insert.Replace("\n", "\n" + indent);
            int? select = null, length = null, caret = null;
            if (Regex.Match(insert, @"\$\{([^}]*)\}") is { Success: true } m)
            {
                select = m.Index;
                length = m.Groups[1].Length;
                insert = insert[..m.Index] + m.Groups[1].Value + insert[(m.Index + m.Length)..];
            }
            if (insert.IndexOf("$0", StringComparison.Ordinal) is var z and >= 0) { caret = z; insert = insert.Remove(z, 2); }
            var start = segment.Offset;
            // Уже стоит «(» за именем — вторую не ставим.
            if (insert.EndsWith("()", StringComparison.Ordinal) && segment.EndOffset < doc.TextLength && doc.GetCharAt(segment.EndOffset) == '(') { insert = insert[..^2]; caret = null; }
            doc.Replace(segment, insert);
            if (select is { } s) area.Selection = Selection.Create(area, start + s, start + s + length!.Value);
            if (select is { } s2) area.Caret.Offset = start + s2 + length!.Value;
            else if (caret is { } c) area.Caret.Offset = start + c;
            if (caret is not null && insert.EndsWith(")", StringComparison.Ordinal)) Dispatcher.UIThread.Post(owner.ShowSignature);
        }

        static string KindTag(Insight.Item i) => i.Snippet ? "{ }" : i.Kind switch
        {
            Insight.Kind.Function => "fn", Insight.Kind.Builtin => "fn", Insight.Kind.Shared => "let", Insight.Kind.Game => "game",
            Insight.Kind.Event => "on", Insight.Kind.Keyword => "kw", _ => "var",
        };

        static Color KindColor(Insight.Item i) => i.Snippet ? Color.Parse("#9AA4B8") : i.Kind switch
        {
            Insight.Kind.Function => Color.Parse("#B48CF2"), Insight.Kind.Builtin => Color.Parse("#4FC3D9"), Insight.Kind.Shared => Color.Parse("#E5B567"),
            Insight.Kind.Game => Color.Parse("#7BC47F"), Insight.Kind.Event => Color.Parse("#4FC3D9"), Insight.Kind.Keyword => Color.Parse("#E88AB4"), _ => Color.Parse("#82AAFF"),
        };
    }

    // ---------------------------------------------------------------- подсказка аргументов

    void ShowSignature()
    {
        var (line, col) = (CaretLine, CaretColumn);
        var text = _editor.Document.GetText(_editor.Document.GetLineByNumber(line));
        if (Insight.Signature(Model, text, col) is not { } sig)
        {
            _insight?.Close();
            _insight = null;
            return;
        }
        var head = new TextBlock { FontFamily = HubLook.Mono, FontSize = 12.5 };
        head.Inlines!.Add(new Run(sig.Name + "("));
        for (var i = 0; i < sig.Params.Length; i++)
        {
            if (i > 0) head.Inlines.Add(new Run(", "));
            head.Inlines.Add(i == sig.Active ? new Run(sig.Params[i]) { FontWeight = FontWeight.Bold, Foreground = HubLook.Cyan } : new Run(sig.Params[i]));
        }
        head.Inlines.Add(new Run(")"));
        var body = sig.Doc == "" ? null : new TextBlock { Text = sig.Doc, FontSize = 12, MaxWidth = 420, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Res("Muted") };
        if (_insight is { } open && open.Provider is Signature p) { p.Set(head, body); return; }
        _insight = new OverloadInsightWindow(_editor.TextArea) { Provider = new Signature(head, body) };
        _insight.Closed += (_, _) => _insight = null;
        _insight.Show();
    }

    sealed class Signature(object header, object? content) : IOverloadProvider
    {
        object _header = header;
        object? _content = content;
        public event PropertyChangedEventHandler? PropertyChanged;
        public int SelectedIndex { get; set; }
        public int Count => 1;
        public string CurrentIndexText => "";
        public object CurrentHeader => _header;
        public object CurrentContent => _content ?? "";

        public void Set(object header, object? content)
        {
            _header = header;
            _content = content;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentHeader)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentContent)));
        }
    }

    // ---------------------------------------------------------------- справка под курсором

    void OnHover(object? sender, PointerEventArgs e)
    {
        var view = _editor.TextArea.TextView;
        if (OffsetAt(e.GetPosition(view)) is not { } at) return;
        var (line, col) = PositionOf(at);
        var parts = new List<Control>();
        if (_layer.Issues.FirstOrDefault(i => i.Line == line && col >= i.Col && col <= i.Col + Math.Max(1, i.Length)) is { } issue)
            parts.Add(Ui.Row(6, Ui.Icon(issue.Warning ? Icons.Alert : Icons.Close, 12, issue.Warning ? Ui.Res("Warn") : Ui.Res("Bad")),
                new TextBlock { Text = issue.Message, FontSize = 12, MaxWidth = 420, TextWrapping = TextWrapping.Wrap }));
        var model = Model;
        if (Insight.Hover(model, line, col) is { } h)
        {
            var title = new SelectableTextBlock { Text = h.Title, FontFamily = HubLook.Mono, FontSize = 12.5, Foreground = HubLook.Cyan, MaxWidth = 460, TextWrapping = TextWrapping.Wrap };
            parts.Add(title);
            if (Insight.At(model, line, col) is { } s && DebugValue?.Invoke(s.Name) is { } value)
                parts.Add(new TextBlock { Text = $"= {value}", FontFamily = HubLook.Mono, FontSize = 12.5, Foreground = new SolidColorBrush(Color.Parse("#E5B567")), MaxWidth = 460, TextWrapping = TextWrapping.Wrap });
            parts.Add(new TextBlock { Text = h.Text, FontSize = 12, MaxWidth = 460, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Res("Muted") });
        }
        if (parts.Count == 0) return;
        ToolTip.SetTip(view, new StackPanel { Spacing = 5, Children = { } }.With(parts));
        ToolTip.SetPlacement(view, PlacementMode.Pointer);
        ToolTip.SetIsOpen(view, true);
    }

    void HideTip()
    {
        var view = _editor.TextArea.TextView;
        ToolTip.SetIsOpen(view, false);
        ToolTip.SetTip(view, null);
    }

    // ---------------------------------------------------------------- отметки: ошибки, пары, совпадения, свёртки

    /// <summary>Находки проверки — волнистыми линиями; заодно обновить свёртки блоков.</summary>
    public void SetIssues(IEnumerable<Insight.Issue> issues)
    {
        _layer.Issues = [.. issues];
        if (_folds is not null)
        {
            var doc = _editor.Document;
            var folds = Insight.Folds(Text)
                .Where(f => f.End <= doc.LineCount)
                .Select(f => new NewFolding(doc.GetLineByNumber(f.Start).EndOffset, doc.GetLineByNumber(f.End).EndOffset) { Name = " ⋯ }" })
                .Where(f => f.EndOffset > f.StartOffset).ToList();
            try { _folds.UpdateFoldings(folds, -1); } catch (ArgumentException) { }
        }
        _editor.TextArea.TextView.InvalidateLayer(_layer.Layer);
    }

    void UpdateMarks()
    {
        _layer.Words = [];
        _layer.Brackets = [];
        var (line, col) = (CaretLine, CaretColumn);
        var model = Model;
        if (Insight.At(model, line, col) is { } s && s.Kind is not (Insight.Kind.Keyword or Insight.Kind.Builtin or Insight.Kind.Event))
        {
            var refs = Insight.References(model, s);
            if (refs.Count > 1) _layer.Words = [.. refs.Select(r => (OffsetOf(r.Line, r.Col), r.Length, r.Write))];
        }
        if (MatchBracket() is { } pair) _layer.Brackets = [pair.A, pair.B];
        _editor.TextArea.TextView.InvalidateLayer(_layer.Layer);
    }

    /// <summary>Скобка у курсора и её пара.</summary>
    (int A, int B)? MatchBracket()
    {
        var doc = _editor.Document;
        var at = _editor.CaretOffset;
        foreach (var p in new[] { at, at - 1 })
        {
            if (p < 0 || p >= doc.TextLength) continue;
            var c = doc.GetCharAt(p);
            var open = "([{".IndexOf(c);
            var close = ")]}".IndexOf(c);
            if (open < 0 && close < 0) continue;
            var (me, other, dir) = open >= 0 ? (c, ")]}"[open], 1) : (c, "([{"[close], -1);
            var depth = 0;
            var limit = 20000;
            for (var i = p; i >= 0 && i < doc.TextLength && limit-- > 0; i += dir)
            {
                var x = doc.GetCharAt(i);
                if (x == me) depth++;
                else if (x == other && --depth == 0) return (p, i);
            }
            return null;
        }
        return null;
    }

    // ---------------------------------------------------------------- точки останова и строка отладчика

    public HashSet<int> Breakpoints => [.. _breakpoints.Where(a => !a.IsDeleted).Select(a => a.Line)];

    public void ToggleBreakpoint(int line)
    {
        var doc = _editor.Document;
        if (line < 1 || line > doc.LineCount) return;
        var have = _breakpoints.Where(a => !a.IsDeleted && a.Line == line).ToList();
        if (have.Count > 0) foreach (var a in have) _breakpoints.Remove(a);
        else
        {
            var anchor = doc.CreateAnchor(doc.GetLineByNumber(line).Offset);
            anchor.MovementType = AnchorMovementType.AfterInsertion;
            anchor.SurviveDeletion = false;
            _breakpoints.Add(anchor);
        }
        _breakpoints.RemoveAll(a => a.IsDeleted);
        _margin?.InvalidateVisual();
        BreakpointsChanged?.Invoke();
    }

    /// <summary>Шаг отладчика: строка (null — отладка кончилась) и значения имён этой строки — серым прямо в коде.</summary>
    public void SetDebug(int? line, IReadOnlyDictionary<string, string>? values)
    {
        _debugLine = line;
        _layer.DebugLine = line;
        _layer.Inline = "";
        if (line is { } l && l >= 1 && l <= _editor.Document.LineCount)
        {
            if (values is not null)
            {
                var names = Insight.Words(_editor.Document.GetText(_editor.Document.GetLineByNumber(l))).Where(w => !w.Member).Select(w => w.Name).Distinct();
                _layer.Inline = string.Join("   ", names.Where(values.ContainsKey).Take(6).Select(n => $"{n} = {values[n]}"));
            }
            _editor.ScrollToLine(l);
        }
        _margin?.InvalidateVisual();
        _editor.TextArea.TextView.InvalidateLayer(_layer.Layer);
    }

    sealed class BreakpointMargin : AbstractMargin
    {
        readonly ScriptEditor _owner;
        static readonly IBrush Dot = new SolidColorBrush(Color.Parse("#E5484D"));
        static readonly IBrush Arrow = new SolidColorBrush(Color.Parse("#F5B83D"));

        public BreakpointMargin(ScriptEditor owner)
        {
            _owner = owner;
            Cursor = new Cursor(StandardCursorType.Hand);
            ToolTip.SetTip(this, I18n.T("v19.bp.tip"));
        }

        protected override Size MeasureOverride(Size availableSize) => new(18, 0);

        protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
        {
            if (oldTextView is not null) { oldTextView.VisualLinesChanged -= Redraw; oldTextView.ScrollOffsetChanged -= Redraw; }
            base.OnTextViewChanged(oldTextView, newTextView);
            if (newTextView is not null) { newTextView.VisualLinesChanged += Redraw; newTextView.ScrollOffsetChanged += Redraw; }
            InvalidateVisual();
        }

        void Redraw(object? sender, EventArgs e) => InvalidateVisual();

        public override void Render(DrawingContext ctx)
        {
            ctx.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
            if (TextView is not { VisualLinesValid: true } view) return;
            var marks = _owner.Breakpoints;
            foreach (var vl in view.VisualLines)
            {
                var line = vl.FirstDocumentLine.LineNumber;
                var y = vl.VisualTop - view.VerticalOffset + vl.Height / 2;
                if (marks.Contains(line)) ctx.DrawEllipse(Dot, null, new Point(9, y), 4.5, 4.5);
                if (_owner._debugLine == line)
                {
                    var g = new StreamGeometry();
                    using (var c = g.Open())
                    {
                        c.BeginFigure(new Point(3, y - 5), true);
                        c.LineTo(new Point(10, y - 5));
                        c.LineTo(new Point(15, y));
                        c.LineTo(new Point(10, y + 5));
                        c.LineTo(new Point(3, y + 5));
                        c.EndFigure(true);
                    }
                    ctx.DrawGeometry(Arrow, null, g);
                }
            }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (TextView is not { } view) return;
            var vl = view.GetVisualLineFromVisualTop(e.GetPosition(this).Y + view.VerticalOffset);
            if (vl is null) return;
            _owner.ToggleBreakpoint(vl.FirstDocumentLine.LineNumber);
            e.Handled = true;
        }
    }

    /// <summary>Слой под текстом: волнистые линии, совпадения имени, пара скобок, строка отладчика и значения в ней.</summary>
    sealed class IdeLayer : IBackgroundRenderer
    {
        public List<Insight.Issue> Issues = [];
        public List<(int Offset, int Length, bool Write)> Words = [];
        public List<int> Brackets = [];
        public int? DebugLine;
        public string Inline = "";

        public KnownLayer Layer => KnownLayer.Selection;

        static readonly IBrush WordBg = new SolidColorBrush(Color.FromArgb(46, 130, 170, 255));
        static readonly IBrush WriteBg = new SolidColorBrush(Color.FromArgb(56, 229, 181, 103));
        static readonly IPen BracketPen = new Pen(new SolidColorBrush(Color.FromArgb(150, 160, 170, 190)), 1);
        static readonly IBrush DebugBg = new SolidColorBrush(Color.FromArgb(52, 245, 184, 61));
        static readonly IPen ErrorPen = new Pen(new SolidColorBrush(Color.Parse("#E5484D")), 1.2);
        static readonly IPen WarnPen = new Pen(new SolidColorBrush(Color.Parse("#E5B567")), 1.2);

        public void Draw(TextView view, DrawingContext ctx)
        {
            if (!view.VisualLinesValid || view.Document is not { } doc) return;
            var first = view.VisualLines.First().FirstDocumentLine.LineNumber;
            var last = view.VisualLines.Last().LastDocumentLine.LineNumber;
            if (DebugLine is { } dl && dl >= first && dl <= last && dl <= doc.LineCount)
            {
                var line = doc.GetLineByNumber(dl);
                foreach (var r in BackgroundGeometryBuilder.GetRectsForSegment(view, new TextSegment { StartOffset = line.Offset, Length = line.Length }, true))
                    ctx.FillRectangle(DebugBg, new Rect(0, r.Y, Math.Max(view.Bounds.Width, r.Right), r.Height));
                if (Inline != "")
                {
                    var end = BackgroundGeometryBuilder.GetRectsForSegment(view, new TextSegment { StartOffset = line.Offset, Length = line.Length }).LastOrDefault();
                    var text = new FormattedText(Inline, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(HubLook.Mono, FontStyle.Italic), 12, new SolidColorBrush(Color.FromArgb(200, 229, 181, 103)));
                    ctx.DrawText(text, new Point(end.Right + 24, end.Y + Math.Max(0, (end.Height - text.Height) / 2)));
                }
            }
            foreach (var (offset, length, write) in Words)
            {
                if (offset + length > doc.TextLength) continue;
                foreach (var r in BackgroundGeometryBuilder.GetRectsForSegment(view, new TextSegment { StartOffset = offset, Length = length }))
                    ctx.FillRectangle(write ? WriteBg : WordBg, r, 2);
            }
            foreach (var b in Brackets)
            {
                if (b >= doc.TextLength) continue;
                foreach (var r in BackgroundGeometryBuilder.GetRectsForSegment(view, new TextSegment { StartOffset = b, Length = 1 }))
                    ctx.DrawRectangle(null, BracketPen, r.Deflate(0.5), 2, 2);
            }
            foreach (var issue in Issues)
            {
                if (issue.Line < first || issue.Line > last || issue.Line > doc.LineCount) continue;
                var line = doc.GetLineByNumber(issue.Line);
                var text = doc.GetText(line);
                int start, length;
                if (issue.Length > 0) { start = Math.Min(issue.Col, line.Length); length = Math.Min(issue.Length, line.Length - start); }
                else { start = text.Length - text.TrimStart().Length; length = text.Trim().Length; }
                if (length <= 0) continue;
                foreach (var r in BackgroundGeometryBuilder.GetRectsForSegment(view, new TextSegment { StartOffset = line.Offset + start, Length = length }))
                    Wave(ctx, r, issue.Warning ? WarnPen : ErrorPen);
            }
        }

        static void Wave(DrawingContext ctx, Rect r, IPen pen)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                var y = r.Bottom - 1.5;
                c.BeginFigure(new Point(r.Left, y), false);
                var up = true;
                for (var x = r.Left + 2.5; x <= r.Right + 0.1; x += 2.5, up = !up) c.LineTo(new Point(x, up ? y - 2 : y));
                c.EndFigure(false);
            }
            ctx.DrawGeometry(null, pen, g);
        }
    }
}

static class PanelExtensions
{
    public static StackPanel With(this StackPanel panel, IEnumerable<Control> children)
    {
        foreach (var c in children) panel.Children.Add(c);
        return panel;
    }
}
