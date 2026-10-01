using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Views;

/// <summary>Разделы «Гайды» (пути от идеи до мода, чек-лист выпуска) и «Инструменты» (что скачать и зачем).</summary>
public sealed partial class CreatorPage
{
    string _guide = "script";
    string _toolGroup = "all";

    Control GuidesView()
    {
        // Слева — список путей, справа — шаги выбранного. Последний пункт — чек-лист выпуска.
        var list = Ui.Col(4);
        void Item(string id, string title, string icon)
        {
            var b = Ui.Button("", () => { _guide = id; Build(); }, "side");
            b.Content = Ui.Row(10, Ui.Icon(icon, 16), new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, MaxWidth = 232, VerticalAlignment = VerticalAlignment.Center });
            if (_guide == id) b.Classes.Add("active");
            list.Children.Add(b);
        }
        foreach (var g in Guides.All) Item(g.Id, g.Title, Icons.Book);
        Item("check", I18n.T("cr.guides.check"), Icons.Check);

        var detail = _guide == "check" ? Checklist() : GuideSteps(Guides.All.FirstOrDefault(g => g.Id == _guide) ?? Guides.All[0]);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("300,*"), ColumnSpacing = 22 };
        var listCard = Ui.Card(list, 10);
        listCard.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(listCard);
        Grid.SetColumn(detail, 1);
        grid.Children.Add(detail);
        return grid;
    }

    Control GuideSteps(Guide g)
    {
        var col = Ui.Col(12, Ui.Text(g.Title, "h2"), Ui.Text(g.Intro, "muted", wrap: true));
        var n = 0;
        foreach (var step in g.Steps)
        {
            n++;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 14 };
            row.Children.Add(new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = Ui.Res("Brand"), VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock { Text = n.ToString(), FontWeight = FontWeight.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
            var text = new TextBlock { Text = step.Text, TextWrapping = TextWrapping.Wrap, FontSize = 14.5, LineHeight = 22, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            col.Children.Add(Ui.Card(row, 16));
        }

        // Куда идти дальше: быстрые переходы по теме гайда.
        var go = Ui.Row(8);
        if (g.Id is "bepinex" or "smapi" or "hk") go.Children.Add(Ui.Button(I18n.T("cr.nav.code"), () => { _wizard = true; Go("code"); }, "primary", Icons.Code));
        if (g.Id == "script") go.Children.Add(Ui.Button(I18n.T("cr.nav.examples"), () => Go("examples"), "primary", Icons.Wand));
        if (g.Id is "model" or "textures" or "audio") go.Children.Add(Ui.Button(I18n.T("cr.nav.assets"), () => Go("assets"), "primary", Icons.Package));
        go.Children.Add(Ui.Button(I18n.T("cr.nav.snippets"), () => Go("snippets"), "", Icons.Layers));
        go.Children.Add(Ui.Button(I18n.T("cr.nav.tools"), () => Go("tools"), "", Icons.Tools));
        go.Children.Add(Ui.Button(I18n.T("cr.nav.pack"), () => Go("pack"), "", Icons.Upload));
        col.Children.Add(go);
        return col;
    }

    static HashSet<int> CheckedItems()
    {
        try { return (Settings.Data["crChecklist"] as JsonArray ?? []).Select(n => n!.GetValue<int>()).ToHashSet(); }
        catch { return []; }
    }

    Control Checklist()
    {
        var done = CheckedItems();
        var col = Ui.Col(12, Ui.Text(I18n.T("cr.guides.check"), "h2"), Ui.Text(I18n.T("cr.guides.check.lead"), "muted", wrap: true));
        var progress = Ui.Text("", "small", color: Ui.Res("Brand2"));
        void Refresh() => progress.Text = I18n.T("cr.guides.check.done", ("done", done.Count), ("total", Guides.Checklist.Length));
        Refresh();
        col.Children.Add(progress);
        for (var i = 0; i < Guides.Checklist.Length; i++)
        {
            var index = i;
            var box = new CheckBox { Content = new TextBlock { Text = Guides.Checklist[i].Text, TextWrapping = TextWrapping.Wrap, MaxWidth = 760 }, IsChecked = done.Contains(i) };
            box.IsCheckedChanged += (_, _) =>
            {
                if (box.IsChecked == true) done.Add(index); else done.Remove(index);
                Settings.Data["crChecklist"] = new JsonArray(done.Order().Select(x => (JsonNode)x).ToArray());
                Settings.Save();
                Refresh();
            };
            col.Children.Add(Ui.Card(box, 14));
        }
        return col;
    }

    Control ToolsView()
    {
        var chips = Ui.Row(6);
        void Chip(string id, string label, int n)
        {
            var chip = Ui.Button($"{label} · {n}", () => { _toolGroup = id; Build(); }, "chip");
            if (_toolGroup == id) chip.Classes.Add("active");
            chips.Children.Add(chip);
        }
        Chip("all", I18n.T("cr.cat.all"), Guides.Tools.Length);
        foreach (var g in Guides.ToolGroups) Chip(g, I18n.T("cr.tools.g." + g), Guides.Tools.Count(t => t.Group == g));

        var grid = new UniformGrid { Columns = 3 };
        foreach (var t in Guides.Tools.Where(t => (_toolGroup == "all" || t.Group == _toolGroup) && (_filter == "" || t.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase) || t.Desc.Contains(_filter, StringComparison.OrdinalIgnoreCase))))
        {
            var tool = t;
            var card = Ui.Card(Ui.Col(8,
                Ui.Row(10, new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(11), Background = Ui.Res("BrandSoft"), Child = Ui.Icon(ToolIcon(t.Group), 18, Ui.Res("Brand2")) },
                    Ui.Col(1, Ui.Text(t.Name, "h3"), Ui.Text(I18n.T("cr.tools.g." + t.Group), "small muted"))),
                new TextBlock { Text = t.Desc, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Foreground = Ui.Res("Muted"), MinHeight = 54 },
                Ui.Button(I18n.T("cr.tools.open"), () => Ui.OpenUrl(tool.Url), "", Icons.External)), 18);
            card.Margin = new Thickness(0, 0, 14, 14);
            grid.Children.Add(card);
        }
        return Ui.Col(16, new ScrollViewer { Content = chips, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }, grid);
    }

    static string ToolIcon(string group) => group switch
    {
        "code" => Icons.Code, "inspect" => Icons.Eye, "models" => Icons.Package, "textures" => Icons.Image,
        "audio" => Icons.Music, "docs" => Icons.Book, _ => Icons.Upload,
    };
}
