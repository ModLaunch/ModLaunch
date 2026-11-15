using Avalonia.Controls;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Social;

namespace ModLaunch.Views;

/// <summary>
/// 31.0: «Пожаловаться» — одно окно для мода Hub, лота рынка, отзыва, комментария и человека.
/// Причина из списка (вирус, нарушение авторских прав, украденный мод, оскорбления, спам, 18+, читы,
/// мошенничество, другое) и пояснение; жалоба уходит в очередь «Модерация» у админа.
/// </summary>
public static class ReportUi
{
    /// <summary>Открыть окно жалобы. kind — creation/listing/review/comment/user, target — id того, на что жалуются.</summary>
    public static void Show(string kind, string target, string owner, string title)
    {
        var w = MainWindow.Current;
        if (w is null) return;
        if (!Account.SignedIn) { w.Toast(I18n.T("v31.rep.signIn"), bad: true); return; }
        var reasons = kind switch
        {
            "user" => new[] { "abuse", "scam", "spam", "cheat", "other" },
            "review" or "comment" => new[] { "abuse", "spam", "adult", "scam", "other" },
            _ => Safety.Reasons,
        };
        var chosen = reasons[0];
        var list = new StackPanel { Spacing = 4 };
        RadioButton? first = null;
        foreach (var r in reasons)
        {
            var reason = r;
            var rb = new RadioButton
            {
                GroupName = "report-reason",
                Content = Ui.Col(0, Ui.Text(I18n.T("v31.rep.r." + reason), ""), Ui.Text(I18n.T("v31.rep.r." + reason + ".hint"), "small muted", wrap: true)),
                IsChecked = first is null,
            };
            rb.IsCheckedChanged += (_, _) => { if (rb.IsChecked == true) chosen = reason; };
            first ??= rb;
            list.Children.Add(rb);
        }
        var text = new TextBox
        {
            Watermark = I18n.T("v31.rep.text.hint"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            MinHeight = 80, MaxLength = Safety.MaxText,
        };
        var head = Ui.Text(I18n.T("v31.rep.about", ("what", I18n.T("v31.rep.kind." + kind)), ("title", title)), "muted", wrap: true);
        var dmca = Ui.Text(I18n.T("v31.rep.dmca"), "small muted", wrap: true);
        w.Dialog(I18n.T("v31.rep.title"), Ui.Col(10, head, new ScrollViewer { Content = list, MaxHeight = 300 }, text, dmca),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("v31.rep.send"), async () =>
            {
                try
                {
                    await Safety.Report(kind, target, owner, title, chosen, text.Text ?? "");
                    w.CloseDialog();
                    w.Toast(I18n.T("v31.rep.done"));
                }
                catch (ServiceError e) when (e.Code == "REPORT_EXISTS") { w.CloseDialog(); w.Toast(I18n.T("v31.rep.exists")); }
                catch (Exception e) { w.CloseDialog(); w.Toast(Safety.Explain(e), bad: true); }
            }, "primary", Icons.Flag));
    }

    /// <summary>Кнопка «Пожаловаться» (маленькая, без акцента) — для карточек и строк.</summary>
    public static Button Button(string kind, string target, string owner, string title, bool iconOnly = false)
    {
        var b = iconOnly
            ? Ui.Button("", () => Show(kind, target, owner, title), "icon", Icons.Flag, I18n.T("v31.rep.button"))
            : Ui.Button(I18n.T("v31.rep.button"), () => Show(kind, target, owner, title), "ghost", Icons.Flag);
        ToolTip.SetTip(b, I18n.T("v31.rep.button"));
        return b;
    }
}
