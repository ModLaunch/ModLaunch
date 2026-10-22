using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Games;
using MX = ModScriptX;

namespace ModLaunch.Features;

/// <summary>Инжект в библиотеке: купленный в Creator Hub или свой .modx.</summary>
public sealed record InjectItem(
    string Id, string Title, string Game, string Kind, string Source, string? Listing, string Seller,
    string FileName, string Sha256, bool Enabled, string? LocalPath, string Changes, DateTime? Added)
{
    /// <summary>Купленный: исходник не показываем, файл на диске зашифрован.</summary>
    public bool Sealed => Source == "market";
}

/// <summary>Что сделал инжектор перед запуском — для уведомления «Запущено · инжект 12 мс».</summary>
public sealed record InjectReport(long Ms, int Applied, int Skipped, bool LoaderBack, List<string> Notes);

/// <summary>
/// 11.0: ModLaunch как инжектор. Между играми папка игры чистая: загрузчик модов отложен (как
/// «без модов» в 4.x), купленные инжекты в игре не лежат. На «Играть» за миллисекунды: загрузчик
/// возвращается на место, инжекты ставятся (свои .modx — переводом ModScript X, архивы — в папку
/// модов), игра стартует. Игра закрылась — инжекты снимаются ровно по списку, мусор загрузчика
/// убирается, загрузчик снова откладывается. Steam, перезапускающий игру сам, дождёмся.
/// Если ModLaunch закрыли посреди игры, список остаётся на диске и уборка случится при следующем старте.
/// </summary>
public static class Inject
{
    public static bool On => Settings.Data.Bool("injectMode", true);

    static string Dir => Directory.CreateDirectory(Path.Combine(Paths.DataDir, "injects")).FullName;
    static readonly JsonFile Lib = new(Path.Combine(Paths.DataDir, "injects", "library.json"), () => new JsonObject { ["items"] = new JsonObject() });
    static JsonObject Items => Lib.Data.Obj("items");

    public static event Action? Changed;
    static void Notify() => Changed?.Invoke();

    /// <summary>Включить или выключить режим инжектора. Выключили — загрузчики всех игр возвращаются на место.</summary>
    public static void SetOn(bool on)
    {
        Settings.Data["injectMode"] = on;
        Settings.Save();
        if (!on)
            foreach (var g in AppState.Games.Where(g => Vanilla.Mode(g.Def.Id) == "inject"))
                try { Vanilla.Unpark(g.Def); g.Refresh(); } catch { }
        Notify();
        AppState.Notify();
    }

    // ---------------------------------------------------------------- библиотека

    public static List<InjectItem> All() => Items.Select(kv => Read(kv.Key, kv.Value as JsonObject)).OfType<InjectItem>()
        .OrderByDescending(i => i.Added).ToList();

    public static List<InjectItem> ForGame(string gameId) => All().Where(i => i.Game == gameId).ToList();

    public static InjectItem? Get(string id) => Read(id, Items[id] as JsonObject);

    static InjectItem? Read(string id, JsonObject? o) => o is null ? null : new InjectItem(
        id, o.Str("title") ?? id, o.Str("game") ?? "", o.Str("kind") ?? "modx", o.Str("source") ?? "local", o.Str("listing"), o.Str("seller") ?? "",
        o.Str("fileName") ?? "", o.Str("sha256") ?? "", o.Bool("enabled", true), o.Str("localPath"), o.Str("changes") ?? "",
        DateTime.TryParse(o.Str("added"), out var d) ? d.ToUniversalTime() : null);

    static string KindOf(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        MX.ModxDoc.Extension => "modx",
        ".zip" => "package",
        _ => "file",
    };

    static string BlobPath(string id) => Path.Combine(Dir, id + ".bin");

    /// <summary>
    /// Купленный товар → инжект: автомодерация, шифрование, в библиотеку. Файл после этого удаляется —
    /// у покупателя остаётся только зашифрованная копия.
    /// </summary>
    public static (InjectItem? Item, Verdict Verdict) AddPurchase(Listing l, string file)
    {
        // Скачанный файл удаляем в любом случае (даже если автомодерация упала с исключением):
        // расшифрованный исходник не должен остаться лежать в %TEMP%.
        try
        {
            var bytes = File.ReadAllBytes(file);
            var name = l.FileName is { Length: > 0 } f ? f : Path.GetFileName(file);
            var v = Moderation.Check(name, bytes);
            var id = "mk_" + l.Id;
            Moderation.Note(id, l.Title, v, "buy");
            if (!v.Ok) return (null, v);
            File.WriteAllBytes(BlobPath(id), Vault.Seal(bytes));
            Items[id] = new JsonObject
            {
                ["title"] = l.Title, ["game"] = GameCatalog.ById(l.Game) is not null ? l.Game : "", ["kind"] = KindOf(name), ["source"] = "market",
                ["listing"] = l.Id, ["seller"] = l.Author, ["fileName"] = name, ["sha256"] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                ["enabled"] = true, ["changes"] = v.Changes, ["added"] = DateTime.UtcNow.ToString("o"),
            };
            Lib.Save();
            Notify();
            return (Get(id), v);
        }
        finally { try { File.Delete(file); } catch { } }
    }

