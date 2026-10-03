using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>
/// Панель «Загрузки», как менеджер загрузок в Vortex: задачи по играм, скорость и сколько
/// осталось, пауза, отмена, «Повторить» и «Очистить».
/// </summary>
public sealed partial class MainWindow
{
    readonly DockPanel _dlHeader = new();
    bool _dlPending;

    /// <summary>Содержимое панели: заголовок с кнопками и прокручиваемый список.</summary>
    Control DownloadsContent() => Ui.Col(12, _dlHeader, new ScrollViewer { Content = _downloadsList, MaxHeight = 440 });

    /// <summary>Открыть панель (из уведомлений и для снимков).</summary>
    public void OpenDownloads()
    {
        _bellPanel.IsVisible = false;
        _downloadsPanel.IsVisible = true;
        DrawDownloads();
    }

    /// <summary>Во время загрузки задачи сообщают о себе много раз в секунду — рисуем не чаще раза в 150 мс.</summary>
    void RenderDownloads()
    {
        _dlBadge.IsVisible = Jobs.ActiveCount > 0;
        UpdateDlRing();
        if (_dlPending) return;
        _dlPending = true;
        DispatcherTimer.RunOnce(() => { _dlPending = false; DrawDownloads(); }, TimeSpan.FromMilliseconds(150));
    }

    void DrawDownloads()
    {
        _dlBadge.IsVisible = Jobs.ActiveCount > 0;
        DrawDownloadsHeader();
        _downloadsList.Children.Clear();
        if (Jobs.All.Count == 0)
        {
            var head = Ui.Text(I18n.T("dl.empty"), "h3");
            var text = Ui.Text(I18n.T("dl.empty.text"), "muted small", wrap: true);
            head.TextAlignment = text.TextAlignment = Avalonia.Media.TextAlignment.Center;
            var icon = Ui.Icon(Icons.Download, 28, Ui.Res("Faint"));
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            _downloadsList.Children.Add(new StackPanel { Spacing = 10, Margin = new Thickness(12, 10, 12, 12), Children = { icon, Ui.Col(4, head, text) } });
            return;
        }
        foreach (var game in Jobs.All.Take(40).GroupBy(j => j.GameName))
        {
            _downloadsList.Children.Add(Ui.Text(game.Key, "small muted"));
            foreach (var job in game) _downloadsList.Children.Add(JobRow(job));
        }
    }

    void DrawDownloadsHeader()
    {
        _dlHeader.Children.Clear();
        var buttons = Ui.Row(2);
        if (Actions.CollectionRun is not null)
            buttons.Children.Add(Ui.Button("", () => { Actions.StopQueue(); DrawDownloads(); }, "icon ghost", Icons.Stop, I18n.T("dl.stopQueue")));
        if (Jobs.All.Any(j => !j.Active))
            buttons.Children.Add(Ui.Button("", () => { Jobs.ClearFinished(); DrawDownloads(); }, "icon ghost", Icons.Check, I18n.T("dl.clearDone")));
        buttons.Children.Add(Ui.Button("", ShowHistory, "icon ghost", Icons.Clock, I18n.T("nx.history")));
        DockPanel.SetDock(buttons, Dock.Right);
        _dlHeader.Children.Add(buttons);

        var running = Jobs.Running;
        var queued = Jobs.All.Count(j => j.Status == JobStatus.Queued);
        var title = Ui.Col(2, Ui.Text(I18n.T("dl.title"), "h3"));
        if (running + queued > 0)
            title.Children.Add(Ui.Text(queued > 0 ? I18n.T("dl.summary.queued", ("n", running), ("q", queued)) : I18n.T("dl.summary", ("n", running)), "small muted"));
        title.VerticalAlignment = VerticalAlignment.Center;
        _dlHeader.Children.Add(title);
    }

    static Control JobRow(Job job)
    {
        var color = job.Status switch
        {
            JobStatus.Done => Ui.Res("Good"),
            JobStatus.Failed => Ui.Res("Bad"),
            JobStatus.Running => Ui.Res("Text"),
            _ => Ui.Res("Muted"),
        };
        var status = job.Status == JobStatus.Queued ? I18n.T("dl.queued") : job.Step;

        var buttons = Ui.Row(0);
        void Add(string icon, string tip, Action click)
        {
            var b = Ui.Button("", click, "icon ghost", icon, I18n.T(tip));
            b.Width = b.Height = 30;
            b.CornerRadius = new CornerRadius(9);
            b.Content = Ui.Icon(icon, 15);
            buttons.Children.Add(b);
        }
        if (job.Link is string link && job.Status == JobStatus.Running) Add(Icons.External, "dl.reopen", () => Ui.OpenUrl(link));
        if (job.Status == JobStatus.Running && job.Total > 0) Add(Icons.Pause, "dl.pause", () => Jobs.Cancel(job, pause: true));
        if (job.Status == JobStatus.Canceled && job.Paused) Add(Icons.Play, "dl.resume", () => Jobs.Retry(job));
        else if (job.Status is JobStatus.Failed or JobStatus.Canceled) Add(Icons.Refresh, "dl.retry", () => Jobs.Retry(job));
        if (job.Active) Add(Icons.Close, "dl.cancel", () => Jobs.Cancel(job));
        buttons.VerticalAlignment = VerticalAlignment.Top;
        buttons.Margin = new Thickness(8, -4, -4, 0);

        var head = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right);
        head.Children.Add(buttons);
        head.Children.Add(new TextBlock { Text = job.Title, Classes = { "h3" }, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });

        var col = Ui.Col(6, head, new TextBlock { Text = status, Classes = { "small" }, Foreground = color, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis });
        if (job.Status == JobStatus.Running)
        {
            var ratio = job.Total > 0 ? (double)job.Received / job.Total : job.Ratio;
            col.Children.Add(new ProgressBar { Minimum = 0, Maximum = 1, Value = Math.Clamp(ratio, 0, 1), IsIndeterminate = ratio < 0, Height = 4, MinHeight = 4 });
            if (job.Total > 0) col.Children.Add(Ui.Text(BytesLine(job), "small muted"));
        }
        if (job.Status == JobStatus.Failed && job.Error is not null) col.Children.Add(Ui.Text(job.Error, "small muted", wrap: true));
        var card = new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10), Child = col };
        if (job.Status == JobStatus.Queued || job.Status == JobStatus.Canceled) card.Opacity = 0.75;
        return card;
    }

    /// <summary>«12,3 из 40 МБ · 2,1 МБ/с · осталось 0:14».</summary>
    public static string BytesLine(Job job)
    {
        var parts = new List<string> { I18n.T("dl.bytes", ("got", GamePage.Size(job.Received)), ("total", GamePage.Size(job.Total))) };
        if (job.Speed > 1)
        {
            parts.Add(I18n.T("dl.speed", ("speed", GamePage.Size((long)job.Speed))));
            parts.Add(I18n.T("dl.eta", ("time", Eta((job.Total - job.Received) / job.Speed))));
        }
        return string.Join(" · ", parts);
    }

    /// <summary>Секунды — как на часах: 0:14, 3:05, 1:02:10.</summary>
    public static string Eta(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, Math.Ceiling(seconds)));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    [SelfTest]
    static string EtaLooksLikeAClock()
    {
        var got = $"{Eta(14)} {Eta(185)} {Eta(3730)} {Eta(0.2)}";
        if (got != "0:14 3:05 1:02:10 0:01") throw new Exception(got);
        return got;
    }
}
