using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Games;
using ModLaunch.Social;

namespace ModLaunch.Views;

/// <summary>
/// 26.0: «Путь автора» — четыре шага от идеи до первых игроков (создать → запустить в игре → опубликовать →
/// поделиться). Галочки берутся из того, что уже есть на диске и в Hub, а не из отдельного «прогресса»: проект есть,
/// последняя проверка или установка в игру записана, у проекта есть id в Hub (или в Hub лежат свои моды), ссылка на
/// мод скопирована. Карточку можно скрыть крестиком; когда все шаги сделаны, она уходит сама.
/// Под пустыми «Моими модами» ещё и готовые примеры для игр, которые уже стоят.
/// </summary>
public sealed partial class CreatorPage
{
    /// <summary>Для снимков: шаги «Пути автора» без чтения проектов.</summary>
    internal static List<JourneyStep>? DemoJourney;

    static List<JourneyStep> JourneyNow()
    {
        if (DemoJourney is not null) return DemoJourney;
        var xs = XProjects.List();
        var ps = Projects.List();
        var dirs = xs.Select(x => x.Dir).Concat(ps.Select(p => p.Dir)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var metas = dirs.Select(ProjectMeta.Load).ToList();
        var ran = metas.Any(m => m.LastTest is not null || m.LastInstall is not null) || xs.Any(x => XState(x).Key is "ingame" or "changed");
        var published = metas.Any(m => m.HubId is not null) || CreatorWatch.Published;
        return CreatorStats.Journey(dirs.Count > 0, ran, published, CreatorWatch.Shared);
    }

    /// <summary>
    /// Карточка «Путь автора»; null — скрыта или все шаги пройдены. underEmpty — карточка под пустыми «Моими модами»,
    /// где «Новый мод» уже есть кнопкой пустого состояния: у шага «Создайте мод» вторая такая же не нужна.
    /// </summary>
    Control? JourneyCard(bool underEmpty = false)
    {
        if (CreatorWatch.JourneyHidden && DemoJourney is null) return null;
        var steps = JourneyNow();
        if (steps.All(s => s.Done)) return null;
        var next = steps.First(s => !s.Done).Id;
        var done = steps.Count(s => s.Done);

        var hide = Ui.Button("", () => { CreatorWatch.JourneyHidden = true; Build(); }, "icon ghost", Icons.Close, I18n.T("v26.features.journey.hide"));
        var head = HubLook.Head("", I18n.T("v26.features.journey.title"), hide);
        var lead = Ui.Text(I18n.T("v26.features.journey.text", ("n", done), ("total", steps.Count)), "small muted", wrap: true);
        lead.Margin = new Thickness(0, -6, 0, 0);

        // Полоска прогресса: по отрезку на шаг.
        var bar = new UniformGrid { Columns = steps.Count, Margin = new Thickness(0, 0, -6, 0) };
        foreach (var s in steps)
            bar.Children.Add(new Border { Height = 4, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 6, 0), Background = s.Done ? HubLook.Amber : HubLook.Raised });

        var grid = new UniformGrid { Columns = steps.Count, Margin = new Thickness(0, 0, -12, 0) };
        for (var i = 0; i < steps.Count; i++)
        {
            var s = steps[i];
            var current = s.Id == next;
            Control mark = s.Done
                ? new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = HubLook.Amber, Child = Ui.Icon(Icons.Check, 15, HubLook.OnAmber) }
                : new Border
                {
                    Width = 28, Height = 28, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(current ? 2 : 1), BorderBrush = current ? HubLook.Amber : HubLook.Edge,
                    Child = new TextBlock
                    {
                        Text = (i + 1).ToString(I18n.Culture), FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = current ? HubLook.Violet : Ui.Res("Muted"),
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    },
                };
            mark.HorizontalAlignment = HorizontalAlignment.Left;
            ToolTip.SetTip(mark, s.Done ? I18n.T("v26.features.journey.done") : null);
            var title = Ui.Text(I18n.T("v26.features.journey." + s.Id), "strong");
            if (s.Done) title.Foreground = Ui.Res("Muted");
            var col = Ui.Col(8, mark, title, Ui.Text(I18n.T($"v26.features.journey.{s.Id}.text"), "small muted", wrap: true));
            if (current && !(underEmpty && s.Id == "create") && JourneyAction(s.Id) is { } go)
            {
                go.Margin = new Thickness(0, 4, 0, 0);
                go.HorizontalAlignment = HorizontalAlignment.Left;
                col.Children.Add(go);
            }
            grid.Children.Add(new Border
            {
                Padding = new Thickness(14, 12), CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 12, 0),
                Background = current ? HubLook.AmberSoft : Brushes.Transparent, BorderBrush = current ? Brushes.Transparent : HubLook.Edge, BorderThickness = new Thickness(1),
                Child = col,
            });
        }
        return HubLook.Frame(Ui.Col(14, head, lead, bar, grid), new Thickness(20, 16, 20, 20));
    }

    /// <summary>Кнопка текущего шага: новый мод, открыть последний в среде (F6 — в игру), опубликовать, скопировать ссылку.</summary>
    Button? JourneyAction(string step)
    {
        switch (step)
        {
            case "create":
                return Ui.Button(I18n.T("v22.hub.new"), () => Go("form"), "primary", Icons.Plus);
            case "run":
                var x = XProjects.List().FirstOrDefault();
                var p = Projects.List().FirstOrDefault();
                if (x is null && p is null) return null;
                return Ui.Button(I18n.T("v26.features.journey.run.go"), () =>
                {
                    if (x is not null && (p is null || x.Updated >= p.Updated)) MainWindow.Current?.Navigate(() => new XStudio(x.Id));
                    else if (p is not null) OpenInStudio(p);
                }, "primary", Icons.Play);
            case "publish":
                return Ui.Button(I18n.T("hub.upload"), () => { _tab = "home"; _minePart = "published"; Build(); }, "primary", Icons.Upload);
            case "share":
                var id = SharedCandidate();
                if (id is null) return Ui.Button(I18n.T("cr.mine.published"), () => { _tab = "home"; _minePart = "published"; Build(); }, "primary", Icons.Globe);
                return Ui.Button(I18n.T("hub.share"), async () =>
                {
                    if (TopLevel.GetTopLevel(this)?.Clipboard is { } clip) await clip.SetTextAsync(CreatorStats.ShareUrl(id));
                    CreatorWatch.MarkShared();
                    MainWindow.Current?.Toast(I18n.T("hub.shared"));
                    Build();
                }, "primary", Icons.Link);
        }
        return null;
    }

    /// <summary>
    /// Какой свой мод Hub предложить для ссылки: самый скачиваемый из загруженной статистики, иначе последний выложенный
    /// из проекта, иначе из снимка статистики.
    /// </summary>
    static string? SharedCandidate()
    {
        var stats = Program.Screenshot ? DemoStats : _stats?.Uid == Account.Uid ? _stats : null;
        if (stats?.Mods.OrderByDescending(m => m.Downloads).FirstOrDefault() is { } top) return top.Id;
        foreach (var p in Projects.List())
            if (p.Meta().HubId is { } hub) return hub;
        if (Account.Uid is { } uid && CreatorWatch.Days(uid).OrderBy(kv => kv.Key, StringComparer.Ordinal).LastOrDefault().Value is System.Text.Json.Nodes.JsonObject day)
            return day.Select(kv => kv.Key).FirstOrDefault();
        return null;
    }

    /// <summary>
    /// Пустые «Мои моды»: общее пустое состояние хаба, «Путь автора» и примеры для игр, что уже стоят (у каких есть
    /// свои шаблоны) — первый мод можно начать с игры, в которую человек играет.
    /// </summary>
    void EmptyMine(StackPanel col)
    {
        col.Children.Add(HubLook.Empty(Icons.Creator, I18n.T("v22.mods.empty"), I18n.T("v22.mods.empty.text"),
            [Ui.Button(I18n.T("v18.other.template"), () => Go("examples"), "ghost", Icons.Wand), Ui.Button(I18n.T("v22.hub.new"), () => Go("form"), "primary", Icons.Plus)]));
        if (JourneyCard(underEmpty: true) is { } journey) col.Children.Add(journey);
        if (Starters() is { } starters) col.Children.Add(starters);
    }

    /// <summary>Шаблоны своих игр: по одному на найденную игру (сначала те, где модов больше), до четырёх.</summary>
    internal static List<(GameDef Game, Template Template)> StarterPicks()
    {
        string[] generic = ["blank", "code-style", "files-readme"];
        return AppState.Games.Where(g => g.Status == Detect.Found)
            .Select(g => (g.Def, Template: Templates.All.FirstOrDefault(t => t.Game == g.Def.Id && !generic.Contains(t.Id) && !t.Id.StartsWith("bepinex-", StringComparison.Ordinal))))
            .Where(x => x.Template is not null)
            .OrderBy(x => x.Def.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(4)
            .Select(x => (x.Def, x.Template!))
            .ToList();
    }

    Control? Starters()
    {
        var picks = StarterPicks();
        if (picks.Count == 0) return null;
        var grid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, -14, 0) };
        foreach (var (game, t) in picks)
        {
            var tpl = t;
            var art = new Border { Height = 92, ClipToBounds = true, CornerRadius = new CornerRadius(10, 10, 0, 0), Child = new Panel { Children = { Cover(game), Scrim() } } };
            var name = Ui.Text(I18n.T($"cr.ex.{t.Id}"), "strong");
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            var about = new TextBlock { Text = I18n.T($"cr.ex.{t.Id}.text"), FontSize = 12.5, Foreground = Ui.Res("Muted"), TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, Height = 34 };
            var body = Ui.Col(6, Ui.Text(game.Name, "small muted"), name, about,
                Ui.Row(6, Ui.Icon(Icons.Wand, 14, HubLook.Violet), Ui.Text(I18n.T("cr.ex.try"), "small", color: HubLook.Violet)));
            body.Margin = new Thickness(14, 10, 14, 14);
            var card = new Button
            {
                Classes = { "tile" }, Padding = new Thickness(0), Margin = new Thickness(0, 0, 14, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = new StackPanel { Children = { art, body } },
            };
            card.Click += (_, _) => TryTemplate(tpl);
            grid.Children.Add(card);
        }
        var head = Ui.Col(4, HubLook.Eyebrow(I18n.T("v26.features.starters")), Ui.Text(I18n.T("v26.features.starters.text"), "small muted", wrap: true));
        return Ui.Col(12, head, grid);
    }
}
