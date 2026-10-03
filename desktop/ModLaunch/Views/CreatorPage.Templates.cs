using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Games;

namespace ModLaunch.Views;

public sealed partial class CreatorPage
{
    // ---------------------------------------------------------------- примочки

    string _exCategory = "all";

    Control Examples()
    {
        var chips = Ui.Row(6);
        foreach (var c in Templates.Categories)
        {
            var cc = c;
            var n = c == "all" ? Templates.All.Length : Templates.All.Count(t => Templates.Category(t) == c);
            var chip = Ui.Button($"{I18n.T("cr.cat." + c)} · {n}", () => { _exCategory = cc; Build(); }, "chip");
            if (_exCategory == c) chip.Classes.Add("active");
            chips.Children.Add(chip);
        }
        var grid = new UniformGrid { Columns = 2 };
        foreach (var t in Templates.All.Where(t => _exCategory == "all" || Templates.Category(t) == _exCategory))
        {
            var title = I18n.T($"cr.ex.{t.Id}");
            if (_filter != "" && !title.Contains(_filter, StringComparison.OrdinalIgnoreCase) && !t.Code.Contains(_filter, StringComparison.OrdinalIgnoreCase)) continue;
            grid.Children.Add(TemplateCard(t));
        }
        return Ui.Col(14, chips, grid);
    }

    /// <summary>Создать проект из шаблона и открыть его в редакторе.</summary>
    void TryTemplate(Template t)
    {
        var p = Projects.Create(I18n.T($"cr.ex.{t.Id}"), t.Code);
        _tab = "mine";
        Open(p);
        Build();
    }

    /// <summary>
    /// Карточка шаблона — целиком кнопка. Внутренней кнопки «Попробовать» нет:
    /// её нажатие всплыло бы к карточке и проект создался бы дважды.
    /// </summary>
    Control TemplateCard(Template t)
    {
        var game = GameCatalog.ById(t.Game);
        var preview = new TextBlock
        {
            Text = Preview(t.Code),
            FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"), FontSize = 12, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var card = new Button
        {
            Classes = { "card-btn" }, Padding = new Thickness(18), Margin = new Thickness(0, 0, 14, 14),
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
            Content = Ui.Col(10,
                Ui.Row(10, Ui.Thumb(game?.ArtUrl, game?.Name ?? t.Game, 36, 9), Ui.Col(2, Ui.Text(I18n.T($"cr.ex.{t.Id}"), "h3"), Ui.Text(game?.Name ?? t.Game, "small muted"))),
                Ui.Text(I18n.T($"cr.ex.{t.Id}.text"), "small muted", wrap: true),
                new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 10), Child = preview },
                Ui.Row(6, Ui.Icon(Icons.Wand, 14, Ui.Res("Brand2")), Ui.Text(I18n.T("cr.ex.try"), "small brand"))),
        };
        card.Click += (_, _) => TryTemplate(t);
        return card;
    }

    /// <summary>Суть примера: команды после описания мода (или подсказки, если команд нет).</summary>
    static string Preview(string code)
    {
        var meta = new[] { "mod ", "version ", "author ", "about ", "game ", "icon " };
        var lines = code.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Trim() != "").ToList();
        var body = lines.Where(l => !l.TrimStart().StartsWith('#') && !meta.Any(m => l.StartsWith(m))).Take(4).ToList();
        if (body.Count == 0) body = lines.Where(l => l.TrimStart().StartsWith("# ") && l.Contains(' ') && !meta.Any(m => l.TrimStart('#', ' ').StartsWith(m)) && (l.Contains("needs") || l.Contains("config"))).Take(4).ToList();
        return string.Join('\n', body);
    }
}
