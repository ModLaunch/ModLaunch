using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Minecraft;

namespace ModLaunch.Views;

/// <summary>Вкладка «Моды»: содержимое выбранной сборки — моды, ресурспаки, шейдеры и датапаки.</summary>
public sealed partial class MinecraftPage
{
    static string _kind = "mod";
    string _filter = "";
    readonly HashSet<string> _updating = [];

    Control ContentView()
    {
        var a = Mc.Active;
        if (a is null) return NoBuildView();
        var col = new StackPanel { Spacing = 14 };

        var updates = McContent.Updates(a);
        if (updates.Count > 0) col.Children.Add(UpdatesBanner(a, updates));

        // Виды содержимого — чипы со счётчиками; справа — добавить, папка, проверка обновлений.
        var chips = Ui.Row(6);
        foreach (var kind in Mc.Kinds)
        {
            var k = kind;
            var n = McContent.Count(a, kind);
            var chip = Ui.Button(I18n.T($"mine.kind.{kind}.many") + (n > 0 ? $" · {n}" : ""), () => { _kind = k; _filter = ""; Build(); }, "chip");
            if (_kind == kind) chip.Classes.Add("active");
            chips.Children.Add(chip);
        }
        chips.VerticalAlignment = VerticalAlignment.Center;
        var filter = new TextBox { Width = 220, Watermark = I18n.T("mine.filter"), Text = _filter, VerticalAlignment = VerticalAlignment.Center };
        var list = new StackPanel { Spacing = 8 };
        filter.TextChanged += (_, _) => { _filter = filter.Text ?? ""; FillContent(list, a); };
        var right = Ui.Row(8,
            filter,
            Ui.Button(I18n.T("mine.add"), () => { _type = _kind; Go("catalog"); }, "primary", Icons.Plus),
            Ui.Button("", () => AddFile(a), "icon ghost", Icons.FilePlus, I18n.T("mine.addFile")),
            Ui.Button("", () => Actions.OpenFolder(Directory.CreateDirectory(a.Folder(_kind)).FullName), "icon ghost", Icons.Folder, I18n.T("games.openFolder")),
            Ui.Button("", () => _ = CheckNow(a), "icon ghost", Icons.Refresh, I18n.T("mr.checkUpdates")));
        var bar = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(right);
        bar.Children.Add(chips);
        col.Children.Add(bar);

        if (Hint(a) is { } hint) col.Children.Add(hint);
        FillContent(list, a);
        col.Children.Add(list);
        return col;
    }

    void FillContent(StackPanel list, McInstance a)
    {
        list.Children.Clear();
        var items = McContent.List(a, _kind)
            .Where(i => _filter == "" || i.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase) || i.File.Contains(_filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => !i.Enabled).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (items.Count == 0)
        {
            list.Children.Add(Ui.Card(Ui.Col(10,
                Ui.Text(_filter != "" ? I18n.T("mine.empty.filter") : I18n.T($"mine.empty.{_kind}"), "h3"),
                Ui.Text(I18n.T("mine.empty.text"), "muted", wrap: true),
                Ui.Row(10,
                    Ui.Button(I18n.T("mine.add"), () => { _type = _kind; Go("catalog"); }, "primary", Icons.Bag),
                    Ui.Button(I18n.T("mine.addFile"), () => AddFile(a), "", Icons.FilePlus))), 24));
            return;
        }
        var updates = McContent.Updates(a).ToDictionary(u => u.Item.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var item in items) list.Children.Add(ItemRow(a, item, updates.GetValueOrDefault(item.Key)));
    }

