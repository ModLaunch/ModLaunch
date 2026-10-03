using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Views;

/// <summary>
/// Редактор ModScript: номера строк, подсветка, подсказки команд (Ctrl+Пробел
/// или просто начните печатать в начале строки) и отметка строки с ошибкой.
/// </summary>
public sealed partial class ScriptEditor : UserControl
{
    readonly TextEditor _editor;
    readonly Colorizer _colors = new();
    CompletionWindow? _completion;

    public event Action? TextChanged;
    public event Action? SaveRequested;

    public ScriptEditor()
    {
        _editor = new TextEditor
        {
            ShowLineNumbers = true,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, JetBrains Mono, DejaVu Sans Mono, monospace"),
            FontSize = 13.5,
            WordWrap = false,
            Background = Brushes.Transparent,
            Foreground = Ui.Res("Text"),
            LineNumbersForeground = Ui.Res("Faint"),
            Padding = new Thickness(6, 8),
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        _editor.Options.ConvertTabsToSpaces = true;
        _editor.Options.IndentationSize = 2;
        _editor.Options.HighlightCurrentLine = true;
        _editor.Options.EnableHyperlinks = false;
        _editor.TextArea.TextView.CurrentLineBackground = Ui.Res("Hover");
        _editor.TextArea.TextView.CurrentLineBorder = new Pen(Brushes.Transparent);
        _editor.TextArea.SelectionBrush = new SolidColorBrush(Color.FromArgb(90, 124, 92, 255));
        _editor.TextArea.TextView.LineTransformers.Add(_colors);
        _editor.TextChanged += (_, _) => TextChanged?.Invoke();
        _editor.TextArea.TextEntered += OnEntered;
        _editor.KeyDown += (_, e) =>
        {
            if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.S) { SaveRequested?.Invoke(); e.Handled = true; }
            else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.Space) { Suggest(); e.Handled = true; }
            else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.OemQuestion) { ToggleComment(); e.Handled = true; }
            else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.D) { DuplicateLine(); e.Handled = true; }
        };
        _editor.ContextFlyout = EditorMenu();
        Content = _editor;
    }

    public string Text
    {
        get => _editor.Text ?? "";
        set => _editor.Document = new TextDocument(value ?? "");
    }

    public int CaretIndex
    {
        get => _editor.CaretOffset;
        set => _editor.CaretOffset = Math.Clamp(value, 0, _editor.Document.TextLength);
    }

    public void Insert(string text)
    {
        var at = _editor.CaretOffset;
        var doc = _editor.Document;
        var line = doc.GetLineByOffset(at);
        if (at > line.Offset && doc.GetText(line.Offset, at - line.Offset).Trim() != "") text = "\n" + text;
        doc.Insert(at, text);
        _editor.CaretOffset = at + text.Length;
        _editor.Focus();
    }

    public void GoToLine(int line)
    {
        line = Math.Clamp(line, 1, _editor.Document.LineCount);
        _editor.CaretOffset = _editor.Document.GetLineByNumber(line).Offset;
        _editor.ScrollToLine(line);
        _editor.Focus();
    }

    /// <summary>Строки с ошибками — красным фоном.</summary>
    public void MarkErrors(IEnumerable<int> lines)
    {
        _colors.Errors = lines.ToHashSet();
        _editor.TextArea.TextView.Redraw();
    }

    // ---------------------------------------------------------------- подсказки

    void OnEntered(object? sender, TextInputEventArgs e)
    {
        var doc = _editor.Document;
        var line = doc.GetLineByOffset(_editor.CaretOffset);
        var before = doc.GetText(line.Offset, _editor.CaretOffset - line.Offset);
        // Подсказываем команды, функции библиотеки, свои функции и переменные — по мере набора слова.
        if (e.Text == "$") { ShowVariables(); return; }
        var word = Regex.Match(before, @"(?<![\w$""])([A-Za-z_][\w.:]*)$");
        if (word.Success && word.Value.Length >= (Regex.IsMatch(before, @"^\s*[a-z]+$") ? 1 : 2) && !InString(before)) ShowCompletion(word.Value);
    }

    /// <summary>Курсор внутри строки в кавычках или в комментарии — подсказки не нужны.</summary>
    static bool InString(string before) => before.Count(c => c == '"') % 2 == 1 || before.TrimStart().StartsWith('#');

    /// <summary>Правый клик в редакторе.</summary>
    MenuFlyout EditorMenu() => Ctx.Menu(
        Ctx.Item(I18n.T("ctx.cut"), Icons.Edit, () => _editor.Cut(), gesture: "Ctrl+X"),
        Ctx.Item(I18n.T("ctx.copy"), Icons.List, () => _editor.Copy(), gesture: "Ctrl+C"),
        Ctx.Item(I18n.T("ctx.paste"), Icons.FilePlus, () => _editor.Paste(), gesture: "Ctrl+V"),
        Ctx.Item(I18n.T("ctx.selectAll"), Icons.Check, () => _editor.SelectAll(), gesture: "Ctrl+A"),
        "-",
        Ctx.Item(I18n.T("ctx.suggest"), Icons.Sparkles, Suggest, gesture: "Ctrl+Space"),
        Ctx.Item(I18n.T("ctx.comment"), Icons.Code, ToggleComment, gesture: "Ctrl+OemQuestion"),
        Ctx.Item(I18n.T("ctx.duplicate"), Icons.Layers, DuplicateLine, gesture: "Ctrl+D"),
        Ctx.Item(I18n.T("ctx.undo"), Icons.Back, () => _editor.Undo(), gesture: "Ctrl+Z"),
        Ctx.Item(I18n.T("ctx.redo"), Icons.Forward, () => _editor.Redo(), gesture: "Ctrl+Y"),
        "-",
        Ctx.Item(I18n.T("cr.save"), Icons.Save, () => SaveRequested?.Invoke(), gesture: "Ctrl+S"));

    /// <summary>Закомментировать / раскомментировать выделенные строки (#).</summary>
    public void ToggleComment()
    {
        var doc = _editor.Document;
        var first = doc.GetLineByOffset(_editor.SelectionLength > 0 ? _editor.SelectionStart : _editor.CaretOffset);
        var last = doc.GetLineByOffset(_editor.SelectionLength > 0 ? _editor.SelectionStart + _editor.SelectionLength : _editor.CaretOffset);
        var lines = Enumerable.Range(first.LineNumber, last.LineNumber - first.LineNumber + 1).Select(doc.GetLineByNumber).ToList();
        var uncomment = lines.All(l => doc.GetText(l).TrimStart().StartsWith('#') || doc.GetText(l).Trim() == "");
        doc.BeginUpdate();
        foreach (var l in lines)
        {
            var text = doc.GetText(l);
            if (text.Trim() == "") continue;
            var indent = text.Length - text.TrimStart().Length;
            if (uncomment) doc.Remove(l.Offset + indent, text.TrimStart().StartsWith("# ") ? 2 : 1);
            else doc.Insert(l.Offset + indent, "# ");
        }
        doc.EndUpdate();
    }

    /// <summary>Повторить текущую строку ниже.</summary>
    public void DuplicateLine()
    {
        var doc = _editor.Document;
        var line = doc.GetLineByOffset(_editor.CaretOffset);
        var column = _editor.CaretOffset - line.Offset;
        doc.Insert(line.EndOffset, "\n" + doc.GetText(line));
        _editor.CaretOffset = doc.GetLineByNumber(line.LineNumber + 1).Offset + column;
    }

    /// <summary>Ctrl+Пробел — подсказки по слову под курсором.</summary>
    public void Suggest()
    {
        var doc = _editor.Document;
        var line = doc.GetLineByOffset(_editor.CaretOffset);
        var before = doc.GetText(line.Offset, _editor.CaretOffset - line.Offset);
        ShowCompletion(Regex.Match(before, @"[A-Za-z_][\w.:]*$").Value);
    }

    void ShowCompletion(string typed)
    {
        if (_completion is not null) return;
        var text = Text;
        var mine = Regex.Matches(text, @"^\s*(?:def|func|function|fn)\s+([A-Za-z_][\w.]*)\s*\(([^)]*)\)", RegexOptions.Multiline)
            .Select(m => (ICompletionData)new Item(m.Groups[1].Value, $"{m.Groups[1].Value}({m.Groups[2].Value})", I18n.T("cr.lib.mine"), call: true, priority: 3));
        var vars = Regex.Matches(text, @"^\s*(?:let|var|const|auto|for)\s+([A-Za-z_]\w*)", RegexOptions.Multiline).Select(m => m.Groups[1].Value).Distinct()
            .Select(v => (ICompletionData)new Item(v, v, I18n.T("cr.lib.var"), priority: 2));
        var keywords = ModScript.Keywords.Select(k => (ICompletionData)new Item(k, I18n.Has($"cr.doc.{k}.syntax") ? I18n.T($"cr.doc.{k}.syntax") : k, I18n.Has($"cr.doc.{k}") ? I18n.T($"cr.doc.{k}") : "", priority: 1));
        var library = ModScript.Library.Select(kv => (ICompletionData)new Item(kv.Key, kv.Value.Sig, $"{kv.Value.Doc}\n[{kv.Value.Group}]", call: true));
        var items = mine.Concat(vars).Concat(keywords).Concat(library)
            .Where(i => typed == "" || i.Text.StartsWith(typed, StringComparison.OrdinalIgnoreCase) || i.Text.Contains("." + typed, StringComparison.OrdinalIgnoreCase) || i.Text.Contains("::" + typed, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(i => i.Text, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(i => i.Priority).ThenBy(i => !i.Text.StartsWith(typed, StringComparison.OrdinalIgnoreCase)).ThenBy(i => i.Text.Length).Take(80).ToList();
        if (items.Count == 0 || (items.Count == 1 && items[0].Text.Equals(typed, StringComparison.OrdinalIgnoreCase))) return;
        Open(items, typed.Length);
    }

    void ShowVariables()
    {
        var names = Regex.Matches(Text, @"^\s*(?:let|for)\s+([A-Za-z_]\w*)", RegexOptions.Multiline).Select(m => m.Groups[1].Value).Distinct().ToList();
        if (names.Count == 0) return;
        Open(names.Select(n => (ICompletionData)new Item(n, "$" + n, "")), 0);
    }

    void Open(IEnumerable<ICompletionData> items, int back)
    {
        _completion = new CompletionWindow(_editor.TextArea) { StartOffset = _editor.CaretOffset - back, CloseWhenCaretAtBeginning = true };
        foreach (var i in items) _completion.CompletionList.CompletionData.Add(i);
        _completion.Closed += (_, _) => _completion = null;
        _completion.Show();
    }

    sealed class Item(string text, string title, string hint, bool call = false, double priority = 0) : ICompletionData
    {
        public Avalonia.Media.IImage? Image => null;
        public string Text => text;
        public object Content => title;
        public object Description => hint;
        public double Priority => priority;
        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            // Функция: вставляем «имя()» и ставим курсор между скобками.
            textArea.Document.Replace(completionSegment, text + (call ? "()" : ModScript.Keywords.Contains(text) ? " " : ""));
            if (call) textArea.Caret.Offset -= 1;
        }
    }

    // ---------------------------------------------------------------- подсветка

    sealed partial class Colorizer : DocumentColorizingTransformer
    {
        public HashSet<int> Errors = [];

        [GeneratedRegex(@"#.*$|""(?:\\.|[^""\\])*""?|\$\{?\w+\}?|\[[^\]\r\n]*\]|\b\d+(?:\.\d+)*\b|[=+\-*/%{}]|\b[A-Za-z_][\w.\-]*\b")]
        private static partial Regex Tokens();

        static IBrush B(string hex) => new SolidColorBrush(Color.Parse(hex));
        static readonly IBrush Keyword = B("#C792EA"), Str = B("#C3E88D"), Var = B("#82AAFF"), Num = B("#F78C6C"),
            Comment = B("#6B7385"), Section = B("#FFCB6B"), Op = B("#89DDFF"), Word = B("#E8EBF2"), Func = B("#7FDBCA");
        static readonly IBrush ErrorBg = new SolidColorBrush(Color.FromArgb(46, 255, 107, 107));
        static readonly HashSet<string> Words = new(ModScript.Keywords.Concat(["to", "from", "in"]), StringComparer.OrdinalIgnoreCase);

        protected override void ColorizeLine(DocumentLine line)
        {
            var text = CurrentContext.Document.GetText(line);
            if (Errors.Contains(line.LineNumber) && line.Length > 0)
                ChangeLinePart(line.Offset, line.EndOffset, el => el.BackgroundBrush = ErrorBg);
            var light = Look.IsLight;
            var first = true;
            foreach (Match m in Tokens().Matches(text))
            {
                var t = m.Value;
                IBrush? brush = t[0] switch
                {
                    '#' => Comment,
                    '"' => Str,
                    '$' => Var,
                    '[' => Section,
                    _ when char.IsDigit(t[0]) => Num,
                    _ when "=+-*/%{}".Contains(t[0]) && t.Length == 1 => Op,
                    _ when Words.Contains(t) && (first || t is "to" or "from" or "in" or "and" or "or" or "not" or "if" or "else") => Keyword,
                    _ when m.Index + m.Length < text.Length && text[m.Index + m.Length] == '(' => Func,
                    _ => light ? null : Word,
                };
                first = false;
                if (brush is null) continue;
                if (light && brush != Comment) brush = Darker(brush);
                var b = brush;
                ChangeLinePart(line.Offset + m.Index, line.Offset + m.Index + m.Length, el => el.TextRunProperties.SetForegroundBrush(b));
            }
        }

        static IBrush Darker(IBrush brush) => brush is SolidColorBrush s ? new SolidColorBrush(Look.Mix(s.Color, Colors.Black, 0.35)) : brush;
    }
}
