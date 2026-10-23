using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>
/// Снимки 19.0 (MODLAUNCH_SHOTS=V19): среда ModScript X как IDE — понимание кода и проблемы с исправлениями,
/// подсказки, отладчик с записью хода, все ссылки, палитра и история файла.
/// </summary>
public static class V19Shots
{
    [DemoShots]
    static void V19Studio(Shots s)
    {
        var id = XStudio.DemoProject();
        s.Window.Navigate(() => new XStudio(id, "files", "problems"));
        s.Pump(1200);
        if (s.Window.CurrentPage is not XStudio page) return;
        foreach (var (scene, shot, wait) in new[]
        {
            ("insight", "v19-1-insight", 900), ("complete", "v19-2-complete", 700), ("debug", "v19-3-debug", 900),
            ("refs", "v19-4-refs", 600), ("palette", "v19-5-palette", 600), ("close", "", 200), ("history", "v19-6-history", 800),
        })
        {
            page.Demo(scene);
            s.Pump(wait);
            if (shot != "") s.Save(shot);
        }
        s.Window.CloseDialog();
        // Перестройка той же страницы (смена языка) не должна падать.
        s.Window.CurrentPage?.Build();
        s.Pump(500);
    }
}
