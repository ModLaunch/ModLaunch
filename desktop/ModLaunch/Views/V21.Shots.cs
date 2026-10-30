using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Views;

/// <summary>
/// Снимки 21.0 (MODLAUNCH_SHOTS=V21): «Собери мод из блоков» в «Создать», ModScript X блоками (палитра, кусочки,
/// условия и повторы), тот же мод кодом (кусочек — строкой use), ошибка на блоке и кусочек на странице лота.
/// </summary>
public static class V21Shots
{
    [DemoShots]
    static void V21Blocks(Shots s)
    {
        s.Window.Navigate(() => new CreatorPage("form"));
        s.Pump(1200);
        s.Save("v21-1-create");

        if (!Features.Inject.All().Any(i => i.Source == "market")) Features.Inject.Demo();
        var p = XProjects.List().FirstOrDefault(x => x.Name == I18n.T("v21.idea.hello.name")) ?? CreatorPage.QuickStart("hello", open: false)!;
        File.WriteAllText(p.Main, CreatorPage.IdeaCode("hello", p.Name, ModScriptX.Target.Find(p.Target ?? "unity")!)
            .Replace("hook start {", $"use \"{I18n.T("v21.piece.gold")}\" (gold_bonus = 3)\nuse \"{I18n.T("v21.piece.luck")}\"\n\nlet stars = 0\n\nhook start {{")
            + "\nhook tick {\n    if health < 20 {\n        say \"" + I18n.T("v21.shot.low") + "\"\n        add health 5\n    } else {\n        add stars 1\n    }\n    for i in 1..3 {\n        log \"wave {i}\"\n    }\n}\n");
        Settings.Data["xView"] = "blocks";
        s.Window.Navigate(() => new XStudio(p.Id, side: "blocks", view: "blocks"));
        s.Pump(1600);
        s.Save("v21-2-blocks");
        // Весь холст целиком — высокое окно.
        var height = s.Window.Height;
        s.Window.Height = 1500;
        s.Pump(900);
        s.Save("v21-2b-blocks-all");
        s.Window.Height = height;
        s.Pump(300);

        Settings.Data["xView"] = "code";
        s.Window.Navigate(() => new XStudio(p.Id, side: "files", view: "code"));
        s.Pump(1400);
        s.Save("v21-3-code");

        // Ошибка на блоке: кусочка нет в библиотеке.
        var broken = File.ReadAllText(p.Main).Replace("let stars = 0", "use \"Jetpack\"\nlet stars = 0");
        File.WriteAllText(p.Main, broken);
        XStudio.Forget(p.Id);
        Settings.Data["xView"] = "blocks";
        var page = new XStudio(p.Id, side: "blocks", view: "blocks");
        s.Window.Navigate(() => page);
        s.Pump(1600);
        s.Save("v21-4-piece-error");
        Settings.Data["xView"] = "blocks";
        Settings.Save();
    }
}
