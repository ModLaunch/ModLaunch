using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Mods;
using ModLaunch.Social;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>
/// 20.0: страница мода по эскизам. Сверху в два столбца: слева — большая галерея (картинки и ролики,
/// стрелки и миниатюры; картинок нет — обложка мода), справа — карточка с названием, тремя цифрами,
/// главной кнопкой («Установить» или «Играть с модом»), «Отключить», «Удалить», ♥ и «⋯» и списком
/// «Ставится вместе». Ниже пять вкладок (Описание · Изменения · Отзывы · Файлы · Статистика),
/// справа от них — «О моде» и коротко об отзывах.
/// </summary>
public sealed partial class ModPage
{
    int _shot;

    /// <summary>Для снимков экрана: подставить картинки и ролики в галерею.</summary>
    internal void ShotMedia(List<string> images, List<string> videos)
    {
        if (_details is null) return;
        _details = _details with { Images = images, Videos = videos };
        Build();
    }

    void Build20()
    {
        var col = new StackPanel { Spacing = 22, Margin = new Thickness(StoreKit.Gutter, 18, StoreKit.Gutter, 40), MaxWidth = StoreKit.MaxWidth };
        col.Children.Add(Crumbs20());

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,384"), ColumnSpacing = 24 };
        top.Children.Add(Gallery20());
        var info = InfoCard20();
        info.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(info, 1);
        top.Children.Add(info);
        col.Children.Add(top);

        col.Children.Add(Tabs20());
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,320"), ColumnSpacing = 24 };
        body.Children.Add(TabContent20());
        var side = new StackPanel { Spacing = 16, VerticalAlignment = VerticalAlignment.Top };
        side.Children.Add(Kit20.Card(Views.Aside.Mod(_g, Mod, _details, _versions?.Count ?? 0), new Thickness(20, 18)));
        side.Children.Add(ReviewsSummary20());
        Grid.SetColumn(side, 1);
        body.Children.Add(side);
        col.Children.Add(body);

        Content = new ScrollViewer { Content = col, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    /// <summary>«Subnautica › Каталог › Название мода».</summary>
    Control Crumbs20()
    {
        var row = Ui.Row(8);
        var id = _g.Def.Id;
        void Part(string text, Action? open)
        {
            if (row.Children.Count > 0) row.Children.Add(Ui.Icon(Icons.ChevronRight, 13, Ui.Res("Faint")));
            if (open is null) { row.Children.Add(new TextBlock { Text = text, FontSize = 13.5, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 520 }); return; }
            var b = new Button { Classes = { "link20" }, Padding = new Thickness(6, 3), Margin = new Thickness(-6, 0), Foreground = Ui.Res("Muted"), Content = new TextBlock { Text = text, FontSize = 13.5, FontWeight = FontWeight.Medium } };
            b.Click += (_, _) => open();
            row.Children.Add(b);
        }
        Part(_g.Def.Name, () => MainWindow.Current?.Navigate(() => new GamePage(id)));
        Part(I18n.T("v20.tab.catalog"), () => MainWindow.Current?.Navigate(() => new GamePage(id, "catalog")));
        Part(Mod.Name, null);
        foreach (var c in row.Children) c.VerticalAlignment = VerticalAlignment.Center;
        return row;
    }

    // ---------------------------------------------------------------- галерея

    Control Gallery20()
    {
        var media = new List<(string Url, string? Video)>();
        foreach (var url in _details?.Images ?? []) media.Add((url, null));
        foreach (var v in _details?.Videos ?? []) media.Add(($"https://i.ytimg.com/vi/{v}/hqdefault.jpg", v));
        if (_shot >= media.Count) _shot = 0;

        var stage = new Panel();
        var frame = new Border { Classes = { "mod-picture" }, CornerRadius = new CornerRadius(18), ClipToBounds = true, Background = Ui.Res("Surface2"), Child = stage, Height = 400 };
        var host = new StackPanel { Spacing = 12, Children = { frame } };
        host.SizeChanged += (_, e) => frame.Height = Math.Clamp(e.NewSize.Width * 9 / 16, 240, 520);

        if (media.Count == 0)
        {
            // Картинок у мода нет — его обложка (значок на арте игры) во всю высоту карточки справа.
            stage.Children.Add(ModCover.Create(_g, Mod, 1280, big: true, iconShare: 0.34));
            frame.Height = double.NaN;
            frame.MinHeight = 300;
            frame.VerticalAlignment = VerticalAlignment.Stretch;
            host.Children.Remove(frame);
            return frame;
        }

        var image = new Image { Stretch = Stretch.UniformToFill };
        var play = new Border
        {
            Width = 72, Height = 50, CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(Color.Parse("#E0FF0033")), IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Child = Ui.Icon(Icons.Play, 24, Brushes.White),
        };
        stage.Children.Add(image);
        stage.Children.Add(play);
        stage.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
        stage.PointerPressed += (_, _) =>
        {
            var (url, video) = media[_shot];
            if (video is not null) Ui.OpenUrl($"https://www.youtube.com/watch?v={video}");
            else ShowImage(url);
        };

        var thumbs = new List<Border>();
        var gen = 0;
        void Show(int i)
        {
            _shot = (i + media.Count) % media.Count;
            var (url, video) = media[_shot];
            var my = ++gen;
            image.Source = null;
            _ = Images.FromUrl(url, 1600).ContinueWith(t => { if (t.Result is Bitmap b) Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (my == gen) image.Source = b; }); });
            play.IsVisible = video is not null;
            for (var k = 0; k < thumbs.Count; k++) thumbs[k].BorderBrush = k == _shot ? Ui.Res("Brand") : Brushes.Transparent;
        }

        if (media.Count > 1)
        {
            Button Arrow(string icon, int step, HorizontalAlignment side)
            {
                var b = new Button
                {
                    Classes = { "chip20" }, Width = 46, Height = 46, CornerRadius = new CornerRadius(23), Padding = new Thickness(0),
                    Background = new SolidColorBrush(Color.FromArgb(150, 10, 10, 14)),
                    HorizontalAlignment = side, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0),
                    HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                    Content = Ui.Icon(icon, 20, Brushes.White),
                };
                b.Click += (_, e) => { e.Handled = true; Show(_shot + step); };
                return b;
            }
            stage.Children.Add(Arrow(Icons.ChevronLeft, -1, HorizontalAlignment.Left));
            stage.Children.Add(Arrow(Icons.ChevronRight, 1, HorizontalAlignment.Right));

            // Миниатюры: до пяти; если медиа больше — на последней «+N», она открывает вкладку картинок.
            var strip = new Grid { ColumnSpacing = 10, Height = 82 };
            var shown = Math.Min(media.Count, 5);
            for (var i = 0; i < 5; i++) strip.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            for (var i = 0; i < shown; i++)
            {
                var index = i;
                var thumbImage = new Image { Stretch = Stretch.UniformToFill };
                _ = Images.FromUrl(media[i].Url, 360).ContinueWith(t => { if (t.Result is Bitmap b) Avalonia.Threading.Dispatcher.UIThread.Post(() => thumbImage.Source = b); });
                var inner = new Panel { Children = { thumbImage } };
                if (media[i].Video is not null)
                    inner.Children.Add(new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = new SolidColorBrush(Color.FromArgb(200, 10, 10, 14)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = Ui.Icon(Icons.Play, 13, Brushes.White) });
                var more = i == shown - 1 && media.Count > shown;
                if (more)
                    inner.Children.Add(new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(170, 8, 8, 12)),
                        Child = new TextBlock { Text = "+" + (media.Count - shown + 1), FontSize = 20, FontWeight = FontWeight.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                    });
                var thumb = new Border
                {
                    CornerRadius = new CornerRadius(12), ClipToBounds = true, Background = Ui.Res("Surface2"), BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent,
                    Child = inner, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                };
                thumb.PointerPressed += (_, _) => { if (more) { _tab = "images"; Build(); } else Show(index); };
                Grid.SetColumn(thumb, i);
                strip.Children.Add(thumb);
                thumbs.Add(thumb);
            }
            host.Children.Add(strip);
        }
        Show(_shot);
        return host;
    }

    // ---------------------------------------------------------------- карточка справа

    Control InfoCard20()
    {
        var mod = Mod;
        var installed = Actions.IsInstalled(_g, mod.Id);
        var busy = Actions.IsBusy(_g, mod.Id);
        var record = _g.Registry?.Get(_g.Def.RecordId(mod.Id, mod.Source));
        var enabled = record?.Bool("enabled", true) ?? true;
        var stat = Reviews.Stats().GetValueOrDefault($"{_g.Def.Id}|{mod.Id}");
        var col = new StackPanel { Spacing = 14 };

        // Значок, игра и раздел.
        var tags = Ui.Row(6, Kit20.Tag(_g.Def.ShortName));
        if (mod.Categories.FirstOrDefault() is { } category) tags.Children.Add(Kit20.Tag(category));
        tags.VerticalAlignment = VerticalAlignment.Center;
        col.Children.Add(Ui.Row(12, Ui.Thumb(mod.Icon, mod.Name, 56, 14, 112), tags));

        col.Children.Add(new TextBlock
        {
            Text = mod.Name, FontFamily = Look.Display, FontSize = 28, FontWeight = FontWeight.Bold, LetterSpacing = -0.6, LineHeight = 34, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, -6),
        });
        var byline = new List<string>();
        if (mod.Author != "") byline.Add(I18n.T("mod.by", ("author", mod.Author)));
        if (mod.UpdatedAt is not null) byline.Add(I18n.T("v20.mod.updated", ("when", Ui.Ago(mod.UpdatedAt))));
        if (byline.Count > 0) col.Children.Add(new TextBlock { Text = string.Join(" · ", byline), FontSize = 13.5, Foreground = Ui.Res("Muted"), TextWrapping = TextWrapping.Wrap });

        // Три цифры: оценка, загрузки (или зависимости), версия.
        Control Fact(Control top, string label, Action? open = null)
        {
            var b = new Border
            {
                Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 9),
                Child = Ui.Col(2, top, new TextBlock { Text = label, FontSize = 12, Foreground = Ui.Res("Faint"), TextTrimming = TextTrimming.CharacterEllipsis }),
            };
            if (open is not null) StoreKit.OnClick(b, open);
            return b;
        }
        Control Big(string text) => new TextBlock { Text = text, FontSize = 17, FontWeight = FontWeight.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
        var facts = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 8 };
        void AddFact(Control c) { Grid.SetColumn(c, facts.Children.Count); facts.Children.Add(c); }
        AddFact(stat is null
            ? Fact(Ui.Row(5, Ui.Icon(Icons.Star, 15, Ui.Hex("#F2C25C")), Big("—")), I18n.T("mod.rate"), () => { _tab = "reviews"; Build(); })
            : Fact(Ui.Row(5, Ui.Icon(Icons.Star, 15, Ui.Hex("#F2C25C"), fill: true), Big($"{stat.Avg:0.0}")), I18n.T("rev.count." + I18n.Plural(stat.Count, "one", "few", "many"), ("n", stat.Count)), () => { _tab = "reviews"; Build(); }));
        if (mod.Downloads > 0) AddFact(Fact(Big(I18n.Compact(mod.Downloads)), I18n.T("mt.stats.downloads")));
        else AddFact(Fact(Big((_details?.Requirements.Count ?? 0).ToString()), I18n.T("mod.depsTile")));
        AddFact(Fact(Big(mod.Version != "" ? mod.Version : "—"), I18n.T("aside.version").ToLower(I18n.Culture)));
        col.Children.Add(facts);

        // Главная кнопка: «Играть с модом», если он стоит, иначе «Установить».
        var running = Launcher.IsRunning(_g.Def.Id);
        Button main;
        if (installed)
        {
            main = Ui.Button(running ? I18n.T("v20.game.running") : I18n.T("v20.mod.play"), () => Actions.Play(_g), "primary", Icons.Play);
            main.IsEnabled = !running && enabled && _g.Status == Detect.Found;
        }
        else
        {
            main = Ui.Button(busy ? I18n.T("v20.installing") : I18n.T("mod.install"), async () => { await Actions.Install(_g, mod); Build(); }, "primary", busy ? Icons.Hourglass : Icons.Download);
            main.IsEnabled = !busy && _g.Status == Detect.Found;
        }
        main.HorizontalAlignment = HorizontalAlignment.Stretch;
        main.HorizontalContentAlignment = HorizontalAlignment.Center;
        main.Height = 52;
        main.FontSize = 16.5;
        main.CornerRadius = new CornerRadius(14);
        col.Children.Add(main);

        // Строка состояния под кнопкой.
        if (installed)
        {
            var update = ModUpdates.Found.TryGetValue(_g.Def.Id, out var list) ? list.FirstOrDefault(u => u.RecordId == record?.Str("id")) : null;
            if (update is not null)
            {
                var go = Kit20.Link(I18n.T("upd.to", ("version", update.Latest)), () => { _ = Actions.UpdateMods(_g, [update]); Build(); }, 13.5);
                col.Children.Add(Ui.Row(8, Ui.Icon(Icons.ArrowUp, 15, Kit20.Readable("Warn")), new TextBlock { Text = I18n.T("v20.mod.hasUpdate"), FontSize = 13.5, FontWeight = FontWeight.SemiBold, Foreground = Kit20.Readable("Warn"), VerticalAlignment = VerticalAlignment.Center }, go));
            }
            else
            {
                var v = record?.Str("version") is { Length: > 0 } rv ? rv : mod.Version;
                var text = !enabled ? I18n.T("v20.mod.off") : v != "" ? I18n.T("v20.mod.installedV", ("version", v)) : I18n.T("mod.installed");
                var color = enabled ? Kit20.Readable("Good") : Ui.Res("Muted");
                col.Children.Add(Ui.Row(7, Ui.Icon(enabled ? Icons.Check : Icons.EyeOff, 15, color), new TextBlock { Text = text, FontSize = 13.5, FontWeight = FontWeight.SemiBold, Foreground = color, VerticalAlignment = VerticalAlignment.Center }));
            }
        }
        else if (mod.Source == "nexus")
            col.Children.Add(Ui.Row(8, Ui.Icon(Icons.Info, 14, Ui.Res("Muted")), new TextBlock { Text = I18n.T("mod.nexusHint"), FontSize = 12.5, LineHeight = 17, Foreground = Ui.Res("Muted"), TextWrapping = TextWrapping.Wrap, MaxWidth = 310 }));

        // Отключить · Удалить · ♥ · ⋯
        var row = Ui.Row(8);
        if (installed && record is not null)
        {
            var toggle = Ui.Button(enabled ? I18n.T("mod.disable") : I18n.T("mod.enable"), () => SetEnabled20(record, !enabled), "");
            toggle.Height = 40;
            toggle.Padding = new Thickness(14, 0);
            toggle.VerticalContentAlignment = VerticalAlignment.Center;
            var remove = Ui.Button(I18n.T("mod.remove"), () => Remove20(record), "");
            remove.Height = 40;
            remove.Padding = new Thickness(14, 0);
            remove.VerticalContentAlignment = VerticalAlignment.Center;
            remove.Foreground = Kit20.Readable("Bad");
            remove.BorderBrush = new SolidColorBrush(Color.FromArgb(110, 255, 107, 107));
            remove.BorderThickness = new Thickness(1);
            row.Children.Add(toggle);
            row.Children.Add(remove);
        }
        row.Children.Add(ActionRow(mod, installed));
        col.Children.Add(row);

        // Предупреждения: старый мод, снят с публикации, просит другой загрузчик.
        if (_g.Def.IsLegacy(mod.UpdatedAt)) col.Children.Add(Warning20(I18n.T("badge.old.hint"), false));
        if (mod.Deprecated) col.Children.Add(Warning20(I18n.T("compat.deprecated"), true));
        if (Compat.Foreign(_g.Def, mod.Dependencies) is { } loader) col.Children.Add(Warning20(I18n.T("compat.page", ("loader", loader), ("ours", _g.Def.LoaderName)), false));

        // «Ставится вместе»: зависимости — что уже стоит и что поставится.
        if (_details is { Requirements.Count: > 0 } d)
        {
            col.Children.Add(new Border { Height = 1, Background = Ui.Res("Line"), Margin = new Thickness(0, 2) });
            var chips = new WrapPanel();
            var missing = 0;
            foreach (var r in d.Requirements.Take(8))
            {
                var have = Actions.IsInstalled(_g, r.Id);
                if (!have) missing++;
                var color = have ? Kit20.Readable("Good") : Ui.Res("Brand2");
                var c = color is ISolidColorBrush sc ? sc.Color : Colors.Gray;
                var chip = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(32, c.R, c.G, c.B)), CornerRadius = new CornerRadius(8), Padding = new Thickness(9, 5), Margin = new Thickness(0, 0, 6, 6),
                    Child = Ui.Row(5, Ui.Icon(have ? Icons.Check : Icons.Plus, 13, color), new TextBlock { Text = r.Name, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = color, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis }),
                };
                var rr = r;
                StoreKit.OnClick(chip, () => MainWindow.Current?.Navigate(() => new ModPage(_g.Def.Id, new ModInfo { Source = Mod.Source == "hub" ? Catalog.GuessSource(_g.Def, rr.Id) : Mod.Source, Id = rr.Id, Name = rr.Name })));
                chips.Children.Add(chip);
            }
            var note = missing == 0 ? I18n.T("v20.mod.with.all")
                : _g.Def.Catalog == Games.CatalogKind.Nexus ? I18n.T("mod.deps.nexus")
                : I18n.T("v20.mod.with.will");
            col.Children.Add(Ui.Col(8,
                new TextBlock { Text = I18n.T("v20.mod.with"), FontSize = 14, FontWeight = FontWeight.Bold },
                chips,
                new TextBlock { Text = note, FontSize = 12.5, Foreground = Ui.Res("Faint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, -4, 0, 0) }));
        }

        // Ссылки: страница мода на сайте.
        if (mod.Url is not null)
        {
            var site = Kit20.Link(I18n.T("aside.openOn", ("site", Views.Aside.SourceName(mod.Source))), () => Ui.OpenUrl(mod.Url), 13.5);
            site.HorizontalAlignment = HorizontalAlignment.Left;
            site.Margin = new Thickness(-8, -4, 0, -6);
            col.Children.Add(site);
        }
        return Kit20.Card(col, new Thickness(22, 20));
    }

    static Control Warning20(string text, bool bad)
    {
        var color = bad ? Kit20.Readable("Bad") : Kit20.Readable("Warn");
        var c = color is ISolidColorBrush s ? s.Color : Colors.Orange;
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(28, c.R, c.G, c.B)), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8),
            Child = Ui.Row(8, Ui.Icon(Icons.Alert, 15, color), new TextBlock { Text = text, FontSize = 12.5, LineHeight = 17, TextWrapping = TextWrapping.Wrap, MaxWidth = 300, VerticalAlignment = VerticalAlignment.Center }),
        };
    }

    /// <summary>Включить или выключить мод; если он нужен другим — спросить, как в списке модов игры.</summary>
    void SetEnabled20(System.Text.Json.Nodes.JsonObject record, bool on)
    {
        var registry = _g.Registry!;
        var id = record.Str("id")!;
        var w = MainWindow.Current!;
        void Apply(IEnumerable<string> ids)
        {
            w.CloseDialog();
            try { foreach (var x in ids.Distinct()) registry.SetEnabled(x, on); w.Toast(I18n.T(on ? "toast.enabled" : "toast.disabled")); }
            catch (Exception e) { w.Toast(Jobs.Explain(e), bad: true); }
            AppState.Notify();
            Build();
        }
        var graph = DepGraph.Build(registry);
        if (!on && graph.Dependents([id]) is { Count: > 0 } dependents)
        {
            var names = string.Join(", ", dependents.Take(6).Select(m => m.Str("displayName") ?? m.Str("name") ?? m.Str("id")));
            w.Dialog(I18n.T("deps.off.title", ("name", Mod.Name)),
                Ui.Text(I18n.T("deps.off.text", ("list", names)), "muted", wrap: true),
                Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
                Ui.Button(I18n.T("deps.off.only"), () => Apply([id]), "ghost"),
                Ui.Button(I18n.T("deps.off.all", ("n", dependents.Count)), () => Apply([id, .. dependents.Select(m => m.Str("id")!)]), "primary", Icons.Power));
            return;
        }
        Apply(on ? [id, .. graph.DisabledNeeds(id).Select(m => m.Str("id")!)] : [id]);
    }

    void Remove20(System.Text.Json.Nodes.JsonObject record)
    {
        var registry = _g.Registry!;
        var id = record.Str("id")!;
        var w = MainWindow.Current!;
        var text = Ui.Col(10, Ui.Text(I18n.T("mod.removeConfirm.text"), "muted", wrap: true));
        var dependents = DepGraph.Build(registry).Dependents([id]);
        if (dependents.Count > 0)
            text.Children.Add(Ui.Row(8, Ui.Icon(Icons.Alert, 15, Ui.Res("Warn")),
                Ui.Text(I18n.T("deps.remove.warn", ("list", string.Join(", ", dependents.Take(6).Select(m => m.Str("name") ?? m.Str("id"))))), "small", wrap: true)));
        w.Dialog(I18n.T("mod.removeConfirm", ("name", Mod.Name)), text,
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("mod.remove"), () =>
            {
                w.CloseDialog();
                try
                {
                    Snapshots.Take(_g, "remove");
                    var preset = record.Str("kind") == "preset";
                    registry.Remove(id);
                    if (preset) Installer.SyncPreset(registry, null);
                    w.Toast(I18n.T("toast.removed"));
                }
                catch (Exception e) { w.Toast(Jobs.Explain(e), bad: true); }
                AppState.Notify();
                Build();
            }, "primary", Icons.Trash));
    }

    // ---------------------------------------------------------------- вкладки

    Control Tabs20()
    {
        var hub = _brief.Source == "hub";
        var reviews = hub ? _comments?.Count ?? 0 : Reviews.Stats().GetValueOrDefault($"{_g.Def.Id}|{Mod.Id}")?.Count ?? 0;
        var tabs = new List<(string Id, string Text, int? Count)>
        {
            ("about", I18n.T("mt.about"), null),
            ("changes", I18n.T("mt.changes"), null),
            ("reviews", hub ? I18n.T("mt.comments") : I18n.T("mt.reviews"), reviews),
            ("files", I18n.T("mt.files"), _versions?.Count),
            ("stats", I18n.T("mt.stats"), null),
        };
        // Картинки, ролики и требования теперь наверху страницы; вкладкой — только если открыли их явно.
        if (_tab == "images") tabs.Add(("images", I18n.T("mt.images"), _details?.Images.Count));
        if (_tab == "videos") tabs.Add(("videos", I18n.T("mt.videos"), _details?.Videos.Count));
        if (_tab == "reqs") tabs.Add(("reqs", I18n.T("mt.reqs"), _details?.Requirements.Count));

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var (id, text, count) in tabs)
        {
            var content = Ui.Row(7, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            if (count is > 0) content.Children.Add(new Border { Classes = { "tab-badge" }, Child = Ui.Text(count.Value.ToString(), "tiny") });
            var b = new Button { Classes = { "pivot" }, Content = content };
            var active = _tab == id;
            if (active) b.Classes.Add("active");
            var tab = id;
            b.Click += (_, _) => { if (_tab == tab) return; _tab = tab; Build(); };
            var ink = new Border { Classes = { "tab-ink" }, Height = 3, CornerRadius = new CornerRadius(2), Margin = new Thickness(14, 0), VerticalAlignment = VerticalAlignment.Bottom, IsVisible = active, IsHitTestVisible = false };
            row.Children.Add(new Panel { Children = { b, ink } });
        }
        return new Border
        {
            BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 8, 0, 0),
            Child = new ScrollViewer { Content = row, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled },
        };
    }

    Control TabContent20()
    {
        if (_tab is not "about") return TabContent();
        if (_error is not null) return Ui.Card(Ui.Text(_error, "muted", wrap: true), 20);
        if (_details is null) return Ui.Col(14, new Border { Classes = { "skeleton" }, Height = 220, CornerRadius = new CornerRadius(16) });
        var col = Ui.Col(18, About());
        if (AuthorCard() is { } author) col.Children.Add(author);
        return col;
    }

    /// <summary>Коротко об отзывах: средняя оценка, звёзды, «Оставить отзыв» и когда его можно оставить.</summary>
    Control ReviewsSummary20()
    {
        var hub = _brief.Source == "hub";
        var col = Ui.Col(12, new TextBlock { Text = hub ? I18n.T("mt.comments") : I18n.T("rev.title"), FontSize = 15, FontWeight = FontWeight.Bold });
        if (hub)
        {
            var n = _hubMod?.Comments ?? _comments?.Count ?? 0;
            col.Children.Add(new TextBlock { Text = I18n.T("hub.comments", ("n", n)), FontSize = 13.5, Foreground = Ui.Res("Muted") });
            col.Children.Add(Wide(Ui.Button(I18n.T("v20.mod.comments.open"), () => { _tab = "reviews"; Build(); }, "", Icons.Chat)));
            return Kit20.Card(col, new Thickness(20, 18));
        }
        var stat = Reviews.Stats().GetValueOrDefault($"{_g.Def.Id}|{Mod.Id}");
        if (stat is not null)
            col.Children.Add(Ui.Row(12,
                new TextBlock { Text = $"{stat.Avg:0.0}", FontSize = 34, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center },
                Ui.Col(3, Stars(stat.Avg, 15), new TextBlock { Text = I18n.T("rev.count." + I18n.Plural(stat.Count, "one", "few", "many"), ("n", stat.Count)), FontSize = 12.5, Foreground = Ui.Res("Faint") })));
        else col.Children.Add(new TextBlock { Text = I18n.T("v20.mod.reviews.none"), FontSize = 13.5, Foreground = Ui.Res("Muted"), TextWrapping = TextWrapping.Wrap });
        if (!Reviews.Configured) return Kit20.Card(col, new Thickness(20, 18));
        var gate = PlayLog.Check(_g, Mod.Id);
        var mine = Reviews.Mine(_g.Def.Id, Mod.Id);
        col.Children.Add(Wide(Ui.Button(mine is null ? I18n.T("v20.mod.review") : I18n.T("rev.yours"), () => { _tab = "reviews"; Build(); }, "", Icons.Edit)));
        if (!gate.Ok) col.Children.Add(new TextBlock { Text = I18n.T("v20.mod.review.hint"), FontSize = 12.5, LineHeight = 17, Foreground = Ui.Res("Faint"), TextWrapping = TextWrapping.Wrap });
        return Kit20.Card(col, new Thickness(20, 18));

        static Button Wide(Button b)
        {
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.HorizontalContentAlignment = HorizontalAlignment.Center;
            b.Height = 42;
            b.VerticalContentAlignment = VerticalAlignment.Center;
            return b;
        }
    }
}