    Control ItemRow(McInstance a, McItem item, McUpdate? update)
    {
        var sub = new List<string>();
        if (item.Version is { Length: > 0 } v) sub.Add(I18n.T("mod.version", ("version", v)));
        if (item.Author is { Length: > 0 } au) sub.Add(au);
        sub.Add(GamePage.Size(item.Size));
        sub.Add(item.Key);

        var title = Ui.Row(8, Ui.Text(item.Name, "h3"));
        if (!item.Enabled) title.Children.Add(ModRow.Tag(I18n.T("inst.off"), Ui.Res("Surface3"), Ui.Res("Muted")));
        if (item.ProjectId is null) title.Children.Add(ModRow.Tag(I18n.T("mine.manual"), Ui.Res("Surface3"), Ui.Res("Muted")));
        var middle = Ui.Col(4, title, Ui.Text(string.Join(" · ", sub), "small muted"));
        if (item.Description is { Length: > 0 } d)
            middle.Children.Add(new TextBlock { Text = d, FontSize = 12.5, Foreground = Ui.Res("Faint"), TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
        middle.VerticalAlignment = VerticalAlignment.Center;

        var toggle = new ToggleSwitch { IsChecked = item.Enabled, OnContent = "", OffContent = "", VerticalAlignment = VerticalAlignment.Center, MinWidth = 0 };
        ToolTip.SetTip(toggle, item.Enabled ? I18n.T("mod.disable") : I18n.T("mod.enable"));
        toggle.IsCheckedChanged += (_, _) => Toggle(a, item, toggle.IsChecked == true);

        var right = Ui.Row(6);
        if (update is not null)
        {
            var busy = _updating.Contains(item.Key);
            var b = Ui.Button(busy ? I18n.T("upd.updating") : I18n.T("upd.to", ("version", update.Latest.Number)), () => RunUpdates(a, [update]), "primary", Icons.ArrowUp);
            b.IsEnabled = !busy;
            right.Children.Add(b);
        }
        if (item.Url is string url) right.Children.Add(Ui.Button("", () => Ui.OpenUrl(url), "icon ghost", Icons.External, I18n.T("mod.page")));
        right.Children.Add(toggle);
        right.Children.Add(Ui.Button("", () => ConfirmDelete(a, item), "icon ghost", Icons.Trash, I18n.T("mod.remove")));
        right.VerticalAlignment = VerticalAlignment.Center;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 14 };
        grid.Children.Add(Ui.Thumb(item.Icon, item.Name, 48, 12, 96));
        Grid.SetColumn(middle, 1);
        grid.Children.Add(middle);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        var card = new Border { Classes = { "card" }, Padding = new Thickness(12), Child = grid };
        if (!item.Enabled) card.Opacity = 0.7;
        Ctx.Attach(card, () => Ctx.Menu(
            Ctx.Item(item.Enabled ? I18n.T("ctx.disable") : I18n.T("ctx.enable"), item.Enabled ? Icons.EyeOff : Icons.Eye, () => Toggle(a, item, !item.Enabled)),
            update is not null ? Ctx.Item(I18n.T("upd.to", ("version", update.Latest.Number)), Icons.ArrowUp, () => RunUpdates(a, [update])) : null,
            "-",
            Ctx.Folder(I18n.T("v4.folder"), a.Folder(item.Kind)),
            item.Url is string link ? Ctx.Link(I18n.T("mod.page"), link) : null,
            Ctx.Copy(I18n.T("ctx.copyName"), item.Name),
            Ctx.Copy(I18n.T("mine.copyFile"), item.Key),
            "-",
            Ctx.Item(I18n.T("mod.remove"), Icons.Trash, () => ConfirmDelete(a, item))));
        return card;
    }

    void Toggle(McInstance a, McItem item, bool on)
    {
        try
        {
            McContent.SetEnabled(a, item, on);
            W.Toast(I18n.T(on ? "toast.enabled" : "toast.disabled"));
        }
        catch (Exception e) { W.Toast(Jobs.Explain(e), bad: true); }
        Build();
        Mc.Notify();
    }

    void ConfirmDelete(McInstance a, McItem item)
    {
        void Remove()
        {
            W.CloseDialog();
            try { McContent.Delete(a, item); W.Toast(I18n.T("mine.removed", ("name", item.Name))); }
            catch (Exception e) { W.Toast(Jobs.Explain(e), bad: true); }
            Build();
            Mc.Notify();
        }
        if (!Settings.Data.Bool("confirmRemove", true)) { Remove(); return; }
        W.Dialog(I18n.T("mod.removeConfirm", ("name", item.Name)),
            Ui.Text(I18n.T("mine.removeConfirm.text"), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), W.CloseDialog),
            Ui.Button(I18n.T("mod.remove"), Remove, "primary", Icons.Trash));
    }

