using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Social;

namespace ModLaunch.Creator;

/// <summary>
/// 29.0: инди-игры — магазин независимых разработчиков на рынке Creator Hub (лот вида game). Деньги, комиссия
/// (сейчас 10 %, потолок 29 % — меньше 30 % у Steam), выплаты и отзывы — те же, что у маркета. Сборка до 8 МБ
/// лежит в базе кусками, как файлы модов; больше — у разработчика по ссылке https, а в базе — только ссылка,
/// SHA-256 и размер в market/{id}/files/link (читают только купившие и сам разработчик). Купленная игра
/// скачивается, проверяется по SHA-256, распаковывается в «Документы\ModLaunch\Games» и появляется в библиотеке.
/// </summary>
public static class IndieGames
{
    public const string Kind = "game";
    const string LinkDoc = "link";

    /// <summary>Жанры игры (вместо тегов модов), до трёх.</summary>
    public static readonly string[] Genres =
        ["action", "adventure", "rpg", "strategy", "puzzle", "platformer", "shooter", "horror", "simulation", "racing", "roguelike", "sandbox", "casual", "story", "multiplayer"];

    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ModLaunch", "Games");

    /// <summary>Витрина: активные лоты-игры.</summary>
    public static async Task<List<Listing>> All(bool force = false) =>
        Program.Demo || Program.Screenshot ? Demo() : (await Market.All(force)).Where(l => l.Kind == Kind && l.Active).ToList();

    public static async Task<List<Listing>> Mine() =>
        Program.Demo || Program.Screenshot ? Demo().Take(1).ToList() : (await Market.Mine()).Where(l => l.Kind == Kind).ToList();

    // ---------------------------------------------------------------- публикация

    /// <summary>SHA-256 и размер файла (сборка может быть в гигабайты — читаем потоком).</summary>
    public static async Task<(string Sha256, long Size)> Fingerprint(string file, CancellationToken ct = default)
    {
        await using var s = File.OpenRead(file);
        var hash = await SHA256.HashDataAsync(s, ct);
        return (Convert.ToHexString(hash).ToLowerInvariant(), s.Length);
    }

    /// <summary>Подходит ли ссылка на сборку: только https и не на свой компьютер.</summary>
    internal static bool GoodUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && !u.IsLoopback && u.Host.Contains('.');

    /// <summary>
    /// Выставить игру. buildFile — сборка на диске (zip или exe). До 8 МБ она уходит в базу целиком; больше —
    /// нужна ссылка https на тот же файл (по ней скачают купившие, отпечаток сверяется с этим файлом).
    /// </summary>
    public static async Task<string> Publish(ListingDraft d, Listing? existing, string? buildFile, string? buildUrl, IProgress<double>? progress = null)
    {
        d.Kind = Kind;
        d.License = "personal";
        d.Tags = d.Tags.Where(Genres.Contains).Distinct().Take(3).ToList();
        ExternalBuild? external = null;
        if (buildFile is not null)
        {
            var ext = Path.GetExtension(buildFile).ToLowerInvariant();
            if (ext is not (".zip" or ".exe")) throw new ServiceError("BAD_BUILD");
            var (sha, size) = await Fingerprint(buildFile);
            if (size > Market.MaxFile || !string.IsNullOrWhiteSpace(buildUrl))
            {
                if (string.IsNullOrWhiteSpace(buildUrl) || !GoodUrl(buildUrl.Trim())) throw new ServiceError("NEED_URL");
                external = new ExternalBuild(buildUrl.Trim(), sha, size, Path.GetFileName(buildFile));
                d.File = null;
            }
            else d.File = buildFile;
            if (d.File is null) await Features.VirusScan.BeforePublish(buildFile);
        }
        else if (existing is null) throw new ServiceError("NO_FILE");

        var id = await Market.Save(d, existing, progress, external);
        if (external is not null)
        {
            var (uid, token, _) = await Hub.Member();
            var data = new JsonObject { ["url"] = external.Url, ["sha256"] = external.Sha256, ["size"] = external.Size, ["fileName"] = external.FileName }.ToJsonString();
            await Hub.Commit([new JsonObject
            {
                ["update"] = new JsonObject
                {
                    ["name"] = Firebase.Doc($"market/{id}/files/{LinkDoc}"),
                    ["fields"] = Firebase.ToFields(new Dictionary<string, object?> { ["uid"] = uid, ["data"] = data }),
                },
            }], token);
        }
        return id;
    }

    // ---------------------------------------------------------------- установка

    static string Slug(string title) => CustomGames.Slug(title) is { Length: > 0 } s ? s : "game";

    public static string FolderFor(Listing l) => Path.Combine(Root, Slug(l.Title));

    /// <summary>Игра уже стоит из ModLaunch (папка с exe на месте) — её свой id в библиотеке.</summary>
    /// <summary>31.0: какая версия игры стоит (из записи об установке) и вышла ли новее.</summary>
    public static string? InstalledVersion(Listing l) => Settings.Data["indie"]?[l.Id]?.Str("version");

