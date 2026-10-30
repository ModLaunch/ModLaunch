using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Creator;
using MX = ModScriptX;

namespace ModLaunch.Views;

/// <summary>
/// 21.0: ModScript X простыми словами. Слева — «Команды» (готовые строки словами и кусочки кода по названию) и «Учебник»:
/// 13 коротких уроков на 1–2 часа, у каждого пример, кнопка «Попробовать» (песочница, 10 секунд игры) и «Вставить».
/// </summary>
public sealed partial class XStudio
{
    /// <summary>Для снимков: забыть открытые файлы проекта (следующий вход прочитает их с диска).</summary>
    internal static void Forget(string projectId) => Sessions.Remove(projectId);

    // ---------------------------------------------------------------- команды

    Control BlocksSide()
    {
        var col = new StackPanel { Spacing = 2, Margin = new Thickness(8, 0, 8, 14) };
        col.Children.Add(new Border { Padding = new Thickness(8, 0, 8, 4), Child = Ui.Text(I18n.T("v21.w.lead"), "small muted", wrap: true) });
        foreach (var group in SimpleWords.Groups)
        {
            col.Children.Add(Caption(I18n.T("v21.wg." + group)));
            foreach (var c in SimpleWords.All.Where(c => c.Group == group))
            {
                var code = SimpleWords.Plain(SimpleWords.Code(c));
                var top = c.Top;
                var b = new Button
                {
                    Classes = { "ghost" }, Padding = new Thickness(8, 6), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Content = Ui.Col(2, Ui.Text(I18n.T($"v21.w.{c.Id}"), "small strong"),
                        new TextBlock { Text = code.Split('\n')[0], FontFamily = Mono, FontSize = 11, Foreground = HubLook.Cyan, TextTrimming = TextTrimming.CharacterEllipsis }),
                };
                ToolTip.SetTip(b, code);
                b.Click += (_, _) => { if (top) InsertTop(code); else InsertBlock(code); };
                col.Children.Add(b);
            }
        }
        col.Children.Add(Caption(I18n.T("v21.wg.pieces")));
        col.Children.Add(new Border { Padding = new Thickness(8, 0, 8, 6), Child = Ui.Text(I18n.T("v21.w.pieces"), "small muted", wrap: true) });
        var t = TargetFor(ActiveDoc());
        var pieces = new StackPanel { Spacing = 6, Margin = new Thickness(4, 0) };
        foreach (var p in Pieces.All()) pieces.Children.Add(PieceItem(p, t));
        col.Children.Add(pieces);
        return col;
    }

    /// <summary>Событие, функция, файл — в конец файла (вне других блоков), через пустую строку.</summary>
    void InsertTop(string code)
    {
        if (_s.Active is not { Modx: true } d) { NewFile(); d = _s.Active!; }
        var text = d.Editor.Text;
        d.Editor.CaretIndex = text.Length;
        d.Editor.Insert((text.TrimEnd() == "" ? "" : text.EndsWith("\n\n") ? "" : text.EndsWith('\n') ? "\n" : "\n\n") + code + "\n");
        d.Editor.FocusText();
    }

