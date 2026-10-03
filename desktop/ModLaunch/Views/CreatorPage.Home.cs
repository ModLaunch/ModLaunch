using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Views;

public sealed partial class CreatorPage
{
    // ---------------------------------------------------------------- главная

    /// <summary>Главная Creator Hub: создать или опубликовать мод, свои проекты, популярное в Hub, шаблоны.</summary>
    Control Home()
    {
        var col = new StackPanel { Spacing = 30 };


        var actions = new UniformGrid { Columns = 3 };
        actions.Children.Add(BigAction(Icons.Plus, I18n.T("cr.home.create"), I18n.T("cr.home.create.text"), NewProject, primary: true));
        actions.Children.Add(BigAction(Icons.Upload, I18n.T("cr.home.publish"), I18n.T("cr.home.publish.text"), PublishArchive, primary: false));
        actions.Children.Add(BigAction(Icons.Bag, I18n.T("mk.sell"), I18n.T("mk.home.sell.text", ("fee", Market.FeePercent)), () => MarketViews.Editor(null, () => { _market = null; _studio = null; Go("studio"); }), primary: false));
        col.Children.Add(actions);

        var recent = Projects.List().OrderByDescending(p => p.Updated).Take(4).ToList();
        if (recent.Count > 0)
        {
            var grid = new UniformGrid { Columns = 4 };
            foreach (var p in recent) grid.Children.Add(ProjectCard(p));
            col.Children.Add(Section(I18n.T("cr.home.continue"), null, grid));
        }

        if (_hub is null && !_hubLoading && !Program.Screenshot) _ = LoadHub();
        Control popular;
        if (_hubLoading && _hub is null)
        {
            var skeleton = new UniformGrid { Columns = 3 };
            for (var i = 0; i < 3; i++) skeleton.Children.Add(Skeleton());
            popular = skeleton;
        }
        else if (_hubError is not null) popular = Ui.Text(I18n.T("hub.error"), "muted");
        else if ((_hub ?? []).Count == 0) popular = Ui.Text(I18n.T("cr.home.hubEmpty"), "muted");
        else popular = Grid3(Hub.Sort(_hub!, "trending").Take(6));
        col.Children.Add(Section(I18n.T("cr.home.popular"), () => Go("hub"), popular));

        if (_market is null && !_marketLoading && !Program.Screenshot) _ = LoadMarket();
        var fresh = Market.Sort(_market ?? [], "new").Take(3).ToList();
        if (fresh.Count > 0)
        {
            var shop = new UniformGrid { Columns = 3 };
            foreach (var l in fresh) shop.Children.Add(MarketViews.Card(l));
            col.Children.Add(Section(I18n.T("mk.home.fresh"), () => Go("market"), shop));
        }

        var templates = new UniformGrid { Columns = 3 };
        foreach (var t in Templates.All.Take(3)) templates.Children.Add(TemplateCard(t));
        col.Children.Add(Section(I18n.T("cr.home.templates"), () => Go("examples"), templates));
        return col;
    }

    /// <summary>Заголовок раздела со ссылкой «Все» справа (если есть куда вести).</summary>
    static Control Section(string title, Action? all, Control body)
    {
        var head = new DockPanel();
        if (all is not null)
        {
            var more = Ui.Button(I18n.T("home.all"), all, "ghost");
            more.Padding = new Thickness(8, 4);
            DockPanel.SetDock(more, Dock.Right);
            head.Children.Add(more);
        }
        head.Children.Add(Ui.Text(title, "h2"));
        return Ui.Col(14, head, body);
    }

    /// <summary>Большая карточка-действие; акцентная — только одна на экране.</summary>
    static Control BigAction(string icon, string title, string text, Action click, bool primary)
    {
        var circle = new Border
        {
            Width = 48, Height = 48, CornerRadius = new CornerRadius(24),
            Background = primary ? new SolidColorBrush(Color.FromArgb(48, 255, 255, 255)) : Ui.Res("Surface3"),
            Child = Ui.Icon(icon, 22, primary ? Brushes.White : Ui.Res("Text")),
        };
        var sub = Ui.Text(text, "small", wrap: true);
        sub.Opacity = 0.8;
        var words = Ui.Col(3, Ui.Text(title, "h3"), sub);
        words.VerticalAlignment = VerticalAlignment.Center;
        var b = new Button
        {
            Padding = new Thickness(22), CornerRadius = new CornerRadius(18), Margin = new Thickness(0, 0, 14, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = Ui.Row(16, circle, words),
        };
        b.Classes.Add(primary ? "primary" : "card-btn");
        b.Click += (_, _) => click();
        return b;
    }
}