    public static bool UpdateAvailable(Listing l) =>
        Installed(l) is not null && InstalledVersion(l) is { } v && Features.Versions.Compare(l.Version, v) > 0;

    public static GameState? Installed(Listing l)
    {
        var id = Settings.Data["indie"]?[l.Id]?.Str("game");
        return id is null ? null : AppState.Games.FirstOrDefault(g => g.Def.Id == id && g.Path is not null && Directory.Exists(g.Path));
    }

    /// <summary>Ссылка на большую сборку (видят только купившие и разработчик).</summary>
    static async Task<ExternalBuild> Link(Listing l)
    {
        var (_, token, _) = await Hub.Member();
        var doc = await Hub.Call(Firebase.Url($"/market/{l.Id}/files/{LinkDoc}"), HttpMethod.Get, null, token);
        var data = JsonNode.Parse(Firebase.FromFields(doc?["fields"] as JsonObject).S("data"));
        var url = data.Str("url") ?? "";
        if (!GoodUrl(url)) throw new ServiceError("NO_FILE");
        return new ExternalBuild(url, data.Str("sha256") ?? l.Sha256, data.Long("size"), data.Str("fileName") ?? l.FileName);
    }

    /// <summary>
    /// Скачать (свою или купленную) игру, проверить, распаковать и добавить в библиотеку. Бесплатную «покупаем» сами.
    /// Возвращает игру в библиотеке.
    /// </summary>
    public static async Task<GameState> Install(Listing l, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!l.Mine && !await Market.Bought(l))
        {
            if (!l.Free) throw new ServiceError("NOT_BOUGHT");
            await Market.Buy(l);
        }
        string file;
        if (l.Chunks > 0) file = await Market.Download(l, progress);
        else
        {
            var link = await Link(l);
            // Отпечаток сверяет загрузчик: подменённый на сервере файл не встанет.
            file = await Http.Download(link.Url, Safe(link.FileName is { Length: > 0 } n ? n : Slug(l.Title) + ".zip"), progress, link.Sha256 is { Length: 64 } sha ? sha : null, ct);
            // 31.0: сборка по ссылке разработчика — тоже через проверку (файлы из базы проверяет Market.Download).
            await Features.VirusScan.BeforeInstall(file, "indie:" + l.Id, ct);
        }
        var dir = FolderFor(l);
        await Task.Run(() => Unpack(file, dir), ct);
        var exe = MainExe(dir, l.Title) ?? throw new ServiceError("NO_EXE");
        var state = AppState.Games.FirstOrDefault(g => g.Path is { } p && Path.GetFullPath(p).TrimEnd('\\') == Path.GetFullPath(dir).TrimEnd('\\'));
        if (state is null)
        {
            var found = CustomGames.FromExe(Path.Combine(dir, exe)) with { Name = l.Title, Path = dir, Exe = exe, AppId = 0, Store = "manual" };
            state = await CustomGames.Add(found, ct);
        }
        if (Settings.Data["indie"] is not JsonObject map) Settings.Data["indie"] = map = new JsonObject();
        map[l.Id] = new JsonObject { ["game"] = state.Def.Id, ["version"] = l.Version, ["at"] = DateTime.UtcNow.ToString("o") };
        Settings.Save();
        return state;
    }

    static string Safe(string name) => System.Text.RegularExpressions.Regex.Replace(Path.GetFileName(name), @"[^\w.\-]+", "_").TrimStart('.') is { Length: > 0 } s ? s : "game.zip";

    /// <summary>
    /// Распаковать сборку в папку игры: zip — с проверкой путей (ничего мимо папки), exe — просто копия.
    /// Прежняя версия заменяется целиком, но сначала новая распаковывается рядом — сорвалось, старая цела.
    /// </summary>
    internal static void Unpack(string file, string dir)
    {
        var staging = dir + ".new";
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);
        try
        {
            if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) File.Copy(file, Path.Combine(staging, Path.GetFileName(file)));
            else
            {
                var root = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
                using var zip = ZipFile.OpenRead(file);
                foreach (var e in zip.Entries)
                {
                    var target = Path.GetFullPath(Path.Combine(staging, e.FullName));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(I18n.T("err.BACKUP_BROKEN"));
                    if (e.FullName.EndsWith('/') || e.FullName.EndsWith('\\')) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    e.ExtractToFile(target, overwrite: true);
                }
                // Архив с одной папкой внутри («MyGame/…») — поднимаем её содержимое наверх.
                var entries = Directory.GetFileSystemEntries(staging);
                if (entries.Length == 1 && Directory.Exists(entries[0]))
                {
                    var inner = entries[0];
                    var lifted = staging + ".lift";
                    Directory.Move(inner, lifted);
                    Directory.Delete(staging, true);
                    Directory.Move(lifted, staging);
                }
            }
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(Path.GetDirectoryName(dir)!);
            Directory.Move(staging, dir);
        }
        finally { try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { } }
    }

    /// <summary>Главный exe: по имени игры, иначе самый крупный exe не из служебных (установщики, крэш-репортеры).</summary>
    internal static string? MainExe(string dir, string title)
    {
        if (CustomGames.MainExe(dir, title) is { } known) return known;
        return Directory.EnumerateFiles(dir, "*.exe", SearchOption.AllDirectories)
            .Where(f => !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(f), "unins|setup|install|crash|redist|vc_?redist|dxsetup|UnityCrashHandler", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            .OrderBy(f => f.Count(c => c == Path.DirectorySeparatorChar))
            .ThenByDescending(f => new FileInfo(f).Length)
            .Select(f => Path.GetRelativePath(dir, f))
            .FirstOrDefault();
    }

    // ---------------------------------------------------------------- демо и проверки

    static List<Listing> Demo()
    {
        var now = DateTime.UtcNow;
        Listing L(string id, string author, string title, string summary, long price, string[] genres, long sales, int days) =>
            new(id, "demo-" + author, author, title, summary, summary, Kind, "windows", price, "personal", "1.0.0", "", [.. genres], [], 0, "", 0, title + ".zip", "active", sales, now.AddDays(-days), now.AddDays(-days / 2));
        return
        [
            L("demo_lantern", "Mira Studio", "Lantern Keeper", I18n.Lang == "ru" ? "Уютная головоломка о смотрителе маяка: свет, тени и шторм." : "A cozy puzzle about a lighthouse keeper: light, shadows and a storm.", 29_900, ["puzzle", "story"], 412, 20),
            L("demo_tiny", "pixelfox", "Tiny Tanks Arena", I18n.Lang == "ru" ? "Танчики на четверых за одним ПК и по сети." : "Four-player tiny tanks on one PC and online.", 0, ["action", "multiplayer"], 2310, 60),
            L("demo_deep", "Kirill D.", "Below the Ice", I18n.Lang == "ru" ? "Выживание подо льдом: строите базу и спускаетесь всё глубже." : "Survival under the ice: build a base and dive deeper.", 49_900, ["simulation", "sandbox"], 96, 9),
            L("demo_rogue", "Two Bytes", "Card Dungeon", I18n.Lang == "ru" ? "Рогалик с колодой: каждый забег — новая колода и новые враги." : "A deckbuilding roguelike: every run, a new deck and new foes.", 19_900, ["roguelike", "strategy"], 1204, 120),
        ];
    }

    [SelfTest]
    static string IndieUnpackAndFindExe()
    {
        if (GoodUrl("http://x.com/a.zip") || GoodUrl("https://localhost/a.zip") || GoodUrl("file:///c:/a.zip") || !GoodUrl("https://github.com/me/game/releases/download/v1/game.zip"))
            throw new Exception("url check");
        var root = Path.Combine(Path.GetTempPath(), "modlaunch-indie-test");
        try { Directory.Delete(root, true); } catch { }
        try
        {
            Directory.CreateDirectory(root);
            var zip = Path.Combine(root, "build.zip");
            using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                void Add(string name, int bytes) { using var s = z.CreateEntry(name).Open(); s.Write(new byte[bytes]); }
                Add("MyGame/MyGame.exe", 5000);
                Add("MyGame/UnityCrashHandler64.exe", 9000);
                Add("MyGame/MyGame_Data/level0", 100);
            }
            var dir = Path.Combine(root, "games", "my-game");
            Unpack(zip, dir);
            if (!File.Exists(Path.Combine(dir, "MyGame.exe"))) throw new Exception("single top folder not lifted");
            if (MainExe(dir, "Something Else") != "MyGame.exe") throw new Exception("main exe: " + MainExe(dir, "Something Else"));
            // Повторная установка заменяет папку целиком.
            File.WriteAllText(Path.Combine(dir, "old.txt"), "x");
            Unpack(zip, dir);
            if (File.Exists(Path.Combine(dir, "old.txt"))) throw new Exception("old version left behind");
            // Архив, который пытается писать мимо папки, не распаковывается, а прежняя версия цела.
            var evil = Path.Combine(root, "evil.zip");
            using (var z = ZipFile.Open(evil, ZipArchiveMode.Create)) { using var s = z.CreateEntry("../../escape.txt").Open(); s.WriteByte(1); }
            try { Unpack(evil, dir); throw new Exception("path escape not caught"); }
            catch (InvalidDataException) { }
            if (!File.Exists(Path.Combine(dir, "MyGame.exe")) || File.Exists(Path.Combine(root, "escape.txt"))) throw new Exception("bad archive damaged the game");
            return "https-only links; zip unpacked with its top folder lifted, crash handler not taken for the game, reinstall replaces, path escape refused";
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
