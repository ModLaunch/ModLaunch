using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Minecraft;
using ModLaunch.Mods;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>
/// 20.0.1: любой мод, который видно в программе, открывается нажатием — его описание. Мод из каталога
/// (установленный или нет) — страница мода; мод из файла или из Minecraft-сборки — окно со сведениями,
/// какие есть: описание, автор, версия, папка.
/// </summary>
public static class ModView
{
    /// <summary>Сделать карточку или строку нажимаемой: курсор-рука, нажатие мимо кнопок открывает описание.</summary>
    public static T Clickable<T>(T c, Action open, string? tip = null) where T : Control
    {
        c.Cursor = new Cursor(StandardCursorType.Hand);
        StoreKit.OnClick(c, open);
        if (tip is not null) ToolTip.SetTip(c, tip);
        return c;
    }

    /// <summary>Описание установленного мода: из каталога — страница мода, из файла — окно со сведениями.</summary>
    public static void Open(GameState g, JsonObject record)
    {
        if (CatalogInfo(g, record) is { } info)
        {
            var id = g.Def.Id;
            MainWindow.Current?.CloseDialog();
            MainWindow.Current?.Navigate(() => new ModPage(id, info));
            return;
        }
        Local(g, record);
    }

    /// <summary>Мод каталога для записи об установленном моде (null — мод из файла, пресет или чужой каталог).</summary>
    public static ModInfo? CatalogInfo(GameState g, JsonObject record)
    {
        var id = record.Str("id");
        if (id is null || record.Str("kind") == "preset" || record.Str("source") is null or "file" || !g.Def.HasCatalog) return null;
        if (g.Def.CatalogId(id) is not { } catalogId) return null;
        return new ModInfo
        {
            Source = g.Def.SourceOf(id, record.Str("source")), Id = catalogId,
            Name = record.Str("displayName") is { Length: > 0 } dn ? dn : record.Str("name") ?? catalogId,
            Author = record.Str("author") ?? "", Version = record.Str("version") ?? "", Icon = record.Str("icon"), Url = record.Str("url"),
            Categories = record["categories"] is JsonArray cats ? cats.Select(c => c?.ToString() ?? "").Where(c => c != "").ToArray() : [],
        };
    }

    /// <summary>Мод из каталога по номеру и источнику (например, из подборки или коллекции).</summary>
    public static void Open(string gameId, ModInfo mod)
    {
        MainWindow.Current?.CloseDialog();
        MainWindow.Current?.Navigate(() => new ModPage(gameId, mod));
    }

    /// <summary>Окно со сведениями о моде, которого нет в каталоге (поставлен из файла).</summary>
    static void Local(GameState g, JsonObject record)
    {
        var w = MainWindow.Current;
        if (w is null) return;
        var name = record.Str("displayName") is { Length: > 0 } dn ? dn : record.Str("name") ?? record.Str("id") ?? "?";
        var facts = new List<(string, string)>();
        if (record.Str("version") is { Length: > 0 } v) facts.Add((I18n.T("aside.version"), v));
        if (record.Str("author") is { Length: > 0 } a) facts.Add((I18n.T("aside.author"), a));
        facts.Add((I18n.T("aside.source"), record.Str("kind") == "preset" ? "ReShade" : I18n.T("v20.mod.fromFile")));
        if (DateTime.TryParse(record.Str("installedAt"), out var at)) facts.Add((I18n.T("v20.mod.installedAt"), Ui.Ago(at.ToUniversalTime())));
        var text = Notes.Get(g.Def.Id, record.Str("id") ?? "") is { Length: > 0 } note ? note : I18n.T("v20.mod.local.text");
        string? folder = null;
        try { folder = g.Registry?.FolderFor(record); } catch { }
        Show(w, name, record.Str("icon"), g.Def.Name, text, facts, folder, record.Str("url"));
    }

    /// <summary>Мод Minecraft-сборки: описание и сведения из файла мода, страница на Modrinth, если она известна.</summary>
    public static void Open(McItem item, string folder)
    {
        var w = MainWindow.Current;
        if (w is null) return;
        var facts = new List<(string, string)>();
        if (item.Version is { Length: > 0 } v) facts.Add((I18n.T("aside.version"), v));
        if (item.Author is { Length: > 0 } a) facts.Add((I18n.T("aside.author"), a));
        facts.Add((I18n.T("aside.source"), item.ProjectId is null ? I18n.T("v20.mod.fromFile") : "Modrinth"));
        facts.Add((I18n.T("v20.mod.file"), item.Key));
        var text = item.Description is { Length: > 0 } d ? d : I18n.T("v20.mod.local.text");
        Show(w, item.Name, item.Icon, "Minecraft", text, facts, folder, item.Url);
    }

    static void Show(MainWindow w, string name, string? icon, string game, string text, List<(string Key, string Value)> facts, string? folder, string? url)
    {
        var head = Ui.Row(14, Ui.Thumb(icon, name, 64, 16, 128),
            Ui.Col(4, new TextBlock { Text = name, FontSize = 20, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 },
                new TextBlock { Text = game, FontSize = 13, Foreground = Ui.Res("Faint") }));
        foreach (var c in head.Children) c.VerticalAlignment = VerticalAlignment.Center;
        var rows = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 16, RowSpacing = 8 };
        foreach (var (k, v) in facts)
        {
            var r = rows.RowDefinitions.Count;
            rows.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var key = new TextBlock { Text = k, FontSize = 13.5, Foreground = Ui.Res("Faint") };
            Grid.SetRow(key, r);
            rows.Children.Add(key);
            var value = new SelectableTextBlock { Text = v, FontSize = 13.5, TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(value, r);
            Grid.SetColumn(value, 1);
            rows.Children.Add(value);
        }
        var body = Ui.Col(16, head,
            new SelectableTextBlock { Text = text, FontSize = 14, LineHeight = 21, Foreground = Ui.Res("Muted"), TextWrapping = TextWrapping.Wrap },
            new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 12), Child = rows });
        var actions = new List<Control>();
        if (url is not null) actions.Add(Ui.Button(I18n.T("mod.page"), () => Ui.OpenUrl(url), "ghost", Icons.External));
        if (folder is not null) actions.Add(Ui.Button(I18n.T("v4.folder"), () => Actions.OpenFolder(folder), "", Icons.Folder));
        actions.Add(Ui.Button(I18n.T("common.close"), w.CloseDialog, "primary"));
        w.Dialog(I18n.T("v20.mod.about"), new ScrollViewer { MaxHeight = 520, Content = body }, 560, [.. actions]);
    }
}