    /// <summary>Свой .modx из Creator Hub: ставится при каждом запуске игры (файл читается заново — правки видны сразу).</summary>
    public static (InjectItem? Item, Verdict Verdict) AddLocal(string modxPath, string gameId)
    {
        var text = File.ReadAllText(modxPath);
        var v = Moderation.CheckModx(text);
        var doc = MX.ModxDoc.Parse(text);
        var id = "my_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(modxPath).ToLowerInvariant() + "|" + gameId)))[..12].ToLowerInvariant();
        Moderation.Note(id, doc.Name, v, "local");
        if (!v.Ok) return (null, v);
        Items[id] = new JsonObject
        {
            ["title"] = doc.Name == "" ? Path.GetFileNameWithoutExtension(modxPath) : doc.Name, ["game"] = gameId, ["kind"] = "modx", ["source"] = "local",
            ["seller"] = Social.Account.Get().Name ?? "", ["fileName"] = Path.GetFileName(modxPath), ["localPath"] = Path.GetFullPath(modxPath),
            ["enabled"] = true, ["changes"] = v.Changes, ["added"] = DateTime.UtcNow.ToString("o"),
        };
        Lib.Save();
        Notify();
        return (Get(id), v);
    }

    public static void SetEnabled(string id, bool on)
    {
        if (Items[id] is not JsonObject o) return;
        o["enabled"] = on;
        if (on) Moderation.Clear(id);
        Lib.Save();
        Notify();
    }

    public static void SetGame(string id, string gameId)
    {
        if (Items[id] is not JsonObject o) return;
        o["game"] = gameId;
        Lib.Save();
        Notify();
    }

    public static void Remove(string id)
    {
        Items.Remove(id);
        try { File.Delete(BlobPath(id)); } catch { }
        Lib.Save();
        Notify();
    }

