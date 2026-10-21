using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Games;

namespace ModLaunch.Views;

/// <summary>
/// 12.0: главная — правая кнопка мыши. По пустому месту — «Добавить виджет…» (галерея как на iPhone:
/// вид, размер, настройки, живой предпросмотр) и быстрое добавление; по виджету — «Изменить виджет…»,
/// размер, игра, цвет, сдвиг, убрать и «добавить рядом». Стрелки ← → листают игры на сцене.
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
        var add = new MenuItem { Header = I18n.T("v12w.quick"), Icon = Ui.Icon(Icons.Zap, 14) };
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
        menu.Items.Add(Item(I18n.T("v12w.add") + "…", Icons.Plus, () => OpenStudio()));
        menu.Items.Add(AddMenu(-1));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(_editWidgets ? I18n.T("v10.w.done") : I18n.T("v10.w.edit"), _editWidgets ? Icons.Check : Icons.Grid, () => { _editWidgets = !_editWidgets; Build(); }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(I18n.T("v10.cfg.short"), Icons.Palette, () => MainWindow.Current?.Navigate(() => new SettingsPage("configs"))));
        menu.Items.Add(Item(I18n.T("v10.w.reset"), Icons.Undo, Widgets.Reset));
        return menu;
    }

    /// <summary>Правая кнопка по виджету: изменить, размер, игра и цвет, сдвиг, убрать, добавить рядом.</summary>
    MenuFlyout WidgetMenu(int index, Widgets.Entry e, int count)
    {
        var (id, size) = (e.Id, e.Size);
        var menu = new MenuFlyout();
        menu.Items.Add(Item(I18n.T("v12w.edit") + "…", Icons.Sliders, () => OpenStudio(edit: index)));
        var kind = Widgets.Find(id);
        // Быстро сменить игру — без галереи: свои игры и игры Steam.
        if (kind?.Options.Any(o => o.Type == "game") == true)
        {
            var games = new MenuItem { Header = I18n.T("v12w.opt.game"), Icon = Ui.Icon(Icons.Gamepad, 14) };
            var current = e.Get("game");
            foreach (var (value, label) in GameChoices(id).Take(24))
            {
                var v = value;
                games.Items.Add(Item((v == current ? "✓  " : "") + label, v.StartsWith("steam:") ? Icons.Layers : Icons.Gamepad, () => SetOpt(index, e, "game", v)));
            }
            menu.Items.Add(games);
        }
        if (kind?.Options.Any(o => o.Key == "tint") == true)
        {
            var colors = new MenuItem { Header = I18n.T("v12w.opt.tint"), Icon = Ui.Icon(Icons.Palette, 14) };
            foreach (var t in Widgets.Tints)
            {
                var one = t;
                colors.Items.Add(Item((one == e.Get("tint") ? "✓  " : "") + I18n.T("v12w.tint." + one), Icons.Palette, () => SetOpt(index, e, "tint", one)));
            }
            menu.Items.Add(colors);
        }
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
        menu.Items.Add(Item(I18n.T("v12w.addNear") + "…", Icons.Plus, () => OpenStudio(at: index + 1)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(I18n.T("v10.w.remove"), Icons.Close, () => Widgets.Remove(index)));
        return menu;
    }

    static void SetOpt(int index, Widgets.Entry e, string key, string value)
    {
        var opts = new Dictionary<string, string>(e.Opts) { [key] = value };
        Widgets.Update(index, e.Size, opts);
    }

    /// <summary>
    /// Игры для настройки «Игра»: «авто» (если вид его понимает), свои игры ModLaunch, потом все установленные
    /// игры Steam, которых в ModLaunch нет (steam:номер).
    /// </summary>
    static List<(string Value, string Label)> GameChoices(string kind)
    {
        var list = new List<(string, string)>();
        if (kind != "game") list.Add(("", I18n.T("v12w.game.auto")));
        foreach (var g in MyGames()) list.Add((g.Def.Id, g.Def.Name));
        foreach (var a in SteamLibrary.Sorted("recent"))
            if (SteamLibrary.Managed(a.AppId) is null) list.Add(("steam:" + a.AppId, "Steam · " + a.Name));
        return list;
    }
}
