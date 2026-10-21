using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>Снимки 12.0: главная с лором на сцене и экран запуска игры на разных стадиях лора.</summary>
public static class V12Shots
{
    [DemoShots]
    static void Twelve0Home(Shots s)
    {
        Settings.Data.Remove("widgets");
        Widgets.Add("lore", "m", 1);
        if (AppState.Games.FirstOrDefault(g => g.Def.Id == "hollow-knight" && g.Status == Detect.Found) is not null) Lore.Forced["hollow-knight"] = 3;
        s.Window.Navigate(() => new HomePage());
        s.Pump(1400);
        s.Save("v12-0-home");
        Settings.Data.Remove("widgets");
    }

    [DemoShots]
    static void Twelve1Launch(Shots s)
    {
        var w = s.Window;
        foreach (var (id, stages) in new[] { ("hollow-knight", new[] { 0, 1, 2, 3 }), ("minecraft", new[] { 2, 3 }), ("subnautica", new[] { 2 }), ("stardew-valley", new[] { 3 }), ("lethal-company", new[] { 2 }) })
        {
            if (AppState.Games.FirstOrDefault(x => x.Def.Id == id && x.Status == Detect.Found) is not { } g) continue;
            foreach (var stage in stages)
            {
                Lore.Forced[id] = stage;
                var launch = w.ShowLaunch(g);
                s.Pump(500);
                launch.DemoProgress();
                s.Pump(200);
                s.Save($"v12-launch-{id}-{stage}");
                w.HideLaunch();
            }
        }
        Lore.Forced.Clear();
    }
}
