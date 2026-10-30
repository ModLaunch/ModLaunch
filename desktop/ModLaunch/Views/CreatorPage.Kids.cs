using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Games;
using MX = ModScriptX;

namespace ModLaunch.Views;

/// <summary>
/// 21.0: «Первый мод за минуту» — первый шаг для того, кто ни разу не программировал. Четыре идеи: нажал — и через
/// секунду открыт ModScript X с модом для твоей игры, написанным простыми словами, и учебником рядом.
/// </summary>
public sealed partial class CreatorPage
{
    static readonly (string Id, string Icon, string Hex)[] Ideas =
    [
        ("hello", Icons.Chat, "#9B7BFF"), ("gold", Icons.Sparkles, "#F5B94A"), ("god", Icons.Shield, "#FF6FB1"), ("blank", Icons.Grid, "#36C2E0"),
    ];

    /// <summary>Карточка быстрого старта над анкетой: заголовок, что это, и плитки идей.</summary>
    static Control KidStart()
    {
        var tiles = new UniformGrid { Columns = 4 };
        foreach (var (id, icon, hex) in Ideas)
        {
            var idea = id;
            var c = Color.Parse(hex);
            var mark = new Border
            {
                Width = 42, Height = 42, CornerRadius = new CornerRadius(11), HorizontalAlignment = HorizontalAlignment.Left,
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(c, 0), new GradientStop(Look.Mix(c, Colors.Black, 0.45), 1) },
                },
                Child = Ui.Icon(icon, 20, Brushes.White),
            };
            var b = new Button
            {
                Classes = { "tile" }, Margin = new Thickness(0, 0, 12, 0), Padding = new Thickness(16, 14), HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
                Content = Ui.Col(10, mark, Ui.Col(3, Ui.Text(I18n.T($"v21.idea.{id}"), "strong"), Ui.Text(I18n.T($"v21.idea.{id}.text"), "small muted", wrap: true))),
            };
            b.Click += (_, _) => QuickStart(idea);
            tiles.Children.Add(b);
        }
        var title = new TextBlock { Text = I18n.T("v21.kids.title"), FontFamily = Look.Display, FontSize = 24, FontWeight = FontWeight.Black, LetterSpacing = -0.4 };
        var steps = new WrapPanel();
        foreach (var (n, key) in new[] { ("1", "v21.kids.step1"), ("2", "v21.kids.step2"), ("3", "v21.kids.step3") })
            steps.Children.Add(new Border
            {
                Margin = new Thickness(0, 0, 16, 6),
                Child = Ui.Row(7, new Border
                {
                    Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = HubLook.Premium(),
                    Child = new TextBlock { Text = n, FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                }, Ui.Text(I18n.T(key), "small")),
            });
        var col = Ui.Col(14, Ui.Col(6, Ui.Row(10, Ui.Icon(Icons.Book, 20, HubLook.Cyan), title), Ui.Text(I18n.T("v21.kids.text"), "muted", wrap: true), steps), tiles);
        return new Border
        {
            Background = HubLook.Panel, BorderBrush = HubLook.Premium(150), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(24, 20), Child = col,
        };
    }

    /// <summary>
    /// Проект одной кнопкой: игра — последняя (или первая найденная, где мост умеет код), движок — по мосту, код —
    /// мод идеи простыми словами. Открывается с учебником слева.
    /// </summary>
    internal static XProject? QuickStart(string idea, bool open = true)
    {
        var found = AppState.Games.Where(g => g.Status == Detect.Found && !g.Def.IsMinecraft).ToList();
        var pick = found.FirstOrDefault(g => g.Def.Id == Settings.Data.Str("lastGame")) ?? found.FirstOrDefault(g => BridgeFor(g.Path, g.Def) is { Code: true }) ?? found.FirstOrDefault();
        var bridge = pick is null ? null : BridgeFor(pick.Path, pick.Def);
        var target = (bridge is { Code: true, TargetId: { } tid } ? MX.Target.Find(tid) : null) ?? MX.Target.Find("unity")!;
        var name = I18n.T($"v21.idea.{idea}.name");
        try
        {
            var p = XProjects.Create(new XForm(name, I18n.T($"v21.idea.{idea}.text"), bridge is { Code: true } ? pick!.Def.Id : "", bridge is { Code: true } ? pick!.Path : null,
                "empty", bridge?.Engine ?? target.Id, target.Id, ["start"], "", "1.0.0", idea switch { "gold" => "orange", "god" => "pink", "blank" => "cyan", _ => "violet" }));
            File.WriteAllText(p.Main, IdeaCode(idea, p.Name, target));
            if (open) MainWindow.Current?.Navigate(() => new XStudio(p.Id, side: "docs"));
            return p;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            MainWindow.Current?.Toast(e.Message, bad: true);
            return null;
        }
    }

    static string Quote(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ") + "\"";

    /// <summary>Код идеи простыми словами (на языке программы): шапка мода и пара строк — то, что новичок сразу поменяет.</summary>
    internal static string IdeaCode(string idea, string name, MX.Target target)
    {
        var ru = SimpleWords.Ru;
        var head = ru ? $"мод {Quote(name)}\nверсия 1.0.0\nдвижок {target.Id}\n\n" : $"mod {Quote(name)}\nversion 1.0.0\nengine {target.Id}\n\n";
        var start = ru ? "когда игра началась {" : "when the game starts {";
        string Say(string key) => $"    {(ru ? "сказать" : "say")} {Quote(I18n.T(key))}\n";
        return head + idea switch
        {
            "gold" => Pieces.Line(I18n.T("v21.piece.gold")) + "\n\n" + start + "\n" + (ru ? "    дать золото 100\n" : "    give gold 100\n")
                + $"    {(ru ? "сказать" : "say")} {Quote(I18n.T("v21.idea.gold.say") + (ru ? " {золото}" : " {gold}"))}\n}}\n",
            "god" => Pieces.Line(I18n.T("v21.piece.god")) + "\n\n" + start + "\n" + Say("v21.idea.god.say") + "}\n",
            "blank" => start + "\n" + Say("v21.idea.blank.say") + "}\n",
            _ => start + "\n" + Say("v21.idea.hello.say") + (ru ? "    с шансом 50 {\n" : "    with chance 50 {\n") + "    " + Say("v21.idea.hello.luck") + "    }\n}\n\n"
                + (ru ? "каждые 60 секунд {\n" : "every 60 seconds {\n") + Say("v21.idea.hello.minute") + "}\n",
        };
    }

    [SelfTest]
    static string IdeasAreValidMods()
    {
        foreach (var (id, _, _) in Ideas)
            foreach (var t in new[] { "unity", "renpy", "godot" })
            {
                var target = MX.Target.Find(t)!;
                var code = IdeaCode(id, "Idea " + id, target);
                var doc = MX.ModxDoc.Parse(code);
                if (!doc.Ok) throw new Exception($"{id}/{t}: {doc.Diags[0].Message}");
                target.Emit(doc, null);
            }
        return $"{Ideas.Length} ideas × 3 engines parse and translate";
    }
}
