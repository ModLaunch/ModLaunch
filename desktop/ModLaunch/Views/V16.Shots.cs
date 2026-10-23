using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>
/// Снимки 16.0 (MODLAUNCH_SHOTS=V16): карточка мода в ИИ-сборке, путь наград (главная, «Забрать», профиль),
/// анкета нового проекта, «Проекты» Hub и среда ModScript X со всеми панелями.
/// </summary>
public static class V16Shots
{
    static void Shot(Shots s, string name, Func<Page> make, int ms = 1200)
    {
        s.Window.Navigate(make);
        s.Pump(ms);
        s.Save(name);
    }

    [DemoShots]
    static void V16Ai(Shots s)
    {
        var g = AppState.Games.FirstOrDefault(x => x.Status == Detect.Found && !x.Def.IsMinecraft && x.Def.HasExternalCatalog);
        if (g is null) return;
        var b = AiBuilds.Generate(g, "красивая графика и удобный интерфейс, без читов", "beauty", 8, true, CancellationToken.None).GetAwaiter().GetResult();
        AiBuildsPage.DemoResult(b);
        Shot(s, "v16-ai-1-result", () => new AiBuildsPage());
        AiBuildsPage.DemoResult(b, 1);
        Shot(s, "v16-ai-2-peek", () => new AiBuildsPage(), 1500);
    }

    [DemoShots]
    static void V16Road(Shots s)
    {
        // Пара уровней «не забрана» — на главной горит «Забрать», окно показывает новые награды.
        var (level, _, _) = RoadKit.Now();
        Settings.Data.Obj("profile")["roadSeen"] = Math.Max(1, level - 3);
        Shot(s, "v16-road-1-home", () => new HomePage(), 1600);
        RoadKit.Claim();
        s.Pump(900);
        s.Save("v16-road-2-claim");
        s.Window.CloseDialog();
        Shot(s, "v16-road-3-profile", () => new ProfilePage("rewards"));
    }

    /// <summary>16.0.1: под «Выбором ModLaunch» и под списком каталога — «Ещё моды».</summary>
    [DemoShots]
    static void V16More(Shots s)
    {
        var g = AppState.Games.FirstOrDefault(x => x.Status == Detect.Found && x.Def.Picks.Length > 0 && x.Def.HasExternalCatalog);
        if (g is null) return;
        var page = new GamePage(g.Def.Id, "catalog");
        s.Window.Navigate(() => page);
        // Снимаем низ списка — там и стоит кнопка.
        void Bottom()
        {
            if (page.Content is Avalonia.Controls.Panel p && p.Children.Count > 0 && p.Children[0] is Avalonia.Controls.ScrollViewer sv)
                sv.Offset = new Avalonia.Vector(0, 100_000);
            s.Pump(500);
        }
        page.ShowSection("picks");
        s.Pump(1400);
        Bottom();
        s.Save("v16-more-1-picks");
        page.ShowSection("all");
        s.Pump(1400);
        Bottom();
        s.Save("v16-more-2-catalog");
    }

    [DemoShots] static void V16Form(Shots s) => Shot(s, "v16-hub-1-form", () => CreatorPage.NewForm());

    [DemoShots]
    static void V16Studio(Shots s)
    {
        var id = XStudio.DemoProject();
        Shot(s, "v16-hub-2-projects", () => new CreatorPage());
        Shot(s, "v16-x-1-files", () => new XStudio(id, "files", "problems"), 1500);
        Shot(s, "v16-x-2-bridge", () => new XStudio(id, "bridge", "inject"));
        Shot(s, "v16-x-3-blocks", () => new XStudio(id, "blocks", "output"));
        Shot(s, "v16-x-4-docs", () => new XStudio(id, "docs", "code"));
        Shot(s, "v16-x-5-settings", () => new XStudio(id, "settings", "problems"));
        // Перестройка той же страницы (смена языка, «Сохранить» в настройках, мост догрузился) не должна падать.
        s.Window.CurrentPage?.Build();
        s.Pump(600);
        s.Save("v16-x-6-rebuilt");
    }
}
