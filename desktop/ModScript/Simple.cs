using System.Text;
using System.Text.RegularExpressions;

namespace ModScriptX;

/// <summary>
/// 22.0: «Простой ModScript» — язык для тех, кто пишет мод первый раз (хоть в 12 лет). Слова по-русски или по-английски,
/// никаких фигурных скобок: блок закрывается словом «конец» или просто отступом, как в Python.
///
///     мод "Много золота"
///     когда игра началась
///         дай золото 1000
///         скажи Привет! У тебя {золото} золота
///     конец
///     каждый кадр
///         если здоровье меньше 10
///             здоровье = 100
///
/// Перед разбором простой текст переводится в обычный .modx (hook, if, add, say, «{ }»), поэтому работает везде,
/// где работает .modx: во всех движках, в песочнице и в ModLaunch. Обычный .modx проходит без изменений.
/// Русские имена переменных становятся латиницей (золото → gold, кошка → koshka): так их понимает язык любого движка.
/// Map[i] — номер строки исходника для строки i + 1 результата: ошибки показываются на строках, которые написал автор.
/// </summary>
public static partial class Simple
{
    /// <summary>
    /// Перевод и раскладка: Text — обычный .modx, Map — строка автора для каждой строки Text; Depth — уровень вложенности
    /// каждой строки автора (−1 — пустая, −2 — код движка внутри native, его не трогаем); Blocks — блоки автора
    /// (строка начала, строка конца, вид: hook, fn, if, loop, native, и сама строка начала).
    /// </summary>
    public sealed record Result(string Text, int[] Map, int[] Depth, List<(int Start, int End, string Kind, string Title)> Blocks);

    enum Op { Plain, Open, Else, Close }

    sealed class Block(string kind, int indent, int start, string title)
    {
        public readonly string Kind = kind;
        public readonly int Indent = indent;
        public readonly int Start = start;
        public readonly string Title = title;
        /// <summary>Тело с отступом (тогда блок закрывается и без «конец»); null — ещё не было ни одной строки.</summary>
        public bool? Indented;
    }

    /// <summary>Есть ли в тексте простой синтаксис (русские слова, «конец», «когда…»). Нет — файл обычный .modx.</summary>
    public static bool IsSimple(string source) =>
        source.Replace("\r\n", "\n").Split('\n').Select(l => StripComment(l).Trim()).Any(l => l != "" && (HasCyrillic(Outside(l)) || KidEnglish().IsMatch(l)));

    public static Result ToModx(string source)
    {
        var lines = source.Replace("\r\n", "\n").Split('\n');
        var known = Declared(lines);
        var outLines = new List<string>();
        var map = new List<int>();
        var stack = new List<Block>();
        var native = -1; // отступ строки native, пока внутри неё (код движка не трогаем)
        var nativeStart = 0;
        var repeat = 0;
        var depth = new int[lines.Length];
        var blocks = new List<(int, int, string, string)>();
        string Pad(int d) => new(' ', 4 * Math.Max(0, d));
        void Emit(string s, int no) { outLines.Add(s); map.Add(no); }
        void Close(int no, bool own = false)
        {
            var b = stack[^1];
            stack.RemoveAt(stack.Count - 1);
            // «}» встаёт сразу после тела блока, перед пустыми строками.
            var at = outLines.Count;
            while (at > 0 && outLines[at - 1] == "") at--;
            outLines.Insert(at, Pad(stack.Count) + "}");
            var end = own ? no : at > 0 ? map[at - 1] : no;
            map.Insert(at, end);
            blocks.Add((b.Start, Math.Max(b.Start, end), b.Kind, b.Title));
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var no = i + 1;
            var raw = lines[i];
            var ind = Indent(raw);
            if (native >= 0)
            {
                if (raw.Trim() == "}" && ind <= native)
                {
                    native = -1;
                    Emit(Pad(stack.Count) + "}", no);
                    depth[i] = stack.Count;
                    blocks.Add((nativeStart, no, "native", lines[nativeStart - 1].Trim()));
                }
                else { Emit(Pad(stack.Count + 1) + raw, no); depth[i] = -2; }
                continue;
            }
            var t = StripComment(raw).Trim();
            if (t.StartsWith("//")) t = "";
            if (t == "") { Emit("", no); depth[i] = -1; continue; }

            if (NativeStart().Match(t) is { Success: true } nm)
            {
                while (stack.Count > 0 && stack[^1].Indented == true && stack[^1].Indent >= ind) Close(no);
                depth[i] = stack.Count;
                Emit(Pad(stack.Count) + $"native {nm.Groups[1].Value} {{", no);
                native = ind;
                nativeStart = no;
                continue;
            }

            var (text, op, kind) = Translate(t, stack.Any(b => b.Kind is "hook" or "fn"), known, ref repeat);

            // Первая строка тела решает, закрывается ли блок отступом.
            if (stack.Count > 0 && stack[^1].Indented is null && op is not (Op.Close or Op.Else)) stack[^1].Indented = ind > stack[^1].Indent;
            // Новое событие или функция закрывает всё открытое (при любом отступе): хуки и функции друг в друга не вкладываются.
            if (op == Op.Open && kind is "hook" or "fn") while (stack.Count > 0) Close(no);
            // Блоки с отступом закрываются, когда отступ вернулся назад.
            while (stack.Count > 0 && stack[^1].Indented == true
                   && (op is Op.Close or Op.Else ? stack[^1].Indent > ind : stack[^1].Indent >= ind)) Close(no);

            switch (op)
            {
                case Op.Close:
                    if (stack.Count > 0) Close(no, own: true);
                    else Emit(text, no); // лишний «конец» — разбор покажет ошибку
                    depth[i] = stack.Count;
                    break;
                case Op.Else:
                    depth[i] = Math.Max(0, stack.Count - 1);
                    Emit(Pad(stack.Count - 1) + text, no);
                    if (stack.Count > 0) stack[^1].Indented = null;
                    break;
                case Op.Open:
                    depth[i] = stack.Count;
                    Emit(Pad(stack.Count) + text, no);
                    stack.Add(new Block(kind, ind, no, t));
                    break;
                default:
                    depth[i] = stack.Count;
                    Emit(Pad(stack.Count) + text, no);
                    break;
            }
        }
        var last = lines.Length;
        while (last > 1 && lines[last - 1].Trim() == "") last--;
        while (stack.Count > 0) Close(Math.Max(1, last));
        // Забыли «мод "…"» — не беда: у мода будет имя по умолчанию.
        if (!outLines.Any(l => l.StartsWith("mod ", StringComparison.Ordinal))) Emit("mod \"Мой мод\"", 1);
        return new Result(string.Join('\n', outLines), [.. map], depth, [.. blocks.OrderBy(b => b.Item1)]);
    }

