using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Views;

/// <summary>
/// 13.0: раздел «ModScript» — язык снова на виду, отдельным пунктом слева. 18.0: вкладки раздела «Скрипты»
/// после библиотеки — «Консоль» (пишете команды — справа сразу вывод, переменные и что получится в моде),
/// «Команды» (все команды с поиском, «новое в 13.0», копировать или отправить в консоль) и «Функции»
/// (стандартная библиотека). Среда ModScript X — только у проектов («Мои проекты»); старая вкладка «ModScript X»
/// (песочница инжектов 11.0) и кнопка «Среда ModScript X» здесь повторяли её и убраны.
/// </summary>
public sealed partial class CreatorPage
{
    /// <summary>Вкладка раздела ModScript X (20.0): overview, console, commands, functions.</summary>
    string _ms = "overview";
    string _msGroup = "";
    bool _msNewOnly;
    static string? _msText;
    ScriptEditor? _msEditor;
    DispatcherTimer? _msDebounce;

    static readonly JsonSerializerOptions MsPretty = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Пример консоли: немного старых и новых команд, чтобы сразу было что запустить.</summary>
    static string ConsoleExample() => I18n.T("v13.ms.console.example").Replace("\\n", "\n");

    static string ConsoleText
    {
        get => _msText ??= Settings.Data.Str("msConsole") is { Length: > 0 } saved ? saved : ConsoleExample();
        set
        {
            _msText = value;
            // Пишется в настройки сразу, на диск — после паузы в наборе (Console) или при уходе со страницы.
            Settings.Data["msConsole"] = value.Length > 20000 ? value[..20000] : value;
        }
    }

    List<Control> MsTabs()
    {
        var tabs = new List<Control>();
        foreach (var (id, key) in new[] { ("overview", "v20.x.tab.overview"), ("console", "v13.ms.tab.console"), ("commands", "v13.ms.tab.commands"), ("functions", "v13.ms.tab.functions") })
        {
            var tab = id;
            var b = Ui.Button(I18n.T(key), () => { if (_ms == tab) return; _ms = tab; Build(); }, "hub-tab");
            if (_ms == id) b.Classes.Add("active");
            tabs.Add(b);
        }
        return tabs;
    }

    // ---------------------------------------------------------------- консоль

