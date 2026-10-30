using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Creator;
using MX = ModScriptX;

namespace ModLaunch.Views;

/// <summary>
/// 22.0: большие игры по-настоящему. В «Командах» — «Числа игры» именно этого движка (деньги, здоровье, броня, розыск
/// в GTA V; деньги и здоровье в Cyberpunk 2077…): одна кнопка — строка словами, которая меняет игрока в игре.
/// В «Мосте» — карточка установки загрузчика: шаги, ссылки на скачивание и папка игры, пока загрузчика нет.
/// </summary>
public sealed partial class XStudio
{
    /// <summary>Слово ModScript X для числа игры (по-русски — как в учебнике, по-английски — имя в коде).</summary>
    static readonly Dictionary<string, string> RuNumber = new()
    {
        ["gold"] = "деньги", ["health"] = "здоровье", ["max_health"] = "макс_здоровье", ["armor"] = "броня", ["wanted"] = "розыск", ["level"] = "уровень",
    };

    /// <summary>Строка словами для числа: деньги — дать, остальное — поставить, только чтение — показать.</summary>
    internal static string NumberCode(string name, bool readOnly, bool ru)
    {
        var word = ru ? RuNumber.GetValueOrDefault(name, name) : name;
        if (readOnly) return ru ? $"сказать \"{I18n.Raw("ru", $"v22.num.{name}") ?? word}: {{{word}}}\"" : $"say \"{name}: {{{name}}}\"";
        var value = name switch { "gold" => "1000", "max_health" => "300", "wanted" => "0", _ => "100" };
        if (name == "gold") return ru ? $"дать {word} {value}" : $"give {word} {value}";
        return ru ? $"поставить {word} = {value}" : $"set {word} = {value}";
    }

    /// <summary>«Числа игры» движка — в «Командах», только если движок их знает.</summary>
    void NumbersGroup(StackPanel col, MX.Target t)
    {
        if (t.GameNumbers.Count == 0) return;
        col.Children.Add(Caption(I18n.T("v22.nums.title")));
        col.Children.Add(new Border { Padding = new Thickness(8, 0, 8, 4), Child = Ui.Text(I18n.T("v22.nums.lead", ("engine", t.Title)), "small muted", wrap: true) });
        foreach (var (name, readOnly) in t.GameNumbers)
        {
            var code = NumberCode(name, readOnly, SimpleWords.Ru);
            var label = I18n.T(t.Id == "cyberpunk" && name == "health" ? "v22.num.health.pct" : $"v22.num.{name}");
            var b = new Button
            {
                Classes = { "ghost" }, Padding = new Thickness(8, 6), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = Ui.Col(2, Ui.Row(6, Ui.Text(label, "small strong"), readOnly ? Ui.Text(I18n.T("v22.nums.read"), "small muted") : new Control()),
                    new TextBlock { Text = code, FontFamily = Mono, FontSize = 11, Foreground = HubLook.Cyan, TextTrimming = TextTrimming.CharacterEllipsis }),
            };
            ToolTip.SetTip(b, code);
            var number = name;
            b.Click += (_, _) =>
            {
                // В песочнице у числа должно быть значение — иначе «не задано, беру 0».
                if (!System.Text.RegularExpressions.Regex.IsMatch(_s.Vars, $@"(?m)^\s*{number}\s*="))
                    _s.Vars = _s.Vars.TrimEnd() + $"\n{number} = {(number == "gold" ? "1000" : number == "wanted" ? "2" : "100")}";
                InsertBlock(code);
            };
            col.Children.Add(b);
        }
    }

    /// <summary>Где скачать загрузчик для движка моста: (подпись, адрес).</summary>
    static readonly Dictionary<string, (string Name, string Url)[]> LoaderLinks = new()
    {
        ["unity-mono"] = [("BepInEx 5", "https://github.com/BepInEx/BepInEx/releases")],
        ["unity-il2cpp"] = [("BepInEx 6 IL2CPP", "https://builds.bepinex.dev/projects/bepinex_be")],
        ["unreal"] = [("UE4SS", "https://github.com/UE4SS-RE/RE-UE4SS/releases")],
        ["reengine"] = [("REFramework", "https://github.com/praydog/REFramework/releases")],
        ["godot"] = [("Godot Mod Loader", "https://github.com/GodotModding/godot-mod-loader/releases")],
        ["redengine"] = [("Cyber Engine Tweaks", "https://github.com/maximegmd/CyberEngineTweaks/releases")],
        ["rage-gta5"] = [("ScriptHookV", "http://www.dev-c.com/gtav/scripthookv/"), ("ScriptHookVDotNet 3", "https://github.com/scripthookvdotnet/scripthookvdotnet/releases")],
        ["rage-rdr2"] = [("ScriptHookRDR2", "http://www.dev-c.com/rdr2/scripthookrdr2/"), ("ScriptHookRDR2 .NET", "https://github.com/Halen84/ScriptHookRDR2DotNet-V2/releases")],
    };

