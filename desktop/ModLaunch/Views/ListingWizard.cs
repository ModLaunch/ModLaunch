using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Views;

/// <summary>
/// 9.5: «Продать работу» — три шага вместо длинной формы: что продаёте → описание и файл →
/// цена и публикация. Справа — карточка лота так, как её увидят на витрине (обновляется на ходу),
/// внизу шага — что ещё не заполнено. Цена — с готовыми суммами и крупным «вы получите».
/// </summary>
public static class ListingWizard
{
    static readonly FontFamily Mono = new("Cascadia Mono, Consolas, JetBrains Mono, DejaVu Sans Mono, monospace");

    /// <summary>Для снимков: открыть сразу на нужном шаге (0–2).</summary>
    internal static int ShotStep = -1;

    public static void Show(Listing? l, Action? done = null, string? presetFile = null, string? presetKind = null)
    {
        var w = MainWindow.Current;
        if (w is null) return;
        if (!Social.Account.SignedIn && !Program.Screenshot) { w.Navigate(() => new AccountPage()); return; }

        // ---- поля
        TextBox Box(string value, string hint, bool multi = false, int max = 200, bool mono = false)
        {
            var b = new TextBox
            {
                Text = value, Watermark = hint, AcceptsReturn = multi, TextWrapping = multi && !mono ? TextWrapping.Wrap : TextWrapping.NoWrap,
                MinHeight = multi ? 96 : 0, MaxLength = max, VerticalContentAlignment = multi ? VerticalAlignment.Top : VerticalAlignment.Center,
            };
            if (mono) { b.FontFamily = Mono; b.FontSize = 12.5; b.MinHeight = 110; }
            return b;
        }
        Control Field(string label, Control input, string? hint = null)
        {
            var col = Ui.Col(5, Ui.Text(label, "small muted"), input);
            if (hint is not null) col.Children.Add(Ui.Text(hint, "small faint", wrap: true));
            return col;
        }

        var title = Box(l?.Title ?? "", I18n.T("mk.f.title"), max: 60);
        title.IsEnabled = l is null;
        var summary = Box(l?.Summary ?? "", I18n.T("mk.f.summary"), max: 200);
        var description = Box(l?.Description ?? "", I18n.T("mk.f.description"), multi: true, max: 5000);
        var preview = Box(l?.Preview ?? "", I18n.T("mk.f.preview.hint"), multi: true, max: Market.MaxPreview, mono: true);
        var images = Box(string.Join("\n", l?.Images ?? []), I18n.T("hub.f.images"), multi: true, max: 2400);
        images.MinHeight = 64;
        var version = Box(l?.Version ?? "1.0.0", "1.0.0", max: 20);
        version.Width = 120;
        var price = Box(l is null ? "0" : (l.Price / 100m).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), "0", max: 12);
        price.Width = 140;
        var gameIds = new List<string> { "" };
        gameIds.AddRange(AppState.Games.Select(g => g.Def.Id));
        var game = new ComboBox { ItemsSource = new[] { I18n.T("mk.anyGame") }.Concat(AppState.Games.Select(g => g.Def.Name)).ToList(), SelectedIndex = Math.Max(0, gameIds.IndexOf(l?.Game ?? "")), MinWidth = 260 };
        var hidden = new CheckBox { Content = I18n.T("mk.f.hidden"), IsChecked = l is { Active: false } };
        var kind = l?.Kind ?? presetKind ?? "model";
        var license = l?.License ?? "personal";
        string? file = null;
        var chosen = new HashSet<string>(l?.Tags ?? []);

