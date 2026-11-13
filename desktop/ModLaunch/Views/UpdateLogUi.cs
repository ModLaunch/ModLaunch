using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>
/// 27.0: журнал обновлений модов — окно со списком «что, когда, было → стало, сам или по кнопке» и текстом изменений
/// от автора; плашка «за неделю обновлено N модов» на вкладке установленных модов игры.
/// </summary>
public static class UpdateLogUi
{
    /// <summary>Окно журнала: одной игры (gameId) или всех.</summary>
    public static void Show(string? gameId = null)
    {
        if (MainWindow.Current is not { } w) return;
        var body = new ContentControl();
        void Render()
        {
            var entries = UpdateLog.All(gameId);
            if (entries.Count == 0)
            {
                body.Content = Ui.Col(6, Ui.Text(I18n.T("v27.log.empty"), "h3"), Ui.Text(I18n.T("v27.log.empty.text"), "muted", wrap: true));
                return;
            }
            var list = new StackPanel { Spacing = 8 };
            string? day = null;
            foreach (var e in entries.Take(150))
            {
                var local = e.At.ToLocalTime();
                var dayText = Day(local);
                if (dayText != day)
                {
                    day = dayText;
                    list.Children.Add(new TextBlock { Text = dayText.ToUpper(I18n.Culture), FontSize = 10, FontWeight = FontWeight.SemiBold, LetterSpacing = 1, Foreground = Ui.Res("Faint"), Margin = new Thickness(2, list.Children.Count == 0 ? 0 : 8, 0, 0) });
                }
                list.Children.Add(Row(e, gameId is null));
            }
            body.Content = new ScrollViewer { MaxHeight = 520, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = list };
        }
        void Changed() => Dispatcher.UIThread.Post(Render);
        UpdateLog.Changed += Changed;
        body.DetachedFromVisualTree += (_, _) => UpdateLog.Changed -= Changed;
        Render();
        w.Dialog(I18n.T("v27.log.title"), body, 640,
            Ui.Button(I18n.T("v27.log.clear"), () => ConfirmClear(gameId), "ghost", Icons.Trash),
            Ui.Button(I18n.T("common.close"), w.CloseDialog, "primary"));
    }

    static void ConfirmClear(string? gameId)
    {
        var w = MainWindow.Current!;
        w.Dialog(I18n.T("v27.log.clear.confirm"), Ui.Text(I18n.T("v27.log.clear.text"), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), () => Show(gameId)),
            Ui.Button(I18n.T("v27.log.clear"), () => { UpdateLog.Clear(gameId); w.CloseDialog(); AppState.Notify(); }, "primary", Icons.Trash));
    }

    static string Day(DateTime local)
    {
        var days = (DateTime.Today - local.Date).Days;
        return days <= 0 ? I18n.T("v14.steam.today") : days == 1 ? I18n.T("v14.steam.yesterday") : local.ToString(local.Year == DateTime.Today.Year ? "d MMMM" : "d MMMM yyyy", I18n.Culture);
    }

    static Control Row(UpdateEntry e, bool showGame)
    {
        var title = Ui.Row(8, Ui.Text(e.Name, "h3"), Ui.Text(e.From == "" ? e.To : $"{e.From} → {e.To}", "small muted"));
        if (e.Auto) title.Children.Add(ModRow.Tag(I18n.T("v27.log.auto"), Ui.Res("BrandSoft"), Ui.Res("Brand2")));
        var meta = e.At.ToLocalTime().ToString("t", I18n.Culture) + (showGame ? " · " + e.GameName : "");
        var col = Ui.Col(6, title, Ui.Text(meta, "tiny muted"));
        col.Children.Add(e.Changelog switch
        {
            null => Ui.Text(I18n.T("v27.log.loading"), "small muted"),
            "" => Ui.Text(I18n.T("v27.log.noNotes"), "small muted", wrap: true),
            var text => new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Foreground = Ui.Res("Text"), MaxHeight = 220 },
        });
        var row = new DockPanel();
        if (e.Url is { Length: > 0 } url && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var open = Ui.Button("", () => Ui.OpenUrl(url), "icon ghost", Icons.External, I18n.T("v27.log.page"));
            open.VerticalAlignment = VerticalAlignment.Top;
            DockPanel.SetDock(open, Dock.Right);
            row.Children.Add(open);
        }
        row.Children.Add(col);
        return new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10), Child = row };
    }

    /// <summary>Плашка на вкладке модов: что обновилось за неделю (null — ничего).</summary>
    public static Control? RecentNotice(GameState g)
    {
        var recent = UpdateLog.Recent(g.Def.Id, 7);
        if (recent.Count == 0) return null;
        var names = string.Join(", ", recent.Select(e => e.Name).Distinct().Take(4));
        var text = I18n.T("v27.log.recent." + I18n.Plural(recent.Count, "one", "few", "many"), ("n", recent.Count), ("list", names));
        var open = Ui.Button(I18n.T("v27.log.what"), () => Show(g.Def.Id), "", Icons.List);
        DockPanel.SetDock(open, Dock.Right);
        open.VerticalAlignment = VerticalAlignment.Center;
        var info = Ui.Row(10, Ui.Icon(Icons.Sparkles, 16, Ui.Res("Brand2")), new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13, MaxWidth = 820, VerticalAlignment = VerticalAlignment.Center });
        info.VerticalAlignment = VerticalAlignment.Center;
        return new Border
        {
            Background = Ui.Res("Surface"), BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 8, 8, 8),
            Child = new DockPanel { Children = { open, info } },
        };
    }
}
