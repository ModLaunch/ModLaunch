using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Views;

/// <summary>Раздел «Библиотека кода»: готовые куски C# по платформам и свои сниппеты.</summary>
public sealed partial class CreatorPage
{
    string _snipPlatform = "all";

    Control SnippetsView()
    {
        var col = new StackPanel { Spacing = 16 };
        var all = Snippets.All();

        // Платформы и «мои» — чипами со счётчиками; справа кнопка нового сниппета.
        var chips = Ui.Row(6);
        void Chip(string id, string label, int n)
        {
            var chip = Ui.Button($"{label} · {n}", () => { _snipPlatform = id; Build(); }, "chip");
            if (_snipPlatform == id) chip.Classes.Add("active");
            chips.Children.Add(chip);
        }
        Chip("all", I18n.T("cr.snip.p.all"), all.Count);
        foreach (var p in Snippets.Platforms) Chip(p, I18n.T("cr.snip.p." + p), all.Count(s => s.Platform == p));
        Chip("own", I18n.T("cr.snip.p.own"), all.Count(s => s.Own));
        var bar = new DockPanel();
        var add = Ui.Button(I18n.T("cr.snip.add"), () => EditSnippet(null), "primary", Icons.Plus);
        DockPanel.SetDock(add, Dock.Right);
        bar.Children.Add(add);
        bar.Children.Add(new ScrollViewer { Content = chips, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        col.Children.Add(bar);

        var shown = all.Where(s => _snipPlatform switch { "all" => true, "own" => s.Own, var p => s.Platform == p })
            .Where(s => _filter == "" || s.Title.Contains(_filter, StringComparison.OrdinalIgnoreCase) || s.Desc.Contains(_filter, StringComparison.OrdinalIgnoreCase) || s.Code.Contains(_filter, StringComparison.OrdinalIgnoreCase)).ToList();
        if (shown.Count == 0)
        {
            col.Children.Add(Ui.Card(Ui.Col(8, Ui.Text(I18n.T("cr.snip.empty"), "h3"), Ui.Text(I18n.T("cr.snip.empty.text"), "muted", wrap: true)), 24));
            return col;
        }
        var grid = new UniformGrid { Columns = 2 };
        foreach (var s in shown)
        {
            var snip = s;
            var code = new SelectableTextBlock { Text = s.Code, FontFamily = Mono, FontSize = 12, Foreground = Ui.Res("Muted") };
            var codeBox = new Border
            {
                Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 10),
                Child = new ScrollViewer { Content = code, MaxHeight = 190, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto },
            };
            var head = new DockPanel();
            var badge = new Border { Background = Ui.Res("BrandSoft"), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 2), VerticalAlignment = VerticalAlignment.Top,
                Child = Ui.Text(I18n.T("cr.snip.p." + (s.Own ? "own" : s.Platform)), "small", color: Ui.Res("Brand2")) };
            DockPanel.SetDock(badge, Dock.Right);
            head.Children.Add(badge);
            head.Children.Add(Ui.Text(s.Title, "h3"));
            var buttons = Ui.Row(8, Ui.Button(I18n.T("cr.snip.copy"), () => Copy(snip.Code, I18n.T("cr.snip.copied")), "primary", Icons.Save));
            if (s.Own)
            {
                buttons.Children.Add(Ui.Button(I18n.T("cr.snip.edit"), () => EditSnippet(snip), "", Icons.Edit));
                buttons.Children.Add(Ui.Button("", () => { Snippets.Remove(snip.Id); Build(); }, "icon ghost", Icons.Trash, I18n.T("cr.delete")));
            }
            else buttons.Children.Add(Ui.Button(I18n.T("cr.snip.saveOwn"), () => { Snippets.Save(null, snip.Platform, snip.Title, snip.Desc, snip.Code); _snipPlatform = "own"; Build(); }, "ghost", Icons.Plus));
            var card = Ui.Card(Ui.Col(10, head, new TextBlock { Text = s.Desc, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Foreground = Ui.Res("Muted") }, codeBox, buttons), 18);
            card.Margin = new Thickness(0, 0, 14, 14);
            grid.Children.Add(card);
        }
        col.Children.Add(grid);
        return col;
    }

    /// <summary>Окно «свой сниппет»: название, платформа, описание и сам код.</summary>
    void EditSnippet(Snippet? existing)
    {
        var w = MainWindow.Current!;
        var platform = existing?.Platform ?? (_snipPlatform is "all" or "own" ? "unity" : _snipPlatform);
        var title = Box(existing?.Title ?? "", I18n.T("cr.snip.f.title"), max: 80);
        var desc = Box(existing?.Desc ?? "", I18n.T("cr.snip.f.desc"), max: 240);
        var code = Box(existing?.Code ?? "", I18n.T("cr.snip.f.code"), multi: true, max: 20000);
        code.MinHeight = 220;
        code.FontFamily = Mono;
        var plat = new WrapPanel();
        void Plats()
        {
            plat.Children.Clear();
            foreach (var p in Snippets.Platforms)
            {
                var id = p;
                var chip = Ui.Button(I18n.T("cr.snip.p." + p), () => { platform = id; Plats(); }, "chip");
                chip.Margin = new Thickness(0, 0, 6, 6);
                if (platform == id) chip.Classes.Add("active");
                plat.Children.Add(chip);
            }
        }
        Plats();
        var body = new StackPanel { Spacing = 12, Width = 620, Children = { Field(I18n.T("cr.snip.f.titleLabel"), title), plat, Field(I18n.T("cr.snip.f.descLabel"), desc), Field(I18n.T("cr.snip.f.codeLabel"), code) } };
        w.Dialog(I18n.T(existing is null ? "cr.snip.add" : "cr.snip.edit"), body,
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("cr.save"), () =>
            {
                if ((title.Text ?? "").Trim() == "" || (code.Text ?? "").Trim() == "") { w.Toast(I18n.T("cr.snip.needs"), bad: true); return; }
                Snippets.Save(existing?.Id, platform, title.Text!, desc.Text ?? "", code.Text!);
                w.CloseDialog();
                _snipPlatform = "own";
                Build();
            }, "primary", Icons.Save));
    }
}
