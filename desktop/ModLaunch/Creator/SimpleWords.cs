using System.Text.RegularExpressions;
using ModLaunch.Core;
using MX = ModScriptX;

namespace ModLaunch.Creator;

/// <summary>
/// 21.0: простые слова ModScript X для подсказок, панели «Команды» и учебника. Одна команда — по-русски и по-английски;
/// показывается та, что на языке программы (русский интерфейс — русские слова, остальные — английские).
/// В коде ${…} — место, которое выделяется после вставки (поменяй число или текст).
/// </summary>
public static class SimpleWords
{
    public static bool Ru => I18n.Lang == "ru";

    public sealed record Cmd(string Id, string Group, bool Top, string Ru, string En);

    public static readonly string[] Groups = ["events", "talk", "numbers", "logic", "own", "files"];

    public static readonly Cmd[] All =
    [
        new("start", "events", true, "когда игра началась {\n    сказать \"${Привет!}\"\n}", "when the game starts {\n    say \"${Hello!}\"\n}"),
        new("ready", "events", true, "когда мод загрузился {\n    в журнал \"${мод включён}\"\n}", "when the mod loads {\n    log \"${mod is on}\"\n}"),
        new("load", "events", true, "когда загрузилось сохранение {\n    сказать \"${С возвращением!}\"\n}", "when the save loads {\n    say \"${Welcome back!}\"\n}"),
        new("every", "events", true, "каждые ${5} секунд {\n    сказать \"Прошло 5 секунд\"\n}", "every ${5} seconds {\n    say \"5 seconds passed\"\n}"),
        new("frame", "events", true, "каждый кадр {\n    поставить здоровье = ${100}\n}", "every frame {\n    set health = ${100}\n}"),
        new("say", "talk", false, "сказать \"${Привет!}\"", "say \"${Hello!}\""),
        new("sayvar", "talk", false, "сказать \"Золото: {золото}\"", "say \"Gold: {gold}\""),
        new("log", "talk", false, "в журнал \"${проверка}\"", "log \"${check}\""),
        new("give", "numbers", false, "дать золото ${100}", "give gold ${100}"),
        new("take", "numbers", false, "забрать здоровье ${10}", "take health ${10}"),
        new("set", "numbers", false, "поставить скорость = ${8}", "set speed = ${8}"),
        new("mul", "numbers", false, "умножить урон_врагов на ${2}", "multiply enemy_damage by ${2}"),
        new("let", "numbers", true, "пусть ${очки} = 0", "let ${score} = 0"),
        new("if", "logic", false, "если ${здоровье меньше 20} {\n    сказать \"Осторожно!\"\n}", "if ${health less than 20} {\n    say \"Careful!\"\n}"),
        new("ifelse", "logic", false, "если ${золото больше 100} {\n    сказать \"Богач!\"\n} иначе {\n    сказать \"Копим\"\n}",
            "if ${gold more than 100} {\n    say \"Rich!\"\n} otherwise {\n    say \"Saving up\"\n}"),
        new("chance", "logic", false, "с шансом ${10} {\n    дать золото случайно от 1 до 50\n}", "with chance ${10} {\n    give gold random 1 to 50\n}"),
        new("repeat", "logic", false, "повторить ${3} раза {\n    дать очки 1\n}", "repeat ${3} times {\n    give score 1\n}"),
        new("while", "logic", false, "пока ${золото меньше 100} {\n    дать золото 10\n}", "while ${gold less than 100} {\n    give gold 10\n}"),
        new("stop", "logic", false, "стоп", "stop"),
        new("fn", "own", true, "функция ${награда}(волна) {\n    вернуть волна * 50\n}", "function ${reward}(wave) {\n    return wave * 50\n}"),
        new("call", "own", false, "дать золото награда(${3})", "give gold reward(${3})"),
        new("file", "files", true, "файл \"${readme.txt}\" = \"Сделано в ModLaunch\"", "write \"${readme.txt}\" = \"Made with ModLaunch\""),
        new("config", "files", true, "настройка \"${MyMod.cfg}\" [General] Speed = 2", "config \"${MyMod.cfg}\" [General] Speed = 2"),
        new("copy", "files", true, "копировать \"${icon.png}\" в \"icon.png\"", "copy \"${icon.png}\" to \"icon.png\""),
    ];

    /// <summary>Код команды со снипетом ${…} — на языке программы.</summary>
    public static string Code(Cmd c) => Ru ? c.Ru : c.En;

    /// <summary>Тот же код без меток ${…}: для вставки кнопкой и для проверки.</summary>
    public static string Plain(string snippet) => Regex.Replace(snippet, @"\$\{([^}]*)\}", "$1");

