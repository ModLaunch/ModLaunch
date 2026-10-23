using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>
/// 9.9: как выглядят награды — рамка аватара, баннер профиля, титул и значки.
/// Одни и те же детали в профиле, на кнопке аккаунта, в меню аккаунта и в шапке главной.
/// </summary>
public static class ProfileKit
{
    static LinearGradientBrush Grad(params string[] hex)
    {
        var b = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative) };
        for (var i = 0; i < hex.Length; i++) b.GradientStops.Add(new GradientStop(Color.Parse(hex[i]), hex.Length == 1 ? 0 : (double)i / (hex.Length - 1)));
        return b;
    }

    static string GameAccent(string? game) => AppState.Games.FirstOrDefault(g => g.Def.Id == game)?.Def.Accent ?? "#7C5CFF";

    static string Lighter(string hex) => Look.Mix(Color.Parse(hex), Colors.White, 0.45).ToString();

    /// <summary>Кисть рамки по её id.</summary>
    public static IBrush FrameBrush(string id)
    {
        var (key, game) = Cosmetics.Split(id);
        return key switch
        {
            "bronze" => Grad("#F0B07A", "#CD7F32", "#7A4419"),
            "silver" => Grad("#FFFFFF", "#C3CAD6", "#6F7888"),
            "gold" => Grad("#FFF1B0", "#FFC93C", "#C27C0E"),
            "neon" => Grad("#19D3F5", "#7C5CFF", "#FF3D81"),
            "flame" => Grad("#FFE066", "#FF8A1F", "#E8290B"),
            "holo" => Grad("#9BF6FF", "#BDB2FF", "#FFC6FF", "#CAFFBF"),
            "steel" => Grad("#F4F7FA", "#9FB0C2", "#3A4A5C"),
            // 16.0: рамки пути уровней (каждый 5-й уровень).
            "ruby" => Grad("#FF9AA8", "#E0115F", "#7A0A2E"),
            "emerald" => Grad("#A8F5C8", "#1FB56A", "#0B5A33"),
            "sapphire" => Grad("#A9C8FF", "#2F6BFF", "#122E7A"),
            "amethyst" => Grad("#E2C4FF", "#9B5CFF", "#4A1F8C"),
            "obsidian" => Grad("#6B6F80", "#1B1D26", "#000000"),
            "prism" => Grad("#FF5A5F", "#F5C542", "#3FCB7E", "#3CC8E6", "#9B7BFF"),
            "lava" => Grad("#FFD36B", "#FF5A1F", "#8A1000"),
            "ice" => Grad("#FFFFFF", "#9BE7FF", "#3A8DC8"),
            "royal" => Grad("#FFE9A8", "#C9A227", "#5A2A82"),
            "cosmic" => Grad("#FF7AF5", "#7C5CFF", "#19D3F5", "#0A0F2C"),
            "game" => Grad(Lighter(GameAccent(game)), GameAccent(game), Look.Mix(Color.Parse(GameAccent(game)), Colors.Black, 0.35).ToString()),
            _ => Ui.Res("Brand"),
        };
    }

    /// <summary>Подсветка вокруг редких рамок.</summary>
    static BoxShadows FrameGlow(string id)
    {
        var (key, game) = Cosmetics.Split(id);
        var hex = key switch
        {
            "holo" => "#BDB2FF", "gold" => "#FFC93C", "neon" => "#7C5CFF", "flame" => "#FF8A1F", "game" => GameAccent(game),
            "prism" => "#F5C542", "lava" => "#FF5A1F", "ice" => "#9BE7FF", "royal" => "#C9A227", "cosmic" => "#7C5CFF", "ruby" => "#E0115F", "emerald" => "#1FB56A",
            "sapphire" => "#2F6BFF", "amethyst" => "#9B5CFF", _ => null,
        };
        return hex is null ? default : BoxShadows.Parse($"0 0 22 2 {Gx.Alpha(Color.Parse(hex), 150)}");
    }

    /// <summary>Аватар в надетой рамке. ring — толщина рамки.</summary>
    public static Control Avatar(string name, double size, string? frame = null, double ring = 0)
    {
        frame ??= Cosmetics.Equipped("frame");
        if (ring <= 0) ring = Math.Max(2, size / 16);
        var inner = size - ring * 2 - 2;
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Padding = new Thickness(ring),
            Background = FrameBrush(frame), BoxShadow = size >= 60 ? FrameGlow(frame) : default,
            Child = new Border
            {
                CornerRadius = new CornerRadius(inner / 2 + 1), Padding = new Thickness(1), Background = Ui.Res("Surface"),
                Child = Ui.Thumb(null, name, inner, inner / 2, person: true),
            },
        };
    }

    /// <summary>Баннер профиля: градиент или картинка игры под затемнением.</summary>
    public static Control Banner(string? id = null)
    {
        id ??= Cosmetics.Equipped("banner");
        var (key, game) = Cosmetics.Split(id);
        if (key == "game" && AppState.Games.FirstOrDefault(g => g.Def.Id == game) is { } gs)
            return new Panel { Children = { Ui.GameImage(gs.Def, 1280, art: Images.Art.Hero), new Border { Background = Gx.ShadeUp(0.2, 220) } } };
        IBrush brush = key switch
        {
            "aurora" => Grad("#0B3D3A", "#1FB58F", "#5A3BD6"),
            "sunset" => Grad("#FF7A59", "#E83E8C", "#3B1D6E"),
            "midnight" => Grad("#0A0F2C", "#1B2A6B", "#0B0D17"),
            "vault" => Grad("#1A1430", "#3B2A7A", "#0C0A14"),
            "forge" => Grad("#2B1A0E", "#B4541A", "#0E0B09"),
            // 16.0: баннеры пути уровней.
            "ocean" => Grad("#04263D", "#0E7FA6", "#06121F"),
            "forest" => Grad("#0C2414", "#2E7D4F", "#081108"),
            "ember" => Grad("#2A0B05", "#C2410C", "#120604"),
            "nebula" => Grad("#1A0B33", "#7B2FBF", "#E83E8C"),
            "glacier" => Grad("#0B2233", "#5FB4D9", "#E6F6FF"),
            "synth" => Grad("#2B0B3F", "#FF2E97", "#19D3F5"),
            "dawn" => Grad("#3B1D4A", "#FF8A5C", "#FFD27A"),
            "void" => Grad("#000000", "#14121F", "#3B2A7A"),
            "neon" => Grad("#04040C", "#00E5A8", "#7C5CFF"),
            "cosmos" => Grad("#050818", "#283A8C", "#B04BE0", "#050818"),
            _ => new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Gx.Accent, 0), new GradientStop(Look.Mix(Gx.Accent, Color.Parse("#E83E8C"), 0.55), 0.6), new GradientStop(Color.Parse("#14121F"), 1) },
            },
        };
        return new Panel { Children = { new Border { Background = brush }, Gx.Scanlines(), new Border { Background = Gx.ShadeUp(0.45, 170) } } };
    }

    /// <summary>Титул — цветная плашка под именем.</summary>
    public static Control Title(string? id = null, double size = 12)
    {
        id ??= Cosmetics.Equipped("title");
        var (key, game) = Cosmetics.Split(id);
        var (bg, fg) = key switch
        {
            "master" => (Grad("#FFE08A", "#E0A21B"), Ui.Hex("#2A1A00")),
            "game" => (Grad(GameAccent(game), Lighter(GameAccent(game))), (IBrush)Brushes.White),
            "newbie" => ((IBrush)new SolidColorBrush(Color.FromArgb(46, 255, 255, 255)), (IBrush)Brushes.White),
            "opener" => (Grad("#2A2140", "#7B5CFF"), (IBrush)Brushes.White),
            // 16.0: титулы пути уровней — чем выше, тем ярче.
            "regular" or "explorer" or "tinkerer" => (Grad("#2B3A55", "#4C8DFF"), (IBrush)Brushes.White),
            "veteran" or "architect" or "curator" => (Grad("#0E5A44", "#3FCB7E"), (IBrush)Brushes.White),
            "pathfinder" or "virtuoso" or "grandmaster" => (Grad("#5A2A82", "#FF6FB1"), (IBrush)Brushes.White),
            "legend" => (Grad("#FFE08A", "#FF8A1F", "#E0115F"), (IBrush)Brushes.White),
            _ => (Grad("#7C5CFF", "#FF3D81"), (IBrush)Brushes.White),
        };
        return new Border
        {
            Background = bg, CornerRadius = new CornerRadius(999), Padding = new Thickness(10, 3), HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = Cosmetics.Name(new Reward("title", id)), FontSize = size, FontWeight = FontWeight.Bold, Foreground = fg },
        };
    }

    /// <summary>Значок: медаль с картинкой игры или символом.</summary>
    public static Control Badge(string id, double size = 40, bool locked = false)
    {
        var (key, game) = Cosmetics.Split(id);
        Control face;
        if (key == "game" && AppState.Games.FirstOrDefault(g => g.Def.Id == game) is { } gs)
            face = new Border { CornerRadius = new CornerRadius(size / 2), ClipToBounds = true, Child = Ui.GameImage(gs.Def, 160, art: Images.Art.Cover) };
        else
            face = new Border
            {
                CornerRadius = new CornerRadius(size / 2),
                Background = key switch
                {
                    "heart" => Grad("#FF7AA8", "#E83E8C"), "case" => Grad("#9B82FF", "#4A2FD0"), "build" => Grad("#5BE3A8", "#168A63"),
                    // 16.0: значки пути уровней.
                    "star" or "comet" or "planet" => Grad("#FFD86B", "#F29A1F"), "bolt" or "rocket" => Grad("#FF8A5C", "#E8290B"),
                    "gem" or "infinity" or "potion" or "music" => Grad("#C69BFF", "#7C3AED"), "shield" or "anvil" or "compass" or "sword" or "key" => Grad("#9FB0C2", "#3A4A5C"),
                    "moon" => Grad("#3A4A7A", "#0A0F2C"),
                    "crown" or "phoenix" => Grad("#FFE08A", "#E0A21B"), "dragon" => Grad("#5BE3A8", "#0E6B4A"), "controller" => Grad("#5AA9FF", "#7C5CFF"),
                    _ => Grad("#5AA9FF", "#2D5BD8"),
                },
                Child = Ui.Icon(key switch
                {
                    "heart" => Icons.Heart, "case" => Icons.Cube, "build" => Icons.Hourglass,
                    "star" or "comet" => Icons.Star, "bolt" => Icons.Zap, "gem" or "potion" => Icons.Sparkles, "rocket" => Icons.Flame, "shield" or "anvil" => Icons.Shield,
                    "crown" => Icons.Trophy, "dragon" or "phoenix" => Icons.Flame, "infinity" => Icons.Shuffle, "compass" or "planet" => Icons.Globe,
                    "controller" => Icons.Gamepad, "music" => Icons.Music, "key" => Icons.Key, "sword" => Icons.Tools, "moon" => Icons.Star, _ => Icons.Clock,
                }, size * 0.46, Brushes.White),
            };
        var medal = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Padding = new Thickness(Math.Max(2, size / 14)),
            Background = Grad("#FFF1B0", "#E0A21B", "#8C5A00"), Child = face, Opacity = locked ? 0.35 : 1,
        };
        ToolTip.SetTip(medal, Cosmetics.Name(new Reward("badge", id)));
        return medal;
    }

    /// <summary>Цвет редкости.</summary>
    public static string TierColor(string tier) => tier switch { "legend" => "#FF5AD1", "epic" => "#F2B84B", "rare" => "#5AA9FF", _ => "#9AA3B2" };

    /// <summary>Как выглядит награда в шкафчике: рамка — аватаром, баннер — полосой, титул — плашкой, значок — медалью.</summary>
    public static Control Preview(Reward r, string name)
    {
        return r.Kind switch
        {
            "frame" => Avatar(name, 64, r.Id, 5),
            "banner" => new Border { Width = 150, Height = 64, CornerRadius = new CornerRadius(12), ClipToBounds = true, Child = Banner(r.Id) },
            "title" => new Border { Height = 64, Child = new Border { VerticalAlignment = VerticalAlignment.Center, Child = Title(r.Id, 13) } },
            // 17.0: сборка создателя — мини-экран программы в её цветах.
            "config" => ConfigShot(r.Id, 112),
            _ => Badge(r.Id, 56),
        };
    }

    /// <summary>Мини-экран сборки создателя шириной width (высота — по пропорциям экрана 228×132).</summary>
    public static Control ConfigShot(string id, double width) =>
        CreatorBuilds.Find(id) is { } b
            ? new Viewbox { Width = width, Height = width * 132 / 228, Stretch = Stretch.Uniform, Child = SettingsPage.MiniScreen(CreatorBuilds.Config(b)) }
            : new Border { Width = width, Height = width * 132 / 228 };

    public static string PlayerName()
    {
        var p = Social.Account.Get();
        return p.SignedIn ? p.Name ?? p.Email ?? I18n.T("v93.feed.player") : I18n.T("v93.feed.player");
    }
}
