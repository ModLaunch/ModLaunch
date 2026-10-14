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

    // 9.5: строки задач не пересоздаются на каждое обновление (раньше — каждые 150 мс):
    // кнопка, которую успели нажать, но не отпустить, пропадала вместе со строкой — «пауза» и «отмена»
    // срабатывали через раз. Теперь строка живёт, пока жива задача, и меняет только текст и полосу.
    readonly Dictionary<Job, JobRowView> _jobRows = [];
    string _dlLayout = "";
    string _dlHeaderKey = "";
    TextBlock? _dlSummary;

    void DrawDownloads()
    {
        _dlBadge.IsVisible = Jobs.ActiveCount > 0;
        DrawDownloadsHeader();
        var jobs = Jobs.All.Take(40).ToList();
        if (jobs.Count == 0)
        {
            if (_dlLayout == "empty") return;
            _dlLayout = "empty";
            _jobRows.Clear();
            _downloadsList.Children.Clear();
            var head = Ui.Text(I18n.T("dl.empty"), "h3");
            var text = Ui.Text(I18n.T("dl.empty.text"), "muted small", wrap: true);
            head.TextAlignment = text.TextAlignment = Avalonia.Media.TextAlignment.Center;
            var icon = Ui.Icon(Icons.Download, 28, Ui.Res("Faint"));
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            _downloadsList.Children.Add(new StackPanel { Spacing = 10, Margin = new Thickness(12, 10, 12, 12), Children = { icon, Ui.Col(4, head, text) } });
            return;
        }
        // Состав списка поменялся (новая задача, «Повторить», «Очистить») — переставляем строки, но сами строки берём старые.
        var layout = string.Join("|", jobs.Select(j => $"{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(j)}:{j.GameName}"));
        if (layout != _dlLayout)
        {
            _dlLayout = layout;
            foreach (var gone in _jobRows.Keys.Where(j => !jobs.Contains(j)).ToList()) _jobRows.Remove(gone);
            _downloadsList.Children.Clear();
            foreach (var game in jobs.GroupBy(j => j.GameName))
            {
                _downloadsList.Children.Add(Ui.Text(game.Key, "small muted"));
                foreach (var job in game)
                {
                    if (!_jobRows.TryGetValue(job, out var row)) _jobRows[job] = row = new JobRowView(job);
                    _downloadsList.Children.Add(row.Card);
                }
            }
        }
        foreach (var (_, row) in _jobRows) row.Update();
    }

    void DrawDownloadsHeader()
    {
        var pausable = Jobs.All.Count(j => j.Status == JobStatus.Running && j.Total > 0);
        var paused = Jobs.All.Count(j => j.Status == JobStatus.Canceled && j.Paused && !j.Retried);
        var anyDone = Jobs.All.Any(j => !j.Active);
        // Кнопки шапки — тоже только когда их набор поменялся.
        var key = $"{pausable > 1}|{paused > 1}|{Actions.CollectionRun is not null}|{anyDone}";
        if (key != _dlHeaderKey || _dlSummary is null)
        {
            _dlHeaderKey = key;
            _dlHeader.Children.Clear();
            var buttons = Ui.Row(2);
            // 9.4: всё на паузу / продолжить всё — как в менеджерах загрузок.
            if (pausable > 1)
                buttons.Children.Add(Ui.Button("", () => { foreach (var j in Jobs.All.Where(j => j.Status == JobStatus.Running && j.Total > 0).ToList()) Jobs.Cancel(j, pause: true); DrawDownloads(); }, "icon ghost", Icons.Pause, I18n.T("dl.pauseAll")));
            if (paused > 1)
                buttons.Children.Add(Ui.Button("", () => { foreach (var j in Jobs.All.Where(j => j.Status == JobStatus.Canceled && j.Paused && !j.Retried).ToList()) Jobs.Retry(j); DrawDownloads(); }, "icon ghost", Icons.Play, I18n.T("dl.resumeAll")));
            if (Actions.CollectionRun is not null)
                buttons.Children.Add(Ui.Button("", () => { Actions.StopQueue(); DrawDownloads(); }, "icon ghost", Icons.Stop, I18n.T("dl.stopQueue")));
            if (anyDone)
                buttons.Children.Add(Ui.Button("", () => { Jobs.ClearFinished(); DrawDownloads(); }, "icon ghost", Icons.Check, I18n.T("dl.clearDone")));
            buttons.Children.Add(Ui.Button("", ShowHistory, "icon ghost", Icons.Clock, I18n.T("nx.history")));
            DockPanel.SetDock(buttons, Dock.Right);
            _dlHeader.Children.Add(buttons);
            _dlSummary = Ui.Text("", "small muted");
            var title = Ui.Col(2, Ui.Text(I18n.T("dl.title"), "h3"), _dlSummary);
            title.VerticalAlignment = VerticalAlignment.Center;
            _dlHeader.Children.Add(title);
        }

        var running = Jobs.Running;
        var queued = Jobs.All.Count(j => j.Status == JobStatus.Queued);
        var line = "";
        if (running + queued > 0)
        {
            line = queued > 0 ? I18n.T("dl.summary.queued", ("n", running), ("q", queued)) : I18n.T("dl.summary", ("n", running));
            // Общая скорость всех загрузок — видно, во что упирается связь.
            var speed = Jobs.All.Where(j => j.Status == JobStatus.Running).Sum(j => j.Speed);
            if (speed > 1) line += " · " + I18n.T("dl.speed", ("speed", GamePage.Size((long)speed)));
            if (Throttle.Limit > 0) line += " · " + I18n.T("dl.limited", ("speed", GamePage.Size(Throttle.Limit)));
        }
        _dlSummary.Text = line;
        _dlSummary.IsVisible = line != "";
    }

    /// <summary>Строка задачи: создаётся один раз, дальше меняются только текст, полоса и (при смене состояния) кнопки.</summary>
    sealed class JobRowView
    {
        internal Control? FirstButton() => _buttons.Children.FirstOrDefault();
        readonly Job _job;
        public Border Card { get; }
        readonly TextBlock _title = new() { Classes = { "h3" }, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock _status = new() { Classes = { "small" }, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
        readonly ProgressBar _bar = new() { Minimum = 0, Maximum = 1, Height = 4, MinHeight = 4 };
        readonly TextBlock _bytes = new() { Classes = { "small", "muted" } };
        readonly TextBlock _error = new() { Classes = { "small", "muted" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, Spacing = 0, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, -4, -4, 0) };
        string _buttonsKey = "";

        public JobRowView(Job job)
        {
            _job = job;
            var head = new DockPanel();
            DockPanel.SetDock(_buttons, Dock.Right);
            head.Children.Add(_buttons);
            head.Children.Add(_title);
            Card = new Border
            {
                Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10),
                Child = Ui.Col(6, head, _status, _bar, _bytes, _error),
            };
        }

        public void Update()
        {
            var job = _job;
            _title.Text = job.Title;
            _status.Text = job.Status == JobStatus.Queued ? I18n.T("dl.queued") : job.Step;
            _status.Foreground = job.Status switch
            {
                JobStatus.Done => Ui.Res("Good"),
                JobStatus.Failed => Ui.Res("Bad"),
                JobStatus.Running => Ui.Res("Text"),
                _ => Ui.Res("Muted"),
            };
            var running = job.Status == JobStatus.Running;
            var total = job.Total;
            var ratio = total > 0 ? (double)job.Received / total : job.Ratio;
            _bar.IsVisible = running;
            if (running)
            {
                _bar.IsIndeterminate = ratio < 0;
                if (ratio >= 0) _bar.Value = Math.Clamp(ratio, 0, 1);
            }
            _bytes.IsVisible = running && total > 0;
            if (_bytes.IsVisible) _bytes.Text = BytesLine(job);
            _error.IsVisible = job.Status == JobStatus.Failed && job.Error is not null;
            if (_error.IsVisible) _error.Text = job.Error;
            Card.Opacity = job.Status is JobStatus.Queued or JobStatus.Canceled ? 0.75 : 1;

            var key = $"{job.Status}|{job.Paused}|{job.Link is not null}|{total > 0}|{job.Retried}";
            if (key == _buttonsKey) return;
            _buttonsKey = key;
            _buttons.Children.Clear();
            void Add(string icon, string tip, Action click)
            {
                var b = Ui.Button("", click, "icon ghost", icon, I18n.T(tip));
                b.Width = b.Height = 30;
                b.CornerRadius = new CornerRadius(9);
                b.Content = Ui.Icon(icon, 15);
                _buttons.Children.Add(b);
            }
            if (job.Link is string link && running) Add(Icons.External, "dl.reopen", () => Ui.OpenUrl(link));
            if (running && total > 0) Add(Icons.Pause, "dl.pause", () => Jobs.Cancel(job, pause: true));
            if (job.Status == JobStatus.Canceled && job.Paused) Add(Icons.Play, "dl.resume", () => Jobs.Retry(job));
            else if (job.Status is JobStatus.Failed or JobStatus.Canceled) Add(Icons.Refresh, "dl.retry", () => Jobs.Retry(job));
            if (job.Active) Add(Icons.Close, "dl.cancel", () => Jobs.Cancel(job));
        }
    }

    /// <summary>Карточка задачи в панели (для проверки: строки не пересоздаются при обновлении).</summary>
    internal Border? JobCard(Job job) => _jobRows.TryGetValue(job, out var r) ? r.Card : null;

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

    /// <summary>
    /// Строка загрузки при обновлении прогресса остаётся той же (и её кнопки — тоже): нажатие «Пауза»
    /// не теряется, если между нажатием и отпусканием пришли новые байты.
    /// </summary>
    [SelfTest]
    static string DownloadRowKeepsItsButtons()
    {
        var job = new Job { Title = "big.zip", GameName = "test", Status = JobStatus.Running };
        var t = job.BeginTransfer();
        t.SetSize(1000);
        t.SetGot(100);
        var row = new JobRowView(job);
        row.Update();
        var pause = row.FirstButton();
        t.SetGot(400);
        row.Update();
        if (!ReferenceEquals(pause, row.FirstButton())) throw new Exception("pause button was recreated by a progress update");
        job.Paused = true;
        job.Status = JobStatus.Canceled;
        row.Update();
        if (ReferenceEquals(pause, row.FirstButton())) throw new Exception("buttons did not change when the job was paused");
        return "same pause button across progress updates, new buttons after pause";
    }
}
