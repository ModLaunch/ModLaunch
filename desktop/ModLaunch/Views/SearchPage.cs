using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>
/// Поиск сразу по всем своим играм: результаты появляются по мере набора, по играм,
/// у каждой — лучшие пять и «Все результаты». Вставленная ссылка на мод открывает его страницу.
/// </summary>
public sealed class SearchPage : Page
{
    string _text;
    CancellationTokenSource? _cts;
    readonly Dictionary<string, (List<ModInfo> Mods, long Total, string? Error)> _results = [];
    readonly HashSet<string> _pending = [];
    readonly TextBox _box;
    readonly StackPanel _found = new() { Spacing = 18 };
    IDisposable? _debounce;

    public SearchPage(string text = "")
    {
        _text = text;
        _box = new TextBox { Text = text, Watermark = I18n.T("gsearch.hint"), Height = 50, FontSize = 17 };
        _box.InnerLeftContent = new Border { Padding = new Thickness(16, 0, 4, 0), Child = Ui.Icon(Icons.Search, 20, Ui.Res("Muted")) };
        _box.TextChanged += (_, _) => Typed(_box.Text ?? "");
        _box.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { _debounce?.Dispose(); Search(_box.Text ?? ""); } };
        // Открыли страницу — сразу можно печатать (один раз: при обновлении результатов фокус не отбираем).
        EventHandler<VisualTreeAttachmentEventArgs>? focus = null;
        focus = (_, _) => { _box.AttachedToVisualTree -= focus; Dispatcher.UIThread.Post(() => { _box.Focus(); _box.CaretIndex = _box.Text?.Length ?? 0; }); };
        _box.AttachedToVisualTree += focus;
        Build();
        if (text.Trim().Length >= 2) _ = Run();
    }

    public override string Title => I18n.T("gsearch.title");
    public override string SearchHint => I18n.T("gsearch.hint");

    /// <summary>Набирают текст — ищем, когда пальцы замрут на треть секунды.</summary>
    public void Typed(string text)
    {
        // Старый таймер гасим всегда: набрали «maps» и стёрли «s» — искать «maps» уже не надо.
        _debounce?.Dispose();
        if (text == _text) return;
        _debounce = DispatcherTimer.RunOnce(() => Search(text), TimeSpan.FromMilliseconds(350));
    }

    public override void Search(string text)
    {
        _debounce?.Dispose();
        _text = text;
        if (_box.Text != text) _box.Text = text;
        _ = Run();
    }

    static List<GameState> Games() => AppState.Games.Where(g => g.Status == Detect.Found && g.Def.PrimarySource != "none" && !g.Def.IsMinecraft).ToList();

    async Task Run()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        _results.Clear();
        _pending.Clear();
        var text = _text.Trim();
        if (text.Length < 2 || ModLink.Parse(text) is not null) { Render(); return; }
        var games = Games();
        foreach (var g in games) _pending.Add(g.Def.Id);
        Render();
        using var gate = new SemaphoreSlim(4);
        await Task.WhenAll(games.Select(async g =>
        {
            try
            {
                await gate.WaitAsync(cts.Token);
                try
                {
                    var page = Program.Demo ? Demo.Catalog(g.Def, new Query(text)) : await Catalog.Browse(g.Def, new Query(text), cts.Token);
                    if (!cts.IsCancellationRequested) _results[g.Def.Id] = (page.Mods.Take(5).ToList(), Math.Max(page.Total, page.Mods.Count), null);
                }
                finally { gate.Release(); }
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (!cts.IsCancellationRequested) _results[g.Def.Id] = ([], 0, Jobs.Explain(e)); }
            if (cts.IsCancellationRequested) return;
            _pending.Remove(g.Def.Id);
            Render(); // результаты игры появляются, как только пришли
        }));
    }

    public override void Build()
    {
        var col = new StackPanel { Spacing = 18, Margin = new Thickness(40, 30, 40, 40), MaxWidth = 1200 };
        col.Children.Add(Ui.Col(6, Ui.Text(I18n.T("gsearch.title"), "h1"), Ui.Text(I18n.T("gsearch.lead"), "muted")));
        if (_box.Parent is Panel old) old.Children.Remove(_box);
        if (_found.Parent is Panel oldFound) oldFound.Children.Remove(_found);
        col.Children.Add(_box);
        col.Children.Add(_found);
        Render();
        Content = new ScrollViewer { Content = col, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    /// <summary>Перерисовать только результаты — поле поиска остаётся тем же, курсор не прыгает.</summary>
    void Render()
    {
        var col = _found;
        col.Children.Clear();
        var text = _text.Trim();
        var games = Games();
        if (games.Count == 0) col.Children.Add(Empty(Icons.Gamepad, I18n.T("gsearch.noGames"), I18n.T("search.noGames")));
        else if (ModLink.Parse(text) is not null)
            col.Children.Add(Ui.Card(Ui.Row(12, Ui.Icon(Icons.Link, 20, Ui.Res("Brand2")), Ui.Text(I18n.T("gsearch.link"), "h3"),
                Ui.Button(I18n.T("gsearch.openLink"), () => MainWindow.Current?.OpenLink(text), "primary", Icons.External)), 18));
        else if (text.Length < 2) col.Children.Add(Empty(Icons.Search, I18n.T("gsearch.start"), I18n.T("gsearch.start.text", ("n", games.Count))));
        else
        {
            var any = false;
            foreach (var g in games.OrderByDescending(x => _results.TryGetValue(x.Def.Id, out var r) ? r.Total : -1))
            {
                if (_pending.Contains(g.Def.Id)) { col.Children.Add(GroupSkeleton(g)); continue; }
                if (!_results.TryGetValue(g.Def.Id, out var r) || (r.Mods.Count == 0 && r.Error is null)) continue;
                any = true;
                col.Children.Add(Group(g, r.Mods, r.Total, r.Error));
            }
            if (!any && _pending.Count == 0) col.Children.Add(Empty(Icons.Search, I18n.T("catalog.nothingFound", ("query", text)), I18n.T("gsearch.nothing.text")));
        }
    }

    static Control Empty(string icon, string title, string text)
    {
        var mark = Ui.Icon(icon, 34, Ui.Res("Faint"));
        mark.HorizontalAlignment = HorizontalAlignment.Center;
        var h = Ui.Text(title, "h3");
        var t = Ui.Text(text, "muted", wrap: true);
        h.HorizontalAlignment = t.HorizontalAlignment = HorizontalAlignment.Center;
        t.TextAlignment = Avalonia.Media.TextAlignment.Center;
        return new Border { Padding = new Thickness(20, 48), Child = new StackPanel { Spacing = 10, MaxWidth = 460, Children = { mark, h, t } } };
    }

    Control GroupHead(GameState g, string subtitle, Control? right)
    {
        var picture = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = Ui.GameImage(g.Def, 120) };
        var title = Ui.Row(12, picture, Ui.Col(1, Ui.Text(g.Def.Name, "h3"), Ui.Text(subtitle, "small muted")));
        title.VerticalAlignment = VerticalAlignment.Center;
        var dock = new DockPanel();
        if (right is not null) { DockPanel.SetDock(right, Dock.Right); dock.Children.Add(right); }
        dock.Children.Add(title);
        return dock;
    }

    Control Group(GameState g, List<ModInfo> mods, long total, string? error)
    {
        var text = _text.Trim();
        var all = Ui.Button(I18n.T("gsearch.all"), () => MainWindow.Current?.Navigate(() => new GamePage(g.Def.Id, "catalog", text)), "ghost", Icons.Forward);
        all.VerticalAlignment = VerticalAlignment.Center;
        var col = Ui.Col(10, GroupHead(g, error is not null ? I18n.T("catalog.error") : I18n.T("gsearch.found", ("n", total.ToString("N0"))), error is null ? all : null));
        if (error is not null) col.Children.Add(Ui.Text(error, "small muted", wrap: true));
        var picks = g.Def.Picks.ToHashSet();
        foreach (var mod in mods)
        {
            var m = mod;
            col.Children.Add(ModRow.Build(g.Def, m, Actions.IsInstalled(g, m.Id), Actions.IsBusy(g, m.Id), picks.Contains(m.Id),
                () => { _ = Actions.Install(g, m); Render(); }, () => MainWindow.Current?.Navigate(() => new ModPage(g.Def.Id, m))));
        }
        return col;
    }

    Control GroupSkeleton(GameState g) => Ui.Col(10, GroupHead(g, I18n.T("catalog.loading"), null),
        new Border { Classes = { "card" }, Height = 96, Opacity = 0.6 });

    /// <summary>Снимки: поиск «map» по всем играм и пустая страница поиска.</summary>
    [DemoShots]
    static void SearchShots(Shots s)
    {
        s.Window.Navigate(() => new SearchPage());
        s.Save("discovery-1-search-empty");
        s.Window.Navigate(() => new SearchPage("mod"));
        s.Pump(600);
        s.Save("discovery-2-search");
    }
}
