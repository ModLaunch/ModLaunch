using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Views;

/// <summary>
/// Снимки 12.1: новый облик Creator Hub — боковая панель, обзор-пульт, плитки верстака, редактор-IDE
/// и студия рынка в той же рамке; тёмная и светлая (бумага) темы, по-английски.
/// </summary>
public static class V12HubDesignShots
{
    /// <summary>Пара проектов, чтобы «Верстак» и «Мои проекты» не были пустыми.</summary>
    static void Seed()
    {
        if (Projects.List().Count >= 3) return;
        foreach (var id in new[] { "sdv-seeds", "sdv-fish", "valheim-builder" })
            Projects.Create(I18n.T($"cr.ex.{id}"), Templates.ById(id)!.Code);
    }

    static void Shot(Shots s, Func<Page> page, string name, int wait = 700)
    {
        s.Window.Navigate(page);
        s.Pump(wait);
        s.Save(name);
    }

    /// <summary>Обзор, проекты, редактор и студия — на одном экране Hub.</summary>
    static void Set(Shots s, string prefix)
    {
        Seed();
        var p = Projects.List().OrderByDescending(x => x.Updated).First();
        Shot(s, () => new CreatorPage("home"), prefix + "-overview");
        Shot(s, () => new CreatorPage("mine"), prefix + "-projects");
        Shot(s, () => new CreatorPage("mine", p.Id), prefix + "-editor");
        Shot(s, () => new MarketPage("studio"), prefix + "-studio");
    }

    [DemoShots]
    static void HubDesign1Dark(Shots s) => Set(s, "v12d-1-dark");

    [DemoShots]
    static void HubDesign2Light(Shots s)
    {
        Look.SetStyle("redwhite");
        try { Set(s, "v12d-2-light"); }
        finally { Look.SetStyle("dark"); }
    }

    [DemoShots]
    static void HubDesign3English(Shots s)
    {
        I18n.Set("en");
        try { Set(s, "v12d-3-en"); }
        finally { I18n.Set("ru"); }
    }
}
