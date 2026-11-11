using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Views;

/// <summary>
/// 26.0: открытые файлы среды и диск. Сессия проекта живёт дольше страницы и раньше держала текст, прочитанный при первом
/// открытии: файл поменяли мимо среды («Обновить у себя» в команде, правка в старом редакторе) — а среда при выходе
/// записывала старый текст поверх. Теперь при входе и при открытии вкладки файл сверяется с диском: без своих правок —
/// перечитывается, со своими правками — спрашиваем; какая бы версия ни осталась, другая уходит в историю файла.
/// </summary>
public sealed partial class XStudio
{
    /// <summary>Файлы со своими правками, которые поменяли на диске: спросить, когда страница на экране.</summary>
    readonly List<Doc> _changedOnDisk = [];

    static string? ReadDisk(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Сверить открытые файлы с диском: чистые — перечитать, с правками — в очередь вопроса.</summary>
    void SyncFromDisk(IEnumerable<Doc> docs)
    {
        var reloaded = new List<string>();
        foreach (var d in docs.ToList())
        {
            if (d.Path is null) continue;
            var disk = ReadDisk(d.Path);
            switch (XHistory.Compare(disk, d.Saved, d.Editor.Text ?? ""))
            {
                case XHistory.DiskChange.Reload:
                    if ((d.Editor.Text ?? "") != disk) { d.Editor.Text = disk!; reloaded.Add(d.Title); }
                    d.Saved = disk!;
                    Analyze(d);
                    break;
                case XHistory.DiskChange.Conflict:
                    if (!_changedOnDisk.Contains(d)) _changedOnDisk.Add(d);
                    break;
            }
        }
        foreach (var title in reloaded) MainWindow.Current?.Toast(I18n.T("v26.bugs.disk.reloaded", ("file", title)));
    }

    /// <summary>Страница на экране — спросить про первый спорный файл (остальные — следом).</summary>
    void AskDiskConflicts()
    {
        if (_live != this || MainWindow.Current is not { } w) return;
        while (_changedOnDisk.Count > 0)
        {
            var d = _changedOnDisk[0];
            var disk = d.Path is null || !_s.Docs.Contains(d) ? null : ReadDisk(d.Path);
            var state = XHistory.Compare(disk, d.Saved, d.Editor.Text ?? "");
            if (state == XHistory.DiskChange.Conflict) { AskDisk(w, d, disk!); return; }
            _changedOnDisk.RemoveAt(0);
            if (state == XHistory.DiskChange.Reload) SyncFromDisk([d]);
        }
    }

    void AskDisk(MainWindow w, Doc d, string disk)
    {
        var mine = d.Editor.Text ?? "";
        var (added, removed) = XHistory.Count(disk, mine);
        var body = Ui.Col(10,
            Ui.Text(I18n.T("v26.bugs.disk.text", ("added", added), ("removed", removed)), "muted", wrap: true),
            Ui.Row(8, Ui.Icon(Icons.Clock, 14, Ui.Res("Muted")), Ui.Text(I18n.T("v26.bugs.disk.history"), "small muted", wrap: true)));
        body.MaxWidth = 520;
        void Next()
        {
            _changedOnDisk.Remove(d);
            RenderAll();
            Dispatcher.UIThread.Post(AskDiskConflicts, DispatcherPriority.Background);
        }
        w.Dialog(I18n.T("v26.bugs.disk.title", ("file", d.Title)), body,
            Ui.Button(I18n.T("v26.bugs.disk.take"), () =>
            {
                // Свои несохранённые правки — в историю, в редактор — файл с диска.
                w.CloseDialog();
                if (d.Path is not null) XHistory.Snap(d.Path, mine);
                d.Editor.Text = disk;
                d.Saved = disk;
                Analyze(d);
                Next();
            }, "ghost", Icons.Download),
            Ui.Button(I18n.T("v26.bugs.disk.keep"), () =>
            {
                // Версия с диска — в историю; правки остаются несохранёнными (точка на вкладке), сохранит их Ctrl+S.
                w.CloseDialog();
                if (d.Path is not null) XHistory.Snap(d.Path, disk, File.Exists(d.Path) ? File.GetLastWriteTimeUtc(d.Path) : null);
                d.Saved = disk;
                Next();
            }, "primary", Icons.Edit));
    }

    /// <summary>Для снимков: открытый файл с правками, который поменяли на диске, — и вопрос, что оставить.</summary>
    internal static XStudio DemoDiskConflict(string projectId)
    {
        var page = new XStudio(projectId, "files", "problems", "code");
        if (page._s.Docs.FirstOrDefault(x => x.Path is not null) is { Path: { } path } d)
        {
            var text = d.Editor.Text ?? "";
            d.Saved = text;
            d.Editor.Text = text + "\n# правка в среде: золото за волну x2\n";
            File.WriteAllText(path, "# обновлено из команды: новые волны\n" + text);
        }
        return new XStudio(projectId, "files", "problems", "code");
    }
}
