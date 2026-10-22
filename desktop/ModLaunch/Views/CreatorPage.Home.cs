using Avalonia;
using Avalonia.Controls;
using ModLaunch.Core;

namespace ModLaunch.Views;

public sealed partial class CreatorPage
{
    // 13.0: обзор-«пульт» 12.x (верстак, путь автора, ящики с инструментами) убран — его место заняли
    // четыре раздела Hub (HubNav) и «Проекты» с шаблонами (CreatorPage.Mine.cs).

    /// <summary>Заголовок раздела в духе мастерской: «// ПОДПИСЬ» и название, справа — «Все» (если есть куда вести).</summary>
    static Control Section(string title, Action? all, Control body, Control? extra = null, string? eyebrow = null)
    {
        var actions = new List<Control>();
        if (extra is not null) actions.Add(extra);
        if (all is not null)
        {
            var more = Ui.Button(I18n.T("home.all"), all, "ghost");
            more.Padding = new Thickness(8, 4);
            actions.Add(more);
        }
        return Ui.Col(14, HubLook.Head(eyebrow ?? "Creator Hub", title, actions.ToArray()), body);
    }
}