    /// <summary>
    /// Выровнять простой код: 4 пробела на уровень, без пробелов в конце строк. Смысл сверяется переводом:
    /// не совпал (такого быть не должно) — null, текст остаётся как был.
    /// </summary>
    public static string? Format(string source)
    {
        var lines = source.Replace("\r\n", "\n").Split('\n');
        var r = ToModx(source);
        var output = new string[lines.Length];
        for (var i = 0; i < lines.Length; i++)
            output[i] = r.Depth[i] switch
            {
                -2 => lines[i].TrimEnd(),
                -1 => "",
                var d => new string(' ', 4 * d) + lines[i].Trim(),
            };
        var text = string.Join('\n', output);
        // Не больше одной пустой строки подряд — так же, как выравнивание обычного .modx.
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return ToModx(text).Text == r.Text || Regex.Replace(ToModx(text).Text, @"\n+", "\n") == Regex.Replace(r.Text, @"\n+", "\n") ? text : null;
    }

    /// <summary>Вид строки для подсветки и структуры: hook, fn, if, loop, else, close, header, plain.</summary>
    public static string Classify(string line)
    {
        var t = StripComment(line).Trim();
        if (t == "") return "plain";
        var repeat = 0;
        var (text, op, kind) = Translate(t, true, [], ref repeat);
        return op switch
        {
            Op.Open => kind,
            Op.Else => "else",
            Op.Close => "close",
            _ => Regex.IsMatch(text, @"^(mod|version|author|about|engine|game|use)\s") ? "header" : "plain",
        };
    }

    /// <summary>Как имя будет называться в коде игры: «золото» → gold. null — имя и так латиницей.</summary>
    public static string? InCode(string word) => HasCyrillic(word) ? Name(word) : null;

    // ---------------------------------------------------------------- словарь

    /// <summary>Слово языка: как пишется, группа (event, header, command, block, op, fn), заготовка ($0 — курсор), смысл и что это в .modx.</summary>
    public sealed record Entry(string Word, string Group, string Snippet, string Meaning, string Modx);