        // ---- живое превью: та же карточка, что на витрине
        var previewHost = new Border { Width = 268 };
        Listing Draft()
        {
            var p = Market.ParseMoney(price.Text) ?? 0;
            var imgs = (images.Text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(u => Ads.HttpsUrl(u) is not null).Take(6).ToList();
            return new Listing(l?.Id ?? "draft", Social.Account.Uid ?? "", Social.Account.Get().Name ?? "ModLaunch",
                string.IsNullOrWhiteSpace(title.Text) ? I18n.T("lw.untitled") : title.Text!.Trim(), summary.Text ?? "", description.Text ?? "",
                kind, gameIds[Math.Max(0, game.SelectedIndex)], p, license, version.Text ?? "1.0.0", preview.Text ?? "", [.. chosen], imgs,
                l?.Size ?? 0, "", 1, "", hidden.IsChecked == true ? "hidden" : "active", l?.Sales ?? 0, l?.Created, DateTime.UtcNow);
        }
        var pending = false;
        void Refresh()
        {
            if (pending) return;
            pending = true;
            DispatcherTimer.RunOnce(() =>
            {
                pending = false;
                try { var card = MarketTiles.Card(Draft()); card.IsHitTestVisible = false; previewHost.Child = card; } catch { }
            }, TimeSpan.FromMilliseconds(180));
        }

        // ---- шаг 1: что продаёте
        var kinds = new UniformGrid { Columns = 5 };
        var kindButtons = new List<(string Id, Button B)>();
        foreach (var k in Market.Kinds)
        {
            var id = k;
            var b = new Button
            {
                Classes = { "lw-kind" }, Margin = new Thickness(0, 0, 8, 0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center,
                Padding = new Thickness(6, 12),
                Content = Ui.Col(6, Ui.Icon(MarketViews.KindIcon(k), 22, Ui.Res("Brand2")), new TextBlock { Text = I18n.T("mk.kind." + k), FontWeight = FontWeight.SemiBold, FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }),
            };
            if (b.Content is StackPanel sp) foreach (var c in sp.Children) c.HorizontalAlignment = HorizontalAlignment.Center;
            ToolTip.SetTip(b, I18n.T("lw.kind." + k));
            b.Click += (_, _) => { kind = id; foreach (var (kid, kb) in kindButtons) kb.Classes.Set("active", kid == kind); Refresh(); };
            b.Classes.Set("active", k == kind);
            kindButtons.Add((k, b));
            kinds.Children.Add(b);
        }
        var step1 = Ui.Col(14,
            Field(I18n.T("mk.f.title"), title, l is null ? I18n.T("lw.title.hint") : null),
            Field(I18n.T("mk.f.kind"), kinds),
            Field(I18n.T("mk.f.game"), game),
            Field(I18n.T("mk.f.summary"), summary, I18n.T("lw.summary.hint")));

        // ---- шаг 2: описание и файл
        var fileName = new TextBlock { Text = l is { FileName.Length: > 0 } ? $"{l.FileName} · {GamePage.Size(l.Size)}" : I18n.T("mk.f.noFile"), FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        void SetFile(string f)
        {
            file = f;
            var size = new FileInfo(f).Length;
            fileName.Text = $"{Path.GetFileName(f)} · {GamePage.Size(size)}";
            fileName.Foreground = size > Market.MaxFile ? Ui.Res("Bad") : Ui.Res("Text");
        }
        if (presetFile is not null && File.Exists(presetFile))
        {
            SetFile(presetFile);
            if (l is null && presetFile.EndsWith(ModScriptX.ModxDoc.Extension, StringComparison.OrdinalIgnoreCase))
                try { title.Text = ModScriptX.ModxDoc.Parse(File.ReadAllText(presetFile)).Name; } catch (IOException) { }
        }
        var pick = Ui.Button(I18n.T("mk.f.pick"), async () => { if (await Pickers.AnyFile(w, I18n.T("mk.f.pick")) is { } f) SetFile(f); }, "", Icons.FilePlus);
        var drop = new Border
        {
            Classes = { "lw-drop" }, Padding = new Thickness(16, 14), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1.5), BorderBrush = Ui.Res("Line"), Background = Ui.Res("Surface2"),
            Child = Ui.Row(12, Ui.Icon(Icons.Upload, 22, Ui.Res("Brand2")), Ui.Col(2, fileName, Ui.Text(I18n.T("lw.file.hint", ("max", GamePage.Size(Market.MaxFile))), "small muted")), pick),
        };
        DragDrop.SetAllowDrop(drop, true);
        drop.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            var f = e.DataTransfer.TryGetFiles()?.Select(x => x.TryGetLocalPath()).FirstOrDefault(p => p is not null && File.Exists(p));
            if (f is not null) SetFile(f);
        });
        var tags = new WrapPanel();
        foreach (var tag in Creator.Hub.Tags)
        {
            var t = tag;
            var chip = Ui.Button("#" + I18n.T("hub.tag." + tag), () => { }, "chip");
            chip.Margin = new Thickness(0, 0, 6, 6);
            chip.Padding = new Thickness(10, 4);
            chip.FontSize = 12;
            chip.Classes.Set("active", chosen.Contains(tag));
            chip.Click += (_, _) => { if (!chosen.Remove(t) && chosen.Count < 6) chosen.Add(t); chip.Classes.Set("active", chosen.Contains(t)); Refresh(); };
            tags.Children.Add(chip);
        }
        var step2 = Ui.Col(14,
            Field(I18n.T("mk.f.file"), drop),
            Field(I18n.T("mk.f.description"), description),
            Field(I18n.T("hub.f.images"), images, I18n.T("lw.images.hint")),
            Field(I18n.T("hub.f.tags"), tags),
            Field(I18n.T("mk.f.preview"), preview, I18n.T("v11.lw.preview.sealed")));

