using ModLaunch.Sources;

namespace ModLaunch.Views;

public sealed partial class CreatorPage
{
    [DemoShots]
    static void V8Shots(Shots s)
    {
        s.Window.Navigate(() => new ControlPanelPage());
        s.Pump(900);
        s.Save("v8-1-panel");

        s.Window.Navigate(() => new MinecraftPage("catalog"));
        s.Pump(900);
        s.Save("v8-2-modrinth");

        s.Window.Navigate(() => new CreatorPage("docs"));
        s.Pump(700);
        s.Save("v8-3-docs");

        s.Window.Navigate(() => new SettingsPage("look"));
        s.Pump(700);
        s.Save("v8-4-look");
        s.Window.Navigate(() => new SettingsPage("system"));
        s.Pump(700);
        s.Save("v8-5-system");
        WhatsNew.Show();
        s.Pump(700);
        s.Save("v8-6-whatsnew");
        s.Window.CloseDialog();
    }
}