    /// <summary>Для снимков экрана: купленный инжект и свой .modx (обычным путём — с автомодерацией и шифрованием).</summary>
    public static void Demo()
    {
        var game = AppState.Games.FirstOrDefault(g => g.Status == Detect.Found && !g.Def.IsMinecraft)?.Def.Id ?? "";
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "modlaunch-shots-injects")).FullName;
        var bought = Path.Combine(dir, "arena.modx");
        File.WriteAllText(bought, Views.CreatorPage.ArenaExample());
        AddPurchase(new Listing("demo-arena", "seller", "Mira", "Arena waves", "", "", "script", game, 9900, "personal", "1.0.0", "", [], [],
            0, "", 1, "arena.modx", "active", 12, DateTime.UtcNow.AddDays(-4), DateTime.UtcNow), bought);
        var own = Path.Combine(dir, "gold.modx");
        File.WriteAllText(own, "mod \"Gold boost\"\nversion 1.0.0\n\nhook start {\n  gold += 500\n  say \"+500 gold\"\n}\n");
        AddLocal(own, game);
    }

    /// <summary>Содержимое инжекта: купленный — расшифровать, свой — прочитать с диска.</summary>
    static byte[] Load(InjectItem i) => i.Sealed ? Vault.Open(File.ReadAllBytes(BlobPath(i.Id))) : File.ReadAllBytes(i.LocalPath!);

    // ---------------------------------------------------------------- сеанс игры

    static string SessionFile(string gameId) => Path.Combine(Dir, $"session-{gameId}.json");

    public static bool Live(string gameId) => File.Exists(SessionFile(gameId));

    /// <summary>Состояние игры для подписи: live — инжекты стоят (идёт игра), clean — папка чистая, off — режим выключен.</summary>
    public static string Status(GameState g) => Live(g.Def.Id) ? "live" : On && Vanilla.Mode(g.Def.Id) == "inject" ? "clean" : On ? "ready" : "off";

    /// <summary>Перед запуском: вернуть загрузчик и поставить включённые инжекты этой игры.</summary>
    public static InjectReport Prepare(GameState g)
    {
        var sw = Stopwatch.StartNew();
        var notes = new List<string>();
        if (g.Path is not { } root || g.Def.IsMinecraft) return new(0, 0, 0, false, notes);
        // Остатки прошлого сеанса (ModLaunch закрыли посреди игры) — сначала убрать.
        if (Live(g.Def.Id)) try { Cleanup(g.Def.Id, park: false); } catch { }
        var back = false;
        if (Vanilla.Mode(g.Def.Id) == "inject") { Vanilla.Unpark(g.Def); back = true; }
        var applied = new JsonArray();
        var skipped = 0;
        foreach (var item in ForGame(g.Def.Id).Where(i => i.Enabled))
        {
            try
            {
                var bytes = Load(item);
                if (item.Sealed && Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != item.Sha256) throw new InvalidDataException("hash");
                var v = Moderation.Check(item.FileName, bytes);
                if (!v.Ok)
                {
                    Moderation.Note(item.Id, item.Title, v, "launch");
                    skipped++;
                    notes.Add(I18n.T("v11.inj.blocked", ("title", item.Title)));
                    continue;
                }
                var entry = Apply(item, bytes, g, root, notes);
                if (entry is null) { skipped++; continue; }
                applied.Add(entry);
                // Список пишем сразу после каждого инжекта: даже если следующий упадёт, уже поставленное
                // запишется и уборка после игры снимет его — ничего не остаётся в папке игры навсегда.
                File.WriteAllText(SessionFile(g.Def.Id), new JsonObject { ["path"] = root, ["at"] = DateTime.UtcNow.ToString("o"), ["items"] = applied }.ToJsonString());
            }
            catch (Exception)
            {
                // Любая ошибка одного инжекта (кривой файл, нет прав, падение перевода) не срывает запуск.
                skipped++;
                notes.Add(I18n.T("v11.inj.failed", ("title", item.Title)));
            }
        }
        Notify();
        return new(sw.ElapsedMilliseconds, applied.Count, skipped, back, notes);
    }

    /// <summary>Поставить один инжект. Возвращает запись для сеанса (что снимать после игры).</summary>
    static JsonObject? Apply(InjectItem item, byte[] bytes, GameState g, string root, List<string> notes)
    {
        switch (item.Kind)
        {
            case "modx":
            {
                // Сеансовый инжект получает свой ModScript id (имя мода + метка инжекта): его файлы и запись в
                // installed.json не пересекаются с постоянной установкой того же мода из Creator Hub и с другим
                // инжектом с тем же именем. Уборка после игры снимает ровно этот id.
                var r = MX.Injector.Inject(Namespaced(Encoding.UTF8.GetString(bytes), item.Id), root, null);
                if (!r.Doc.Ok || r.Target is null) return null;
                if (r.Target.Id == "unity")
                {
                    // C# нужно собрать. Нет BepInEx или ошибка сборки — dll не появится: инжект не применён.
                    // 15.0: сборка кладёт dll в свою папку plugins/ModScript_<id>/ — раньше её искали прямо в plugins/,
                    // не находили и снимали каждый Unity-инжект как «не собрался».
                    if (!Built(root, r.Doc.Id))
                    {
                        try { MX.Injector.Eject(r.Doc.Id, root); } catch { }
                        notes.Add(I18n.T("v11fix.inj.buildFailed", ("title", item.Title)) + Reason(r.Note));
                        return null;
                    }
                    // Покупателю — без исходника: после сборки остаётся только dll.
                    if (item.Sealed)
                        foreach (var f in r.Files.Where(f => f.EndsWith(".cs") || f.EndsWith(".csproj")))
                            try { File.Delete(Path.Combine(root, f)); } catch { }
                }
                return new JsonObject { ["id"] = item.Id, ["kind"] = "modx", ["modx"] = r.Doc.Id };
            }
            default:
            {
                var baseDir = g.Registry?.ModsDir is { } mods && Inside(mods, root) ? mods : root;
                var folder = Path.Combine(baseDir, "ML-inject-" + item.Id);
                var files = new JsonArray();
                try
                {
                    if (item.Kind == "package")
                    {
                        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
                        foreach (var e in zip.Entries.Where(e => !e.FullName.EndsWith('/')))
                        {
                            var dest = Path.GetFullPath(Path.Combine(folder, e.FullName));
                            if (!Inside(dest, folder)) continue;
                            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                            e.ExtractToFile(dest, true);
                            files.Add((JsonNode)Path.GetRelativePath(root, dest));
                        }
                    }
                    else
                    {
                        Directory.CreateDirectory(folder);
                        var dest = Path.Combine(folder, Path.GetFileName(item.FileName));
                        File.WriteAllBytes(dest, bytes);
                        files.Add((JsonNode)Path.GetRelativePath(root, dest));
                    }
                }
                catch
                {
                    // Распаковка упала на полпути — убираем недоделанную папку, чтобы не осталась в игре неучтённой.
                    try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch { }
                    throw;
                }
                return new JsonObject { ["id"] = item.Id, ["kind"] = item.Kind, ["folder"] = Path.GetRelativePath(root, folder), ["files"] = files };
            }
        }
    }

    /// <summary>Собранный Unity-мод: dll в своей папке плагина (15.0) или прямо в plugins (старая раскладка).</summary>
    internal static bool Built(string root, string id)
    {
        var plugins = Path.Combine(root, "BepInEx", "plugins");
        return File.Exists(Path.Combine(plugins, $"ModScript_{id}", $"ModScript_{id}.dll")) || File.Exists(Path.Combine(plugins, $"ModScript_{id}.dll"));
    }

    /// <summary>Почему не собралось — первая строка ошибки, коротко, для уведомления.</summary>
    internal static string Reason(string? note)
    {
        if (note is null) return "";
        var line = note.Replace("build failed:", "").Trim().Split('\n').FirstOrDefault(l => l.Trim() != "")?.Trim() ?? "";
        if (line.Contains("BepInEx is not installed")) return ": " + I18n.T("v15.inj.noBepinex");
        return line == "" ? "" : ": " + (line.Length > 140 ? line[..140] + "…" : line);
    }

    /// <summary>Имя мода дополняем меткой инжекта, чтобы ModScript id сеанса был уникальным (см. Apply).</summary>
    static string Namespaced(string source, string injectId)
    {
        var tag = " ~" + injectId;
        var m = System.Text.RegularExpressions.Regex.Match(source, "(?m)^\\s*mod\\s+\"([^\"]*)\"");
        if (!m.Success || m.Groups[1].Value.EndsWith(tag)) return source;
        var g1 = m.Groups[1];
        return source[..g1.Index] + g1.Value + tag + source[(g1.Index + g1.Length)..];
    }

    static bool Inside(string path, string root) =>
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// После игры: снять инжекты, убрать мусор загрузчика и отложить загрузчик. park = false — только снять
    /// (перед новым запуском). Возвращает, сколько инжектов снято.
    /// </summary>
    public static int Cleanup(string gameId, bool park = true)
    {
        var n = 0;
        var file = SessionFile(gameId);
        if (File.Exists(file))
        {
            JsonObject? s = null;
            try { s = JsonNode.Parse(File.ReadAllText(file)) as JsonObject; } catch { }
            var root = s.Str("path");
            if (root is not null && Directory.Exists(root))
                foreach (var e in s.Arr("items").OfType<JsonObject>())
                {
                    try
                    {
                        if (e.Str("kind") == "modx" && e.Str("modx") is { } modx) MX.Injector.Eject(modx, root);
                        else
                        {
                            foreach (var rel in e.Arr("files").Select(x => x?.ToString()).OfType<string>())
                            {
                                var p = Path.Combine(root, rel);
                                if (Inside(p, root) && File.Exists(p)) File.Delete(p);
                            }
                            if (e.Str("folder") is { } folder && Path.Combine(root, folder) is var dir && Inside(dir, root) && Directory.Exists(dir)) Directory.Delete(dir, true);
                        }
                        n++;
                    }
                    catch (Exception x) when (x is IOException or UnauthorizedAccessException) { }
                }
            try { File.Delete(file); } catch { }
        }
        if (park && On && AppState.Games.FirstOrDefault(x => x.Def.Id == gameId) is { Path: { } path } g && !g.Def.IsMinecraft)
        {
            // Чистую загрузку — только если пользователь её включил: иначе уборка затирала бы логи загрузчика
            // (BepInEx/LogOutput.log) до того, как помощник по вылетам их прочитает. Карантин — по настройке.
            if (CleanBoot.On) try { CleanBoot.Run(g); } catch { }
            try { if (!Vanilla.IsParked(gameId) && Vanilla.LoaderFiles(g.Def, path).Count > 0) Vanilla.Park(g.Def, path, "inject"); } catch { }
            g.Refresh();
        }
        Notify();
        return n;
    }

    /// <summary>
    /// Игра закрылась. Steam может перезапустить её сам за секунды — тогда ждём настоящую. Потом уборка
    /// и автомодерация: вылет в первую минуту с ошибкой — страйк каждому инжекту этого сеанса.
    /// </summary>
    public static async Task AfterExit(GameDef game, TimeSpan ran)
    {
        if (game.IsMinecraft || (!On && !Live(game.Id))) return;
        if (ran < TimeSpan.FromSeconds(20)) await Task.Delay(15000);
        // Ждём, пока закроется настоящий процесс игры (Steam мог перезапустить её сам) — без жёсткого
        // потолка: иначе на длинной сессии уборка снесла бы инжекты из ещё работающей игры. Запас на случай
        // «вечно живого» процесса — сутки.
        for (var i = 0; i < 28800 && Vanilla.GameProcessRunning(game); i++) await Task.Delay(3000);
        var ids = SessionIds(game.Id);
        var crashed = Launcher.LastExit(game.Id) is { ByUser: false, Ms: < 60_000, Code: not null and not 0 };
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (Launcher.IsRunning(game.Id)) return;
            Cleanup(game.Id);
            var off = new List<string>();
            foreach (var id in ids)
            {
                if (!crashed) { Moderation.Clear(id); continue; }
                if (Moderation.Strike(id, game.Id) && Get(id) is { } item) { SetEnabled(id, false); off.Add(item.Title); }
            }
            if (off.Count > 0) Views.MainWindow.Current?.Toast(I18n.T("v11.inj.suspended", ("list", string.Join(", ", off))), bad: true);
            AppState.Notify();
        });
    }

    static List<string> SessionIds(string gameId)
    {
        try { return (JsonNode.Parse(File.ReadAllText(SessionFile(gameId))) as JsonObject).Arr("items").OfType<JsonObject>().Select(e => e.Str("id")).OfType<string>().ToList(); }
        catch { return []; }
    }

    /// <summary>При старте ModLaunch: сеансы, которые не закрыли (программу закрыли посреди игры), — убрать.</summary>
    public static void Recover()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(Dir, "session-*.json"))
            {
                var id = Path.GetFileNameWithoutExtension(file)["session-".Length..];
                if (GameCatalog.ById(id) is { } def && Vanilla.GameProcessRunning(def))
                {
                    // Игра ещё идёт (ModLaunch закрыли/перезапустили во время игры): дождёмся выхода и тогда уберём,
                    // а не пропускаем — иначе инжекты оставались бы в папке игры до следующего «Играть».
                    _ = AfterExit(def, TimeSpan.FromMinutes(5));
                    continue;
                }
                Cleanup(id);
            }
        }
        catch { }
    }

    [SelfTest]
    static string InjectorPutsAndTakesBack()
    {
        var root = Path.Combine(Paths.DataDir, "inject-test", "Game");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(Path.Combine(root, "game"));
        File.WriteAllText(Path.Combine(root, "game", "script.rpy"), "label start:\n    return\n");
        var def = GameCatalog.All.First(d => !d.IsMinecraft);
        var g = new GameState { Def = def, Path = root, Status = Detect.Found };
        var item = new InjectItem("t1", "Test", def.Id, "modx", "local", null, "", "t.modx", "", true, null, "", null);
        var modx = Encoding.UTF8.GetBytes("mod \"Test\"\nengine renpy\nhook start {\n  add gold 5\n}\n");
        var notes = new List<string>();
        var entry = Apply(item, modx, g, root, notes) ?? throw new Exception("modx not applied");
        var pkg = new MemoryStream();
        using (var z = new ZipArchive(pkg, ZipArchiveMode.Create, true))
        using (var w = new StreamWriter(z.CreateEntry("tex/skin.png").Open())) w.Write("png");
        var entry2 = Apply(item with { Id = "t2", Kind = "package", FileName = "p.zip" }, pkg.ToArray(), g, root, notes) ?? throw new Exception("package not applied");
        // Имя мода помечено id инжекта (t1) — файл с уникальным именем, не затирает чужую установку.
        if (!File.Exists(Path.Combine(root, "game", "modscript_Test_t1.rpy"))) throw new Exception("rpy missing");
        File.WriteAllText(SessionFile("inject-test"), new JsonObject { ["path"] = root, ["items"] = new JsonArray(entry, entry2) }.ToJsonString());
        var n = Cleanup("inject-test", park: false);
        var left = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(root, f)).Where(f => !f.StartsWith(".modscript")).ToList();
        Directory.Delete(Path.Combine(Paths.DataDir, "inject-test"), true);
        if (n != 2 || left.Count != 1) throw new Exception($"removed {n}, left: {string.Join(", ", left)}");
        return "2 injects in and out, only the game's own file is left";
    }
}