        // ---- шаг 3: цена и публикация
        var net = new TextBlock { FontFamily = Gx.Display, FontSize = 24, FontWeight = FontWeight.Bold, Foreground = Ui.Res("Good") };
        var netText = Ui.Text("", "small muted", wrap: true);
        void Net()
        {
            var p = Market.ParseMoney(price.Text);
            if (p is null) { net.Text = "—"; netText.Text = I18n.T("mk.f.price.bad"); return; }
            if (p == 0) { net.Text = I18n.T("mk.free"); netText.Text = I18n.T("mk.f.price.free"); return; }
            net.Text = Market.Money(p.Value - Market.Fee(p.Value));
            netText.Text = I18n.T("lw.net", ("fee", Market.FeePercent), ("feeSum", Market.Money(Market.Fee(p.Value))));
        }
        price.TextChanged += (_, _) => { Net(); Refresh(); };
        Net();
        var presets = Ui.Row(6);
        foreach (var rub in new[] { 0, 99, 199, 499, 999 })
        {
            var v = rub;
            var chip = Ui.Button(v == 0 ? I18n.T("mk.free") : $"{v} {Market.Currency}", () => price.Text = v.ToString(I18n.Culture), "chip");
            chip.Padding = new Thickness(10, 4);
            presets.Children.Add(chip);
        }
        var licenses = Ui.Col(6);
        var licenseButtons = new List<(string Id, Button B)>();
        foreach (var k in Market.Licenses)
        {
            var id = k;
            var b = new Button
            {
                Classes = { "lw-kind" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 9),
                Content = Ui.Col(1, new TextBlock { Text = I18n.T("mk.license." + k), FontWeight = FontWeight.SemiBold }, Ui.Text(I18n.T("lw.license." + k), "small muted", wrap: true)),
            };
            b.Click += (_, _) => { license = id; foreach (var (lid, lb) in licenseButtons) lb.Classes.Set("active", lid == license); Refresh(); };
            b.Classes.Set("active", k == license);
            licenseButtons.Add((k, b));
            licenses.Children.Add(b);
        }
        var youGet = new Border
        {
            Classes = { "card" }, Padding = new Thickness(16, 12),
            Child = Ui.Col(4, Ui.Text(I18n.T("lw.youGet"), "small muted"), net, netText),
        };
        var step3 = Ui.Col(14,
            Field(I18n.T("mk.f.price", ("cur", Market.Currency)), Ui.Col(8, Ui.Row(10, price, Ui.Text(Market.Currency, "muted")), presets)),
            youGet,
            Field(I18n.T("mk.f.license"), licenses),
            Field(I18n.T("hub.f.version"), version, I18n.T("lw.version.hint")),
            hidden);

