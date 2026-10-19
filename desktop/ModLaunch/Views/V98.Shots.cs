using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>Снимки 9.8: «ИИ-сборки» — конструктор с готовой сборкой, «Мои сборки», профиль с достижениями.</summary>
public static class V98Shots
{
    static Avalonia.Controls.ScrollViewer? FindScroll(Avalonia.Controls.Window w) =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(w).OfType<AiBuildsPage>().FirstOrDefault()?.Content as Avalonia.Controls.ScrollViewer;

    [DemoShots]
    static void AiBuilds98Shots(Shots s)
    {
        var w = s.Window;
        w.Navigate(() => new AiBuildsPage());
        s.Pump(900);
        s.Save("v98-0-ai-empty");

        var g = AppState.Games.FirstOrDefault(x => x.Status == Detect.Found && !x.Def.IsMinecraft && x.Def.HasExternalCatalog);
        if (g is not null)
        {
            var b = AiBuilds.Generate(g, "красивая графика и удобный интерфейс", "beauty", 8, true, CancellationToken.None).GetAwaiter().GetResult();
            AiBuilds.Save(b);
            AiBuilds.Save(AiBuilds.Generate(g, "хардкор", "hardcore", 5, true, CancellationToken.None).GetAwaiter().GetResult());
            AiBuildsPage.DemoResult(b);
        }
        w.Navigate(() => new AiBuildsPage());
        s.Pump(900);
        s.Save("v98-1-ai-result");
        if (w.Content is not null && FindScroll(w) is { } sv) { sv.Offset = new Avalonia.Vector(0, 560); s.Pump(500); s.Save("v98-1b-ai-result-list"); }
        w.Navigate(() => new AiBuildsPage("mine"));
        s.Pump(900);
        s.Save("v98-2-ai-mine");
        w.Navigate(() => new ProfilePage());
        s.Pump(900);
        s.Save("v98-3-player");
    }
}
