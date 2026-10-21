using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>Снимки 12.0: виджеты с настройками (игра из Steam, заметка, цвет), галерея виджетов и все игры Steam.</summary>
public static class V12WidgetsShots
{
    static void Layout()
    {
        Settings.Data["widgets"] = new System.Text.Json.Nodes.JsonArray(
            "game l game=steam%3A1245620", "note s text=" + Uri.EscapeDataString(I18n.T("v12w.shot.note")), "clock s tint=violet",
            "playtime s tint=green", "random s from=steam", "steam m", "continue m");
    }

    [DemoShots]
    static void WidgetsStudio1Home(Shots s)
    {
        Layout();
        s.Window.Navigate(() => new HomePage());
        s.Pump(900);
        // Виджеты — под сценой: прокручиваем к ним.
        if (s.Window.CurrentPage?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } scroll) scroll.Offset = new Vector(0, 520);
        s.Pump(500);
        s.Save("v12w-1-home");
    }

    [DemoShots]
    static void WidgetsStudio2Gallery(Shots s)
    {
        Layout();
        s.Window.Navigate(() => new HomePage());
        s.Pump(600);
        (s.Window.CurrentPage as HomePage)?.OpenStudio(kind: "clock");
        s.Pump(700);
        s.Save("v12w-2-gallery");
        s.Window.CloseDialog();
        (s.Window.CurrentPage as HomePage)?.OpenStudio(edit: 0);
        s.Pump(700);
        s.Save("v12w-3-edit");
        s.Window.CloseDialog();
        Settings.Data.Remove("widgets");
    }

    [DemoShots]
    static void WidgetsStudio3Steam(Shots s)
    {
        s.Window.Navigate(() => new SteamGamesPage());
        s.Pump(900);
        s.Save("v12w-4-steam");
    }
}
