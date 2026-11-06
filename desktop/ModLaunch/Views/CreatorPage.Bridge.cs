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

/// <summary>Карточка моста ModScript (движок, уверенность, улики) — для среды и для подробностей в анкете.</summary>
public sealed partial class CreatorPage
{
    /// <summary>Карточка моста: движок, уверенность, улики, загрузчик и путь (код или файлы).</summary>
    internal static Control BridgeCard(MX.BridgeReport? r, bool noPath)
    {
        if (r is null)
            return new Border
            {
                Padding = new Thickness(14, 12), CornerRadius = new CornerRadius(4), BorderBrush = HubLook.Edge, BorderThickness = new Thickness(1),
                Child = noPath ? Ui.Text(I18n.T("v16.bridge.noPath"), "small muted", wrap: true)
                    : Ui.Row(10, new ProgressBar { IsIndeterminate = true, Width = 80, VerticalAlignment = VerticalAlignment.Center }, Ui.Text(I18n.T("v16.bridge.scanning"), "small muted")),
            };
        var target = MX.Bridge.TargetOf(r);
        var color = r.AntiCheat is not null ? Ui.Res("Bad") : r.Code ? Ui.Res("Good") : Ui.Res("Warn");
        var route = r.Code
            ? I18n.T("v16.bridge.code", ("lang", XStudio.Lang(target!.Language)), ("loader", r.Loader))
            : I18n.T("v16.bridge.files");
        var head = new WrapPanel();
        foreach (var part in new Control[] { Ui.Icon(Icons.Layers, 16, HubLook.Cyan), Ui.Text(I18n.T("v16.bridge.title"), "strong"),
            Gx.Tag(r.Title, Ui.Res("Surface3")), Gx.Tag(route, color is ISolidColorBrush c ? new SolidColorBrush(Color.FromArgb(40, c.Color.R, c.Color.G, c.Color.B)) : null, color) })
        {
            part.Margin = new Thickness(0, 0, 8, 4);
            part.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(part);
        }
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = r.Confidence, Height = 4, MinHeight = 4, MinWidth = 0, Width = 90, VerticalAlignment = VerticalAlignment.Center, Foreground = HubLook.Cyan };
        var sure = Ui.Row(10, Ui.Text(I18n.T("v16.bridge.sure", ("n", r.Confidence)), "small muted"), bar);
        var clues = new WrapPanel();
        foreach (var e in r.Evidence.Take(6))
        {
            var t = new Border { CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 2), Margin = new Thickness(0, 0, 6, 6), Background = Ui.Res("Surface2"), Child = HubLook.MonoText(e, 10.5) };
            clues.Children.Add(t);
        }
        var dot = Ui.Dot(r.LoaderReady ? Ui.Res("Good") : Ui.Res("Warn"));
        dot.Margin = new Thickness(0, 0, 8, 0);
        var loader = new DockPanel { Children = { Dockd(dot),
            Ui.Text(r.LoaderReady ? I18n.T("v16.bridge.loader.ok", ("loader", r.Loader)) : I18n.T("v16.bridge.loader.no", ("loader", r.Loader)), "small", wrap: true) } };
        var col = Ui.Col(10, head, sure, clues);
        if (r.Code) col.Children.Add(loader);
        if (r.Hint is { } hint) col.Children.Add(Ui.Text(BridgeHint(r), "small muted", wrap: true));
        return new Border { Padding = new Thickness(14, 12), CornerRadius = new CornerRadius(4), BorderBrush = HubLook.Edge, BorderThickness = new Thickness(1), Background = HubLook.Raised, Child = col };
    }

    /// <summary>Подсказка моста по-русски (у ModScript X консольного — по-английски, здесь — из строк программы).</summary>
    internal static string BridgeHint(MX.BridgeReport r)
    {
        var key = r.AntiCheat is not null ? "v16.bridge.hint.anticheat" : $"v16.bridge.hint.{r.Engine}{(r.LoaderReady ? ".ok" : "")}";
        if (I18n.Has(key)) return I18n.T(key, ("ac", r.AntiCheat ?? ""));
        if (I18n.Has($"v16.bridge.hint.{r.Engine}")) return I18n.T($"v16.bridge.hint.{r.Engine}");
        return r.Hint ?? "";
    }

    static Control Dockd(Control c) { DockPanel.SetDock(c, Dock.Left); return c; }
}
