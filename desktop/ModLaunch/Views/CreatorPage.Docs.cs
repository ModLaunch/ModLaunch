using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Views;

public sealed partial class CreatorPage
{
    // ---------------------------------------------------------------- справка

    /// <summary>Справка по ModScript: слева оглавление, справа разделы.</summary>
    static Control Docs()
    {
        var sections = new StackPanel { Spacing = 12 };
        var toc = new StackPanel { Spacing = 2 };
        var intro = Ui.Card(Ui.Col(8, Ui.Text(I18n.T("cr.docs.title"), "h3"), Ui.Text(I18n.T("cr.docs.intro"), "muted", wrap: true)), 20);
        sections.Children.Add(intro);
        toc.Children.Add(TocItem(I18n.T("cr.docs.title"), intro));
        foreach (var group in new[]
        {
            ("cr.docs.basics", new[] { "mod", "version", "author", "about", "game", "icon", "needs" }),
            ("cr.docs.logic", new[] { "let", "if", "else", "for", "when", "print" }),
            ("cr.docs.code", new[] { "expr", "def", "return", "while", "break", "import", "listcomp", "method" }),
            ("cr.docs.stardew", new[] { "edit", "entry", "dialogue", "mail", "image" }),
            ("cr.docs.bepinex", new[] { "config", "ini", "copy", "write", "json" }),
        })
        {
            var rows = new StackPanel { Spacing = 10 };
            foreach (var cmd in group.Item2)
                rows.Children.Add(Ui.Col(3,
                    new SelectableTextBlock { Text = I18n.T($"cr.doc.{cmd}.syntax"), FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"), FontSize = 13, Foreground = Ui.Res("Brand2") },
                    Ui.Text(I18n.T($"cr.doc.{cmd}"), "small muted", wrap: true)));
            var card = Ui.Card(Ui.Col(12, Ui.Text(I18n.T(group.Item1), "h3"), rows), 20);
            sections.Children.Add(card);
            toc.Children.Add(TocItem(I18n.T(group.Item1), card));
        }
        // Стандартная библиотека: все функции по группам, с поиском.
        var lib = LibraryDocs();
        sections.Children.Add(lib);
        toc.Children.Add(TocItem(I18n.T("cr.lib.title", ("n", ModScript.LibraryList.Count)), lib));
        var left = Ui.Col(8, Ui.Text(I18n.T("cr.docs.toc"), "small muted"), toc);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*"), ColumnSpacing = 24 };
        grid.Children.Add(left);
        Grid.SetColumn(sections, 1);
        grid.Children.Add(sections);
        return grid;
    }

    static Control LibraryDocs()
    {
        var mono = new FontFamily("Cascadia Mono, Consolas, monospace");
        var list = new StackPanel { Spacing = 16 };
        var search = new TextBox { Watermark = I18n.T("cr.lib.search"), Width = 360, HorizontalAlignment = HorizontalAlignment.Left };
        var groups = ModScript.LibraryList.Select(f => f.Group).Distinct().ToList();
        string? group = null;
        var chips = new WrapPanel();
        void Fill()
        {
            list.Children.Clear();
            var q = (search.Text ?? "").Trim();
            foreach (var g in ModScript.LibraryList.Where(f => (group is null || f.Group == group)
                         && (q == "" || f.Names.Any(n => n.Contains(q, StringComparison.OrdinalIgnoreCase)) || f.Doc.Contains(q, StringComparison.OrdinalIgnoreCase)))
                     .GroupBy(f => f.Group))
            {
                var rows = new StackPanel { Spacing = 8 };
                foreach (var f in g)
                {
                    var names = string.Join("  ·  ", f.Names.Skip(1).Take(5));
                    var row = Ui.Col(2,
                        new SelectableTextBlock { Text = f.Sig, FontFamily = mono, FontSize = 13, Foreground = Ui.Res("Brand2") },
                        Ui.Text(f.Doc, "small", wrap: true));
                    if (names != "") row.Children.Add(new TextBlock { Text = I18n.T("cr.lib.alias") + " " + names, FontFamily = mono, FontSize = 11, Foreground = Ui.Res("Faint"), TextWrapping = TextWrapping.Wrap });
                    rows.Children.Add(row);
                }
                list.Children.Add(Ui.Col(8, Ui.Text(g.Key, "h3"), rows));
            }
            if (list.Children.Count == 0) list.Children.Add(Ui.Text(I18n.T("cr.lib.none"), "muted"));
        }
        void Chips()
        {
            chips.Children.Clear();
            foreach (var g in new string?[] { null }.Concat(groups))
            {
                var id = g;
                var chip = Ui.Button(g ?? I18n.T("mk.kind.all"), () => { group = id; Chips(); Fill(); }, "chip");
                chip.Margin = new Thickness(0, 0, 6, 6);
                chip.Padding = new Thickness(10, 4);
                chip.FontSize = 12;
                if (group == g) chip.Classes.Add("active");
                chips.Children.Add(chip);
            }
        }
        search.TextChanged += (_, _) => Fill();
        Chips();
        Fill();
        return Ui.Card(Ui.Col(12,
            Ui.Text(I18n.T("cr.lib.title", ("n", ModScript.LibraryList.Count)), "h3"),
            Ui.Text(I18n.T("cr.lib.intro"), "muted", wrap: true),
            search, chips, list), 20);
    }

    /// <summary>Пункт оглавления: прокручивает страницу к разделу.</summary>
    static Control TocItem(string title, Control target)
    {
        var b = Ui.Button("", () => target.BringIntoView(), "tab");
        b.Content = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap };
        b.HorizontalAlignment = HorizontalAlignment.Stretch;
        b.HorizontalContentAlignment = HorizontalAlignment.Left;
        return b;
    }
}