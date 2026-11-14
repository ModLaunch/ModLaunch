using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>Снимки 30.0 (MODLAUNCH_SHOTS=V30…) на демо-данных.</summary>
public static class V30Shots
{
    /// <summary>MODLAUNCH_SHOTS=V30Nexus: окно ожидания браузера Nexus в задаче на несколько модов — «Пропустить» и «Отменить всё».</summary>
    [DemoShots]
    static void V30Nexus(Shots s)
    {
        s.Pump(4500);
        var job = new Job { Title = "Обновления: 3", GameName = "Skyrim" };
        Actions.ShowNexusWait("SkyUI", "https://www.nexusmods.com/", job, new TaskCompletionSource<object>(), skippable: true);
        s.Pump(800);
        s.Save("v30-01-nexus-skip");
        s.Window.CloseDialog();
    }

    /// <summary>MODLAUNCH_SHOTS=V30Shot: настройки снимка экрана в игре (рядом с оверлеем).</summary>
    [DemoShots]
    static void V30Shot(Shots s)
    {
        s.Pump(4500);
        s.Window.Navigate(() => new SettingsPage("launch"));
        s.Pump(1500);
        if (s.Window.CurrentPage is SettingsPage p) p.DemoScroll(1080);
        s.Pump(800);
        s.Save("v30-02-shot-settings");
    }
}
