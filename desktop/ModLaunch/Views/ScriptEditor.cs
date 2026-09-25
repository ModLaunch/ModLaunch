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
            else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.Space) { ShowCompletion(""); e.Handled = true; }
        };
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
        // Подсказываем команду, пока печатается первое слово строки.
        if (Regex.IsMatch(before, @"^\s*[a-z]{1,8}$")) ShowCompletion(before.Trim());
        else if (e.Text == "$") ShowVariables();
    }

    void ShowCompletion(string typed)
    {
        if (_completion is not null) return;
        var items = ModScript.Keywords.Where(k => typed == "" || k.StartsWith(typed, StringComparison.OrdinalIgnoreCase)).ToList();
        if (items.Count == 0 || (items.Count == 1 && items[0] == typed)) return;
        Open(items.Select(k => (ICompletionData)new Item(k, I18n.Has($"cr.doc.{k}.syntax") ? I18n.T($"cr.doc.{k}.syntax") : k, I18n.Has($"cr.doc.{k}") ? I18n.T($"cr.doc.{k}") : "")), typed.Length);
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

    sealed class Item(string text, string title, string hint) : ICompletionData
    {
        public Avalonia.Media.IImage? Image => null;
        public string Text => text;
        public object Content => title;
        public object Description => hint;
        public double Priority => 0;
        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs) =>
            textArea.Document.Replace(completionSegment, text + (ModScript.Keywords.Contains(text) ? " " : ""));
    }

    // ---------------------------------------------------------------- подсветка

    sealed partial class Colorizer : DocumentColorizingTransformer
    {
        public HashSet<int> Errors = [];

        [GeneratedRegex(@"#.*$|""(?:\\.|[^""\\])*""?|\$\{?\w+\}?|\[[^\]\r\n]*\]|\b\d+(?:\.\d+)*\b|[=+\-*/%{}]|\b[A-Za-z_][\w.\-]*\b")]
        private static partial Regex Tokens();

        static IBrush B(string hex) => new SolidColorBrush(Color.Parse(hex));
        static readonly IBrush Keyword = B("#C792EA"), Str = B("#C3E88D"), Var = B("#82AAFF"), Num = B("#F78C6C"),
            Comment = B("#6B7385"), Section = B("#FFCB6B"), Op = B("#89DDFF"), Word = B("#E8EBF2");
        static readonly IBrush ErrorBg = new SolidColorBrush(Color.FromArgb(46, 255, 107, 107));
        static readonly HashSet<string> Words = new(ModScript.Keywords.Concat(["to", "from", "in"]), StringComparer.OrdinalIgnoreCase);

        protected override void ColorizeLine(DocumentLine line)
        {
            var text = CurrentContext.Document.GetText(line);
            if (Errors.Contains(line.LineNumber) && line.Length > 0)
                ChangeLinePart(line.Offset, line.EndOffset, el => el.BackgroundBrush = ErrorBg);
            var light = Look.Theme == "light";
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
                    _ when Words.Contains(t) && (first || t is "to" or "from" or "in") => Keyword,
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