    /// <summary>Свой файл: .jar в моды, .zip в ресурспаки/шейдеры/датапаки — копируем в папку сборки.</summary>
    async void AddFile(McInstance a)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var patterns = _kind == "mod" ? new[] { "*.jar" } : ["*.zip"];
        if (await Pickers.Typed(top, I18n.T("mine.addFile"), patterns, I18n.T($"mine.kind.{_kind}.many")) is not { } file) return;
        try
        {
            var dir = Directory.CreateDirectory(a.Folder(_kind)).FullName;
            File.Copy(file, Path.Combine(dir, Path.GetFileName(file)), true);
            W.Toast(I18n.T("mine.added", ("name", Path.GetFileName(file))));
            _ = Refresh(a, force: true);
        }
        catch (Exception e) { W.Toast(Jobs.Explain(e), bad: true); }
        Build();
    }

    async Task CheckNow(McInstance a)
    {
        W.Toast(I18n.T("mine.checking"));
        try
        {
            var found = await Task.Run(async () =>
            {
                await McContent.Identify(a);
                return await McContent.CheckUpdates(a);
            });
            W.Toast(found.Count == 0 ? I18n.T("mr.upToDate") : I18n.T("mr.updatesFound", ("n", found.Count)));
        }
        catch (Exception e) { W.Toast(Jobs.Explain(e), bad: true); }
        Build();
        Mc.Notify();
    }

    Control UpdatesBanner(McInstance a, List<McUpdate> updates)
    {
        var names = string.Join(", ", updates.Take(4).Select(u => u.Item.Name)) + (updates.Count > 4 ? "…" : "");
        var text = Ui.Col(3, Ui.Text(I18n.T("mine.updates.title", ("n", updates.Count)), "h3"), Ui.Text(names, "small muted"));
        text.VerticalAlignment = VerticalAlignment.Center;
        var all = Ui.Button(I18n.T("mr.updateAll"), () => RunUpdates(a, updates), "primary", Icons.ArrowUp);
        all.IsEnabled = _updating.Count == 0;
        all.VerticalAlignment = VerticalAlignment.Center;
        var dock = new DockPanel();
        DockPanel.SetDock(all, Dock.Right);
        dock.Children.Add(all);
        dock.Children.Add(Ui.Row(14, new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(12), Background = Ui.Hex("#263FB950"), Child = Ui.Icon(Icons.ArrowUp, 20, Ui.Res("Good")) }, text));
        return new Border { Classes = { "card" }, Padding = new Thickness(16, 14), BorderBrush = Ui.Hex("#553FB950"), BorderThickness = new Thickness(1), Child = dock };
    }

    void RunUpdates(McInstance a, List<McUpdate> updates)
    {
        foreach (var u in updates) _updating.Add(u.Item.Key);
        Build();
        Jobs.Run(updates.Count == 1 ? updates[0].Item.Name : I18n.T("mine.updates.job", ("n", updates.Count)), "Minecraft", async (_, progress, ct) =>
        {
            var failed = 0;
            foreach (var u in updates)
            {
                try { await McModrinth.Update(a, u, progress, ct); }
                catch (OperationCanceledException) { throw; }
                catch (Exception e) { failed++; Guard.Log(e); }
            }
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                foreach (var u in updates) _updating.Remove(u.Item.Key);
                W.Toast(failed == 0 ? I18n.T("mr.updated") : I18n.T("mine.updates.failed", ("n", failed)), bad: failed > 0);
                Build();
                Mc.Notify();
            });
            if (failed == updates.Count) throw new InvalidOperationException(I18n.T("mine.updates.failed", ("n", failed)));
        });
    }

    /// <summary>Подсказки под чипами: чистая игра без загрузчика, шейдеры без Iris.</summary>
    Control? Hint(McInstance a)
    {
        if (!a.Modded && _kind == "mod")
            return HintCard(Icons.Info, I18n.T("mine.hint.vanilla"), I18n.T("mine.hint.vanilla.go"), () => Go("settings"));
        if (_kind == "shader" && a.Modded && !HasShaderMod(a))
        {
            var (slug, name) = ShaderMod(a);
            return HintCard(Icons.Sparkles, I18n.T("mine.hint.shaders", ("mod", name)), I18n.T("mine.hint.shaders.go", ("mod", name)), () => InstallBySlug(a, slug, name));
        }
        if (_kind == "shader" && !a.Modded)
            return HintCard(Icons.Info, I18n.T("mine.hint.shadersVanilla"), I18n.T("mine.hint.vanilla.go"), () => Go("settings"));
        return null;
    }

    static Control HintCard(string icon, string text, string action, Action run)
    {
        var go = Ui.Button(action, run, "", Icons.Forward);
        go.VerticalAlignment = VerticalAlignment.Center;
        var words = Ui.Text(text, "small", wrap: true);
        words.VerticalAlignment = VerticalAlignment.Center;
        var dock = new DockPanel();
        DockPanel.SetDock(go, Dock.Right);
        dock.Children.Add(go);
        dock.Children.Add(Ui.Row(12, Ui.Icon(icon, 18, Ui.Res("Brand")), words));
        return new Border { Classes = { "card" }, Padding = new Thickness(14, 10), Child = dock };
    }

    /// <summary>Шейдеры работают через Iris (Fabric, Quilt, NeoForge) или Oculus (Forge).</summary>
    static (string Slug, string Name) ShaderMod(McInstance a) => a.Loader == "forge" ? ("oculus", "Oculus") : ("iris", "Iris");

    static bool HasShaderMod(McInstance a) =>
        McContent.List(a, "mod").Any(m => m.Enabled && (m.Name.Contains("Iris", StringComparison.OrdinalIgnoreCase) || m.Name.Contains("Oculus", StringComparison.OrdinalIgnoreCase)
            || m.Key.StartsWith("iris", StringComparison.OrdinalIgnoreCase) || m.Key.StartsWith("oculus", StringComparison.OrdinalIgnoreCase)));

    void InstallBySlug(McInstance a, string slug, string name)
    {
        Jobs.Run(name, "Minecraft", async (_, progress, ct) =>
        {
            var p = await McModrinth.Project(slug, ct);
            await McModrinth.Install(a, p, null, progress, ct);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { W.Toast(I18n.T("mr.installedOne", ("title", p.Title))); Build(); Mc.Notify(); });
        });
    }

    // ---------------------------------------------------------------- нет сборок

    Control NoBuildView()
    {
        var art = new Border
        {
            Width = 72, Height = 72, CornerRadius = new CornerRadius(20), Background = Ui.Hex("#263FB950"),
            Child = Ui.Icon(Icons.Cube, 34, Ui.Hex("#3FB950")),
        };
        var col = Ui.Col(14,
            art,
            Ui.Text(I18n.T("mine.first.title"), "h2"),
            Ui.Text(I18n.T("mine.first.text"), "muted", wrap: true),
            Ui.Row(10,
                Ui.Button(I18n.T("mine.build.new"), CreateDialog, "primary", Icons.Plus),
                Ui.Button(I18n.T("mine.import.mrpack"), () => ImportMrpack(), "", Icons.FilePlus),
                Ui.Button(I18n.T("mine.import.other"), () => Go("builds"), "ghost", Icons.Layers)));
        col.MaxWidth = 640;
        return Ui.Card(col, 30);
    }
}