        foreach (var box in new[] { title, summary, description, images, version, preview }) box.TextChanged += (_, _) => Refresh();
        game.SelectionChanged += (_, _) => Refresh();
        hidden.IsCheckedChanged += (_, _) => Refresh();

        // ---- шаги, проверка и кнопки
        var stepsHost = new ContentControl();
        var problems = Ui.Col(4);
        var dots = Ui.Row(8);
        var back = Ui.Button(I18n.T("lw.back"), () => { }, "", Icons.Back);
        var nextButton = Ui.Button(I18n.T("lw.next"), () => { }, "primary", Icons.Forward);
        var bar = new ProgressBar { Minimum = 0, Maximum = 1, IsVisible = false, Height = 4, MinHeight = 4 };
        var step = Math.Clamp(ShotStep >= 0 ? ShotStep : 0, 0, 2);
        Control[] steps = [step1, step2, step3];
        string[] stepKeys = ["lw.s1", "lw.s2", "lw.s3"];

        List<string> Missing(int s)
        {
            var m = new List<string>();
            if (s >= 0 && string.IsNullOrWhiteSpace(title.Text)) m.Add(I18n.T("lw.miss.title"));
            if (s >= 1 && l is null && file is null) m.Add(I18n.T("lw.miss.file"));
            if (s >= 1 && file is not null && new FileInfo(file).Length > Market.MaxFile) m.Add(I18n.T("lw.miss.big", ("max", GamePage.Size(Market.MaxFile))));
            if (s >= 2 && Market.ParseMoney(price.Text) is null) m.Add(I18n.T("mk.f.price.bad"));
            if (s >= 2 && !System.Text.RegularExpressions.Regex.IsMatch((version.Text ?? "").Trim(), @"^\d+\.\d+\.\d+$")) m.Add(I18n.T("lw.miss.version"));
            return m;
        }

