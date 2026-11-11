using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Views;

/// <summary>
/// 26.0: уведомления автора в колокольчике — что прибавилось у своих модов Hub и лотов рынка с прошлого просмотра
/// статистики: «Скачивания ваших модов: +120», «Новые комментарии к X: 3», «Продажи на рынке: +2». Счётчики
/// сравниваются при запуске, при загрузке данных рынка и Hub и в самой статистике; щелчок открывает «Статистику».
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Подписки: уведомления изменились — перерисовать колокольчик; данные Hub пришли — записать снимок.</summary>
    void WatchCreator()
    {
        CreatorWatch.Changed += () => Dispatcher.UIThread.Post(RenderBell);
        // Вышли или вошли другим аккаунтом — чужие уведомления убрать и проверить свои.
        Social.Account.Changed += () => Dispatcher.UIThread.Post(() => { CreatorWatch.Reset(); if (_started) _ = CreatorWatch.Check(); });
        // Рынок и мастерская загрузили моды Hub — тот же список, снимок дня и «что нового» без лишнего запроса.
        MarketData.Changed += () =>
        {
            if (Program.Demo || !Social.Account.SignedIn || Social.Account.Uid is not { } uid || MarketData.Workshop is not { } hub) return;
            // Список — до 300 свежих модов: если старый свой мод в него не попал, снимок допишет полная проверка.
            var mine = hub.Where(m => m.Uid == uid).ToList();
            if (CreatorWatch.Covers(uid, mine)) CreatorWatch.Observe(uid, mine);
        };
    }

    /// <summary>Строки «Автору» в начале списка колокольчика.</summary>
    void RenderCreatorBell(IReadOnlyList<CreatorNote> notes)
    {
        if (notes.Count == 0) return;
        var head = HubLook.Label(I18n.T("v26.features.bell.title"));
        head.Margin = new Thickness(8, 0, 0, 2);
        _bellList.Children.Add(head);
        foreach (var n in notes)
        {
            var icon = n.Kind switch { "sales" => Icons.Bag, "comments" => Icons.Chat, "likes" => Icons.Heart, _ => Icons.Download };
            var mark = new Border
            {
                Width = 40, Height = 40, CornerRadius = new CornerRadius(10), Background = HubLook.AmberSoft,
                Child = Ui.Icon(icon, 18, HubLook.Violet),
            };
            var title = Ui.Text(CreatorPage.NoteTitle(n), "h3", wrap: true);
            var words = Ui.Col(2, title, Ui.Text(I18n.T("v26.features.note.open"), "small muted"));
            words.VerticalAlignment = VerticalAlignment.Center;
            var body = new DockPanel();
            mark.Margin = new Thickness(0, 0, 10, 0);
            DockPanel.SetDock(mark, Dock.Left);
            body.Children.Add(mark);
            body.Children.Add(words);
            var row = new Button
            {
                Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8),
                Content = body,
            };
            row.Click += (_, _) =>
            {
                CreatorWatch.MarkSeen();
                _bellPanel.IsVisible = false;
                Navigate(() => new CreatorPage("stats"));
            };
            _bellList.Children.Add(row);
        }
        _bellList.Children.Add(new Border { Height = 1, Background = Ui.Res("Line"), Margin = new Thickness(8, 4) });
    }

    /// <summary>Для снимков: открыть колокольчик.</summary>
    internal void DemoOpenBell()
    {
        _bellPanel.IsVisible = true;
        _downloadsPanel.IsVisible = false;
        RenderBell();
    }

    internal void DemoCloseBell() => _bellPanel.IsVisible = false;
}