    /// <summary>Все слова простого синтаксиса — для подсказок, справки под курсором, шпаргалки и блоков среды.</summary>
    public static readonly Entry[] Vocabulary =
    [
        new("когда игра началась", "event", "когда игра началась\n    $0\nконец", "Срабатывает, когда игрок начал игру.", "hook start"),
        new("когда игра загружена", "event", "когда игра загружена\n    $0\nконец", "Срабатывает, когда загрузили сохранение.", "hook load"),
        new("когда мод загружен", "event", "когда мод загружен\n    $0\nконец", "Сразу при запуске игры, когда мод подключился.", "hook ready"),
        new("каждый кадр", "event", "каждый кадр\n    $0\nконец", "Много раз в секунду, всё время, пока идёт игра.", "hook tick"),
        new("мод", "header", "мод \"$0\"", "Название мода.", "mod"),
        new("автор", "header", "автор $0", "Кто сделал мод.", "author"),
        new("версия", "header", "версия $0", "Номер версии: 1, 1.2 или 1.2.3.", "version"),
        new("описание", "header", "описание $0", "Пара слов о том, что делает мод.", "about"),
        new("движок", "header", "движок $0", "Движок игры: unity, godot, renpy, rpgmaker, unreal, lua, html5 или «свой».", "engine"),
        new("используй", "header", "используй \"$0\"", "Подключить кусочек кода с Рынка по названию.", "use"),
        new("пусть", "command", "пусть $0 = 0", "Завести свою переменную: «пусть бонус = 500».", "let"),
        new("дай", "command", "дай $0 100", "Прибавить: «дай золото 100» или «дай 100 золота».", "add"),
        new("забери", "command", "забери $0 10", "Отнять: «забери золото 10».", "-="),
        new("поставь", "command", "поставь $0 в 100", "Сделать равным: «поставь здоровье в 100» или просто «здоровье = 100».", "set"),
        new("умножь", "command", "умножь $0 на 2", "Умножить: «умножь золото на 2».", "*="),
        new("раздели", "command", "раздели $0 на 2", "Разделить: «раздели урон на 2».", "/="),
        new("скажи", "command", "скажи $0", "Показать игроку сообщение. Кавычки не нужны, {имя} подставит значение.", "say"),
        new("запиши", "command", "запиши $0", "Написать в журнал — игрок не увидит, а ты увидишь в «Выводе».", "log"),
        new("если", "block", "если $0\n    \nконец", "Сделать, только если условие верно: «если здоровье меньше 10».", "if"),
        new("иначе если", "block", "иначе если $0", "Ещё одно условие после «если».", "else if"),
        new("иначе", "block", "иначе", "Что сделать, если условие не подошло.", "else"),
        new("пока", "block", "пока $0\n    \nконец", "Повторять, пока условие верно.", "while"),
        new("повтори", "block", "повтори $0 раз\n    \nконец", "Повторить несколько раз: «повтори 3 раза».", "for"),
        new("для", "block", "для i от 1 до $0\n    \nконец", "Перебрать числа или список: «для i от 1 до 10», «для каждого x из список».", "for"),
        new("функция", "block", "функция $0()\n    \nконец", "Своя команда: «функция удвоить(х)» и внутри «верни х * 2».", "fn"),
        new("верни", "command", "верни $0", "Ответ функции.", "return"),
        new("вызови", "command", "вызови $0()", "Запустить свою функцию.", "call"),
        new("стоп", "command", "стоп", "Выйти из повторения.", "break"),
        new("дальше", "command", "дальше", "Сразу к следующему кругу повторения.", "continue"),
        new("конец", "block", "конец", "Закрывает «когда», «если», «пока», «повтори», «функция». Если пишешь с отступами — можно не писать.", "}"),
        new("больше", "op", "больше ", "«золото больше 100» — то же, что >.", ">"),
        new("меньше", "op", "меньше ", "«здоровье меньше 10» — то же, что <.", "<"),
        new("равно", "op", "равно ", "«уровень равно 5» — то же, что ==. Можно писать и «=».", "=="),
        new("не равно", "op", "не равно ", "Не совпадает — то же, что !=.", "!="),
        new("больше или равно", "op", "больше или равно ", "То же, что >=.", ">="),
        new("меньше или равно", "op", "меньше или равно ", "То же, что <=.", "<="),
        new("и", "op", "и ", "Оба условия верны.", "and"),
        new("или", "op", "или ", "Верно хотя бы одно.", "or"),
        new("не", "op", "не ", "Наоборот: «если не бессмертие».", "not"),
        new("да", "op", "да", "Правда (включено).", "true"),
        new("нет", "op", "нет", "Ложь (выключено).", "false"),
        new("случайно", "fn", "случайно(1, $0)", "Случайное число: случайно(1, 6) — как кубик.", "rand"),
        new("шанс", "fn", "шанс($0)", "Шанс в процентах: шанс(10) верно в 10 случаях из 100.", "chance"),
        new("длина", "fn", "длина($0)", "Сколько элементов в списке или букв в тексте.", "len"),
        new("мин", "fn", "мин($0)", "Меньшее из двух чисел.", "min"),
        new("макс", "fn", "макс($0)", "Большее из двух чисел.", "max"),
        new("округли", "fn", "округли($0)", "Округлить до целого.", "round"),
        new("модуль", "fn", "модуль($0)", "Число без минуса.", "abs"),
        new("между", "fn", "между($0, 0, 100)", "Не меньше и не больше: между(x, 0, 100).", "clamp"),
        new("выбери", "fn", "выбери($0)", "Случайный элемент списка.", "pick"),
    ];

