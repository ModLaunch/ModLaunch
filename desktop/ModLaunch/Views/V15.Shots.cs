namespace ModLaunch.Views;

/// <summary>Снимки 15.0 (MODLAUNCH_SHOTS=V15): Minecraft, панель управления в настройках, ModScript, справочник игр.</summary>
public static class V15Shots
{
    static void Shot(Shots s, string name, Func<Page> make, int ms = 1200)
    {
        s.Window.Navigate(make);
        s.Pump(ms);
        s.Save(name);
    }

    [DemoShots] static void V15Mc(Shots s) => Shot(s, "v15-mc-1", () => new MinecraftPage());
    [DemoShots] static void V15McMods(Shots s) => Shot(s, "v15-mc-2-mods", () => new MinecraftPage("mods"));
    [DemoShots] static void V15McCatalog(Shots s) => Shot(s, "v15-mc-3-catalog", () => new MinecraftPage("catalog"));
    [DemoShots] static void V15McBuilds(Shots s) => Shot(s, "v15-mc-4-builds", () => new MinecraftPage("builds"));
    [DemoShots] static void V15McSettings(Shots s) => Shot(s, "v15-mc-5-settings", () => new MinecraftPage("settings"));
    [DemoShots] static void V15Panel(Shots s) => Shot(s, "v15-panel", () => new SettingsPage("panel"));

    static void Studio(Shots s, string name, string panel)
    {
        ModStudio.Demo(panel);
        Shot(s, name, () => new CreatorPage("ide"), 1500);
    }

    [DemoShots] static void V15Studio(Shots s) => Studio(s, "v15-studio-1-problems", "problems");
    [DemoShots] static void V15StudioRun(Shots s) => Studio(s, "v15-studio-2-run", "output");
    [DemoShots] static void V15StudioCode(Shots s) => Studio(s, "v15-studio-3-code", "code");
    [DemoShots] static void V15StudioInject(Shots s) => Studio(s, "v15-studio-4-inject", "inject");
    [DemoShots] static void V15Games(Shots s) => Shot(s, "v15-games", () => new AddGamePage(), 2500);
}