    Control Console()
    {
        _msEditor = new ScriptEditor { MinHeight = 520, Text = ConsoleText };
        var output = new StackPanel { Spacing = 14 };
        void Render()
        {
            output.Children.Clear();
            output.Children.Add(ConsoleResult(_msEditor.Text ?? ""));
        }
        _msEditor.TextChanged += () =>
        {
            ConsoleText = _msEditor.Text ?? "";
            _msDebounce?.Stop();
            _msDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _msDebounce.Tick += (_, _) => { _msDebounce!.Stop(); Render(); Settings.Save(); };
            _msDebounce.Start();
        };
        Render();

        var example = Ui.Button(I18n.T("v13.ms.example"), () => { ConsoleText = ConsoleExample(); Settings.Save(); Build(); }, "ghost", Icons.Undo);
        var toProject = Ui.Button(I18n.T("v13.ms.toProject"), () =>
        {
            var text = _msEditor.Text ?? "";
            var b = ModScript.Compile(text);
            var name = b.Name != "" ? b.Name : I18n.T("v13.ms.consoleProject");
            if (!text.Contains("\nmod ") && !text.StartsWith("mod ")) text = $"mod \"{name}\"\nversion 1.0.0\ngame stardew-valley\n\n" + text;
            OpenProject(Projects.Create(name, text));
        }, "ghost", Icons.FilePlus);
        var file = Ui.Row(8, Ui.Dot(HubLook.Violet, 6), HubLook.MonoText("console.mls", 12, Ui.Res("Text")));
        var strip = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        // 18.0: «Запустить» убрана — консоль и так выполняется на лету, пока вы пишете.
        var actions = Ui.Row(6, example, toProject);
        DockPanel.SetDock(actions, Dock.Right);
        strip.Children.Add(actions);
        file.VerticalAlignment = VerticalAlignment.Center;
        strip.Children.Add(file);

        var left = HubLook.Frame(Ui.Col(0, strip, _msEditor), new Thickness(12));
        var right = HubLook.Frame(output, new Thickness(18, 16));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,2*"), ColumnSpacing = 16 };
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    /// <summary>Что вышло из текста консоли: ошибки, вывод, переменные и во что это соберётся.</summary>
    static Control ConsoleResult(string text)
    {
        var b = ModScript.Compile(text);
        // Консоль — не мод: без «mod» и «game» ошибок нет, проверяется только сам код.
        var diags = b.Diags.Where(d => d.Message is not ("err.noName" or "err.noGame")).ToList();
        var col = Ui.Col(16);
        var errors = diags.Count(d => !d.Warning);
        var state = Ui.Row(8, Ui.Dot(errors == 0 ? Ui.Res("Good") : Ui.Res("Bad"), 9),
            new TextBlock { Text = errors == 0 ? I18n.T("v13.ms.ok") : I18n.T("v13.ms.errors", ("n", errors)), FontWeight = FontWeight.SemiBold, FontSize = 15 });
        col.Children.Add(Ui.Col(6, HubLook.Eyebrow(I18n.T("v13.ms.result")), state));

        var mono = HubLook.Mono;
        if (diags.Count > 0)
        {
            var list = Ui.Col(4);
            foreach (var d in diags.Take(12))
                list.Children.Add(new SelectableTextBlock
                {
                    Text = $"{d.Line,3}  {ModScript.Explain(d, I18n.T)}", FontFamily = mono, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                    Foreground = d.Warning ? Ui.Res("Warn") : Ui.Res("Bad"),
                });
            col.Children.Add(list);
        }

        var log = b.Log.Count == 0 ? I18n.T("v13.ms.noOutput") : string.Join("\n", b.Log.TakeLast(60));
        col.Children.Add(Ui.Col(6, HubLook.Eyebrow(I18n.T("v13.ms.output")), new SelectableTextBlock
        {
            Text = log, FontFamily = mono, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Foreground = b.Log.Count == 0 ? Ui.Res("Faint") : Ui.Res("Text"),
        }));

        if (b.Vars.Count > 0)
        {
            var vars = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 14, RowSpacing = 3 };
            var row = 0;
            foreach (var (k, v) in b.Vars.Where(kv => kv.Key != "args").Take(24))
            {
                vars.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var name = HubLook.MonoText(k, 12, HubLook.Violet);
                var value = new TextBlock { Text = v.Length > 120 ? v[..120] + "…" : v, FontFamily = mono, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
                Grid.SetRow(name, row);
                Grid.SetRow(value, row);
                Grid.SetColumn(value, 1);
                vars.Children.Add(name);
                vars.Children.Add(value);
                row++;
            }
            col.Children.Add(Ui.Col(6, HubLook.Eyebrow(I18n.T("v13.ms.vars")), vars));
        }

        // Во что соберётся: content.json (правки, настройки игрока, токены) и файлы пакета.
        var content = new JsonObject();
        if (b.Options.Count > 0) content["ConfigSchema"] = new JsonObject(b.Options.Select(o => new KeyValuePair<string, JsonNode?>(o.Name, o.Schema.DeepClone())));
        if (b.Tokens.Count > 0) content["DynamicTokens"] = new JsonArray(b.Tokens.Select(x => (JsonNode)x.DeepClone()).ToArray());
        if (b.Changes.Count > 0) content["Changes"] = new JsonArray(b.Changes.Select(c => (JsonNode)c.DeepClone()).ToArray());
        var files = b.Writes.Select(w => w.Path).Concat(b.Copies.Select(c => c.To)).Concat(b.Configs.Select(c => "config/" + c.File)).Distinct().ToList();
        if (content.Count > 0 || files.Count > 0)
        {
            var summary = I18n.T("v13.ms.builds", ("changes", b.Changes.Count), ("options", b.Options.Count), ("files", files.Count));
            var parts = Ui.Col(8, HubLook.Eyebrow(I18n.T("v13.ms.makes")), Ui.Text(summary, "small muted", wrap: true));
            if (content.Count > 0)
            {
                var json = content.ToJsonString(MsPretty).Split('\n');
                parts.Children.Add(new Border
                {
                    Background = HubLook.Raised, CornerRadius = new CornerRadius(3), Padding = new Thickness(10, 8), MaxHeight = 320,
                    Child = new ScrollViewer
                    {
                        Content = new SelectableTextBlock { Text = string.Join("\n", json.Take(160)) + (json.Length > 160 ? "\n…" : ""), FontFamily = mono, FontSize = 11.5 },
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    },
                });
            }
            foreach (var f in files.Take(8)) parts.Children.Add(HubLook.MonoText("+ " + f, 11.5));
            col.Children.Add(parts);
        }
        return col;
    }

    // ---------------------------------------------------------------- команды

    Control Commands()
    {
        var search = new TextBox { Watermark = I18n.T("v13.ms.search"), Text = _filter, Width = 360, HorizontalAlignment = HorizontalAlignment.Left };
        var chips = new WrapPanel();
        // Две колонки, высота — по паре карточек (а не по самой длинной команде, как у UniformGrid).
        var grid = new StackPanel();
        var count = Ui.Text("", "small muted");

        void Fill()
        {
            grid.Children.Clear();
            var q = (search.Text ?? "").Trim();
            var list = ModScriptCommands.All.Where(c => (_msGroup == "" || c.Group == _msGroup) && (!_msNewOnly || c.New) && ModScriptCommands.Matches(c, q)).ToList();
            for (var i = 0; i < list.Count; i += 2)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
                row.Children.Add(CommandCard(list[i]));
                if (i + 1 < list.Count)
                {
                    var second = CommandCard(list[i + 1]);
                    Grid.SetColumn(second, 1);
                    row.Children.Add(second);
                }
                grid.Children.Add(row);
            }
            count.Text = I18n.T("v13.ms.count", ("n", list.Count));
            if (list.Count == 0) grid.Children.Add(Ui.Text(I18n.T("cr.lib.none"), "muted"));
        }
        void Chips()
        {
            chips.Children.Clear();
            foreach (var g in new[] { "" }.Concat(ModScriptCommands.Groups))
            {
                var id = g;
                var chip = Ui.Button(g == "" ? I18n.T("mk.kind.all") : I18n.T("v13.ms.g." + g), () => { _msGroup = id; Chips(); Fill(); }, "chip");
                chip.Margin = new Thickness(0, 0, 6, 6);
                if (_msGroup == g) chip.Classes.Add("active");
                chips.Children.Add(chip);
            }
            var fresh = Ui.Button(I18n.T("v13.ms.newOnly", ("n", ModScriptCommands.NewCount)), () => { _msNewOnly = !_msNewOnly; Chips(); Fill(); }, "chip", Icons.Sparkles);
            fresh.Margin = new Thickness(10, 0, 6, 6);
            if (_msNewOnly) fresh.Classes.Add("active");
            chips.Children.Add(fresh);
        }
        search.TextChanged += (_, _) => Fill();
        Chips();
        Fill();
        var head = new DockPanel();
        DockPanel.SetDock(count, Dock.Right);
        count.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(count);
        head.Children.Add(search);
        return Ui.Col(12, head, chips, grid);
    }

