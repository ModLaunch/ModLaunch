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
    /// <summary>Значок «Хит» / «Лучшее» / «Новое», как в ModLaunch 3.</summary>
    public static Control? Badge(string? kind) => kind switch
    {
        "hit" => Tag("🔥 " + I18n.T("badge.hit"), Ui.Hex("#3A1C12"), Ui.Hex("#FF8A5B")),
        "best" => Tag("🏆 " + I18n.T("badge.best"), Ui.Hex("#3A3212"), Ui.Hex("#F2C25C")),
        "new" => Tag("✦ " + I18n.T("badge.new"), Ui.Hex("#123A26"), Ui.Hex("#5BD68F")),
        _ => null,
    };

    public static Control Build(GameDef game, ModInfo mod, bool installed, bool installing, bool pick, Action install, Action? open = null, string? badge = null)
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
        var compact = Settings.Data.Bool("compactLists");
        var desc = new TextBlock { Text = mod.Description, Foreground = Ui.Res("Muted"), TextWrapping = TextWrapping.Wrap, MaxLines = compact ? 1 : 2, TextTrimming = TextTrimming.CharacterEllipsis };

        var tags = Ui.Row(6);
        if (Badge(badge) is { } b) tags.Children.Add(b);
        if (mod.Adult) tags.Children.Add(Tag("18+", Ui.Hex("#3A1216"), Ui.Hex("#FF6B6B")));
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
        var thumb = Ui.Thumb(mod.Icon, mod.Name, compact ? 52 : 88, compact ? 10 : 14, 200);
        grid.Children.Add(thumb);
        Grid.SetColumn(middle, 1);
        grid.Children.Add(middle);
        Grid.SetColumn(stats, 2);
        grid.Children.Add(stats);

        var card = new Border { Classes = { "card" }, Padding = new Thickness(compact ? 9 : 14), Child = grid, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
        card.PointerPressed += (s, e) =>
        {
            if (e.Source is Visual v && Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<Button>(v, true) is not null) return;
            if (open is not null) open();
            else if (mod.Url is not null) Ui.OpenUrl(mod.Url);
        };
        return card;
    }

    /// <summary>Плитка мода для вида «сеткой» (как в Modrinth): большая картинка, имя, автор, загрузки.</summary>
    public static Control Tile(GameDef game, ModInfo mod, bool installed, bool installing, Action install, Action open, string? badge = null)
    {
        var picture = new Border { Height = 132, CornerRadius = new CornerRadius(12, 12, 0, 0), ClipToBounds = true, Child = Ui.Thumb(mod.Icon, mod.Name, 236, 0, 480) };
        var layers = new Panel { Children = { picture } };
        if (Badge(badge) is { } b)
        {
            b.Margin = new Thickness(8);
            b.HorizontalAlignment = HorizontalAlignment.Left;
            b.VerticalAlignment = VerticalAlignment.Top;
            layers.Children.Add(b);
        }
        Button action;
        if (installed) action = Ui.Button("", () => { }, "icon", Icons.Check, I18n.T("mod.installed"));
        else action = Ui.Button("", install, "icon primary", Icons.Download, I18n.T("mod.install"));
        action.IsEnabled = !installed && !installing;
        action.VerticalAlignment = VerticalAlignment.Center;
        var foot = new DockPanel();
        DockPanel.SetDock(action, Dock.Right);
        foot.Children.Add(action);
        foot.Children.Add(Ui.Col(1,
            new TextBlock { Text = mod.Name, FontWeight = FontWeight.SemiBold, FontSize = 14.5, TextTrimming = TextTrimming.CharacterEllipsis },
            Ui.Text((mod.Author == "" ? "" : mod.Author + " · ") + (mod.Downloads > 0 ? "↓ " + I18n.Compact(mod.Downloads) : ""), "small muted")));
        var body = Ui.Col(8, foot, new TextBlock { Text = mod.Description, FontSize = 12.5, Foreground = Ui.Res("Muted"), TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, Height = 34 });
        body.Margin = new Thickness(12, 10, 12, 12);
        var card = new Button { Classes = { "card-btn" }, Width = 238, Padding = new Thickness(0), Margin = new Thickness(0, 0, 14, 14), VerticalContentAlignment = VerticalAlignment.Top, Content = Ui.Col(0, layers, body) };
        card.Click += (_, _) => open();
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
