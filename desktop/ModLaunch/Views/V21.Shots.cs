using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Views;

/// <summary>
/// Снимки 21.0 (MODLAUNCH_SHOTS=V21): «Первый мод за минуту» в «Создать», ModScript X простыми словами с учебником,
/// панель «Команды» с кусочками, команды ModScript в том же файле и ошибка «кусочка нет».
/// </summary>
public static class V21Shots
{
    static string Sample(string head, string piece) => SimpleWords.Ru
        ? head + $"{Pieces.Line(piece)} (gold_bonus = 3)\nпусть звёзды = 0\n\nкогда игра началась {{\n    сказать \"Привет! Золото: {{золото}}\"\n    с шансом 50 {{\n        дать золото случайно от 1 до 100\n    }}\n    дать золото награда(2)\n}}\n\n" +
          "каждые 5 секунд {\n    если здоровье меньше 20 {\n        сказать \"" + I18n.T("v21.shot.low") + "\"\n        дать здоровье 5\n    } иначе {\n        дать звёзды 1\n    }\n}\n\n" +
          "функция награда(волна) {\n    вернуть волна * 50\n}\n\nфайл \"readme.txt\" = \"Сделано в ModLaunch\"\nнастройка \"MyMod.cfg\" [General] Speed = 2\n"
        : head + $"{Pieces.Line(piece)} (gold_bonus = 3)\nlet stars = 0\n\nwhen the game starts {{\n    say \"Hi! Gold: {{gold}}\"\n    with chance 50 {{\n        give gold random 1 to 100\n    }}\n    give gold reward(2)\n}}\n\n" +
          "every 5 seconds {\n    if health less than 20 {\n        say \"" + I18n.T("v21.shot.low") + "\"\n        give health 5\n    } otherwise {\n        give stars 1\n    }\n}\n\n" +
          "function reward(wave) {\n    return wave * 50\n}\n\nwrite \"readme.txt\" = \"Made with ModLaunch\"\nconfig \"MyMod.cfg\" [General] Speed = 2\n";

    [DemoShots]
    static void V21Words(Shots s)
    {
        s.Window.Navigate(() => new CreatorPage("form"));
        s.Pump(1200);
        s.Save("v21-1-create");

        if (!Features.Inject.All().Any(i => i.Source == "market")) Features.Inject.Demo();
        var p = XProjects.List().FirstOrDefault(x => x.Name == I18n.T("v21.idea.hello.name")) ?? CreatorPage.QuickStart("hello", open: false)!;
        var target = ModScriptX.Target.Find(p.Target ?? "unity")!;
        var head = CreatorPage.IdeaCode("blank", p.Name, target).Split("\n\n")[0] + "\n\n";
        File.WriteAllText(p.Main, Sample(head, I18n.T("v21.piece.gold")));
        XStudio.Forget(p.Id);
        XStudio.DemoLesson("if");
        s.Window.Navigate(() => new XStudio(p.Id, side: "docs"));
        s.Pump(1600);
        s.Save("v21-2-words-learn");

        s.Window.Navigate(() => new XStudio(p.Id, side: "blocks"));
        s.Pump(1400);
        s.Save("v21-3-commands");

        // Ошибка: кусочка нет в библиотеке.
        File.WriteAllText(p.Main, Sample(head, "Jetpack"));
        XStudio.Forget(p.Id);
        s.Window.Navigate(() => new XStudio(p.Id, side: "files"));
        s.Pump(1600);
        s.Save("v21-4-piece-error");
        File.WriteAllText(p.Main, Sample(head, I18n.T("v21.piece.gold")));
        XStudio.Forget(p.Id);
    }
}