    Control CommandCard(ModScriptCommands.Cmd c)
    {
        var syntax = ModScriptCommands.Syntax(c).Replace("\\n", "\n");
        var name = Ui.Row(8, HubLook.MonoText(c.Name, 14, HubLook.Violet));
        if (c.New) name.Children.Add(new Border { Background = HubLook.Signature(), CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 1), VerticalAlignment = VerticalAlignment.Center, Child = HubLook.MonoText("13.0", 9.5, Brushes.White) });
        var copy = Ui.Button("", () => { MainWindow.Current?.Clipboard?.SetTextAsync(syntax); MainWindow.Current?.Toast(I18n.T("ctx.copied")); }, "icon ghost", Icons.Copy, I18n.T("v13.ms.copy"));
        var send = Ui.Button("", () =>
        {
            ConsoleText = ConsoleText.TrimEnd() + "\n" + syntax + "\n";
            _ms = "console";
            Build();
        }, "icon ghost", Icons.Play, I18n.T("v13.ms.send"));
        var tools = Ui.Row(2, copy, send);
        var top = new DockPanel();
        DockPanel.SetDock(tools, Dock.Right);
        top.Children.Add(tools);
        top.Children.Add(name);
        var body = Ui.Col(6, top,
            new SelectableTextBlock { Text = syntax, FontFamily = HubLook.Mono, FontSize = 12.5, Foreground = Ui.Res("Brand2"), TextWrapping = TextWrapping.Wrap },
            Ui.Text(ModScriptCommands.Doc(c), "small muted", wrap: true));
        var frame = HubLook.Frame(body, new Thickness(14, 12));
        frame.Margin = new Thickness(0, 0, 12, 12);
        return frame;
    }
}
