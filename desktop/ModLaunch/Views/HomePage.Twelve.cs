using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>
/// 12.0: главная — правая кнопка мыши. По пустому месту — «Добавить виджет» (сразу с размером),
/// по виджету — размер, сдвиг, убрать и «добавить рядом». Стрелки ← → листают игры на сцене,
/// виджет «Путь» показывает лор-стадию каждой игры.
/// </summary>
public sealed partial class HomePage
{
    /// <summary>Игра на сцене этой главной (для Ctrl+Enter); null — сцены сейчас нет.</summary>
    public string? ShownStageGame => _stageHost?.Parent is not null ? _stage : null;

    /// <summary>Следующая/предыдущая игра на сцене (стрелки). false — сцены нет.</summary>
    public bool StepStage(int delta)
    {
        if (_stageHost is null) return false;
        var mine = MyGames();
        if (mine.Count < 2) return false;
        var at = Math.Max(0, mine.FindIndex(g => g.Def.Id == _stage));
        _stage = mine[((at + delta) % mine.Count + mine.Count) % mine.Count].Def.Id;
        _stageHost.Child = Stage(mine, Settings.Data.Str("homeHero") == "compact");
        if (Animate.On) Animate.From(_stageHost, delta > 0 ? "translateX(8px)" : "translateX(-8px)", 220, 0, null, 0.7);
        return true;
    }

    static MenuItem Item(string text, string icon, Action run)
    {
        var m = new MenuItem { Header = text, Icon = Ui.Icon(icon, 14) };
        m.Click += (_, _) => Guard.Later(run);
        return m;
    }

    static string IconOf(Widgets.Kind k) => typeof(Icons).GetField(k.Icon)?.GetValue(null) as string ?? Icons.Grid;

    static string SizeName(string size) => I18n.T("v12.w.size." + size);

    /// <summary>«Добавить виджет ›» — каждый вид, у кого несколько размеров — подменю размеров.</summary>
    static MenuItem AddMenu(int at)
    {
        var add = new MenuItem { Header = I18n.T("v10.w.add"), Icon = Ui.Icon(Icons.Plus, 14) };
        foreach (var k in Widgets.Catalog)
        {
            var name = I18n.T("v10.w.name." + k.Id);
            if (k.Sizes.Length == 1) { add.Items.Add(Item(name, IconOf(k), () => Widgets.Add(k.Id, k.Sizes[0], at))); continue; }
            var sub = new MenuItem { Header = name, Icon = Ui.Icon(IconOf(k), 14) };
            foreach (var size in k.Sizes)
            {
                var s = size;
                sub.Items.Add(Item(SizeName(s), Icons.Maximize, () => Widgets.Add(k.Id, s, at)));
            }
            add.Items.Add(sub);
        }
        return add;
    }

    /// <summary>Правая кнопка по пустому месту главной.</summary>
    MenuFlyout AreaMenu()
    {
        var menu = new MenuFlyout();
        menu.Items.Add(AddMenu(-1));
        menu.Items.Add(Item(_editWidgets ? I18n.T("v10.w.done") : I18n.T("v10.w.edit"), _editWidgets ? Icons.Check : Icons.Grid, () => { _editWidgets = !_editWidgets; Build(); }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(I18n.T("v10.cfg.short"), Icons.Palette, () => MainWindow.Current?.Navigate(() => new SettingsPage("configs"))));
        menu.Items.Add(Item(I18n.T("v10.w.reset"), Icons.Undo, Widgets.Reset));
        return menu;
    }

    /// <summary>Правая кнопка по виджету: размер, сдвиг, убрать, добавить рядом.</summary>
    MenuFlyout WidgetMenu(int index, string id, string size, int count)
    {
        var menu = new MenuFlyout();
        if (Widgets.Find(id) is { Sizes.Length: > 1 } k)
        {
            var sizes = new MenuItem { Header = I18n.T("v10.w.size"), Icon = Ui.Icon(Icons.Maximize, 14) };
            foreach (var s in k.Sizes)
            {
                var one = s;
                sizes.Items.Add(Item((one == size ? "✓  " : "") + SizeName(one), Icons.Maximize, () => Widgets.SetSize(index, one)));
            }
            menu.Items.Add(sizes);
        }
        if (index > 0) menu.Items.Add(Item(I18n.T("v10.w.left"), Icons.ChevronLeft, () => Widgets.Move(index, -1)));
        if (index < count - 1) menu.Items.Add(Item(I18n.T("v10.w.right"), Icons.ChevronRight, () => Widgets.Move(index, 1)));
        menu.Items.Add(AddMenu(index + 1));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(I18n.T("v10.w.remove"), Icons.Close, () => Widgets.Remove(index)));
        return menu;
    }

    /// <summary>Виджет «Путь»: стадия лора по каждой игре — четыре точки и название стадии.</summary>
    Control LoreWidget(string size, List<GameState> mine)
    {
        var rows = Ui.Col(size == "l" ? 9 : 6);
        foreach (var g in mine.Take(size == "l" ? 6 : 2))
        {
            var d = Lore.Of(g.Def);
            var accent = Color.TryParse(g.Def.Accent, out var c) ? c : Gx.Accent;
            var dots = Ui.Row(3);
            for (var i = 0; i < 4; i++)
                dots.Children.Add(new Border { Width = 14, Height = 4, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center, Background = i <= d.Stage ? new SolidColorBrush(accent) : Ui.Res("Surface3") });
            var name = new TextBlock { Text = g.Def.ShortName, FontSize = 12.5, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            var stage = new TextBlock { Text = Lore.StageName(g.Def, d.Stage), FontSize = 12, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            var row = new DockPanel();
            DockPanel.SetDock(dots, Dock.Right);
            row.Children.Add(dots);
            row.Children.Add(Ui.Row(0, name, stage));
            rows.Children.Add(row);
        }
        if (mine.Count == 0) rows.Children.Add(Small(I18n.T("home.noGames")));
        return Shell(Stack(Cap(Icons.Sparkles, I18n.T("v10.w.name.lore")), rows), () => MainWindow.Current?.Navigate(() => new ProfilePage()));
    }
}