    /// <summary>Служебные слова (не имена переменных): для подсветки и понимания кода в среде.</summary>
    public static readonly HashSet<string> Reserved = new(
        Vocabulary.SelectMany(e => e.Group is "event" ? [] : e.Word.Split(' ')).Concat(
        [
            "когда", "при", "каждый", "каждую", "каждое", "кадр", "тик", "всегда", "игра", "игру", "началась", "начинается", "загружена", "загрузилась",
            "загружен", "готова", "готов", "запущена", "старт", "старте", "раз", "раза", "на", "в", "во", "из", "от", "до", "каждого", "каждой", "то", "тогда",
            "делай", "игроку", "мне", "ему", "у", "игрока", "меня", "чем", "равен", "равна", "а", "правда", "ложь", "ничего", "пусто", "добавь", "прибавь",
            "отними", "убавь", "уменьши", "увеличь", "установи", "сделай", "напиши", "покажи", "выведи", "лог", "станет", "теперь", "будет", "переменная",
            "запомни", "хватит", "прерви", "пропусти", "всё", "все", "название", "end", "when", "every", "give", "take", "repeat", "times", "otherwise", "show",
        ]), StringComparer.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- строка

    static (string Text, Op Op, string Kind) Translate(string line, bool inCode, HashSet<string> known, ref int repeat)
    {
        var s = line.Replace('«', '"').Replace('»', '"').Replace('“', '"').Replace('”', '"');
        var close = s.StartsWith('}');
        var open = s.EndsWith('{');
        var core = s.TrimStart('}').TrimEnd('{').Trim();
        // «если x то», «пока x делай», «if x then», двоеточие в конце — как в Python.
        core = TailWords().Replace(core, "").TrimEnd(':').Trim();
        var low = Low(core);
        Match m;

        if (core == "" && close && !open) return ("}", Op.Close, "");
        if (Regex.IsMatch(low, @"^(конец|end|всё|все|закончили|готово)(\s.*)?$") && !low.Contains('=')) return ("}", Op.Close, "");

        // ----- блоки
        // «когда золото больше 100» внутри события — это «если».
        if (inCode && (m = Rx(core, @"^(?:когда|when)\s+(.+)$")).Success && Regex.IsMatch(Low(m.Groups[1].Value), @"[<>=]|больше|меньше|равн|than|equals"))
            return ($"if {Cond(m.Groups[1].Value, known)} {{", Op.Open, "if");
        if (Event(core) is { } ev) return ($"hook {ev} {{", Op.Open, "hook");
        if ((m = Rx(core, @"^(?:функция|function|func|fn|def|сделай\s+функцию)\s+([\p{L}_][\p{L}\w]*)\s*(?:\((.*)\))?$")).Success)
        {
            var pars = string.Join(", ", m.Groups[2].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Name));
            return ($"fn {Name(m.Groups[1].Value)}({pars}) {{", Op.Open, "fn");
        }
        if ((m = Rx(core, @"^(?:иначе\s+если|а\s+если|или\s+если|else\s+if|elif|otherwise\s+if)\s+(.+)$")).Success) return ($"}} else if {Cond(m.Groups[1].Value, known)} {{", Op.Else, "if");
        if (Regex.IsMatch(low, @"^(иначе|а\s+иначе|else|otherwise)$")) return ("} else {", Op.Else, "if");
        if ((m = Rx(core, @"^(?:если|if)\s+(.+)$")).Success) return ($"if {Cond(m.Groups[1].Value, known)} {{", Op.Open, "if");
        if ((m = Rx(core, @"^(?:пока|while)\s+(.+)$")).Success) return ($"while {Cond(m.Groups[1].Value, known)} {{", Op.Open, "loop");
        if ((m = Rx(core, @"^(?:повтори|повторяй|repeat)\s+(.+?)\s+(?:раз|раза|times|time)$")).Success
            || (m = Rx(core, @"^(.+?)\s+раз(?:а)?$")).Success && !HasCyrillic(Outside(m.Groups[1].Value).Replace("раз", "")) && Regex.IsMatch(m.Groups[1].Value, @"^[\w\s+\-*/().]+$"))
        {
            var n = Expr(m.Groups[1].Value, false, known);
            var v = $"mx_n{++repeat}";
            return (inCode ? $"for {v} in 1..{n} {{" : $"for {v} from 1 to {n} {{", Op.Open, "loop");
        }
        if ((m = Rx(core, @"^(?:для|for)\s+(?:каждого\s+|каждой\s+|каждый\s+|each\s+)?([\p{L}_][\p{L}\w]*)\s+(?:от|from)\s+(.+?)\s+(?:до|to)\s+(.+)$")).Success)
        {
            var (v, a, b) = (Name(m.Groups[1].Value), Expr(m.Groups[2].Value, false, known), Expr(m.Groups[3].Value, false, known));
            return (inCode ? $"for {v} in {a}..{b} {{" : $"for {v} from {a} to {b} {{", Op.Open, "loop");
        }
        if ((m = Rx(core, @"^(?:для|for)\s+(?:каждого\s+|каждой\s+|каждый\s+|each\s+)?([\p{L}_][\p{L}\w]*)\s+(?:в|во|из|in|of)\s+(.+)$")).Success)
            return ($"for {Name(m.Groups[1].Value)} in {Expr(m.Groups[2].Value, false, known)} {{", Op.Open, "loop");

        // ----- шапка мода
        if ((m = Rx(core, @"^(?:мод|название|имя\s+мода|mod\s+name)\s+(.+)$")).Success) return ("mod " + Quoted(m.Groups[1].Value), Op.Plain, "");
        if ((m = Rx(core, @"^(?:версия)\s+(.+)$")).Success) return ("version " + FullVersion(m.Groups[1].Value.Trim()), Op.Plain, "");
        if ((m = Rx(core, @"^(?:автор|author)\s+(.+)$")).Success) return ("author " + Quoted(m.Groups[1].Value), Op.Plain, "");
        if ((m = Rx(core, @"^(?:описание|о\s+моде|про\s+мод|about)\s+(.+)$")).Success) return ("about " + Quoted(m.Groups[1].Value), Op.Plain, "");
        if ((m = Rx(core, @"^(?:движок|engine)\s+(.+)$")).Success) return ("engine " + EngineId(m.Groups[1].Value), Op.Plain, "");
        if ((m = Rx(core, @"^(?:игра)\s+([A-Za-z0-9_.\-]+)$")).Success) return ("game " + m.Groups[1].Value.Trim(), Op.Plain, "");
        if ((m = Rx(core, @"^(?:используй|возьми\s+кусочек|кусочек)\s+(.+)$")).Success) return ("use " + m.Groups[1].Value.Trim(), Op.Plain, "");

        // ----- команды
        if ((m = Rx(core, @"^(?:пусть|переменная|запомни|заведи|let|var)\s+([\p{L}_][\p{L}\w]*)(?:\s*=\s*|\s+(?:будет|это|be)\s+)(.+)$")).Success)
            return ($"let {Name(m.Groups[1].Value)} = {Expr(m.Groups[2].Value, false, known)}", Op.Plain, "");
        if ((m = Rx(core, @"^(?:дай|добавь|прибавь|начисли|увеличь|give|increase)\s+(?:игроку\s+|мне\s+|ему\s+|player\s+|the\s+player\s+)?(.+)$")).Success && Amount(m.Groups[1].Value, known) is { } give)
            return ($"add {give.Target} {give.Value}", Op.Plain, "");
        if ((m = Rx(core, @"^(?:забери|отними|убавь|уменьши|вычти|сними|take|decrease|remove)\s+(?:у\s+игрока\s+|у\s+меня\s+|from\s+player\s+)?(.+)$")).Success && Amount(m.Groups[1].Value, known) is { } take)
            return ($"{take.Target} -= {take.Value}", Op.Plain, "");
        if ((m = Rx(core, @"^(?:умножь|multiply)\s+([\p{L}_][\p{L}\w.]*)\s+(?:на|by)\s+(.+)$")).Success) return ($"{Name(m.Groups[1].Value)} *= {Expr(m.Groups[2].Value, false, known)}", Op.Plain, "");
        if ((m = Rx(core, @"^(?:раздели|подели|divide)\s+([\p{L}_][\p{L}\w.]*)\s+(?:на|by)\s+(.+)$")).Success) return ($"{Name(m.Groups[1].Value)} /= {Expr(m.Groups[2].Value, false, known)}", Op.Plain, "");
        if ((m = Rx(core, @"^(?:поставь|установи|сделай|назначь|set|make)\s+([\p{L}_][\p{L}\w.]*)(?:\s*=\s*|\s+(?:в|на|равным|равной|to)\s+|\s+)(.+)$")).Success && !(low.StartsWith("set ") && core.Contains('=') && !HasCyrillic(core)))
            return ($"set {Name(m.Groups[1].Value)} = {Expr(m.Groups[2].Value, false, known)}", Op.Plain, "");
        if ((m = Rx(core, @"^([\p{L}_][\p{L}\w.]*)\s+(?:станет|теперь|будет|становится)\s+(.+)$")).Success)
            return ($"set {Name(m.Groups[1].Value)} = {Expr(m.Groups[2].Value, false, known)}", Op.Plain, "");
        if ((m = Rx(core, @"^(?:скажи|напиши|покажи|выведи|сообщи|say|show|tell)\s+(.+)$")).Success) return ("say " + Text(m.Groups[1].Value, known), Op.Plain, "");
        if ((m = Rx(core, @"^(?:запиши|в\s+лог|лог|log)\s+(.+)$")).Success) return ("log " + Text(m.Groups[1].Value, known), Op.Plain, "");
        if ((m = Rx(core, @"^(?:верни|ответ|return)(?:\s+(.+))?$")).Success) return ("return" + (m.Groups[1].Success ? " " + Expr(m.Groups[1].Value, false, known) : ""), Op.Plain, "");
        if (Regex.IsMatch(low, @"^(стоп|хватит|прерви|выйди\s+из\s+цикла|break|stop)$")) return ("break", Op.Plain, "");
        if (Regex.IsMatch(low, @"^(дальше|пропусти|следующий|continue|skip|next)$")) return ("continue", Op.Plain, "");
        if ((m = Rx(core, @"^(?:вызови|запусти|call|run)\s+([\p{L}_][\p{L}\w.]*)\s*(\(.*\))?$")).Success)
            return ($"{Name(m.Groups[1].Value)}{(m.Groups[2].Success ? Expr(m.Groups[2].Value, false, known) : "()")}", Op.Plain, "");

        // ----- всё остальное: обычный .modx; русские имена и слова в выражениях всё равно переводим
        var text = HasCyrillic(Outside(core)) || core.Contains('"') && Regex.IsMatch(core, @"\{[^}]*\p{IsCyrillic}") ? Expr(core, false, known) : core;
        if (close && open) return ("} " + text + " {", Op.Else, "if");
        if (open) return (text + " {", Op.Open, Regex.IsMatch(text, @"^(hook)\b") ? "hook" : Regex.IsMatch(text, @"^(fn|func|function|def)\b") ? "fn" : Regex.IsMatch(text, @"^(while|for)\b") ? "loop" : "if");
        if (close) return ("}", Op.Close, "");
        return (text, Op.Plain, "");
    }

