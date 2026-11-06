using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Views;

/// <summary>
/// Снимки 22.0 (MODLAUNCH_SHOTS=V22Hub): Creator Hub в облике «верстака» — одна полка «Мои моды» для скриптов
/// и правок файлов, «Новый мод», справочник ModScript (скрипты в игре, правки файлов) и проект правок файлов,
/// открытый в общей среде ModScript (с проверкой после F5).
/// </summary>
public static class V22HubShots
{
    [DemoShots]
    static void V22Hub(Shots s)
    {
        XStudio.DemoProject();
        if (Projects.List().Count < 2)
            foreach (var id in new[] { "sdv-seeds", "valheim-builder" })
                Projects.Create(I18n.T($"cr.ex.{id}"), Templates.ById(id)!.Code);
        if (!Features.Inject.All().Any(i => i.Source == "import")) Features.Inject.Demo();
        foreach (var (tab, shot) in new[] { ("home", "v22-1-mods"), ("create", "v22-2-new"), ("guide", "v22-3-guide"), ("commands", "v22-4-guide-files"), ("scripts", "v22-5-scripts") })
        {
            s.Window.Navigate(() => new CreatorPage(tab));
            s.Pump(1200);
            s.Save(shot);
        }
        var classic = Projects.List().First();
        var x = XProjects.Adopt(classic);
        s.Window.Navigate(() => new XStudio(x.Id, "files", "problems", "code"));
        s.Pump(1500);
        s.Save("v22-6-mls-in-studio");
        if (s.Window.CurrentPage is XStudio studio)
        {
            studio.DemoRun();
            s.Pump(800);
            s.Save("v22-7-mls-checked");
        }
    }
}
