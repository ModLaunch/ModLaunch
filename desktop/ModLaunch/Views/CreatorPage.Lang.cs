using System.IO.Compression;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Features;
using MX = ModScriptX;

namespace ModLaunch.Views;

/// <summary>
/// 11.0: язык ModScript X 2 в Creator Hub. 18.0: его старая песочница (вкладка «ModScript X» раздела ModScript)
/// повторяла среду ModScript X у проектов и убрана; здесь остались примеры, папка скриптов, текст ошибок разбора
/// и итог автомодерации — ими пользуются среда, магазин и мастер продажи.
/// </summary>
public sealed partial class CreatorPage
{
    internal static string Example(string name)
    {
        using var s = AssetLoader.Open(new Uri($"avares://ModLaunch/Assets/modscriptx/{name}.modx"));
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    /// <summary>Пример «Арена» — для нового файла и снимков экрана.</summary>
    internal static string ArenaExample() => Example("arena");

    internal static string ScriptsDir()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Directory.CreateDirectory(Path.Combine(docs != "" ? docs : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ModScript X")).FullName;
    }

    /// <summary>Сообщение ModScript X: ключи err.* — переводом (cr.err.*, как в редакторе), остальное и непереведённое — как есть.</summary>
    internal static string LxMsg(Diag d)
    {
        if (!d.Message.StartsWith("err.")) return d.Message;
        var text = Msg(d);
        return text.StartsWith("cr.err.") ? d.Message : text;
    }

    /// <summary>Итог автомодерации: пропущен или нет, что найдено и что меняет мод.</summary>
    internal static Control VerdictView(Verdict v)
    {
        var col = Ui.Col(6, Ui.Row(8, Ui.Icon(v.Ok ? Icons.Check : Icons.Alert, 15, v.Ok ? Ui.Res("Good") : Ui.Res("Bad")),
            Ui.Text(I18n.T(v.Ok ? (v.Clean ? "v11.mod.pass" : "v11.mod.passWarn") : "v11.mod.fail"), "h3", color: v.Ok ? Ui.Res("Good") : Ui.Res("Bad"))));
        if (v.Changes != "") col.Children.Add(Ui.Text(v.Changes, "small muted", wrap: true));
        foreach (var f in v.Findings)
            col.Children.Add(Ui.Row(6, Ui.Icon(f.Level == "block" ? Icons.Close : f.Level == "warn" ? Icons.Alert : Icons.Info, 13,
                f.Level == "block" ? Ui.Res("Bad") : f.Level == "warn" ? Ui.Res("Warn") : Ui.Res("Muted")), Ui.Text(f.Text, "small", wrap: true)));
        return new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 12), Child = col };
    }

    /// <summary>Свободное имя для нового .modx: arena.modx, arena-2.modx… — другой файл с тем же именем мода не затираем.</summary>
    internal static string FreeModxPath(string dir, string id)
    {
        var path = Path.Combine(dir, id + MX.ModxDoc.Extension);
        for (var n = 2; File.Exists(path); n++) path = Path.Combine(dir, $"{id}-{n}{MX.ModxDoc.Extension}");
        return path;
    }

    [SelfTest]
    static string NewModxNeverOverwrites()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "modlaunch-modx-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var first = FreeModxPath(dir, "arena");
            if (Path.GetFileName(first) != "arena.modx") throw new Exception("first: " + first);
            File.WriteAllText(first, "mine");
            var second = FreeModxPath(dir, "arena");
            if (Path.GetFileName(second) != "arena-2.modx") throw new Exception("second: " + second);
            File.WriteAllText(second, "mine too");
            if (Path.GetFileName(FreeModxPath(dir, "arena")) != "arena-3.modx") throw new Exception("third");
            // Ключи разбора — переводом (если словари загружены: в --selfcheck их нет), обычный текст — как есть.
            var translated = I18n.T("cr.err.openBrace") != "cr.err.openBrace";
            if (translated && LxMsg(new Diag(7, "err.openBrace")) == "err.openBrace" || LxMsg(new Diag(1, "err.noSuchKey")) != "err.noSuchKey" || LxMsg(new Diag(1, "a | b")) != "a | b")
                throw new Exception("diag text");
            return "new .modx gets a free name, diag keys translated";
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}