    /// <summary>Подпись в подсказке: первые слова команды («дать золото», «каждые 5 секунд»).</summary>
    public static string Label(Cmd c)
    {
        var first = Plain(Code(c)).Split('\n')[0].TrimEnd('{', ' ');
        return first.Length > 28 ? first[..28].TrimEnd() + "…" : first;
    }

    [SelfTest]
    static string SimpleWordsWork()
    {
        // Каждая команда на обоих языках — правильный код ModScript X: в своём месте файла, хуке или функции.
        var n = 0;
        foreach (var c in All)
            foreach (var code in new[] { Plain(c.Ru), Plain(c.En) })
            {
                var src = c.Top ? "mod \"T\"\n" + code + "\n" : "mod \"T\"\nhook start {\n" + string.Join("\n", code.Split('\n').Select(l => "    " + l)) + "\n}\n";
                if (c.Id == "call") src += "fn reward(wave) {\n    return wave\n}\nфункция награда(волна) {\n    вернуть волна\n}\n";
                var doc = MX.ModxDoc.Parse(src);
                if (!doc.Ok) throw new Exception($"{c.Id}: {code} → {string.Join("; ", doc.Diags.Where(d => !d.Warning).Select(d => d.Message))}");
                n++;
            }

        string Run(string code, string vars = "gold = 100\nhealth = 80\nspeed = 5\nenemy_damage = 3", int ticks = 1)
        {
            var doc = MX.ModxDoc.Parse(code);
            if (!doc.Ok) throw new Exception("parse: " + string.Join("; ", doc.Diags.Where(d => !d.Warning).Select(d => $"{d.Line}: {d.Message}")));
            var r = MX.Sandbox.Run(doc, MX.Sandbox.ParseVars(vars, []), ticks);
            if (!r.Ok) throw new Exception("run: " + string.Join("; ", r.Errors.Select(e => e.Message)));
            return string.Join("\n", r.Output.Select(o => Regex.Replace(o, @"^\[[^\]]*\]\s*", "")));
        }

        var ru = Run("мод \"Тест\"\nпусть очки = 0\n\nкогда игра началась {\n    дать золото 50\n    дать 5 очков\n    забрать здоровье 30\n    умножить скорость на 2\n" +
            "    если здоровье меньше 60 и золото не меньше 150 {\n        сказать \"ок {золото} {очки} {скорость}\"\n    } иначе {\n        сказать \"нет\"\n    }\n" +
            "    повторить 3 раза {\n        дать очки 1\n    }\n    с шансом 100 {\n        дать золото случайно от 1 до 1\n    }\n    сказать \"очки {очки}, золото {золото}\"\n}\n");
        if (ru != "💬 ок 150 5 10\n💬 очки 8, золото 151") throw new Exception("ru: " + ru);
        var en = Run("mod \"Test\"\nwhen the game starts {\n    give gold 10\n    if gold is at least 110 and health less than 100 {\n        say \"yes {gold}\"\n    } otherwise {\n        say \"no\"\n    }\n}\n");
        if (en != "💬 yes 110") throw new Exception("en: " + en);
        // Таймер: 60 кадров в секунду — за 10 секунд «каждые 5 секунд» срабатывает дважды; у кусочка свой счётчик.
        var timer = Run("мод \"Т\"\nкаждые 5 секунд {\n    сказать \"тик\"\n}\nкаждую секунду {\n    дать золото 1\n}\nкогда игра началась {\n    сказать \"старт\"\n}\n", ticks: 600);
        if (timer != "💬 старт\n💬 тик\n💬 тик") throw new Exception("timer: " + timer.Replace("\n", " | "));
        // Строки и слова внутри кавычек не переводятся, кроме {вставок}.
        if (MX.Words.Line("    сказать \"дать золото и больше\" # коммент", 1) != "    say \"дать золото и больше\" # коммент") throw new Exception("strings changed");
        if (MX.Words.Line("hook start {", 1) != "hook start {") throw new Exception("canonical changed");
        // Перевод в движки: слова не попадают в Python/C#/GDScript.
        var emitted = MX.ModxDoc.Parse("мод \"Т\"\nдвижок renpy\nкогда игра началась {\n    если золото больше 5 {\n        дать золото 1\n    }\n}\n");
        foreach (var t in new[] { "renpy", "unity", "godot" })
        {
            var text = string.Concat(MX.Target.Find(t)!.Emit(emitted, null).Select(f => f.Text));
            if (text.Contains("больше") || text.Contains("золото") || text.Contains("дать")) throw new Exception(t + " kept words");
        }
        return $"{n} commands parse in ru and en, sandbox runs words, timers and chance, engines get plain code";
    }
}