    /// <summary>Событие: «когда игра началась», «каждый кадр», «when game loads», «hook start». null — это не событие.</summary>
    static string? Event(string core)
    {
        var low = Low(core);
        if (Regex.IsMatch(low, @"^(игра|мод)\s+(началась|начинается|стартовала|загрузилась|загружена|загружен|готова|готов|запустилась|запущена|запущен)$")) low = "когда " + low;
        var m = Regex.Match(low, @"^(?:hook|когда|при|в\s+начале|в\s+момент|when|on|every|каждый|каждую|каждое|всегда|always|forever)\b\s*(.*)$");
        if (!m.Success || low.StartsWith("when") && core.Contains('=')) return null;
        var head = low.Split(' ')[0];
        var rest = m.Groups[1].Value;
        if (head is "всегда" or "always" or "forever") return "tick";
        if (head == "hook") return rest switch
        {
            "ready" or "start" or "load" or "tick" => rest,
            _ => Event("когда " + rest) ?? rest,
        };
        var all = low;
        if (Regex.IsMatch(all, @"готов|ready|мод\s+загружен|mod\s+load|запуск|запущ|launch|включ")) return "ready";
        if (Regex.IsMatch(all, @"кадр|тик|frame|tick|update|обновл")) return "tick";
        if (Regex.IsMatch(all, @"начал|начин|начн|старт|start|нов\w*\s+игр|new\s+game|begin|в\s+начале")) return "start";
        if (Regex.IsMatch(all, @"загру[зж]|load|сохранени|save")) return "load";
        return head is "every" or "каждый" or "каждую" or "каждое" ? "tick" : null;
    }

