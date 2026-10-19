using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>Снимки 9.9: главная без ленты, профиль (достижения игры, награды), запуск игры, Creator Hub.</summary>
public static class V99Shots
{
    [DemoShots]
    static void Profile99Shots(Shots s)
    {
        var w = s.Window;
        // Надеть награды, чтобы на снимках были видны рамка, баннер, титул и значки.
        Cosmetics.Toggle(new Reward("frame", "game:minecraft"));
        Cosmetics.Toggle(new Reward("banner", "game:minecraft"));
        Cosmetics.Toggle(new Reward("title", "game:minecraft"));
        Cosmetics.Toggle(new Reward("badge", "game:minecraft"));
        Cosmetics.Toggle(new Reward("badge", "game:subnautica"));
        w.Navigate(() => new HomePage());
        s.Pump(1400);
        s.Save("v99-0-home");
        if (Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(w).OfType<HomePage>().FirstOrDefault()?.Content is Avalonia.Controls.ScrollViewer sv)
        {
            sv.Offset = new Avalonia.Vector(0, 700);
            s.Pump(500);
            s.Save("v99-0b-home-down");
        }
        w.Navigate(() => new ProfilePage());
        s.Pump(900);
        s.Save("v99-1-profile");
        var game = Achievements.Sets().Select(x => x.Set).FirstOrDefault(x => x != Achievements.Main);
        if (game is not null)
        {
            w.Navigate(() => new ProfilePage("achievements", game));
            s.Pump(900);
            s.Save("v99-2-profile-game");
        }
        w.Navigate(() => new ProfilePage("rewards"));
        s.Pump(900);
        s.Save("v99-3-rewards");
        w.OpenAccountPanel();
        s.Pump(500);
        s.Save("v99-3b-account-menu");
        w.CloseAccountPanel();
        w.Navigate(() => new CreatorPage());
        s.Pump(900);
        s.Save("v99-4-creator");
        var g = AppState.Games.FirstOrDefault(x => x.Status == Detect.Found && !x.Def.IsMinecraft);
        if (g is not null)
        {
            var launch = w.ShowLaunch(g);
            s.Pump(600);
            launch.DemoProgress();
            s.Pump(500);
            s.Save("v99-5-launch");
            launch.DemoDone();
            s.Pump(700);
            s.Save("v99-6-launched");
            w.HideLaunch();
        }
    }
}
