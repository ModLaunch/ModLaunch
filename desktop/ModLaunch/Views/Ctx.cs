using Avalonia.Controls;
using Avalonia.Input;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>
/// Контекстные меню (правый клик) — одним стилем по всей программе.
/// Меню собирается в момент щелчка, поэтому всегда показывает текущее
/// состояние (включён ли мод, в избранном ли, куплен ли лот…).
/// </summary>
public static class Ctx
{
    /// <summary>Пункт меню; null в списке — пропуск, "-" — разделитель.</summary>
    public static MenuItem Item(string text, string icon, Action run, bool enabled = true, string? gesture = null)
    {
        var m = new MenuItem { Header = text, Icon = Ui.Icon(icon, 14), IsEnabled = enabled };
        if (gesture is not null) m.InputGesture = KeyGesture.Parse(gesture);
        // Действие — после закрытия меню и под страховкой (см. Guard.Later).
        m.Click += (_, _) => Guard.Later(run);
        return m;
    }

    public static MenuItem Sub(string text, string icon, params object?[] items)
    {
        var m = new MenuItem { Header = text, Icon = Ui.Icon(icon, 14) };
        Fill(m.Items, items);
        return m;
    }

    public static MenuFlyout Menu(params object?[] items)
    {
        var menu = new MenuFlyout();
        Fill(menu.Items, items);
        return menu;
    }

    static void Fill(ItemCollection target, object?[] items)
    {
        foreach (var item in items)
        {
            switch (item)
            {
                case null: continue;
                case "-": if (target.Count > 0 && target[^1] is not Separator) target.Add(new Separator()); break;
                case IEnumerable<object?> many: Fill(target, many.ToArray()); break;
                default: target.Add(item); break;
            }
        }
        while (target.Count > 0 && target[^1] is Separator) target.RemoveAt(target.Count - 1);
    }

    /// <summary>Повесить меню на элемент: собирается заново при каждом правом щелчке.</summary>
    public static T Attach<T>(T control, Func<MenuFlyout> make) where T : Control
    {
        control.ContextRequested += (_, e) =>
        {
            if (e.Handled) return;
            e.Handled = true;
            var menu = make();
            if (menu.Items.Count > 0) menu.ShowAt(control, true);
        };
        return control;
    }

    /// <summary>«Скопировать …» — в буфер обмена с уведомлением.</summary>
    public static MenuItem Copy(string label, string text) => Item(label, Icons.List, async () =>
    {
        if (MainWindow.Current?.Clipboard is { } c)
        {
            await c.SetTextAsync(text);
            MainWindow.Current.Toast(I18n.T("ctx.copied"));
        }
    });

    public static MenuItem Link(string label, string url) => Item(label, Icons.External, () => Ui.OpenUrl(url));
    public static MenuItem Folder(string label, string path) => Item(label, Icons.Folder, () => Actions.OpenFolder(path));
}