    /// <summary>«золото 100», «100 золота», «золото на 100», «gold by 5» → (gold, 100).</summary>
    static (string Target, string Value)? Amount(string rest, HashSet<string> known)
    {
        var r = rest.Trim();
        Match m;
        if ((m = Regex.Match(r, @"^([\p{L}_][\p{L}\w.]*)\s+(?:на|by)\s+(.+)$")).Success
            || (m = Regex.Match(r, @"^([\p{L}_][\p{L}\w.]*)\s+(.+)$")).Success)
            return (Name(m.Groups[1].Value), Expr(m.Groups[2].Value, false, known));
        if ((m = Regex.Match(r, @"^(-?[\d.]+|\(.+\))\s+([\p{L}_][\p{L}\w.]*)$")).Success)
            return (Name(m.Groups[2].Value), Expr(m.Groups[1].Value, false, known));
        return null;
    }

    /// <summary>Текст для «скажи»: в кавычках — как есть; одно знакомое имя или выражение — значение; иначе — это просто слова.</summary>
    static string Text(string v, HashSet<string> known)
    {
        var t = v.Trim();
        if (t.Contains('"')) return Expr(t, false, known);
        var single = Regex.IsMatch(t, @"^[\p{L}_][\p{L}\w.]*(\(.*\))?$");
        var isExpr = single && (!HasCyrillic(t) || known.Contains(Low(t)) || Dict(Low(t)) is not null || t.Contains('('))
                     || Regex.IsMatch(t, @"^[\p{L}\w.\s]*[+\-*/%<>]=?[\p{L}\w.\s()+\-*/%]*$") && !Regex.IsMatch(t, @"[!?,]");
        return isExpr ? Expr(t, false, known) : Expr("\"" + t.Replace("\\", "\\\\") + "\"", false, known);
    }

    /// <summary>«версия 2» → 2.0.0, «версия 1.5» → 1.5.0.</summary>
    static string FullVersion(string v) => Regex.IsMatch(v, @"^\d+$") ? v + ".0.0" : Regex.IsMatch(v, @"^\d+\.\d+$") ? v + ".0" : v;

    static string Quoted(string v)
    {
        var t = v.Trim();
        return t.StartsWith('"') ? t : "\"" + t.Replace("\"", "\\\"") + "\"";
    }

    static string Cond(string v, HashSet<string> known) => Expr(v, true, known);

    // ---------------------------------------------------------------- выражения

