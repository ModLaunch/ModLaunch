using Avalonia.Headless;
using ModLaunch.Core;
using ModLaunch.Setup;

namespace ModLaunch.Views;

/// <summary>Снимки на других языках: проверяем шрифты (китайский) и длинные слова (немецкий).</summary>
public static class IntlShots
{
    [DemoShots]
    static void Intl(Shots s)
    {
        foreach (var lang in new[] { "zh-CN", "de" })
        {
            I18n.Set(lang);
            s.Window.Navigate(() => new HomePage());
            s.Pump(700);
            s.Save($"intl-{lang}-home");
            s.Window.Navigate(() => new GamePage("lethal-company", "catalog"));
            s.Pump(900);
            s.Save($"intl-{lang}-catalog");
            // 23.8: страницы, переведённые на все языки разом, — проверяем, что длинные строки помещаются.
            foreach (var (name, page) in new (string, Func<Page>)[]
            {
                ("settings", () => new SettingsPage("backups")),
                ("ingame", () => new CreatorPage("ingame")),
                ("community", () => new CommunityPage("servers")),
                ("profile", () => new ProfilePage("rewards")),
                ("ai", () => new AiBuildsPage()),
            })
            {
                s.Window.Navigate(page);
                s.Pump(900);
                s.Save($"intl-{lang}-{name}");
            }
            var setup = new SetupWindow(SetupMode.Install);
            setup.Show();
            s.Pump(600);
            setup.CaptureRenderedFrame()?.Save(Path.Combine(s.OutDir, $"intl-{lang}-setup.png"));
            setup.Close();
        }
        I18n.Set("ru");
    }
}
