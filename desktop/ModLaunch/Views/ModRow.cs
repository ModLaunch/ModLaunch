using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>Строка мода в каталоге: картинка, название, описание, цифры и кнопка.</summary>
public static class ModRow
{
    public static Control Build(GameDef game, ModInfo mod, bool installed, bool installing, bool pick, Action install, Action? open = null)
    {
        var name = new TextBlock
        {
            FontSize = 17, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
            Inlines =
            {
                new Avalonia.Controls.Documents.Run(mod.Name),
                new Avalonia.Controls.Documents.Run("  " + (mod.Author == "" ? "" : I18n.T("mod.by", ("author", mod.Author)))) { FontSize = 13, Foreground = Ui.Res("Brand2") },
            },
        };
        var desc = new TextBlock { Text = mod.Description, Foreground = Ui.Res("Muted"), TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };

        var tags = Ui.Row(6);
        if (pick) tags.Children.Add(Tag(I18n.T("badge.pick"), Ui.Res("BrandSoft"), Ui.Res("Brand2")));
        if (game.IsLegacy(mod.UpdatedAt))
        {
            var old = Tag(I18n.T("badge.old"), Ui.Hex("#3A2A12"), Ui.Res("Warn"));
            ToolTip.SetTip(old, I18n.T("badge.old.hint"));
            tags.Children.Add(old);
        }
        if (mod.Source != game.PrimarySource) tags.Children.Add(Tag(Catalog.Title(mod.Source), Ui.Hex("#1B2A3A"), Ui.Hex("#7FB4E6")));
        foreach (var c in mod.Categories.Take(2)) tags.Children.Add(Tag(c, Ui.Res("Surface3"), Ui.Res("Muted")));

        var middle = Ui.Col(6, name, desc, tags);
        middle.VerticalAlignment = VerticalAlignment.Center;

        Button action;
        if (installed) action = Ui.Button(I18n.T("mod.installed"), () => { }, "", Icons.Check);
        else if (installing) action = Ui.Button(I18n.T("aside.installing"), () => { }, "primary");
        else action = Ui.Button(I18n.T("mod.install"), install, "primary", Icons.Download);
        action.IsEnabled = !installed && !installing;
        action.HorizontalAlignment = HorizontalAlignment.Right;

        var stats = Ui.Col(4, action);
        if (Social.Reviews.Stats().GetValueOrDefault($"{game.Id}|{mod.Id}") is { } rating) stats.Children.Add(Stat(Icons.Star, $"{rating.Avg:0.0} ({rating.Count})"));
        if (mod.Downloads > 0) stats.Children.Add(Stat(Icons.Download, I18n.Compact(mod.Downloads)));
        if (mod.UpdatedAt is not null) stats.Children.Add(Stat(Icons.Refresh, Ui.Ago(mod.UpdatedAt)));
        stats.VerticalAlignment = VerticalAlignment.Center;
        stats.MinWidth = 150;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 18 };
        var thumb = Ui.Thumb(mod.Icon, mod.Name, 88, 14, 200);
        grid.Children.Add(thumb);
        Grid.SetColumn(middle, 1);
        grid.Children.Add(middle);
        Grid.SetColumn(stats, 2);
        grid.Children.Add(stats);

        var card = new Border { Classes = { "card" }, Padding = new Thickness(14), Child = grid, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
        card.PointerPressed += (s, e) =>
        {
            if (e.Source is Visual v && Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<Button>(v, true) is not null) return;
            if (open is not null) open();
            else if (mod.Url is not null) Ui.OpenUrl(mod.Url);
        };
        return card;
    }

    static Control Stat(string icon, string text)
    {
        var row = Ui.Row(6, Ui.Icon(icon, 12, Ui.Res("Muted")), Ui.Text(text, "small muted"));
        row.HorizontalAlignment = HorizontalAlignment.Right;
        return row;
    }

    public static Border Tag(string text, IBrush bg, IBrush fg) => new()
    {
        Background = bg,
        CornerRadius = new CornerRadius(999),
        Padding = new Thickness(9, 3),
        Child = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = fg },
    };
}