    /// <summary>
    /// Русские слова в выражении: «и, или, не, да, нет», «больше, меньше, равно», встроенные функции (случайно(1, 6)),
    /// имена переменных (золото → gold). В условии одиночное «=» — это «==»: «если золото = 100».
    /// В строках переводится только то, что в {фигурных скобках}.
    /// </summary>
    public static string Expr(string src, bool cond, HashSet<string>? known = null)
    {
        var toks = new List<(char Kind, string Text)>(); // w — слово, s — строка, o — остальное
        for (var i = 0; i < src.Length;)
        {
            var c = src[i];
            if (c == '"')
            {
                var j = i + 1;
                while (j < src.Length && src[j] != '"') j += src[j] == '\\' ? 2 : 1;
                j = Math.Min(j + 1, src.Length);
                toks.Add(('s', src[i..j]));
                i = j;
            }
            else if (char.IsLetter(c) || c == '_')
            {
                var j = i;
                while (j < src.Length && (char.IsLetterOrDigit(src[j]) || src[j] is '_' or '.')) j++;
                toks.Add(('w', src[i..j].TrimEnd('.')));
                i += toks[^1].Text.Length;
            }
            else { toks.Add(('o', c.ToString())); i++; }
        }

        var sb = new StringBuilder();
        // Слова без пробелов между ними: «больше или равно», «не равно», «is bigger than».
        var words = new List<int>();
        for (var k = 0; k < toks.Count; k++)
        {
            var (kind, text) = toks[k];
            if (kind == 's') { sb.Append(InString(text, cond, known)); continue; }
            if (kind == 'o')
            {
                if (cond && text == "=" && !Prev(sb, "<>!=") && !(k + 1 < toks.Count && toks[k + 1].Text == "=")) { sb.Append("=="); continue; }
                sb.Append(text);
                continue;
            }
            // Фраза из нескольких слов: смотрим вперёд по словам, разделённым пробелами.
            var (phrase, used) = Phrase(toks, k);
            if (phrase is not null) { sb.Append(phrase); k += used - 1; continue; }
            var low = Low(text);
            var next = NextNonSpace(toks, k);
            var call = next is { Text: "(" };
            if (call && Builtin(low) is { } fn) { sb.Append(fn); continue; }
            sb.Append(low switch
            {
                "и" or "а" => "and",
                "или" => "or",
                "не" => "not",
                "да" or "правда" or "истина" or "вкл" or "включено" => "true",
                "нет" or "ложь" or "выкл" or "выключено" => "false",
                "ничего" or "пусто" => "null",
                _ => Name(text),
            });
        }
        return sb.ToString();
    }

    static bool Prev(StringBuilder sb, string chars) => sb.Length > 0 && chars.Contains(sb[^1]);

    static (char Kind, string Text)? NextNonSpace(List<(char Kind, string Text)> toks, int k)
    {
        for (var j = k + 1; j < toks.Count; j++) if (toks[j].Text != " ") return toks[j];
        return null;
    }

    static readonly (string[] Words, string Op)[] Phrases =
    [
        (["больше", "или", "равно"], ">="), (["меньше", "или", "равно"], "<="), (["не", "меньше"], ">="), (["не", "больше"], "<="),
        (["не", "равно"], "!="), (["не", "равен"], "!="), (["не", "равна"], "!="), (["больше", "чем"], ">"), (["меньше", "чем"], "<"),
        (["больше"], ">"), (["меньше"], "<"), (["равно"], "=="), (["равен"], "=="), (["равна"], "=="),
        (["is", "bigger", "than"], ">"), (["is", "more", "than"], ">"), (["is", "greater", "than"], ">"), (["is", "less", "than"], "<"), (["is", "smaller", "than"], "<"),
        (["bigger", "than"], ">"), (["more", "than"], ">"), (["greater", "than"], ">"), (["less", "than"], "<"), (["smaller", "than"], "<"),
        (["is", "not"], "!="), (["equals"], "=="),
    ];

    static (string? Op, int Used) Phrase(List<(char Kind, string Text)> toks, int k)
    {
        foreach (var (words, op) in Phrases)
        {
            var j = k;
            var ok = true;
            for (var w = 0; w < words.Length; w++)
            {
                if (w > 0)
                {
                    if (j >= toks.Count || toks[j].Text != " ") { ok = false; break; }
                    while (j < toks.Count && toks[j].Text == " ") j++;
                }
                if (j >= toks.Count || toks[j].Kind != 'w' || Low(toks[j].Text) != words[w]) { ok = false; break; }
                j++;
            }
            if (ok) return (op, j - k);
        }
        return (null, 0);
    }

    /// <summary>Строка: текст не трогаем, а {выражения} внутри переводим.</summary>
    static string InString(string lit, bool cond, HashSet<string>? known) =>
        Regex.Replace(lit, @"\{([^{}]*)\}", m => "{" + Expr(m.Groups[1].Value, false, known) + "}");

    static string? Builtin(string low) => low switch
    {
        "случайно" or "случайное" or "случайное_число" or "random" => "rand",
        "шанс" => "chance",
        "длина" or "размер" => "len",
        "мин" or "минимум" => "min",
        "макс" or "максимум" => "max",
        "округли" or "округлить" => "round",
        "модуль" => "abs",
        "текст" => "str",
        "число" => "num",
        "целое" => "int",
        "выбери" or "любой" => "pick",
        "сумма" => "sum",
        "между" or "ограничь" => "clamp",
        "корень" => "sqrt",
        "степень" => "pow",
        _ => null,
    };

    /// <summary>Частые слова игр: так «золото» из мода совпадает с переменной gold в игре.</summary>
    static string? Dict(string low)
    {
        (string Stem, string Name)[] stems =
        [
            ("золот", "gold"), ("деньг", "money"), ("денег", "money"), ("монет", "coins"), ("здоровь", "health"), ("жизн", "lives"),
            ("очк", "score"), ("счёт", "score"), ("счет", "score"), ("опыт", "xp"), ("уровен", "level"), ("уровн", "level"),
            ("скорост", "speed"), ("урон", "damage"), ("брон", "armor"), ("патрон", "ammo"), ("энерги", "energy"), ("выносливост", "stamina"),
        ];
        foreach (var (stem, name) in stems) if (low.StartsWith(stem)) return name;
        return low switch { "мана" or "ману" or "маны" => "mana", "сила" or "силу" or "силы" => "strength", "хп" => "hp", _ => null };
    }