    /// <summary>Кусочек — строкой use под шапкой файла (после mod, engine и других use); уже есть — только подсказка.</summary>
    void AddUse(string name)
    {
        if (_s.Active is not { Modx: true } d) { NewFile(); d = _s.Active!; }
        var text = d.Editor.Text;
        if (Pieces.Used(text, name)) { MainWindow.Current?.Toast(I18n.T("v21.piece.already")); return; }
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var at = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var head = MX.Words.Line(lines[i], i + 1).Trim();
            if (head.EndsWith('{')) break;
            if (Regex.IsMatch(head, @"^(?:mod|version|author|about|game|engine|use)\b")) at = i + 1;
        }
        var offset = lines.Take(at).Sum(l => l.Length + 1);
        var line = Pieces.Line(name) + "\n" + (at < lines.Length && lines[at].Trim() != "" && MX.ModxDoc.ReadUse(lines[at], at + 1) is null ? "\n" : "");
        d.Editor.CaretIndex = Math.Min(offset, text.Length);
        if (offset > text.Length) line = "\n" + line;
        d.Editor.Insert(line);
        d.Editor.FocusText();
        MainWindow.Current?.Toast(I18n.T("v21.piece.added", ("name", name)));
        if (_s.Side == "blocks") RenderSide();
    }

    /// <summary>Кусочек в панели: золотая плашка с названием и автором (купленный — «куплено у …»).</summary>
    Control PieceItem(Piece p, MX.Target t)
    {
        var c = Avalonia.Media.Color.Parse(p.Color);
        var tile = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(7),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(c, 0), new GradientStop(Look.Mix(c, Colors.Black, 0.45), 1) },
            },
            Child = Ui.Icon(p.Icon, 14, Brushes.White),
        };
        var used = _s.Active is { } d && Pieces.Used(d.Editor.Text ?? "", p.Name);
        var sub = used ? I18n.T("v21.piece.inProject") : p.Free ? I18n.T("v21.piece.free") : I18n.T("v21.piece.bought", ("seller", p.Seller));
        var text = Ui.Col(1, new TextBlock { Text = p.Title, FontSize = 12.5, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
            Ui.Text(sub, "small", color: used ? Ui.Res("Good") : Ui.Res("Muted")));
        text.VerticalAlignment = VerticalAlignment.Center;
        var gold = Avalonia.Media.Color.Parse("#F5B94A");
        var box = new Border
        {
            CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 6), Cursor = new Cursor(StandardCursorType.Hand),
            Background = new SolidColorBrush(Avalonia.Media.Color.FromArgb(Look.IsLight ? (byte)0x26 : (byte)0x1C, gold.R, gold.G, gold.B)),
            BorderBrush = new SolidColorBrush(Avalonia.Media.Color.FromArgb(0x66, gold.R, gold.G, gold.B)), BorderThickness = new Thickness(1),
            Child = Ui.Row(10, tile, text),
        };
        var missing = p.Events.Where(e => !t.Events.Contains(e)).ToList();
        ToolTip.SetTip(box, (p.About == "" ? "" : p.About + "\n") + I18n.T("v21.piece.tip", ("line", Pieces.Line(p.Title)))
            + (missing.Count > 0 ? "\n" + I18n.T("v16.event.missing", ("engine", t.Title)) : ""));
        box.PointerEntered += (_, _) => box.BorderBrush = new SolidColorBrush(gold);
        box.PointerExited += (_, _) => box.BorderBrush = new SolidColorBrush(Avalonia.Media.Color.FromArgb(0x66, gold.R, gold.G, gold.B));
        box.Tapped += (_, _) => AddUse(p.Title);
        return box;
    }

    // ---------------------------------------------------------------- учебник

    /// <summary>Урок: пример словами на двух языках. {PIECE} — название бесплатного кусочка на языке программы.</summary>
    internal static readonly (string Id, string Ru, string En)[] Lessons =
    [
        ("hello", "когда игра началась {\n    сказать \"Привет! Это мой мод\"\n}", "when the game starts {\n    say \"Hi! This is my mod\"\n}"),
        ("events", "когда мод загрузился {\n    в журнал \"мод включён\"\n}\n\nкогда игра началась {\n    сказать \"Игра началась\"\n}\n\nкогда загрузилось сохранение {\n    сказать \"С возвращением!\"\n}",
            "when the mod loads {\n    log \"mod is on\"\n}\n\nwhen the game starts {\n    say \"The game has started\"\n}\n\nwhen the save loads {\n    say \"Welcome back!\"\n}"),
        ("numbers", "когда игра началась {\n    дать золото 100\n    забрать здоровье 5\n    поставить скорость = 8\n    умножить урон_врагов на 2\n    сказать \"Золото: {золото}, здоровье: {здоровье}\"\n}",
            "when the game starts {\n    give gold 100\n    take health 5\n    set speed = 8\n    multiply enemy_damage by 2\n    say \"Gold: {gold}, health: {health}\"\n}"),
        ("text", "когда игра началась {\n    сказать \"У тебя {золото} золота\"\n    сказать \"Вдвое больше: {золото * 2}\"\n    в журнал \"это видно только в журнале\"\n}",
            "when the game starts {\n    say \"You have {gold} gold\"\n    say \"Twice as much: {gold * 2}\"\n    log \"only the log shows this\"\n}"),
        ("vars", "пусть бонус = 50\n\nкогда игра началась {\n    дать золото бонус\n    пусть двойной = бонус * 2\n    сказать \"Бонус {бонус}, вдвое {двойной}\"\n}",
            "let bonus = 50\n\nwhen the game starts {\n    give gold bonus\n    let double = bonus * 2\n    say \"Bonus {bonus}, doubled {double}\"\n}"),
        ("if", "когда игра началась {\n    если золото больше 50 {\n        сказать \"Ты богат!\"\n    } иначе {\n        сказать \"Копим дальше\"\n    }\n    если здоровье меньше 20 и скорость больше 0 {\n        сказать \"Беги!\"\n    }\n}",
            "when the game starts {\n    if gold more than 50 {\n        say \"You are rich!\"\n    } otherwise {\n        say \"Keep saving\"\n    }\n    if health less than 20 and speed more than 0 {\n        say \"Run!\"\n    }\n}"),
        ("repeat", "когда игра началась {\n    повторить 3 раза {\n        дать золото 10\n        сказать \"Золото: {золото}\"\n    }\n}",
            "when the game starts {\n    repeat 3 times {\n        give gold 10\n        say \"Gold: {gold}\"\n    }\n}"),
        ("timer", "каждые 5 секунд {\n    дать золото 1\n    сказать \"+1 золото, всего {золото}\"\n}", "every 5 seconds {\n    give gold 1\n    say \"+1 gold, {gold} in total\"\n}"),
        ("luck", "когда игра началась {\n    с шансом 50 {\n        сказать \"Повезло!\"\n    }\n    дать золото случайно от 1 до 100\n    сказать \"Золото: {золото}\"\n}",
            "when the game starts {\n    with chance 50 {\n        say \"Lucky!\"\n    }\n    give gold random 1 to 100\n    say \"Gold: {gold}\"\n}"),
        ("fn", "функция награда(волна) {\n    вернуть волна * 50\n}\n\nкогда игра началась {\n    дать золото награда(3)\n    сказать \"За третью волну: {награда(3)}\"\n}",
            "function reward(wave) {\n    return wave * 50\n}\n\nwhen the game starts {\n    give gold reward(3)\n    say \"Wave three pays {reward(3)}\"\n}"),
        ("pieces", "использовать \"{PIECE}\" (gold_bonus = 3)\n\nкогда игра началась {\n    сказать \"Золото: {золото}\"\n}", "use \"{PIECE}\" (gold_bonus = 3)\n\nwhen the game starts {\n    say \"Gold: {gold}\"\n}"),
        ("files", "файл \"readme.txt\" = \"Мой мод. Сделано в ModLaunch\"\nнастройка \"MyMod.cfg\" [General] Speed = 2\n\nкогда игра началась {\n    сказать \"Файлы ставятся вместе с модом\"\n}",
            "write \"readme.txt\" = \"My mod. Made with ModLaunch\"\nconfig \"MyMod.cfg\" [General] Speed = 2\n\nwhen the game starts {\n    say \"Files go in together with the mod\"\n}"),
        ("pro", "hook start {\n    set gold = gold + 100\n    if gold > 50 {\n        say \"Rich: {gold}\"\n    }\n}\n\nкогда игра началась {\n    сказать \"Слова и код работают вместе\"\n}",
            "hook start {\n    set gold = gold + 100\n    if gold > 50 {\n        say \"Rich: {gold}\"\n    }\n}\n\nwhen the game starts {\n    say \"Words and code work together\"\n}"),
    ];

    /// <summary>Переменные игры для «Попробовать»: мало золота и здоровья — чтобы сработали условия уроков.</summary>
    const string LessonVars = "gold = 20\nhealth = 15\nspeed = 5\nenemy_damage = 3\nlevel = 1\nwave = 1\nscore = 0";

    internal static string LessonCode((string Id, string Ru, string En) l) => (SimpleWords.Ru ? l.Ru : l.En).Replace("{PIECE}", I18n.T("v21.piece.gold"));

    /// <summary>Запуск урока в песочнице: 10 секунд игры (600 кадров), чтобы сработали и таймеры.</summary>
    internal static MX.Sandbox.Result TryLesson(string code) =>
        MX.Sandbox.Run(MX.ModxDoc.Parse($"mod \"{I18n.T("v21.learn.mod")}\"\n" + code), MX.Sandbox.ParseVars(LessonVars, []), 600);

    static HashSet<string> LessonsDone() => (Settings.Data.Str("xLessons") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

    static string? _lessonOpen;
    static readonly Dictionary<string, MX.Sandbox.Result> LessonRuns = [];

    Control DocsSide()
    {
        var done = LessonsDone();
        var open = _lessonOpen ?? Lessons.FirstOrDefault(l => !done.Contains(l.Id)).Id ?? Lessons[0].Id;
        var col = new StackPanel { Spacing = 8, Margin = new Thickness(12, 0, 12, 14) };
        var count = Lessons.Count(l => done.Contains(l.Id));
        col.Children.Add(Ui.Col(6, Ui.Text(I18n.T("v21.learn.lead"), "small muted", wrap: true),
            new ProgressBar { Minimum = 0, Maximum = Lessons.Length, Value = count, Height = 6, MinHeight = 6, Foreground = Color },
            Ui.Text(I18n.T("v21.learn.progress", ("n", count), ("all", Lessons.Length)), "tiny muted")));
        for (var i = 0; i < Lessons.Length; i++)
        {
            var l = Lessons[i];
            col.Children.Add(l.Id == open ? LessonCard(i + 1, l, done.Contains(l.Id)) : LessonRow(i + 1, l, done.Contains(l.Id)));
        }
        // Шпаргалка полного языка: встроенные функции и клавиши среды.
        var builtins = new WrapPanel();
        foreach (var f in MX.ModxDoc.Builtins)
            builtins.Children.Add(new Border { CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 2), Margin = new Thickness(0, 0, 5, 5), Background = HubLook.Raised, Child = HubLook.MonoText(f + "()", 10.5, Ui.Res("Text")) });
        col.Children.Add(Ui.Col(6, Ui.Text(I18n.T("v16.x.docs.builtins"), "small strong"), builtins));
        var keys = Ui.Col(3);
        foreach (var (k, key) in new[]
        {
            ("F5", "v21.key.run"), ("F6", "v21.key.inject"), ("Ctrl+Space", "v15.ms.key.suggest"), ("F12 · Ctrl+клик", "v19.cmd.definition"), ("F2", "v19.cmd.rename"),
            ("Shift+Alt+F", "v19.cmd.format"), ("F9", "v19.cmd.breakpoint"), ("Ctrl+P", "v19.key.palette"),
        })
            keys.Children.Add(Ui.Row(8, new TextBlock { Text = k, FontFamily = Mono, FontSize = 11, Width = 104, Foreground = HubLook.Cyan }, Ui.Text(I18n.T(key), "tiny muted", wrap: true)));
        col.Children.Add(Ui.Col(6, Ui.Text(I18n.T("v19.docs.keys"), "small strong"), keys));
        return col;
    }

    static Control LessonMark(int n, bool done, IBrush color) => new Border
    {
        Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = done ? color : HubLook.Raised, VerticalAlignment = VerticalAlignment.Center,
        Child = done ? Ui.Icon(Icons.Check, 12, Brushes.White)
            : new TextBlock { Text = n.ToString(), FontSize = 11, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
    };

    Control LessonRow(int n, (string Id, string Ru, string En) l, bool done)
    {
        var b = new Button
        {
            Classes = { "ghost" }, Padding = new Thickness(6, 5), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = Ui.Row(10, LessonMark(n, done, Color), Ui.Text(I18n.T($"v21.l.{l.Id}"), done ? "small muted" : "small")),
        };
        var id = l.Id;
        b.Click += (_, _) => { _lessonOpen = id; RenderSide(); };
        return b;
    }

    Control LessonCard(int n, (string Id, string Ru, string En) l, bool done)
    {
        var code = LessonCode(l);
        var box = new Border
        {
            Background = HubLook.Raised, CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 8),
            Child = new SelectableTextBlock { Text = code, FontFamily = Mono, FontSize = 11.5, LineHeight = 17, TextWrapping = TextWrapping.Wrap },
        };
        var output = new StackPanel { Spacing = 2 };
        void Show(MX.Sandbox.Result r)
        {
            output.Children.Clear();
            foreach (var line in r.Output.Take(12))
                output.Children.Add(new TextBlock { Text = Regex.Replace(line, @"^\[([^\]]*)\]\s*", "$1 · "), FontFamily = Mono, FontSize = 11, TextWrapping = TextWrapping.Wrap });
            foreach (var e in r.Errors.Take(4))
                output.Children.Add(new TextBlock { Text = CreatorPage.LxMsg(e), FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Res(e.Warning ? "Muted" : "Bad") });
            if (r.Output.Count == 0 && r.Errors.Count == 0) output.Children.Add(Ui.Text(I18n.T("v21.learn.silent"), "tiny muted", wrap: true));
        }
        if (LessonRuns.TryGetValue(l.Id, out var last)) Show(last);
        var id = l.Id;
        var tryIt = Ui.Button(I18n.T("v21.learn.try"), () =>
        {
            var r = TryLesson(code);
            LessonRuns[id] = r;
            Show(r);
            var set = LessonsDone();
            if (r.Ok && set.Add(id))
            {
                Settings.Data["xLessons"] = string.Join(",", set);
                Settings.Save();
            }
        }, "primary", Icons.Play);
        var insert = Ui.Button(I18n.T("v21.learn.insert"), () => InsertTop(code), "ghost", Icons.Plus);
        var next = Ui.Button(I18n.T("v21.learn.next"), () =>
        {
            var at = Array.FindIndex(Lessons, x => x.Id == id);
            _lessonOpen = Lessons[(at + 1) % Lessons.Length].Id;
            RenderSide();
        }, "ghost", Icons.ChevronRight);
        var buttons = new WrapPanel();
        foreach (var b in new Control[] { tryIt, insert, next }) { b.Margin = new Thickness(0, 0, 6, 6); buttons.Children.Add(b); }
        var col = Ui.Col(8, Ui.Row(10, LessonMark(n, done, Color), Ui.Text(I18n.T($"v21.l.{l.Id}"), "strong")),
            Ui.Text(I18n.T($"v21.l.{l.Id}.text"), "small muted", wrap: true), box, buttons, output);
        return new Border
        {
            Background = HubLook.Panel, BorderBrush = Color, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10), Child = col,
        };
    }

    /// <summary>Для снимков: открыть урок и показать его запуск.</summary>
    internal static void DemoLesson(string id)
    {
        _lessonOpen = id;
        LessonRuns[id] = TryLesson(LessonCode(Lessons.First(l => l.Id == id)));
    }

    [SelfTest]
    static string LessonsRunInSandbox()
    {
        var saved = MX.ModxDoc.Pieces;
        try
        {
            MX.ModxDoc.Pieces = Pieces.Source;
            foreach (var l in Lessons)
                foreach (var code in new[] { l.Ru, l.En })
                {
                    var text = code.Replace("{PIECE}", "Double gold");
                    var doc = MX.ModxDoc.Parse("mod \"Lesson\"\n" + text);
                    if (!doc.Ok) throw new Exception($"{l.Id}: {string.Join("; ", doc.Diags.Where(d => !d.Warning).Select(d => $"{d.Line}: {d.Message}"))}");
                    var r = MX.Sandbox.Run(doc, MX.Sandbox.ParseVars(LessonVars, []), 600);
                    if (!r.Ok || r.Output.Count == 0) throw new Exception($"{l.Id}: {string.Join(" | ", r.Errors.Select(e => e.Message))} out={r.Output.Count}");
                    // Строки есть только при загруженных ресурсах (в --selfcheck без окна их нет).
                    if (I18n.Raw("en", "v21.side.learn") is not null)
                        foreach (var lang in new[] { "ru", "en" })
                            if (I18n.Raw(lang, $"v21.l.{l.Id}") is null || I18n.Raw(lang, $"v21.l.{l.Id}.text") is null) throw new Exception($"no {lang} text for lesson {l.Id}");
                }
            // Урок «Файлы»: команды ModScript попадают в сборку вместе с кодом X.
            var files = MX.ModxDoc.Parse("mod \"L\"\n" + Lessons.First(l => l.Id == "files").Ru);
            if (files.Classic.Writes.Count != 1 || files.Classic.Configs.Count != 1 || !files.HasClassic) throw new Exception("classic part lost");
            return $"{Lessons.Length} lessons × 2 languages parse and run";
        }
        finally { MX.ModxDoc.Pieces = saved; }
    }

    // ---------------------------------------------------------------- рынок

    /// <summary>Лот Рынка, который ставится в проект кусочком: часть кода или скрипт файлом .modx.</summary>
    internal static bool IsPiece(Listing l) => l.Kind is "code" or "script" && l.FileName.EndsWith(MX.ModxDoc.Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// На странице лота: как кусочек выглядит в ModScript X (золотая карточка и строка use) и кнопка «В проект»,
    /// когда он уже в библиотеке.
    /// </summary>
    internal static Control PieceNote(Listing l)
    {
        var gold = Avalonia.Media.Color.Parse("#F5B94A");
        var tile = new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(10),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Avalonia.Media.Color.Parse("#C9A8FF"), 0), new GradientStop(Look.Mix(Avalonia.Media.Color.Parse("#C9A8FF"), Colors.Black, 0.45), 1) },
            },
            Child = Ui.Icon(Icons.Package, 19, Brushes.White),
        };
        var card = new Border
        {
            CornerRadius = new CornerRadius(12), Padding = new Thickness(12), MinWidth = 240, HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Avalonia.Media.Color.FromArgb(0x22, gold.R, gold.G, gold.B)), BorderBrush = new SolidColorBrush(Avalonia.Media.Color.FromArgb(0x90, gold.R, gold.G, gold.B)), BorderThickness = new Thickness(1),
            Child = Ui.Row(10, tile, Ui.Col(2, Ui.Text(l.Title, "strong"), Ui.Text(I18n.T("v21.piece.bought", ("seller", l.Author)), "small muted"))),
        };
        var line = new Border
        {
            Background = HubLook.Raised, CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8), HorizontalAlignment = HorizontalAlignment.Left,
            Child = new SelectableTextBlock { Text = Pieces.Line(l.Title), FontFamily = HubLook.Mono, FontSize = 12.5, Foreground = new SolidColorBrush(Avalonia.Media.Color.Parse("#FFD58A")) },
        };
        var look = Ui.Row(14, card, line);
        foreach (var c in look.Children) c.VerticalAlignment = VerticalAlignment.Center;
        var col = Ui.Col(10, Ui.Row(8, Ui.Icon(Icons.Package, 16, HubLook.Cyan), Ui.Text(I18n.T("v21.mk.piece.title"), "h3")),
            Ui.Text(I18n.T("v21.mk.piece.text"), "small muted", wrap: true), look);
        if (Features.Inject.Get("mk_" + l.Id) is not null)
            col.Children.Add(Ui.Button(I18n.T("v21.mk.piece.use"), () => UsePiece(l.Title), "primary", Icons.Plus));
        return new Border { Classes = { "card" }, Padding = new Thickness(16, 14), Child = col };
    }

    /// <summary>Кусочек — строкой use в последний открытый проект ModScript X; проектов нет — анкета нового.</summary>
    internal static void UsePiece(string name)
    {
        if (Settings.Data.Str("xLast") is not { } id || XProjects.Get(id) is null)
        {
            MainWindow.Current?.Toast(I18n.T("v21.mk.piece.noProject"));
            CreatorPage.OpenForm();
            return;
        }
        var page = new XStudio(id, side: "blocks");
        MainWindow.Current?.Navigate(() => page);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => page.AddUse(name), Avalonia.Threading.DispatcherPriority.Background);
    }
}
