using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Games;

namespace ModLaunch.Views;

/// <summary>
/// 12.0: «Добавить виджет» как на iPhone. Слева — все виды, в середине — живой предпросмотр в настоящем размере
/// (под ним — S, M, L), справа — настройки вида: игра (своя или из Steam), цвет, текст, формат. Тот же экран —
/// «Изменить виджет»: вид уже выбран, меняются размер и настройки.
/// </summary>
public sealed partial class HomePage
{
    // Клетка предпросмотра — как на главной при обычной ширине окна.
    const double Cell = 172, CellH = 134, CellGap = 14;

    static readonly IReadOnlyDictionary<string, string> NoOpts = new Dictionary<string, string>();

    /// <summary>Галерея виджетов. edit — номер виджета для правки, at — куда вставить новый, kind — какой вид открыть.</summary>
    public void OpenStudio(int edit = -1, int at = -1, string? kind = null)
    {
        var w = MainWindow.Current;
        if (w is null) return;
        var entries = Widgets.Entries();
        var current = edit >= 0 && edit < entries.Count ? entries[edit] : null;
        var id = current?.Id ?? (kind is not null && Widgets.Find(kind) is not null ? kind : "game");
        var size = current?.Size ?? DefaultSize(Widgets.Find(id)!);
        var opts = new Dictionary<string, string>(current?.Opts ?? NoOpts);
        var mine = MyGames();

        var kinds = new StackPanel { Spacing = 2 };
        var stage = new Border
        {
            Width = 2 * Cell + CellGap + 40, Height = 2 * CellH + CellGap + 40, CornerRadius = new CornerRadius(18),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(((Ui.Res("Surface3") as ISolidColorBrush)?.Color ?? Colors.DimGray), 0), new GradientStop(((Ui.Res("Surface2") as ISolidColorBrush)?.Color ?? Colors.Black), 1) },
            },
        };
        var sizes = Ui.Row(6);
        sizes.HorizontalAlignment = HorizontalAlignment.Center;
        var settings = new StackPanel { Spacing = 14 };
        var name = Ui.Text("", "h3");
        var about = Ui.Text("", "small muted", wrap: true);

        void Preview()
        {
            Control widget;
            try { widget = Widget(new Widgets.Entry(id, size, new Dictionary<string, string>(opts)), mine); }
            catch { widget = Shell(Ui.Text(I18n.T("v10.w.error"), "small muted"), null); }
            var (cw, ch) = Widgets.Cells(size);
            widget.Width = cw * Cell + (cw - 1) * CellGap;
            widget.Height = ch * CellH + (ch - 1) * CellGap;
            widget.HorizontalAlignment = HorizontalAlignment.Center;
            widget.VerticalAlignment = VerticalAlignment.Center;
            // Предпросмотр — картинка: кнопки внутри не жмутся.
            widget.IsHitTestVisible = false;
            stage.Child = widget;
            sizes.Children.Clear();
            foreach (var sz in Widgets.Find(id)!.Sizes)
            {
                var one = sz;
                var b = Ui.Button(SizeName(one), () => { size = one; Preview(); }, one == size ? "primary" : "ghost");
                b.MinWidth = 70;
                sizes.Children.Add(b);
            }
        }

        void Settings()
        {
            settings.Children.Clear();
            var k = Widgets.Find(id)!;
            name.Text = I18n.T("v10.w.name." + id);
            about.Text = I18n.T("v12w.desc." + id);
            if (k.Options.Length == 0) settings.Children.Add(Ui.Text(I18n.T("v12w.noopts"), "small muted", wrap: true));
            foreach (var o in k.Options)
            {
                var opt = o;
                string Value() => opts.TryGetValue(opt.Key, out var v) ? v : opt.Default;
                Control control;
                switch (opt.Type)
                {
                    case "game":
                    {
                        var choices = GameChoices(id);
                        var box = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 360 };
                        if (id == "game") box.Items.Add(I18n.T("v12w.game.pick"));
                        foreach (var (_, label) in choices) box.Items.Add(label);
                        var shift = id == "game" ? 1 : 0;
                        var at0 = choices.FindIndex(c => c.Value == Value());
                        box.SelectedIndex = at0 >= 0 ? at0 + shift : 0;
                        box.SelectionChanged += (_, _) =>
                        {
                            var i = box.SelectedIndex - shift;
                            if (i < 0 || i >= choices.Count) opts.Remove(opt.Key); else opts[opt.Key] = choices[i].Value;
                            Preview();
                        };
                        control = box;
                        break;
                    }
                    case "choice" when opt.Key == "tint":
                    {
                        var row = new WrapPanel();
                        foreach (var t in opt.Choices)
                        {
                            var one = t;
                            var fill = TintOf(one) ?? Ui.Res("Brand");
                            var dot = new Border
                            {
                                Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 8, 8), Background = fill,
                                BorderBrush = one == Value() ? Ui.Res("Text") : Brushes.Transparent, BorderThickness = new Thickness(2.5),
                                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                            };
                            // «Как тема» — половинкой: видно, что это не отдельный цвет.
                            if (one == "auto") dot.Child = new Border { Width = 12, Height = 24, HorizontalAlignment = HorizontalAlignment.Right, CornerRadius = new CornerRadius(0, 12, 12, 0), Background = Ui.Res("Surface3") };
                            ToolTip.SetTip(dot, I18n.T("v12w.tint." + one));
                            StoreKit.OnClick(dot, () => { opts[opt.Key] = one; Preview(); Settings(); });
                            row.Children.Add(dot);
                        }
                        control = row;
                        break;
                    }
                    case "choice":
                    {
                        var row = new WrapPanel();
                        foreach (var c in opt.Choices)
                        {
                            var one = c;
                            var b = Ui.Button(I18n.T($"v12w.opt.{opt.Key}.{one}"), () => { opts[opt.Key] = one; Preview(); Settings(); }, one == Value() ? "primary" : "");
                            b.Margin = new Thickness(0, 0, 6, 6);
                            row.Children.Add(b);
                        }
                        control = row;
                        break;
                    }
                    case "text":
                    {
                        var box = new TextBox { Text = Value(), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 400, Height = 110, Watermark = I18n.T("v12w.note.hint") };
                        box.TextChanged += (_, _) => { opts[opt.Key] = box.Text ?? ""; Preview(); };
                        control = box;
                        break;
                    }
                    default:
                    {
                        var check = new CheckBox { Content = I18n.T($"v12w.opt.{opt.Key}.on"), IsChecked = Value() == "1" };
                        check.IsCheckedChanged += (_, _) => { opts[opt.Key] = check.IsChecked == true ? "1" : "0"; Preview(); };
                        control = check;
                        break;
                    }
                }
                settings.Children.Add(Ui.Col(6, Ui.Text(I18n.T("v12w.opt." + opt.Key), "small muted"), control));
            }
        }

        void Kinds()
        {
            kinds.Children.Clear();
            foreach (var k in Widgets.Catalog)
            {
                var one = k.Id;
                var on = one == id;
                var badge = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(9), Background = on ? Ui.Res("Brand") : Ui.Res("Surface3"), Child = Ui.Icon(IconOf(k), 15, on ? Brushes.White : Ui.Res("Muted")) };
                var row = new Border
                {
                    Padding = new Thickness(8, 6), CornerRadius = new CornerRadius(10), Background = on ? Ui.Res("Surface3") : Brushes.Transparent,
                    Child = Ui.Row(10, badge, new TextBlock { Text = I18n.T("v10.w.name." + one), FontSize = 13.5, FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }),
                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                };
                StoreKit.OnClick(row, () =>
                {
                    if (one == id) return;
                    id = one;
                    var nk = Widgets.Find(id)!;
                    if (!nk.Sizes.Contains(size)) size = DefaultSize(nk);
                    opts.Clear();
                    Kinds();
                    Preview();
                    Settings();
                });
                kinds.Children.Add(row);
            }
        }

        Kinds();
        Preview();
        Settings();

        var middle = Ui.Col(12, stage, sizes);
        var right = Ui.Col(10, Ui.Col(2, name, about), new ScrollViewer { MaxHeight = 300, Content = settings });
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions(current is null ? "200,18,Auto,18,*" : "0,0,Auto,18,*") };
        var list = new ScrollViewer { MaxHeight = 380, Content = kinds, IsVisible = current is null };
        Grid.SetColumn(list, 0);
        Grid.SetColumn(middle, 2);
        Grid.SetColumn(right, 4);
        body.Children.Add(list);
        body.Children.Add(middle);
        body.Children.Add(right);

        var done = Ui.Button(current is null ? I18n.T("v12w.add") : I18n.T("v10.w.done"), () =>
        {
            if (current is null) Widgets.Add(id, size, at, new Dictionary<string, string>(opts));
            else Widgets.Update(edit, size, new Dictionary<string, string>(opts));
            w.CloseDialog();
        }, "primary", current is null ? Icons.Plus : Icons.Check);
        w.Dialog(current is null ? I18n.T("v12w.gallery") : I18n.T("v12w.edit"), body, current is null ? 940 : 760, Ui.Button(I18n.T("common.cancel"), w.CloseDialog), done);
    }

    static string DefaultSize(Widgets.Kind k) => k.Sizes.Contains("m") ? "m" : k.Sizes[0];
}