    /// <summary>Карточка «Поставить загрузчик»: только когда путь — код, а загрузчика в игре ещё нет.</summary>
    static Control? SetupCard(MX.BridgeReport? b, string? path)
    {
        if (b is not { Code: true, LoaderReady: false } || !I18n.Has($"v22.setup.{b.Engine}")) return null;
        var steps = new StackPanel { Spacing = 6 };
        var n = 0;
        foreach (var step in I18n.T($"v22.setup.{b.Engine}").Split('\n').Where(s => s.Trim() != ""))
        {
            var no = new Border
            {
                Width = 20, Height = 20, CornerRadius = new CornerRadius(10), BorderBrush = HubLook.Cyan, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock { Text = (++n).ToString(), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = HubLook.Cyan },
            };
            var line = new DockPanel { Children = { Dockd(no), Ui.Text(step.Trim(), "small", wrap: true) } };
            no.Margin = new Thickness(0, 0, 10, 0);
            steps.Children.Add(line);
        }
        var buttons = new WrapPanel();
        foreach (var (name, url) in LoaderLinks.GetValueOrDefault(b.Engine) ?? [])
        {
            var get = Ui.Button(I18n.T("v22.setup.get", ("loader", name)), () => Ui.OpenUrl(url), "", Icons.Download);
            get.Margin = new Thickness(0, 0, 8, 8);
            buttons.Children.Add(get);
        }
        if (path is not null)
        {
            var open = Ui.Button(I18n.T("games.openFolder"), () => Actions.OpenFolder(path), "ghost", Icons.Folder);
            open.Margin = new Thickness(0, 0, 8, 8);
            buttons.Children.Add(open);
        }
        return new Border
        {
            Padding = new Thickness(14, 12), CornerRadius = new CornerRadius(4), BorderBrush = Ui.Res("Warn"), BorderThickness = new Thickness(1), Background = HubLook.Raised,
            Child = Ui.Col(10, Ui.Row(8, Ui.Icon(Icons.Download, 15, Ui.Res("Warn")), Ui.Text(I18n.T("v22.setup.title", ("loader", b.Loader)), "strong")), steps, buttons,
                Ui.Text(I18n.T("v22.setup.after"), "small muted", wrap: true)),
        };
    }

    static Control Dockd(Control c)
    {
        DockPanel.SetDock(c, Dock.Left);
        return c;
    }

    /// <summary>Числа игры каждого движка на простых словах — правильный код; у каждого кода-движка с загрузчиком есть шаги установки.</summary>
    [SelfTest]
    static string GameNumbersAndSetup()
    {
        var n = 0;
        foreach (var t in MX.Target.All.Where(t => t.GameNumbers.Count > 0))
            foreach (var ru in new[] { true, false })
            {
                var body = string.Join("\n", t.GameNumbers.Select(x => "    " + NumberCode(x.Name, x.ReadOnly, ru)));
                var doc = MX.ModxDoc.Parse($"mod \"T\"\nengine {t.Id}\nhook start {{\n{body}\n}}\n");
                if (!doc.Ok) throw new Exception($"{t.Id}: {string.Join("; ", doc.Diags.Where(d => !d.Warning).Select(d => d.Message))}");
                var text = string.Concat(t.Emit(doc, null).Select(f => f.Text));
                foreach (var (name, readOnly) in t.GameNumbers)
                    if (!text.Contains(name)) throw new Exception($"{t.Id}: {name} not bound");
                n++;
            }
        foreach (var engine in LoaderLinks.Keys)
            if (I18n.Raw("en", "v22.setup.title") is not null && I18n.Raw("en", $"v22.setup.{engine}") is null) throw new Exception("no setup steps for " + engine);
        return $"{n} game-number sets parse and bind, setup steps for {LoaderLinks.Count} loaders";
    }
}
