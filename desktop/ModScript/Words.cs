using System.Text;
using System.Text.RegularExpressions;

namespace ModScriptX;

/// <summary>
/// 21.0: «Простые слова» — ModScript X словами, по-русски и по-английски: «когда игра началась {», «сказать "Привет"»,
/// «дать золото 100», «если здоровье меньше 20 {», «каждые 5 секунд {», «с шансом 10 {». Перед разбором каждая строка
/// переводится в обычную строку языка — одна в одну, поэтому номера строк, ошибки и отладчик не сдвигаются.
/// Обычный ModScript X (hook start, set, add, if …) работает как раньше, слова и код можно смешивать.
/// </summary>
public static partial class Words
{
    /// <summary>Переменные игры по-русски — те же, что у кусочков и моста (gold, health …), в любом падеже, что нужен командам.</summary>
    public static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["золото"] = "gold", ["золота"] = "gold", ["здоровье"] = "health", ["здоровья"] = "health", ["скорость"] = "speed", ["скорости"] = "speed",
        ["уровень"] = "level", ["уровня"] = "level", ["волна"] = "wave", ["волны"] = "wave", ["очки"] = "score", ["очков"] = "score",
        ["опыт"] = "xp", ["опыта"] = "xp", ["урон"] = "damage", ["урона"] = "damage", ["урон_врагов"] = "enemy_damage", ["урона_врагов"] = "enemy_damage",
    };

    /// <summary>Слова команд — для подсветки и подсказок (не переменные).</summary>
    public static readonly string[] Keywords =
    [
        "мод", "версия", "автор", "описание", "игра", "движок", "использовать", "подключить", "пусть", "переменная", "файл", "настройка", "копировать",
        "когда", "началась", "начинается", "старт", "запустилась", "загрузился", "загружен", "включился", "загрузилось", "загрузили", "сохранение",
        "каждый", "каждые", "каждую", "кадр", "секунду", "секунды", "секунд", "сек", "минуту", "минуты", "минут", "мин",
        "сказать", "скажи", "показать", "покажи", "написать", "напиши", "журнал", "дать", "дай", "прибавить", "прибавь", "добавить", "добавь",
        "забрать", "забери", "отнять", "отними", "убрать", "поставить", "поставь", "сделать", "сделай", "умножить", "умножь", "разделить", "раздели",
        "если", "то", "иначе", "пока", "повторить", "повтори", "раз", "раза", "для", "каждого", "из", "в", "на", "с", "шансом", "от", "до",
        "стоп", "хватит", "вернуть", "верни", "прервать", "дальше", "функция", "вызвать", "больше", "меньше", "равно", "равен", "равна", "чем",
        "и", "или", "не", "да", "нет", "случайно", "шанс",
        "when", "starts", "begins", "loads", "loaded", "save", "every", "frame", "second", "seconds", "minute", "minutes", "show", "give", "take",
        "multiply", "divide", "by", "otherwise", "repeat", "times", "each", "with", "chance", "stop", "function", "random", "than", "more", "less",
        "least", "most", "equals",
    ];

    const string Name = @"[\p{L}_][\p{L}\w.\[\]]*";
    const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>Шаблон → замена ($1 … и {no} — номер строки). Порядок важен: длинные фразы раньше коротких.</summary>
    static readonly (Regex Re, string To)[] Heads = Build(
    [
        // Шапка и верх файла.
        (@"^мод\s+(.+)$", "mod $1"), (@"^версия\s+(.+)$", "version $1"), (@"^автор\s+(.+)$", "author $1"), (@"^описание\s+(.+)$", "about $1"),
        (@"^игра\s+(\S+)$", "game $1"), (@"^движок\s+(\S+)$", "engine $1"), (@"^(?:использовать|подключить)\s+("".+)$", "use $1"),
        (@"^(?:пусть|переменная)\s+(.+)$", "let $1"),
        // Команды ModScript: файлы и настройки игры — в том же файле .modx.
        (@"^файл\s+("".+)$", "write $1"), (@"^настройка\s+("".+)$", "config $1"), (@"^копировать\s+(""[^""]*"")\s+в\s+(""[^""]*"")$", "copy $1 to $2"),
        // События.
        (@"^когда\s+(?:игра\s+)?(?:началась|начинается|запустилась|старт)\s*\{$", "hook start {"),
        (@"^когда\s+мод\s+(?:загрузился|загружен|включился)\s*\{$", "hook ready {"),
        (@"^когда\s+(?:загрузилось\s+сохранение|сохранение\s+загрузилось|загрузили\s+сохранение)\s*\{$", "hook load {"),
        (@"^каждый\s+кадр\s*\{$", "hook tick {"),
        (@"^каждую\s+секунду\s*\{$", "every 1 {"), (@"^каждую\s+минуту\s*\{$", "every 60 {"),
        (@"^каждые\s+(.+?)\s+(?:секунд[уы]?|сек)\s*\{$", "every $1 {"), (@"^каждые\s+(.+?)\s+(?:минут[уы]?|мин)\s*\{$", "every ($1) * 60 {"),
        (@"^when\s+(?:the\s+)?game\s+(?:starts|begins|started)\s*\{$", "hook start {"),
        (@"^when\s+(?:the\s+)?mod\s+(?:loads|is\s+loaded|loaded)\s*\{$", "hook ready {"),
        (@"^when\s+(?:a\s+|the\s+)?save\s+(?:loads|is\s+loaded|loaded)\s*\{$", "hook load {"),
        (@"^every\s+frame\s*\{$", "hook tick {"), (@"^every\s+second\s*\{$", "every 1 {"), (@"^every\s+minute\s*\{$", "every 60 {"),
        (@"^every\s+(.+?)\s+seconds?\s*\{$", "every $1 {"), (@"^every\s+(.+?)\s+minutes?\s*\{$", "every ($1) * 60 {"),
        // Сказать и журнал.
        (@"^(?:сказать|скажи|показать|покажи|написать|напиши|show)\s+(.+)$", "say $1"), (@"^(?:в\s+журнал|журнал)\s+(.+)$", "log $1"),
        // Числа игры: дать, забрать, поставить, умножить, разделить («дать золото 100» и «дать 100 золота»).
        (@"^(?:дать|дай|прибавить|прибавь|добавить|добавь|give)\s+(-?\d+(?:\.\d+)?)\s+(" + Name + @")$", "add $2 $1"),
        (@"^(?:дать|дай|прибавить|прибавь|добавить|добавь|give)\s+(" + Name + @")\s+(.+)$", "add $1 $2"),
        (@"^(?:забрать|забери|отнять|отними|убрать|take)\s+(-?\d+(?:\.\d+)?)\s+(" + Name + @")$", "set $2 = $2 - ($1)"),
        (@"^(?:забрать|забери|отнять|отними|убрать|take)\s+(" + Name + @")\s+(.+)$", "set $1 = $1 - ($2)"),
        (@"^(?:поставить|поставь|сделать|сделай|make)\s+(" + Name + @")\s*(?:=|\s+на\s+|\s+to\s+|\s)\s*(.+)$", "set $1 = $2"),
        (@"^(?:умножить|умножь)\s+(" + Name + @")\s+на\s+(.+)$", "set $1 = $1 * ($2)"), (@"^multiply\s+(" + Name + @")\s+by\s+(.+)$", "set $1 = $1 * ($2)"),
        (@"^(?:разделить|раздели)\s+(" + Name + @")\s+на\s+(.+)$", "set $1 = $1 / ($2)"), (@"^divide\s+(" + Name + @")\s+by\s+(.+)$", "set $1 = $1 / ($2)"),
        // Условия и повторы.
        (@"^\}\s*иначе\s+если\s+(.+?)(?:\s+то)?\s*\{$", "} else if $1 {"), (@"^\}\s*иначе\s*\{$", "} else {"),
        (@"^\}\s*otherwise\s+if\s+(.+?)\s*\{$", "} else if $1 {"), (@"^\}\s*otherwise\s*\{$", "} else {"),
        (@"^если\s+(.+?)(?:\s+то)?\s*\{$", "if $1 {"), (@"^пока\s+(.+?)\s*\{$", "while $1 {"),
        (@"^(?:повторить|повтори)\s+(.+?)\s+раза?\s*\{$", "for _repeat{no} in 1..($1) {"), (@"^repeat\s+(.+?)\s+times?\s*\{$", "for _repeat{no} in 1..($1) {"),
        (@"^для\s+каждого\s+([\p{L}_][\p{L}\w]*)\s+(?:в|из)\s+(.+?)\s*\{$", "for $1 in $2 {"), (@"^for\s+each\s+([\p{L}_][\p{L}\w]*)\s+in\s+(.+?)\s*\{$", "for $1 in $2 {"),
        (@"^с\s+шансом\s+(.+?)\s*%?\s*\{$", "if chance($1) {"), (@"^with\s+(?:a\s+)?chance\s+(?:of\s+)?(.+?)\s*%?\s*\{$", "if chance($1) {"),
        (@"^with\s+(.+?)\s*%\s+chance\s*\{$", "if chance($1) {"),
        // Выход, функции.
        (@"^(?:стоп|хватит|stop)$", "return"), (@"^(?:вернуть|верни)(?:\s+(.+))?$", "return $1"), (@"^прервать$", "break"), (@"^дальше$", "continue"),
        (@"^(?:функция|function)\s+([\p{L}_][\p{L}\w]*)\s*\((.*)\)\s*\{$", "fn $1($2) {"), (@"^вызвать\s+(.+)$", "call $1"),
    ]);

    static (Regex, string)[] Build((string Pattern, string To)[] list) => list.Select(x => (new Regex(x.Pattern, Opt), x.To)).ToArray();

    /// <summary>Команды ModScript, в которых после слова команды — пути и ключи настроек: в них слова не переводятся.</summary>
    static readonly Regex Classic = new(@"^(?:write|config|ini|copy|edit|entry|dialogue|mail|image|json|needs|файл|настройка|копировать)\b", Opt);

    /// <summary>Слова-операторы в выражениях: длинные фразы раньше коротких.</summary>
    static readonly (Regex Re, string To)[] Ops = Build(
    [
        (@"больше\s+или\s+равно", ">="), (@"меньше\s+или\s+равно", "<="), (@"не\s+меньше(?:\s+чем)?", ">="), (@"не\s+больше(?:\s+чем)?", "<="),
        (@"не\s+равн[оаы]", "!="), (@"равн[оаы]|равен", "=="), (@"больше(?:\s+чем)?", ">"), (@"меньше(?:\s+чем)?", "<"),
        (@"или", "or"), (@"и", "and"), (@"не", "not"), (@"да|правда", "true"), (@"нет|ложь", "false"),
        (@"(?:is\s+)?at\s+least", ">="), (@"(?:is\s+)?at\s+most", "<="), (@"(?:is\s+)?(?:more|greater)\s+than", ">"), (@"(?:is\s+)?less\s+than", "<"),
        (@"is\s+not", "!="), (@"equals", "=="),
    ]);

    static readonly Regex RandRu = new(@"(?<![\p{L}\w])случайно\s+от\s+(-?[\p{L}\w.]+)\s+до\s+(-?[\p{L}\w.]+)", Opt);
    static readonly Regex RandEn = new(@"(?<![\p{L}\w])random\s+(?:from\s+)?(-?[\p{L}\w.]+)\s+to\s+(-?[\p{L}\w.]+)", Opt);
    static readonly Regex Fns = new(@"(?<![\p{L}\w.])(случайно|шанс|мин|макс|округлить)\s*\(", Opt);

    static readonly Regex[] OpWords = Ops.Select(o => new Regex(@"(?<![\p{L}\w.])(?:" + o.Re + @")(?![\p{L}\w])", Opt)).ToArray();
    static readonly Regex Ident = new(@"(?<![\p{L}\w.])[\p{L}_][\p{L}\w]*", Opt);

    /// <summary>
    /// Строка словами → строка языка (отступ и комментарий сохраняются). Строка, где переводить нечего, возвращается
    /// как есть; no — номер строки (для скрытых имён счётчиков «повторить N раз»).
    /// </summary>
    public static string Line(string line, int no)
    {
        var code = Code(line);
        var t = code.Trim();
        if (t == "" || !NeedsWork(t)) return line;
        var indent = line[..(line.Length - line.TrimStart().Length)];
        var comment = line[code.Length..];
        var head = t;
        foreach (var (re, to) in Heads)
        {
            var m = re.Match(t);
            if (!m.Success) continue;
            head = m.Result(to.Replace("{no}", no.ToString())).TrimEnd();
            break;
        }
        if (!Classic.IsMatch(t)) head = Expr(head);
        return head == t ? line : indent + head + (comment == "" ? "" : " " + comment.TrimStart());
    }

    /// <summary>Есть ли в строке не-ASCII буквы или английские слова команд — иначе переводить нечего (быстрый путь).</summary>
    static bool NeedsWork(string t)
    {
        foreach (var c in t) if (c > 127 && char.IsLetter(c)) return true;
        return Regex.IsMatch(t, @"^(?:\}\s*otherwise|when|every|show|give|take|make|multiply|divide|repeat|for\s+each|with|stop|function)\b|\b(?:at\s+least|at\s+most|more\s+than|greater\s+than|less\s+than|is\s+not|equals|random)\b", Opt);
    }

    /// <summary>Выражение: слова-операторы, «случайно от 1 до 50», русские имена функций и переменных игры. Строки не трогаем, кроме {вставок}.</summary>
    public static string Expr(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var (part, inString) in Parts(text))
            sb.Append(inString ? Regex.Replace(part, @"\{([^{}]+)\}", m => "{" + Plain(m.Groups[1].Value) + "}") : Plain(part));
        return sb.ToString();
    }

    static string Plain(string code)
    {
        code = RandRu.Replace(code, "rand($1, $2)");
        code = RandEn.Replace(code, "rand($1, $2)");
        code = Fns.Replace(code, m => m.Groups[1].Value.ToLowerInvariant() switch
        {
            "случайно" => "rand(", "шанс" => "chance(", "мин" => "min(", "макс" => "max(", _ => "round(",
        });
        for (var i = 0; i < Ops.Length; i++) code = OpWords[i].Replace(code, Ops[i].To);
        return Ident.Replace(code, m => Aliases.TryGetValue(m.Value, out var en) ? en : m.Value);
    }

    /// <summary>Строка без комментария (# вне кавычек).</summary>
    static string Code(string line)
    {
        var inStr = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '\\' && inStr) { i++; continue; }
            if (line[i] == '"') inStr = !inStr;
            else if (line[i] == '#' && !inStr) return line[..i];
        }
        return line;
    }

    /// <summary>Куски строки: код и строки в кавычках (с кавычками).</summary>
    static IEnumerable<(string Part, bool InString)> Parts(string text)
    {
        var start = 0;
        var inStr = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && inStr) { i++; continue; }
            if (text[i] != '"') continue;
            if (!inStr) { if (i > start) yield return (text[start..i], false); start = i; inStr = true; }
            else { yield return (text[start..(i + 1)], true); start = i + 1; inStr = false; }
        }
        if (start < text.Length) yield return (text[start..], inStr);
    }
}
