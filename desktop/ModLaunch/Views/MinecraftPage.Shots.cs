using ModLaunch.Core;
using ModLaunch.Minecraft;

namespace ModLaunch.Views;

public sealed partial class MinecraftPage
{
    /// <summary>Снимки 8.5: Minecraft — моды сборки, каталог, сборки, новая сборка, настройки, миры, лог; боковая панель.</summary>
    [DemoShots]
    static void MinecraftShots(Shots s)
    {
        McDemo.Pictures();
        if (Mc.Active is { } a) _ = McContent.CheckUpdates(a);
        s.Window.Navigate(() => new HomePage());
        s.Pump(700);
        s.Save("v85-0-home-rail");

        s.Window.Navigate(() => new GamePage(Mc.Id));
        s.Pump(900);
        s.Save("v85-1-mods");

        _kind = "shader";
        s.Window.Navigate(() => new MinecraftPage("mods"));
        s.Pump(700);
        s.Save("v85-1b-shaders");
        _kind = "mod";

        _type = "mod";
        s.Window.Navigate(() => new MinecraftPage("catalog"));
        s.Pump(900);
        s.Save("v85-2-catalog");

        _type = "modpack";
        s.Window.Navigate(() => new MinecraftPage("catalog"));
        s.Pump(900);
        s.Save("v85-2b-modpacks");
        _type = "mod";

        s.Window.Navigate(() => new MinecraftPage("builds"));
        s.Pump(900);
        s.Save("v85-3-builds");

        CreateDialog();
        s.Pump(900);
        s.Save("v85-4-create");
        s.Window.CloseDialog();

        s.Window.Navigate(() => new MinecraftPage("settings"));
        s.Pump(900);
        s.Save("v85-5-settings");

        s.Window.Navigate(() => new MinecraftPage("worlds"));
        s.Pump(900);
        s.Save("v85-6-worlds");

        s.Window.Navigate(() => new MinecraftPage("shots"));
        s.Pump(900);
        s.Save("v85-6b-shots");

        s.Window.Navigate(() => new MinecraftPage("log"));
        s.Pump(700);
        s.Save("v85-7-log");

        s.Window.Navigate(() => new LibraryPage());
        s.Pump(900);
        s.Save("v85-8-library");

        I18n.Set("en");
        s.Window.Navigate(() => new MinecraftPage("mods"));
        s.Pump(900);
        s.Save("v85-9-mods-en");
        I18n.Set("ru");
    }
}