        void Show(int s)
        {
            step = s;
            stepsHost.Content = new ScrollViewer { Content = steps[s], Height = 440, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 12, 0) };
            dots.Children.Clear();
            for (var i = 0; i < 3; i++)
            {
                var done = i < s;
                var circle = new Border
                {
                    Width = 26, Height = 26, CornerRadius = new CornerRadius(13),
                    Background = i <= s ? Ui.Res("Brand") : Ui.Res("Surface3"),
                    Child = done ? Ui.Icon(Icons.Check, 13, Ui.Res("OnBrand"))
                        : new TextBlock { Text = (i + 1).ToString(), FontWeight = FontWeight.Bold, FontSize = 12, Foreground = i <= s ? Ui.Res("OnBrand") : Ui.Res("Muted"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                };
                var label = new TextBlock { Text = I18n.T(stepKeys[i]), FontWeight = i == s ? FontWeight.Bold : FontWeight.Normal, Foreground = i == s ? Ui.Res("Text") : Ui.Res("Muted"), VerticalAlignment = VerticalAlignment.Center };
                var item = Ui.Row(8, circle, label);
                var target = i;
                if (i < s) { item.Cursor = new Cursor(StandardCursorType.Hand); item.PointerReleased += (_, _) => Show(target); }
                dots.Children.Add(item);
                if (i < 2) dots.Children.Add(new Border { Width = 28, Height = 2, Background = i < s ? Ui.Res("Brand") : Ui.Res("Line"), VerticalAlignment = VerticalAlignment.Center });
            }
            back.IsVisible = s > 0;
            nextButton.Content = Ui.Row(8, Ui.Icon(s == 2 ? Icons.Upload : Icons.Forward, 15),
                new TextBlock { Text = s < 2 ? I18n.T("lw.next") : l is null ? I18n.T("mk.f.publish") : I18n.T("common.save"), VerticalAlignment = VerticalAlignment.Center });
            problems.Children.Clear();
            Animate.From(stepsHost, "translateX(12px)", 220, 0, new Avalonia.Animation.Easings.CubicEaseOut());
        }

        async Task Publish()
        {
            // Кнопки — сразу, до первого await: пока читается файл и идёт проверка, второй щелчок создал бы второе объявление.
            nextButton.IsEnabled = back.IsEnabled = false;
            try
            {
                var p = Market.ParseMoney(price.Text)!.Value;
                var d = new ListingDraft
                {
                    Title = title.Text ?? "", Summary = summary.Text ?? "", Description = description.Text ?? "",
                    Kind = kind, License = license, Game = gameIds[Math.Max(0, game.SelectedIndex)], Price = p, Version = (version.Text ?? "").Trim(),
                    Preview = preview.Text ?? "", Tags = [.. chosen], Hidden = hidden.IsChecked == true, File = file,
                    Images = (images.Text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                };
                // 11.0: файл проходит автомодерацию до загрузки. Код, скрипт и сборка продаются инжектом:
                // вместо исходника покупатель видит, что мод меняет в игре.
                if (file is not null)
                {
                    var v = Moderation.Check(Path.GetFileName(file), await File.ReadAllBytesAsync(file));
                    Moderation.Note("lw_" + Path.GetFileName(file), d.Title, v, "publish");
                    if (!v.Ok)
                    {
                        problems.Children.Clear();
                        problems.Children.Add(CreatorPage.VerdictView(v));
                        nextButton.IsEnabled = back.IsEnabled = true;
                        return;
                    }
                    if (Moderation.Sealed(d.Kind, d.License)) d.Preview = Moderation.Mark + v.Changes;
                }
                else if (Moderation.Sealed(d.Kind, d.License) && l is null) d.Preview = "";
                bar.IsVisible = true;
                await Market.Save(d, l, new Progress<double>(r => bar.Value = r));
                w.CloseDialog();
                w.Toast(I18n.T(l is null ? "mk.published" : "mk.saved", ("title", d.Title)));
                done?.Invoke();
            }
            // Файл переместили или он занят — тоже сюда: раньше ошибка чтения терялась в «_ = Publish()».
            catch (Exception e) { w.Toast(Market.Explain(e), bad: true); nextButton.IsEnabled = back.IsEnabled = true; bar.IsVisible = false; }
        }

        back.Click += (_, _) => { if (step > 0) Show(step - 1); };
        nextButton.Click += (_, _) =>
        {
            var miss = Missing(step);
            problems.Children.Clear();
            if (miss.Count > 0)
            {
                foreach (var m in miss) problems.Children.Add(Ui.Row(6, Ui.Icon(Icons.Alert, 13, Ui.Res("Warn")), Ui.Text(m, "small", color: Ui.Res("Warn"))));
                return;
            }
            if (step < 2) Show(step + 1);
            else _ = Publish();
        };

        var left = Ui.Col(14, dots, stepsHost, problems, bar);
        var right = Ui.Col(10, Ui.Text(I18n.T("lw.preview"), "small muted"), previewHost, Ui.Text(I18n.T("lw.preview.text"), "small faint", wrap: true));
        right.Width = 268;
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 22 };
        body.Children.Add(left);
        Grid.SetColumn(right, 1);
        body.Children.Add(right);
        Show(step);
        Refresh();
        w.Dialog(l is null ? I18n.T("mk.new") : I18n.T("mk.edit"), body, 940, Ui.Button(I18n.T("common.cancel"), w.CloseDialog), back, nextButton);
    }
}