    /// <summary>Имя переменной или функции: русское → латиница (золото → gold, кот → kot), английское — как есть.</summary>
    public static string Name(string word)
    {
        if (!HasCyrillic(word)) return word;
        return string.Join('.', word.Split('.').Select(part =>
        {
            if (!HasCyrillic(part)) return part;
            return Dict(Low(part)) ?? Latin(part);
        }));
    }

    /// <summary>Просто латиница, без словаря: «Много» → «mnogo».</summary>
    public static string Latin(string word)
    {
        var sb = new StringBuilder();
        foreach (var ch in Low(word)) sb.Append(Translit.TryGetValue(ch, out var lat) ? lat : char.IsLetterOrDigit(ch) || ch == '_' ? ch.ToString() : "");
        var id = sb.ToString();
        return id == "" ? "x" : char.IsDigit(id[0]) ? "v" + id : id;
    }

    static readonly Dictionary<char, string> Translit = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "yo", ['ж'] = "zh", ['з'] = "z", ['и'] = "i", ['й'] = "y",
        ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n", ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t", ['у'] = "u", ['ф'] = "f",
        ['х'] = "h", ['ц'] = "ts", ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "sch", ['ъ'] = "", ['ы'] = "y", ['ь'] = "", ['э'] = "e", ['ю'] = "yu", ['я'] = "ya",
        ['і'] = "i", ['ї'] = "yi", ['є'] = "ye", ['ґ'] = "g",
    };

    static string EngineId(string v) => Low(v.Trim().Trim('"')) switch
    {
        "юнити" or "юнити (bepinex)" => "unity",
        "годот" => "godot",
        "ренпай" or "ренпи" or "ren'py" => "renpy",
        "рпг мейкер" or "rpg maker" or "рпг-мейкер" => "rpgmaker",
        "анрил" or "unreal engine" => "unreal",
        "луа" => "lua",
        "лав" or "love" or "löve" => "love",
        "веб" or "html" or "браузер" => "html5",
        "свой" or "свой движок" or "кастом" or "самодельный" => "custom",
        var x => x.Replace(' ', '-'),
    };

    // ---------------------------------------------------------------- мелочи

    /// <summary>Имена, которые автор сам завёл: пусть x = …, x = …, дай x 5. «скажи x» с таким именем — значение, а не слово.</summary>
    static HashSet<string> Declared(string[] lines)
    {
        var set = new HashSet<string>();
        foreach (var l in lines)
        {
            var t = StripComment(l).Trim();
            var m = Regex.Match(t, @"^(?:(?:пусть|переменная|запомни|заведи|let|var|для|for|дай|добавь|give|add|поставь|set)\s+(?:каждого\s+)?)?([\p{L}_][\p{L}\w]*)\s*(?:=|\+=|-=|будет|станет|от|в|из|in|\d)", RegexOptions.IgnoreCase);
            if (m.Success) set.Add(Low(m.Groups[1].Value));
            foreach (Match p in Regex.Matches(t, @"^(?:функция|function|fn)\s+[\p{L}_][\p{L}\w]*\s*\(([^)]*)\)", RegexOptions.IgnoreCase))
                foreach (var name in p.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) set.Add(Low(name));
        }
        return set;
    }

    static Match Rx(string s, string pattern) => Regex.Match(s, pattern, RegexOptions.IgnoreCase);

    static string Low(string s) => s.ToLowerInvariant().Replace('ё', 'е');

    static bool HasCyrillic(string s) => s.Any(c => c is >= 'Ѐ' and <= 'ӿ');

    /// <summary>Текст вне кавычек.</summary>
    static string Outside(string s) => Regex.Replace(s, @"""(?:\\.|[^""\\])*""?", "");

    static int Indent(string line)
    {
        var n = 0;
        foreach (var c in line)
        {
            if (c == ' ') n++;
            else if (c == '\t') n += 4;
            else break;
        }
        return n;
    }

    /// <summary># вне строки — комментарий.</summary>
    static string StripComment(string line)
    {
        var inStr = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '\\' && inStr) { i++; continue; }
            if (line[i] is '"' or '«' or '»') inStr = line[i] == '"' ? !inStr : line[i] == '«';
            else if (line[i] == '#' && !inStr) return line[..i];
        }
        return line;
    }

    [GeneratedRegex(@"^native\s+([\p{L}\w#]+)\s*\{$")]
    private static partial Regex NativeStart();

    [GeneratedRegex(@"\s+(то|тогда|делай|then|do)$", RegexOptions.IgnoreCase)]
    private static partial Regex TailWords();

    /// <summary>Английские слова простого синтаксиса, которых нет в обычном .modx.</summary>
    [GeneratedRegex(@"^(end\b|when\s+(?!.*=)|on\s+\w|every\s|always$|forever$|give\s|take\s|repeat\s|otherwise\b|increase\s|decrease\s|multiply\s|divide\s|show\s|tell\s|set\s+\S+\s+to\s|stop$|skip$)", RegexOptions.IgnoreCase)]
    private static partial Regex KidEnglish();
}
