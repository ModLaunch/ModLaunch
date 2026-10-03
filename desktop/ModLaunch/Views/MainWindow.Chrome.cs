using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Threading;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>
/// Шапка окна (8.4): меню «⋯» вместо ряда редких кнопок, плавное появление панелей,
/// кольцо прогресса на значке загрузок и место под рекламу.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Правая панель сведений — из меню «⋯» или Ctrl+I.</summary>
    public void ToggleAside()
    {
        Settings.Data["asideOpen"] = !Settings.Data.Bool("asideOpen", false);
        Settings.Save();
        RenderAside();
        if (_asideHost.IsVisible) Animate.From(_asideHost, "translateX(40px)", 300);
    }

    /// <summary>Всё, что раньше занимало шапку отдельными кнопками.</summary>
    MenuFlyout MoreMenu()
    {
        var hasAside = _aside.Content is not null;
        var asideOpen = Settings.Data.Bool("asideOpen", false);
        return Ctx.Menu(
            hasAside ? Ctx.Item(asideOpen ? I18n.T("top.aside.hide") : I18n.T("top.aside.show"), Icons.Sidebar, ToggleAside, gesture: "Ctrl+I") : null,
            Ctx.Item(I18n.T("bp.open"), Icons.Tv, BigPictureWindow.Open, gesture: "F11"),
            Ctx.Item(I18n.T("cp.title"), Icons.Grid, () => Navigate(() => new ControlPanelPage()), gesture: "Ctrl+Shift+P"),
            Ctx.Item(I18n.T("lib.title"), Icons.Layers, () => Navigate(() => new LibraryPage()), gesture: "Ctrl+L"),
            Ctx.Item(I18n.T("mc.title"), Icons.Package, () => Navigate(() => new ModsCenterPage()), gesture: "Ctrl+U"),
            Ctx.Item(Settings.Data.Bool("railHidden") ? I18n.T("top.rail.show") : I18n.T("top.rail.hide"), Icons.Layers, ToggleRail, gesture: "Ctrl+B"),
            "-",
            Ctx.Item(I18n.T("cmd.title"), Icons.Search, CommandPalette, gesture: "Ctrl+P"),
            Ctx.Item(I18n.T("keys.title"), Icons.Keyboard, Shortcuts, gesture: "F1"),
            Ctx.Item(I18n.T("new.title"), Icons.Sparkles, WhatsNew.Show),
            "-",
            Ctx.Item(I18n.T("svc.title"), Icons.Server, () => Navigate(() => new SettingsPage("services"))),
            Ctx.Item(I18n.T("nav.settings"), Icons.Settings, () => Navigate(() => new SettingsPage()), gesture: "Ctrl+OemComma"));
    }

    /// <summary>Открыть или закрыть всплывающую панель (загрузки, уведомления) с мягким «выпадением».</summary>
    void TogglePanel(Border panel)
    {
        panel.IsVisible = !panel.IsVisible;
        if (panel.IsVisible) Reveal(panel);
    }

    static void Reveal(Control c) => Animate.From(c, "translateY(-10px) scale(0.97)", 300, 0, new BackEaseOut());

    /// <summary>Реклама в шапке — только когда окну хватает ширины, чтобы не теснить поиск.</summary>
    void UpdateAdVisibility()
    {
        if (_adWrap is null) return;
        _adWrap.IsVisible = Ads.Enabled && Bounds.Width / Math.Max(0.5, _appliedScale) >= 1400;
    }

    /// <summary>Кольцо на кнопке загрузок: общий прогресс всего, что сейчас качается.</summary>
    void UpdateDlRing()
    {
        var running = Jobs.All.Where(j => j.Status == JobStatus.Running).ToList();
        if (running.Count == 0) { _dlRing.IsVisible = false; return; }
        double total = running.Sum(j => j.Total > 0 ? j.Total : 0), got = running.Sum(j => j.Total > 0 ? j.Received : 0);
        var ratio = total > 0 ? got / total : running.Average(j => Math.Clamp(j.Ratio, 0, 1));
        _dlRing.IsVisible = true;
        _dlRing.SweepAngle = Math.Max(12, Math.Clamp(ratio, 0, 1) * 360);
    }
}
