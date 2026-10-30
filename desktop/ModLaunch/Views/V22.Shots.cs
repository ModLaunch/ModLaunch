using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Views;

/// <summary>
/// Снимки 22.0 (MODLAUNCH_SHOTS=V22): мод для GTA V словами — мост узнал игру и показывает, как поставить ScriptHook .NET,
/// «Команды» с числами игры (деньги, здоровье, броня, розыск) и мост Cyberpunk 2077 с готовым Cyber Engine Tweaks.
/// </summary>
public static class V22Shots
{
    static string Folder(string name, params string[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "modlaunch-shot-" + name);
        foreach (var f in files)
        {
            var path = Path.Combine(dir, f);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (!File.Exists(path)) File.WriteAllText(path, "");
        }
        return dir;
    }

    static XProject Project(string name, string folder, string engine, string target, string code)
    {
        var p = XProjects.List().FirstOrDefault(x => x.Name == name)
            ?? XProjects.Create(new XForm(name, "", "", folder, "economy", engine, target, ["start"], "", "1.0.0", "cyan"));
        p.GamePath = folder;
        p.Engine = engine;
        p.Target = target;
        p.Save();
        File.WriteAllText(p.Main, code);
        XStudio.Forget(p.Id);
        CreatorPage.BridgeFor(folder, null);
        return p;
    }

    [DemoShots]
    static void V22Engines(Shots s)
    {
        var ru = SimpleWords.Ru;
        var gta = Project(ru ? "Богач" : "Rich Guy", Folder("gta5", "GTA5.exe", "x64a.rpf", "update/update.rpf"), "rage-gta5", "gta5", ru
            ? "мод \"Богач\"\nдвижок gta5\n\nкогда игра началась {\n    дать деньги 1000\n    поставить броня = 100\n    сказать \"Деньги: {деньги}\"\n}\n\n" +
              "каждые 5 секунд {\n    если здоровье меньше 50 {\n        поставить здоровье = 200\n        поставить розыск = 0\n        сказать \"Подлечился и ушёл от погони\"\n    }\n}\n"
            : "mod \"Rich Guy\"\nengine gta5\n\nwhen the game starts {\n    give gold 1000\n    set armor = 100\n    say \"Money: {gold}\"\n}\n\n" +
              "every 5 seconds {\n    if health less than 50 {\n        set health = 200\n        set wanted = 0\n        say \"Healed and lost the cops\"\n    }\n}\n");
        s.Pump(600);
        s.Window.Navigate(() => new XStudio(gta.Id, "bridge"));
        s.Pump(1600);
        s.Save("v22-1-gta-bridge-setup");

        s.Window.Navigate(() => new XStudio(gta.Id, "blocks"));
        s.Pump(1400);
        s.Save("v22-2-gta-numbers");

        var cp = Project("Night City", Folder("cp2077", "bin/x64/Cyberpunk2077.exe", "bin/x64/plugins/cyber_engine_tweaks.asi", "archive/pc/content/basegame_1_engine.archive"),
            "redengine", "cyberpunk", ru
            ? "мод \"Night City\"\nдвижок cyberpunk\n\nкогда загрузилось сохранение {\n    дать деньги 5000\n    сказать \"Эдди: {деньги}\"\n}\n\nкаждую секунду {\n    если здоровье меньше 30 {\n        поставить здоровье = 100\n    }\n}\n"
            : "mod \"Night City\"\nengine cyberpunk\n\nwhen the save loads {\n    give gold 5000\n    say \"Eddies: {gold}\"\n}\n\nevery 1 seconds {\n    if health less than 30 {\n        set health = 100\n    }\n}\n");
        s.Pump(600);
        s.Window.Navigate(() => new XStudio(cp.Id, "bridge"));
        s.Pump(1600);
        s.Save("v22-3-cyberpunk-bridge");
    }
}
