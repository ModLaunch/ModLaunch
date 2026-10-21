using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Social;

namespace ModLaunch.Features;

/// <summary>
/// Куда сохранять бэкап. Kind: local — «Документы\ModLaunch\Backups», service — папка облачного
/// сервиса (OneDrive, Google Диск…), custom — своя папка, cloud — облако ModLaunch.
/// Folder — папка, куда ложат архивы (у облака ModLaunch её нет).
/// </summary>
public sealed record BackupTarget(string Id, string Kind, string Title, bool Available, string? Folder);

/// <summary>Один бэкап в месте сохранения. File — путь к архиву (у облака ModLaunch — null).</summary>
public sealed record BackupEntry(string Target, string Name, string? File, DateTime At, long Size, string Device, string Version, bool Saves, bool Safety);

/// <summary>Чем закончился бэкап. Error — понятный человеку текст, если не получилось.</summary>
public sealed record BackupResult(bool Ok, string Target, string? File, long Size, int Files, int Skipped, DateTime At, string? Error);

/// <summary>
/// Что вернулось из бэкапа. Restart — файлы, которые программа держит в памяти: они подменятся
/// при следующем запуске (ApplyPending). Safety — имя страховочного бэкапа, сделанного перед этим.
/// </summary>
public sealed record RestoreResult(int Files, IReadOnlyList<string> Restart, int ModLists, int Saves, string? Safety)
{
    public bool NeedsRestart => Restart.Count > 0;
}

/// <summary>
/// 11.0: бэкап в один клик. Всё важное и лёгкое — одним zip с manifest.json (версия программы, когда,
/// с какого ПК, список файлов с sha256): настройки (с избранным, коллекциями и заметками), профили модов
/// с их настройками, часы игры и часы по сборкам, история, сборки Minecraft (описания, без миров),
/// стили, проекты Creator Hub и состав модов каждой игры — после восстановления он появляется
/// в «Истории изменений» игры, и моды можно поставить заново одной кнопкой. По желанию — последняя
/// копия сохранений каждой игры.
/// Куда: «Документы\ModLaunch\Backups», папка OneDrive, Google Диска, Dropbox, Яндекс Диска, MEGA или
/// iCloud Drive (сервис сам выгрузит архив), своя папка или облако ModLaunch (Firestore, до 8 МБ).
/// Вход в аккаунт, ключи и токены, а также зашифрованные инжекты (их ключ привязан к этому ПК)
/// в бэкап не попадают и при восстановлении не перезаписываются.
/// </summary>
public static partial class OneClickBackup
{
    /// <summary>
    /// Файлы данных в бэкапе (settings.json — отдельно, очищенный). vanilla.json сюда не входит: это запись,
    /// какие файлы загрузчиков отложены в папках игр этого ПК, — со старой записью загрузчик не вернуть.
    /// </summary>
    static readonly string[] DataFiles = ["profiles.json", "playtime.json", "buildtime.json", "history.json", "ai-builds.json", "plays.json"];
    /// <summary>Папки данных в бэкапе: настройки модов профилей, стили, проекты Creator Hub, описания сборок Minecraft.</summary>
    static readonly string[] DataFolders = ["profiles", "configs", "creator", "minecraft"];
    /// <summary>Файл из папки данных больше этого — не «лёгкий» (картинка, собранный мод), в бэкап не идёт.</summary>
    const long MaxDataFile = 4 * 1024 * 1024;

    /// <summary>
    /// Ключи настроек, которые не уходят в бэкап и при восстановлении остаются нынешними: вход и ключи
    /// (аккаунт, Nexus, ИИ) и то, что относится только к этому ПК (найденные игры, номер ПК облачного
    /// профиля, сами настройки бэкапа).
    /// </summary>
    static readonly HashSet<string> Kept = new(StringComparer.OrdinalIgnoreCase)
    {
        "account", "accounts", "nexusApiKey", "nexusPremium", "aiKey",
        "detected", "deviceId", "cloudSyncedAt", "cloudDismissed", "bkDest", "bkCustom", "bkLast",
    };

    /// <summary>Имя похоже на секрет — такое поле убирается на любой глубине.</summary>
    [GeneratedRegex("token|secret|password|passwd|apikey|api_key|cookie|credential|dpapi", RegexOptions.IgnoreCase)]
    private static partial Regex SecretKey();

    [GeneratedRegex(@"^ModLaunch-([0-9]{8}-[0-9]{6})\.zip$")] private static partial Regex FileName();
    [GeneratedRegex(@"^[a-z0-9][a-z0-9._-]{0,63}$")] private static partial Regex GameId();
    /// <summary>Имя копии сохранений, как в Backups: ГГГГММДД-ЧЧММСС-причина.zip.</summary>
    [GeneratedRegex(@"^\d{8}-\d{6}-[a-z]+\.zip$")] private static partial Regex SaveName();

    /// <summary>Папка с JSON-файлами, которые подменятся при следующем запуске.</summary>
    const string PendingDir = "restore-pending";
    /// <summary>Подпапка в папке облачного сервиса.</summary>
    const string SyncFolder = "ModLaunch Backups";

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    /// <summary>Идёт бэкап или восстановление (вторая кнопка ждёт).</summary>
    public static bool Running { get; private set; }

    /// <summary>Бэкап начался, закончился или сменились настройки — панели перерисоваться.</summary>
    public static event Action? Changed;

    // ---------------------------------------------------------------- настройки

    /// <summary>Куда делать бэкап по кнопке (запоминается).</summary>
    public static string CurrentId => Settings.Data.Str("bkDest") is { Length: > 0 } id ? id : "local";

    public static void Choose(string id) { Settings.Data["bkDest"] = id; Settings.Save(); Changed?.Invoke(); }

    /// <summary>Сколько бэкапов хранить в каждой папке (старые удаляются сами).</summary>
    public static int Keep
    {
        get => Settings.Data.Long("bkKeep") is >= 1 and <= 100 and var k ? (int)k : 10;
        set { Settings.Data["bkKeep"] = Math.Clamp(value, 1, 100); Settings.Save(); }
    }

    /// <summary>Добавлять последнюю копию сохранений каждой игры (по умолчанию — нет: архив станет большим).</summary>
    public static bool WithSaves
    {
        get => Settings.Data.Bool("bkSaves");
        set { Settings.Data["bkSaves"] = value; Settings.Save(); }
    }

    /// <summary>Своя папка для бэкапов.</summary>
    public static void SetCustom(string folder) { Settings.Data["bkCustom"] = folder; Settings.Data["bkDest"] = "custom"; Settings.Save(); Changed?.Invoke(); }

    /// <summary>Последний бэкап (удачный или нет).</summary>
    public static BackupResult? Last => Settings.Data["bkLast"] is JsonObject o
        ? new BackupResult(o.Bool("ok"), o.Str("target") ?? "local", o.Str("file"), o.Long("size"), (int)o.Long("files"), (int)o.Long("skipped"),
            Firebase.Time(o.Str("at")) ?? DateTime.MinValue, o.Str("error"))
        : null;

    static void Remember(BackupResult r) => OnUi(() =>
    {
        Settings.Data["bkLast"] = new JsonObject
        {
            ["ok"] = r.Ok, ["target"] = r.Target, ["file"] = r.File, ["size"] = r.Size, ["files"] = r.Files, ["skipped"] = r.Skipped,
            ["at"] = r.At.ToString("o"), ["error"] = r.Error,
        };
        Settings.Save();
    });

    /// <summary>Общий словарь настроек меняем только в потоке интерфейса (бэкап может закончиться в фоне).</summary>
    static void OnUi(Action action)
    {
        if (Avalonia.Application.Current is null || Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) action();
        else Avalonia.Threading.Dispatcher.UIThread.Post(action);
    }

    static string Device => Environment.MachineName.Length > 64 ? Environment.MachineName[..64] : Environment.MachineName;

    // ---------------------------------------------------------------- куда

    /// <summary>«Документы\ModLaunch\Backups».</summary>
    public static string LocalFolder
    {
        get
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (docs == "") docs = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(docs, "ModLaunch", "Backups");
        }
    }

    /// <summary>Для снимков экрана: свои места вместо настоящих папок этого ПК.</summary>
    internal static List<BackupTarget>? DemoTargets { get; set; }

    /// <summary>Все места: эта папка, сервисы (найденные и нет), своя папка, облако ModLaunch.</summary>
    public static List<BackupTarget> Targets()
    {
        if (DemoTargets is { } demo) return demo;
        var list = new List<BackupTarget> { new("local", "local", I18n.T("v11.bk.dest.local"), true, LocalFolder) };
        static string F(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
        List<string> drives = [];
        if (OperatingSystem.IsWindows())
        {
            try { drives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed).Select(d => d.RootDirectory.FullName).ToList(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        list.AddRange(Services(Environment.GetEnvironmentVariable, F(Environment.SpecialFolder.UserProfile), F(Environment.SpecialFolder.MyDocuments),
            F(Environment.SpecialFolder.ApplicationData), F(Environment.SpecialFolder.LocalApplicationData), drives));
        var custom = Settings.Data.Str("bkCustom");
        list.Add(new("custom", "custom", I18n.T("v11.bk.dest.custom"), custom is not null && (Remote(custom) || Directory.Exists(custom)), custom));
        list.Add(new("cloud", "cloud", I18n.T("v11.bk.dest.cloud"), Firebase.Configured && Account.SignedIn, null));
        return list;
    }

    /// <summary>
    /// Сетевая папка: проверка Directory.Exists на недоступном сервере держит поток десятки секунд,
    /// поэтому такую папку считаем доступной, а ошибку записи покажем при бэкапе.
    /// </summary>
    static bool Remote(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal)) return true;
        if (!OperatingSystem.IsWindows() || Path.GetPathRoot(path) is not { Length: > 0 } root) return false;
        try { return new DriveInfo(root).DriveType == DriveType.Network; }
        catch (ArgumentException) { return false; }
    }

    /// <summary>
    /// Папки облачных сервисов: если сервис стоит, его папка синхронизации есть на диске, и архив
    /// в «&lt;папка&gt;\ModLaunch Backups» сервис выгрузит сам. Только проверки папок и один маленький
    /// JSON Dropbox — дёшево; ничего не бросает.
    /// </summary>
    internal static List<BackupTarget> Services(Func<string, string?> env, string home, string docs, string appData, string localAppData, IEnumerable<string> drives)
    {
        static string? Under(string? root, string name) => string.IsNullOrWhiteSpace(root) ? null : Path.Combine(root, name);
        static string? First(IEnumerable<string?> dirs) => dirs.FirstOrDefault(d => d is not null && Path.IsPathFullyQualified(d) && Directory.Exists(d));
        var list = new List<BackupTarget>();
        void Add(string id, Func<string?> find)
        {
            string? root;
            try { root = find(); }
            catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { root = null; }
            list.Add(new BackupTarget(id, "service", I18n.T("v11.bk.dest." + id), root is not null, root is null ? null : Path.Combine(root, SyncFolder)));
        }
        Add("onedrive", () => First([env("OneDrive"), env("OneDriveConsumer"), env("OneDriveCommercial")]));
        // Google Drive для компьютера: диск «G:\My Drive» (или «Мой диск»); в режиме зеркала — папка в профиле.
        Add("gdrive", () => First(drives.SelectMany(d => new[] { Under(d, "My Drive"), Under(d, "Мой диск") })
            .Concat([Under(home, "My Drive"), Under(home, "Мой диск"), Under(home, "Google Drive")])));
        Add("dropbox", () => First([Dropbox(appData), Dropbox(localAppData), Under(home, "Dropbox")]));
        Add("yandex", () => First([Under(home, "YandexDisk"), Under(home, "Yandex.Disk")]));
        Add("mega", () => First([Under(home, "MEGA"), Under(docs, "MEGA"), Under(docs, "MEGAsync")]));
        Add("icloud", () => First([Under(home, "iCloudDrive")]));
        return list;
    }

    /// <summary>Dropbox пишет, где его папка, в Dropbox\info.json: личная, иначе рабочая.</summary>
    static string? Dropbox(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return null;
        try
        {
            var file = Path.Combine(dir, "Dropbox", "info.json");
            if (!File.Exists(file) || new FileInfo(file).Length > 64 * 1024) return null;
            var info = JsonNode.Parse(File.ReadAllText(file)) as JsonObject;
            return info?["personal"].Str("path") ?? info?["business"].Str("path");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { return null; }
    }

    // ---------------------------------------------------------------- бэкап одной кнопкой

    /// <summary>
    /// Сделать бэкап: в запомненное место (bkDest) или в указанное. Не бросает: итог и понятный текст
    /// ошибки — в результате (он же запоминается как «последний бэкап»).
    /// </summary>
    public static async Task<BackupResult> Run(string? destinationId = null, IProgress<double>? progress = null)
    {
        var id = destinationId ?? CurrentId;
        if (Running) return new BackupResult(false, id, null, 0, 0, 0, DateTime.UtcNow, I18n.T("v11.bk.err.running"));
        var target = Targets().FirstOrDefault(t => t.Id == id);
        BackupResult result;
        if (target is null || !target.Available)
        {
            var key = target?.Kind == "cloud" ? (Firebase.Configured ? "v11.bk.err.signIn" : "v11.bk.err.cloud") : "v11.bk.err.missing";
            result = new BackupResult(false, id, null, 0, 0, 0, DateTime.UtcNow, I18n.T(key, ("name", target?.Title ?? id)));
        }
        else
        {
            var saves = WithSaves;
            var keep = Keep;
            Running = true;
            Changed?.Invoke();
            try { result = await Task.Run(() => Make(target, saves, keep, "manual", progress)); }
            catch (Exception e) { result = new BackupResult(false, target.Id, null, 0, 0, 0, DateTime.UtcNow, Explain(e)); }
            finally { Running = false; }
        }
        Remember(result);
        OnUi(() => Changed?.Invoke());
        return result;
    }

    /// <summary>Собрать архив из папки данных и отдать его в место сохранения.</summary>
    static async Task<BackupResult> Make(BackupTarget target, bool saves, int keep, string reason, IProgress<double>? progress, string? spare = null)
    {
        // Настройки и списки, ждущие фоновой записи, — сначала на диск: в бэкап идёт самое свежее.
        JsonFile.FlushAll();
        var mods = ModLists();
        var copies = saves ? LatestSaves(Paths.DataDir) : [];
        if (target.Kind == "cloud")
        {
            // Больше 8 МБ облако не примет — не собираем и не читаем в память гигабайты: копии сохранений
            // (они не сжимаются) проверяем сразу, остальное Write проверяет по ходу.
            if (copies.Sum(c => Length(c.File)) > CloudLimit) throw new ServiceError("TOO_BIG");
            var temp = Path.Combine(Paths.CacheDir, $"cloud-backup-{Guid.NewGuid():N}.zip");
            try
            {
                var (files, skipped) = Write(Paths.DataDir, temp, mods, copies, reason, Scale(progress, 0, 0.4), CloudLimit);
                var bytes = await File.ReadAllBytesAsync(temp);
                await Upload(bytes, Scale(progress, 0.4, 1));
                return new BackupResult(true, target.Id, null, bytes.Length, files, skipped, DateTime.UtcNow, null);
            }
            finally { try { File.Delete(temp); } catch (IOException) { } }
        }
        var folder = Directory.CreateDirectory(target.Folder!).FullName;
        var path = Path.Combine(folder, FreeName(folder, DateTime.Now));
        // Пишем рядом под временным именем и переименовываем: сервис синхронизации не выгрузит половину архива.
        var tmp = path + ".tmp";
        try
        {
            var (files, skipped) = Write(Paths.DataDir, tmp, mods, copies, reason, progress);
            File.Move(tmp, path);
            Prune(folder, keep, spare);
            progress?.Report(1);
            return new BackupResult(true, target.Id, path, new FileInfo(path).Length, files, skipped, DateTime.UtcNow, null);
        }
        catch
        {
            try { File.Delete(tmp); } catch (IOException) { }
            throw;
        }
    }

    static long Length(string file)
    {
        try { return new FileInfo(file).Length; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return 0; }
    }

    static string FreeName(string folder, DateTime at)
    {
        // Григорианский календарь при любом языке Windows: ListFolder читает дату так же.
        static string Name(DateTime t) => "ModLaunch-" + t.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".zip";
        var name = Name(at);
        for (var i = 1; File.Exists(Path.Combine(folder, name)); i++) name = Name(at.AddSeconds(i));
        return name;
    }

    /// <summary>Состав модов каждой найденной игры (с версиями) — чтобы после восстановления поставить их заново.</summary>
    static Dictionary<string, JsonObject> ModLists()
    {
        var lists = new Dictionary<string, JsonObject>();
        GameState[] games;
        try { games = [.. AppState.Games]; }
        catch (InvalidOperationException) { return lists; }
        foreach (var g in games)
        {
            if (g.Status != Detect.Found || g.Def.IsMinecraft || g.Registry is not { } registry) continue;
            List<SetupMod> mods;
            // Список одной игры не прочитался (папка занята, игра удалена) — бэкап всё равно делаем, без него.
            try { mods = ModSetup.Capture(registry); }
            catch { continue; }
            if (mods.Count > 0) lists[g.Def.Id] = new JsonObject { ["game"] = g.Def.Name, ["mods"] = ModSetup.ToJson(mods) };
        }
        return lists;
    }

    /// <summary>Последняя копия сохранений каждой игры (backups\&lt;игра&gt;\…).</summary>
    static List<(string Game, string File)> LatestSaves(string root)
    {
        var dir = Path.Combine(root, "backups");
        var list = new List<(string, string)>();
        if (!Directory.Exists(dir)) return list;
        foreach (var game in Directory.EnumerateDirectories(dir))
        {
            var id = Path.GetFileName(game);
            if (!GameId().IsMatch(id)) continue;
            var newest = Directory.EnumerateFiles(game, "*.zip").Select(f => Path.GetFileName(f))
                .Where(n => SaveName().IsMatch(n)).OrderByDescending(n => n, StringComparer.Ordinal).FirstOrDefault();
            if (newest is not null) list.Add((id, Path.Combine(game, newest)));
        }
        return list;
    }

    // ---------------------------------------------------------------- архив

    /// <summary>Настройки для бэкапа: без входа, ключей, своего для этого ПК и всего, что похоже на токен.</summary>
    public static JsonObject Strip(JsonObject settings)
    {
        var copy = new JsonObject();
        foreach (var (k, v) in settings)
            if (!Kept.Contains(k) && !SecretKey().IsMatch(k)) copy[k] = Clean(v);
        return copy;
    }

    static JsonNode? Clean(JsonNode? node) => node switch
    {
        JsonObject o => new JsonObject(o.Where(kv => !SecretKey().IsMatch(kv.Key)).Select(kv => KeyValuePair.Create(kv.Key, Clean(kv.Value)))),
        JsonArray a => new JsonArray(a.Select(Clean).ToArray()),
        _ => node?.DeepClone(),
    };

    /// <summary>
    /// Настройки из бэкапа поверх нынешних: вход, ключи и своё для этого ПК остаются нынешними, пути
    /// к играм из бэкапа добавляются, только если такая папка есть на этом ПК.
    /// </summary>
    internal static JsonObject Merge(JsonObject restored, JsonObject? current)
    {
        var result = Strip(restored);
        foreach (var (k, v) in current ?? [])
            if (Kept.Contains(k) || SecretKey().IsMatch(k)) result[k] = v?.DeepClone();
        var paths = (current?["gamePaths"] as JsonObject)?.DeepClone() as JsonObject ?? new JsonObject();
        foreach (var (game, node) in restored["gamePaths"] as JsonObject ?? [])
            if (!paths.ContainsKey(game) && node is JsonValue v && v.TryGetValue<string>(out var dir) && Path.IsPathFullyQualified(dir) && Directory.Exists(dir)) paths[game] = dir;
        result["gamePaths"] = paths;
        return result;
    }

    /// <summary>
    /// Собрать архив из папки данных root: файлы, составы модов (mods/&lt;игра&gt;.json) и копии
    /// сохранений (saves/&lt;игра&gt;/…), а в конце manifest.json с sha256 каждого файла.
    /// Возвращает, сколько файлов вошло и сколько не прочиталось (заняты другой программой).
    /// Архив перерос limit — ServiceError TOO_BIG (для облака ModLaunch).
    /// </summary>
    internal static (int Files, int Skipped) Write(string root, string zipPath, IReadOnlyDictionary<string, JsonObject> mods,
        IReadOnlyList<(string Game, string File)> saves, string reason, IProgress<double>? progress = null, long limit = long.MaxValue)
    {
        var items = new List<(string Entry, Func<Stream> Open)>();
        if (ReadJson(Path.Combine(root, "settings.json")) is { } settings)
        {
            var text = Encoding.UTF8.GetBytes(Strip(settings).ToJsonString(Pretty));
            items.Add(("data/settings.json", () => new MemoryStream(text)));
        }
        foreach (var name in DataFiles)
        {
            var file = Path.Combine(root, name);
            if (File.Exists(file)) items.Add(("data/" + name, () => OpenRead(file)));
        }
        var walk = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var folder in DataFolders)
        {
            var dir = Path.Combine(root, folder);
            if (!Directory.Exists(dir)) continue;
            foreach (var info in new DirectoryInfo(dir).EnumerateFiles("*", walk))
            {
                var rel = folder + "/" + Path.GetRelativePath(dir, info.FullName).Replace('\\', '/');
                // Собранные моды Creator Hub (_build), недописанные и тяжёлые файлы — не нужны.
                if ((folder == "creator" && rel.Split('/').Skip(1).Any(p => p.StartsWith('_'))) || rel.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                if (info.Length > MaxDataFile) continue;
                var file = info.FullName;
                items.Add(("data/" + rel, () => OpenRead(file)));
            }
        }
        var at = DateTime.UtcNow.ToString("o");
        foreach (var (game, list) in mods)
        {
            if (!GameId().IsMatch(game)) continue;
            var snap = (JsonObject)list.DeepClone();
            snap["at"] = at;
            var text = Encoding.UTF8.GetBytes(snap.ToJsonString());
            items.Add(($"mods/{game}.json", () => new MemoryStream(text)));
        }
        foreach (var (game, file) in saves)
            if (GameId().IsMatch(game) && SaveName().IsMatch(Path.GetFileName(file))) items.Add(($"saves/{game}/{Path.GetFileName(file)}", () => OpenRead(file)));

        var listed = new JsonArray();
        var skipped = 0;
        using (var stream = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            for (var i = 0; i < items.Count; i++)
            {
                var (name, open) = items[i];
                try
                {
                    using var input = open();
                    var entry = zip.CreateEntry(name, name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
                    using var output = entry.Open();
                    var (size, sha) = Copy(input, output);
                    listed.Add(new JsonObject { ["path"] = name, ["size"] = size, ["sha256"] = sha });
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { skipped++; }
                if (stream.Length > limit) throw new ServiceError("TOO_BIG");
                progress?.Report((i + 1.0) / (items.Count + 1));
            }
            var manifest = new JsonObject
            {
                ["modlaunchBackup"] = 1, ["app"] = Http.Version, ["created"] = at, ["device"] = Device, ["reason"] = reason,
                ["saves"] = listed.Any(f => f.Str("path")?.StartsWith("saves/", StringComparison.Ordinal) == true), ["files"] = listed,
            };
            using var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open(), new UTF8Encoding(false));
            writer.Write(manifest.ToJsonString(Pretty));
        }
        return (listed.Count, skipped);
    }

    static FileStream OpenRead(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    static (long Size, string Sha) Copy(Stream input, Stream output)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long size = 0;
        int n;
        while ((n = input.Read(buffer)) > 0)
        {
            hash.AppendData(buffer, 0, n);
            output.Write(buffer, 0, n);
            size += n;
        }
        return (size, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    static string Sha(Stream s) => Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
    static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    static JsonObject? ReadJson(string path)
    {
        try { return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    static JsonObject? Manifest(ZipArchive zip)
    {
        if (zip.GetEntry("manifest.json") is not { Length: < 4 * 1024 * 1024 } entry) return null;
        using var s = entry.Open();
        return JsonNode.Parse(s) as JsonObject;
    }

    static InvalidDataException Broken() => new(I18n.T("v11.bk.err.broken"));

    /// <summary>Путь из архива — только наши места: файлы и папки данных из списка, составы модов, копии сохранений.</summary>
    static bool Allowed(string path)
    {
        if (path.Contains('\\') || path.Contains(':')) return false;
        var parts = path.Split('/');
        if (parts.Any(p => p is "" or "." or "..")) return false;
        return parts[0] switch
        {
            "data" => parts.Length == 2 ? parts[1] == "settings.json" || DataFiles.Contains(parts[1]) : parts.Length > 2 && DataFolders.Contains(parts[1]),
            "mods" => parts.Length == 2 && parts[1].EndsWith(".json", StringComparison.Ordinal) && GameId().IsMatch(parts[1][..^5]),
            "saves" => parts.Length == 3 && GameId().IsMatch(parts[1]) && SaveName().IsMatch(parts[2]),
            _ => false,
        };
    }

    /// <summary>Проверить архив: манифест на месте, каждый файл есть, размер и sha256 совпадают, пути — только наши.</summary>
    static (JsonObject Manifest, List<(string Path, ZipArchiveEntry Entry)> Files) Verify(ZipArchive zip)
    {
        JsonObject? manifest;
        try { manifest = Manifest(zip); }
        catch (JsonException) { manifest = null; }
        if (manifest is null) throw Broken();
        if (manifest.Long("modlaunchBackup") > 1) throw new InvalidDataException(I18n.T("v11.bk.err.newer"));
        if (manifest.Long("modlaunchBackup") != 1) throw Broken();
        var files = new List<(string, ZipArchiveEntry)>();
        foreach (var f in manifest.Arr("files").OfType<JsonObject>())
        {
            var path = f.Str("path") ?? "";
            // Бэкапы первых сборок 11.0 ещё содержат vanilla.json — пропускаем его, не трогая свой.
            if (path == "data/vanilla.json") continue;
            if (!Allowed(path) || zip.GetEntry(path) is not { } entry || entry.Length != f.Long("size")) throw Broken();
            bool same;
            try
            {
                using var s = entry.Open();
                same = Sha(s) == f.Str("sha256");
            }
            catch (InvalidDataException) { same = false; } // запись не распаковывается
            if (!same) throw Broken();
            files.Add((path, entry));
        }
        return (manifest, files);
    }

    /// <summary>Открыть архив; не zip — «бэкап повреждён», а не текст исключения.</summary>
    static ZipArchive OpenZip(string path)
    {
        try { return ZipFile.OpenRead(path); }
        catch (InvalidDataException) { throw Broken(); }
    }

    /// <summary>Полный путь внутри root (или ошибка: архив пытается писать мимо папки).</summary>
    static string Inside(string root, string rel)
    {
        var full = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw Broken();
        return full;
    }

    /// <summary>Записать файл целиком: во временный рядом, потом переименовать.</summary>
    static void WriteAtomic(string target, Action<Stream> write)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var tmp = target + ".bk-tmp";
        try
        {
            using (var f = File.Create(tmp)) write(f);
            File.Move(tmp, target, true);
        }
        catch
        {
            try { File.Delete(tmp); } catch (IOException) { }
            throw;
        }
    }

    static void WriteAtomic(string target, string text) => WriteAtomic(target, s => s.Write(new UTF8Encoding(false).GetBytes(text)));

    static void Extract(ZipArchiveEntry entry, string target) => WriteAtomic(target, s => { using var src = entry.Open(); src.CopyTo(s); });

    /// <summary>
    /// Разложить проверенный архив в папку данных root. stage — это папка работающей программы: JSON-файлы
    /// она держит в памяти, поэтому они ложатся в restore-pending и подменяются при следующем запуске.
    /// Составы модов становятся снимками «как было» (snapshots), копии сохранений — копиями игр (backups);
    /// вход, ключи и инжекты не трогаются: их в архиве не бывает, а лишние пути не проходят проверку.
    /// При stage — всё или ничего: файлы данных сначала ложатся во временную папку и попадают на место, только
    /// когда распакован весь архив; не удалось — временная папка, новые снимки и копии сохранений убираются.
    /// </summary>
    internal static RestoreResult Unpack(string zipPath, string root, bool stage, string? safety = null)
    {
        root = Path.GetFullPath(root);
        using var zip = OpenZip(zipPath);
        var (manifest, files) = Verify(zip);
        var pending = Path.Combine(root, PendingDir);
        var staging = pending + ".tmp";
        if (stage && Directory.Exists(staging)) Directory.Delete(staging, true);
        var created = Firebase.Time(manifest.Str("created")) ?? DateTime.UtcNow;
        var restart = new List<string>();
        var moves = new List<string>();
        var added = new List<string>();
        int written = 0, lists = 0, copies = 0;
        try
        {
            foreach (var (path, entry) in files)
            {
                var parts = path.Split('/');
                if (parts[0] == "data")
                {
                    var rel = string.Join('/', parts[1..]);
                    var target = Inside(stage ? staging : root, rel);
                    if (rel == "settings.json" && !stage)
                    {
                        using var s = entry.Open();
                        var restored = JsonNode.Parse(s) as JsonObject ?? throw Broken();
                        WriteAtomic(target, Merge(restored, ReadJson(target)).ToJsonString(Pretty));
                    }
                    else Extract(entry, target);
                    // Не JSON программа в памяти не держит — он встанет на место сразу после распаковки.
                    if (stage) (rel.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? restart : moves).Add(rel);
                    written++;
                }
                else if (parts[0] == "mods")
                {
                    JsonObject snap;
                    using (var s = entry.Open()) snap = JsonNode.Parse(s) as JsonObject ?? throw Broken();
                    snap["at"] = created.ToString("o");
                    snap["reason"] = "backup";
                    snap["config"] = false;
                    var file = Inside(root, $"snapshots/{parts[1][..^5]}/{created:yyyyMMdd-HHmmss}-backup.json");
                    var fresh = !File.Exists(file);
                    WriteAtomic(file, snap.ToJsonString());
                    if (fresh) added.Add(file);
                    lists++;
                }
                else
                {
                    // Своя копия сохранений не заменяется: из бэкапа добавляется только то, чего здесь нет.
                    var copy = Inside(root, $"backups/{parts[1]}/{parts[2]}");
                    if (File.Exists(copy)) continue;
                    Extract(entry, copy);
                    added.Add(copy);
                    copies++;
                }
            }
            if (stage)
            {
                foreach (var rel in moves)
                {
                    var target = Inside(root, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Move(Inside(staging, rel), target, true);
                }
                // Прошлое неприменённое восстановление заменяется этим — только теперь, когда это удалось целиком.
                if (Directory.Exists(pending)) Directory.Delete(pending, true);
                if (Directory.Exists(staging)) Directory.Move(staging, pending);
            }
        }
        catch
        {
            try { if (stage && Directory.Exists(staging)) Directory.Delete(staging, true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            foreach (var file in added)
            {
                try { File.Delete(file); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
            throw;
        }
        return new RestoreResult(written, restart, lists, copies, safety);
    }

    /// <summary>
    /// При запуске, до чтения настроек: подменить JSON-файлы, отложенные восстановлением. settings.json
    /// сливается с нынешним (вход и ключи этого ПК остаются). Подменённый файл сразу уходит из папки: иначе
    /// при следующем запуске настройки из бэкапа слились бы снова поверх новых. Не подменился (файл занят) —
    /// остаётся до следующего запуска. Ничего не бросает; возвращает, сколько файлов подменено.
    /// </summary>
    public static int ApplyPending(string? root = null)
    {
        var done = 0;
        try
        {
            root = Path.GetFullPath(root ?? Paths.DataDir);
            var pending = Path.Combine(root, PendingDir);
            if (!Directory.Exists(pending)) return 0;
            foreach (var file in Directory.EnumerateFiles(pending, "*", SearchOption.AllDirectories).ToList())
            {
                var rel = Path.GetRelativePath(pending, file).Replace('\\', '/');
                try
                {
                    // Чужой, недописанный или нечитаемый файл не подменится никогда — просто убираем.
                    var restored = rel == "settings.json" ? ReadJson(file) : null;
                    if (!Allowed("data/" + rel) || rel.EndsWith(".bk-tmp", StringComparison.Ordinal) || (rel == "settings.json" && restored is null))
                    {
                        File.Delete(file);
                        continue;
                    }
                    var target = Inside(root, rel);
                    if (restored is not null)
                    {
                        WriteAtomic(target, Merge(restored, ReadJson(target)).ToJsonString(Pretty));
                        File.Delete(file);
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.Move(file, target, true);
                    }
                    done++;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException) { }
            }
            if (!Directory.EnumerateFiles(pending, "*", SearchOption.AllDirectories).Any()) Directory.Delete(pending, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
        return done;
    }

    // ---------------------------------------------------------------- список, восстановление, удаление

    /// <summary>Бэкапы в месте сохранения — новые сверху. У облака ModLaunch — один, последний.</summary>
    public static async Task<List<BackupEntry>> List(BackupTarget target)
    {
        if (target.Kind == "cloud") return await CloudList();
        if (target.Folder is not { } folder) return [];
        return await Task.Run(() => ListFolder(target.Id, folder));
    }

    internal static List<BackupEntry> ListFolder(string id, string folder)
    {
        var list = new List<BackupEntry>();
        if (!Directory.Exists(folder)) return list;
        foreach (var file in Directory.EnumerateFiles(folder, "ModLaunch-*.zip"))
        {
            // Имя с невозможной датой (ModLaunch-20261399-250000.zip) — не наш бэкап: пропускаем, а не падаем.
            var m = FileName().Match(Path.GetFileName(file));
            if (!m.Success || !DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var made)) continue;
            var at = made.ToUniversalTime();
            JsonObject? manifest = null;
            long size = 0;
            try
            {
                size = new FileInfo(file).Length;
                using var zip = ZipFile.OpenRead(file);
                manifest = Manifest(zip);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or JsonException) { }
            list.Add(new BackupEntry(id, Path.GetFileName(file), file, at, size, manifest.Str("device") ?? "", manifest.Str("app") ?? "",
                manifest.Bool("saves"), manifest.Str("reason") == "safety"));
        }
        return list.OrderByDescending(b => b.At).ToList();
    }

    /// <summary>
    /// Оставить в папке keep новых бэкапов этого ПК: папку облачного сервиса делят все компьютеры человека,
    /// поэтому бэкапы других ПК и чужие файлы не трогаются. spare — не удалять (из него сейчас восстанавливают).
    /// Возвращает, сколько удалено.
    /// </summary>
    internal static int Prune(string folder, int keep, string? spare = null)
    {
        var removed = 0;
        var mine = ListFolder("", folder).Where(b => string.Equals(b.Device, Device, StringComparison.OrdinalIgnoreCase));
        foreach (var old in mine.Skip(Math.Max(1, keep)))
        {
            if (spare is not null && string.Equals(Path.GetFullPath(old.File!), Path.GetFullPath(spare), StringComparison.OrdinalIgnoreCase)) continue;
            try { File.Delete(old.File!); removed++; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return removed;
    }

    /// <summary>
    /// Восстановить: проверить архив (манифест и sha256), сделать страховочный бэкап в «Документы»,
    /// разложить файлы. JSON-файлы программы подменятся при следующем запуске (RestoreResult.Restart).
    /// Ошибки — исключениями; текст для человека — Explain.
    /// </summary>
    public static async Task<RestoreResult> Restore(BackupEntry entry, IProgress<double>? progress = null)
    {
        if (Running) throw new InvalidOperationException(I18n.T("v11.bk.err.running"));
        var local = Targets().First(t => t.Id == "local");
        var keep = Keep;
        Running = true;
        Changed?.Invoke();
        // Работаем с копией (сервис синхронизации может заменить файл посреди восстановления), а сам бэкап
        // страховочный по сроку хранения не удаляет: если восстановление не удастся, он нужен целым.
        var work = Path.Combine(Paths.CacheDir, $"restore-{Guid.NewGuid():N}.zip");
        try
        {
            if (entry.Target == "cloud") await Download(work, Scale(progress, 0, 0.4));
            else await Task.Run(() => File.Copy(entry.File!, work, true));
            await Task.Run(() => { using var zip = OpenZip(work); Verify(zip); });
            var safety = await Task.Run(() => Make(local, saves: false, keep, "safety", Scale(progress, 0.4, 0.8), entry.File));
            var result = await Task.Run(() => Unpack(work, Paths.DataDir, stage: true, Path.GetFileName(safety.File)));
            progress?.Report(1);
            return result;
        }
        finally
        {
            Running = false;
            try { File.Delete(work); } catch (IOException) { }
            OnUi(() => Changed?.Invoke());
        }
    }

    /// <summary>Удалить бэкап (файл в папке или облачный бэкап аккаунта).</summary>
    public static async Task Delete(BackupEntry entry)
    {
        if (entry.Target == "cloud") await DeleteCloud();
        else await Task.Run(() => File.Delete(entry.File!));
        OnUi(() => Changed?.Invoke());
    }

    // ---------------------------------------------------------------- облако ModLaunch (Firestore)

    /// <summary>Часть архива в base64 — меньше лимита документа Firestore (1 МБ) с запасом.</summary>
    const int ChunkChars = 700_000;
    const int MaxChunks = 16;
    /// <summary>Самый большой бэкап для облака ModLaunch: 8 МБ (16 частей).</summary>
    public const long CloudLimit = 8L * 1024 * 1024;

    /// <summary>Base — с какой части лежит архив: 0 (files/0…15) или 16 (files/16…31).</summary>
    sealed record CloudMeta(DateTime At, long Size, string Sha, int Chunks, int Base, string Device, string Version);

    /// <summary>Куда писать новый бэкап: в половину, где прошлого нет, — он цел, пока описание не переключится.</summary>
    static int NextBase(CloudMeta? old) => old is { Base: 0 } ? MaxChunks : 0;

    static async Task<(string Uid, string Token)> Member()
    {
        if (!Firebase.Configured) throw new ServiceError("NOT_CONFIGURED");
        if (!Account.SignedIn) throw new ServiceError("SIGN_IN");
        return await Account.Token();
    }

    /// <summary>Запрос к Firestore через общий клиент (с офлайн-режимом) и своим сроком: часть архива на медленной связи идёт дольше 15 секунд.</summary>
    static async Task<JsonNode?> Call(string path, HttpMethod method, string token, JsonNode? body = null)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var request = new HttpRequestMessage(method, Firebase.Url(path));
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        try
        {
            using var response = await Http.Client.SendAsync(request, cts.Token);
            JsonNode? json = null;
            try { json = JsonNode.Parse(await response.Content.ReadAsStringAsync(cts.Token)); }
            catch (JsonException) { }
            var status = (int)response.StatusCode;
            if (status is >= 200 and < 300) return json;
            var reason = Firebase.Reason(new Firebase.Reply(response.StatusCode, json));
            throw new ServiceError(status switch
            {
                404 => "NOT_FOUND",
                401 => "SESSION",
                403 => "DENIED",
                429 or 503 => "BUSY",
                _ => reason.Contains("PERMISSION_DENIED") ? "DENIED" : "SERVER",
            }, reason);
        }
        catch (OfflineException e) { throw new ServiceError("OFFLINE_MODE", e.Message); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { throw new ServiceError("OFFLINE", e.Message); }
    }

    static async Task<CloudMeta?> Meta(string uid, string token)
    {
        JsonNode? doc;
        try { doc = await Call($"/backups/{uid}", HttpMethod.Get, token); }
        catch (ServiceError e) when (e.Code == "NOT_FOUND") { return null; }
        var f = Firebase.FromFields(doc?["fields"] as JsonObject);
        return new CloudMeta(Firebase.Time(f.S("at")) ?? DateTime.MinValue, f.L("size"), f.S("sha256"), (int)Math.Clamp(f.L("chunks"), 0, MaxChunks),
            f.L("base") == MaxChunks ? MaxChunks : 0, f.S("device"), f.S("version"));
    }

    /// <summary>
    /// Выгрузить архив, не трогая прошлый бэкап: части — в другую половину (NextBase), потом описание
    /// backups/&lt;uid&gt; переключается на них (по нему восстановление сверяет sha256), и только тогда части
    /// прошлого бэкапа удаляются. Оборвалась выгрузка — описание и части прошлого бэкапа целы.
    /// </summary>
    static async Task Upload(byte[] zip, IProgress<double>? progress)
    {
        if (zip.Length > CloudLimit) throw new ServiceError("TOO_BIG");
        var (uid, token) = await Member();
        var old = await Meta(uid, token);
        var start = NextBase(old);
        var data = Convert.ToBase64String(zip);
        var chunks = (data.Length + ChunkChars - 1) / ChunkChars;
        for (var i = 0; i < chunks; i++)
        {
            var part = data.Substring(i * ChunkChars, Math.Min(ChunkChars, data.Length - i * ChunkChars));
            // Токен — на каждую часть: на медленной связи выгрузка идёт дольше, чем живёт один токен.
            (_, token) = await Member();
            await Call($"/backups/{uid}/files/{start + i}", HttpMethod.Patch, token,
                new JsonObject { ["fields"] = Firebase.ToFields(new Dictionary<string, object?> { ["data"] = part }) });
            progress?.Report((i + 1.0) / (chunks + 1));
        }
        var fields = Firebase.ToFields(new Dictionary<string, object?>
        {
            ["size"] = (long)zip.Length, ["sha256"] = Sha(zip), ["chunks"] = (long)chunks, ["base"] = (long)start, ["device"] = Device, ["version"] = Http.Version,
        });
        fields["at"] = new JsonObject { ["timestampValue"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture) };
        (_, token) = await Member();
        await Call($"/backups/{uid}", HttpMethod.Patch, token, new JsonObject { ["fields"] = fields });
        if (old is not null) await DeleteChunks(uid, old.Base, old.Chunks);
        progress?.Report(1);
    }

    /// <summary>Убрать части from…from+count-1. Ошибки не мешают: на часть без описания ничто не ссылается.</summary>
    static async Task DeleteChunks(string uid, int from, int count)
    {
        for (var i = from; i < from + count; i++)
        {
            try
            {
                var (_, token) = await Member();
                await Call($"/backups/{uid}/files/{i}", HttpMethod.Delete, token);
            }
            catch (ServiceError e) when (e.Code is "OFFLINE" or "OFFLINE_MODE" or "SIGN_IN" or "SESSION") { return; } // связи нет — остальные тоже не удалятся
            catch (ServiceError) { }
        }
    }

    static async Task Download(string path, IProgress<double>? progress)
    {
        var (uid, token) = await Member();
        var meta = await Meta(uid, token) ?? throw new ServiceError("NOT_FOUND");
        var data = new StringBuilder();
        for (var i = 0; i < meta.Chunks; i++)
        {
            (_, token) = await Member();
            var doc = await Call($"/backups/{uid}/files/{meta.Base + i}", HttpMethod.Get, token);
            data.Append(Firebase.FromFields(doc?["fields"] as JsonObject).S("data"));
            progress?.Report((i + 1.0) / meta.Chunks);
        }
        byte[] bytes;
        try { bytes = Convert.FromBase64String(data.ToString()); }
        catch (FormatException) { throw Broken(); }
        if (bytes.Length != meta.Size || Sha(bytes) != meta.Sha) throw Broken();
        await File.WriteAllBytesAsync(path, bytes);
    }

    static async Task<List<BackupEntry>> CloudList()
    {
        var (uid, token) = await Member();
        return await Meta(uid, token) is { } m ? [new BackupEntry("cloud", "cloud", null, m.At, m.Size, m.Device, m.Version, false, false)] : [];
    }

    static async Task DeleteCloud()
    {
        var (uid, token) = await Member();
        if (await Meta(uid, token) is null) return;
        await Call($"/backups/{uid}", HttpMethod.Delete, token);
        // Обе половины целиком: части оборванной выгрузки тоже уходят.
        await DeleteChunks(uid, 0, 2 * MaxChunks);
    }

    /// <summary>
    /// Понятный текст ошибки. Про правила базы и настройку сервера — только владельцу;
    /// остальным — «облако сейчас недоступно, сохраните в папку».
    /// </summary>
    public static string Explain(Exception e) => e switch
    {
        ServiceError { Code: "OFFLINE_MODE" } => I18n.T("v11.bk.err.offlineMode"),
        ServiceError { Code: "OFFLINE" } => I18n.T("v11.bk.err.offline"),
        ServiceError { Code: "SIGN_IN" or "SESSION" } => I18n.T("v11.bk.err.signIn"),
        ServiceError { Code: "TOO_BIG" } => I18n.T("v11.bk.err.tooBig", ("max", I18n.T("bytes.mb", ("n", CloudLimit / 1048576)))),
        ServiceError { Code: "BUSY" } => I18n.T("v11.bk.err.busy"),
        ServiceError { Code: "NOT_FOUND" } => I18n.T("v11.bk.err.gone"),
        ServiceError { Code: "DENIED" } when Account.Owner => I18n.T("v11.bk.err.rules"),
        ServiceError { Code: "NOT_CONFIGURED" } when Account.Owner => I18n.T("v11.bk.err.config"),
        ServiceError s when Account.Owner => I18n.T("v11.bk.err.server", ("reason", s.Message)),
        ServiceError => I18n.T("v11.bk.err.cloud"),
        InvalidDataException d => d.Message,
        JsonException => I18n.T("v11.bk.err.broken"),
        UnauthorizedAccessException => I18n.T("v11.bk.err.access"),
        IOException io => I18n.T("v11.bk.err.disk", ("reason", io.Message)),
        _ => Jobs.Explain(e),
    };

    /// <summary>Часть общего прогресса: from…to.</summary>
    sealed class Part(IProgress<double> outer, double from, double to) : IProgress<double>
    {
        public void Report(double value) => outer.Report(from + (to - from) * Math.Clamp(value, 0, 1));
    }

    static IProgress<double>? Scale(IProgress<double>? p, double from, double to) => p is null ? null : new Part(p, from, to);

    // ---------------------------------------------------------------- самопроверки

    /// <summary>Папка проверки внутри временной папки данных.</summary>
    static string TestDir(string name)
    {
        var dir = Path.Combine(Paths.DataDir, "bk-test", name);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        return Directory.CreateDirectory(dir).FullName;
    }

    [SelfTest]
    static string BackupRoundTrip()
    {
        var src = TestDir("src");
        var dst = TestDir("dst");
        var games = Directory.CreateDirectory(Path.Combine(dst, "Valheim")).FullName;
        File.WriteAllText(Path.Combine(src, "settings.json"), new JsonObject
        {
            ["language"] = "ru", ["theme"] = "neon", ["favorites"] = new JsonArray(new JsonObject { ["gameId"] = "valheim", ["modId"] = "x" }),
            ["gamePaths"] = new JsonObject { ["valheim"] = games, ["gone"] = Path.Combine(dst, "nope") },
        }.ToJsonString());
        File.WriteAllText(Path.Combine(src, "profiles.json"), """{"games":{"valheim":{"Строитель":{"enabled":["a"]}}},"active":{}}""");
        File.WriteAllText(Path.Combine(src, "playtime.json"), """{"games":{"valheim":{"totalMs":3600000}}}""");
        var cfg = RandomNumberGenerator.GetBytes(3000);
        File.WriteAllBytes(Path.Combine(Directory.CreateDirectory(Path.Combine(src, "profiles", "valheim")).FullName, "Строитель.zip"), cfg);
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(src, "creator", "my-mod")).FullName, "main.mls"), "mod \"Мой\"");
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(src, "creator", "_build")).FullName, "out.zip"), "build");
        var saves = Directory.CreateDirectory(Path.Combine(src, "backups", "valheim")).FullName;
        File.WriteAllText(Path.Combine(saves, "20261001-120000-manual.zip"), "old");
        File.WriteAllText(Path.Combine(saves, "20261002-120000-launch.zip"), "new");
        var mods = new Dictionary<string, JsonObject>
        {
            ["valheim"] = new() { ["game"] = "Valheim", ["mods"] = ModSetup.ToJson([new SetupMod("denikson-BepInExPack_Valheim", "BepInEx", "5.4.2202", "thunderstore", true)]) },
        };
        // vanilla.json — что отложено в папках игр этого ПК: в бэкап не идёт.
        File.WriteAllText(Path.Combine(src, "vanilla.json"), """{"games":{"valheim":{"mode":"inject","files":["winhttp.dll"]}}}""");
        var zip = Path.Combine(TestDir("zip"), "b.zip");
        var (files, skipped) = Write(src, zip, mods, LatestSaves(src), "manual");
        if (skipped != 0 || files != 7) throw new Exception($"{files} files, {skipped} skipped");

        // Для облака: архив перерос предел — сборка останавливается сразу, а не читает в память гигабайты.
        try { Write(src, Path.Combine(TestDir("big"), "b.zip"), mods, [], "manual", null, 1000); throw new Exception("archive over the limit written"); }
        catch (ServiceError e) when (e.Code == "TOO_BIG") { }

        File.WriteAllText(Path.Combine(dst, "settings.json"), """{"nexusApiKey":"KEEP-ME","language":"en"}""");
        var r = Unpack(zip, dst, stage: false);
        var settings = ReadJson(Path.Combine(dst, "settings.json"))!;
        if (settings.Str("language") != "ru" || settings.Str("theme") != "neon" || settings.Str("nexusApiKey") != "KEEP-ME") throw new Exception("settings: " + settings.ToJsonString());
        if (settings["gamePaths"].Str("valheim") != games || settings["gamePaths"].Str("gone") is not null) throw new Exception("game paths: " + settings["gamePaths"]?.ToJsonString());
        if (File.ReadAllText(Path.Combine(dst, "profiles.json")) != File.ReadAllText(Path.Combine(src, "profiles.json"))) throw new Exception("profiles.json differs");
        if (!File.ReadAllBytes(Path.Combine(dst, "profiles", "valheim", "Строитель.zip")).SequenceEqual(cfg)) throw new Exception("profile config differs");
        if (!File.Exists(Path.Combine(dst, "creator", "my-mod", "main.mls")) || Directory.Exists(Path.Combine(dst, "creator", "_build"))) throw new Exception("creator projects");
        if (r.ModLists != 1 || r.Saves != 1 || !File.Exists(Path.Combine(dst, "backups", "valheim", "20261002-120000-launch.zip"))) throw new Exception("mods or saves");
        var snap = Directory.GetFiles(Path.Combine(dst, "snapshots", "valheim"), "*.json").Single();
        if (JsonNode.Parse(File.ReadAllText(snap)).Str("reason") != "backup" || ModSetup.FromJson(JsonNode.Parse(File.ReadAllText(snap))?["mods"] as JsonArray).Single().Version != "5.4.2202")
            throw new Exception("mod list snapshot");

        // Работающая программа: JSON — в restore-pending, подмена при запуске.
        var live = TestDir("live");
        var staged = Unpack(zip, live, stage: true);
        if (!staged.NeedsRestart || File.Exists(Path.Combine(live, "profiles.json")) || !File.Exists(Path.Combine(live, PendingDir, "settings.json"))) throw new Exception("not staged");
        if (!File.Exists(Path.Combine(live, "profiles", "valheim", "Строитель.zip"))) throw new Exception("non-JSON file not written at once");
        if (ApplyPending(live) != staged.Restart.Count || !File.Exists(Path.Combine(live, "profiles.json")) || Directory.Exists(Path.Combine(live, PendingDir))) throw new Exception("pending not applied");

        // Восстановление сорвалось на последнем файле — ни отложенных JSON, ни половины файлов, ни новых снимков.
        var fail = TestDir("fail");
        Directory.CreateDirectory(Path.Combine(fail, "backups", "valheim", "20261002-120000-launch.zip"));
        try { Unpack(zip, fail, stage: true); throw new Exception("restore over a busy file succeeded"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        var snaps = Path.Combine(fail, "snapshots");
        if (Directory.Exists(Path.Combine(fail, PendingDir)) || Directory.Exists(Path.Combine(fail, PendingDir + ".tmp"))
            || File.Exists(Path.Combine(fail, "profiles", "valheim", "Строитель.zip")) || (Directory.Exists(snaps) && Directory.EnumerateFiles(snaps, "*", SearchOption.AllDirectories).Any()))
            throw new Exception("failed restore left files behind");

        // Подменённый файл сразу уходит из restore-pending: занятый ждёт следующего запуска, настройки второй раз не сливаются.
        var retry = TestDir("retry");
        Unpack(zip, retry, stage: true);
        var busy = Directory.CreateDirectory(Path.Combine(retry, "playtime.json")).FullName;
        ApplyPending(retry);
        if (!File.Exists(Path.Combine(retry, PendingDir, "playtime.json")) || File.Exists(Path.Combine(retry, PendingDir, "settings.json"))) throw new Exception("pending: busy file lost or settings left");
        File.WriteAllText(Path.Combine(retry, "settings.json"), """{"theme":"mine"}""");
        Directory.Delete(busy);
        if (ApplyPending(retry) != 1 || ReadJson(Path.Combine(retry, "settings.json")).Str("theme") != "mine" || !File.Exists(Path.Combine(retry, "playtime.json"))
            || Directory.Exists(Path.Combine(retry, PendingDir))) throw new Exception("pending retry");

        // Испорченный архив не восстанавливается.
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Update))
        {
            z.GetEntry("data/playtime.json")!.Delete();
            using var w = new StreamWriter(z.CreateEntry("data/playtime.json").Open());
            w.Write("{}");
        }
        try { Unpack(zip, TestDir("bad"), stage: false); throw new Exception("tampered backup restored"); }
        catch (InvalidDataException) { }
        return $"{files} files backed up and restored; JSON staged until restart; a failed restore leaves nothing; tampering detected";
    }

    [SelfTest]
    static string BackupHasNoSecrets()
    {
        var src = TestDir("secret-src");
        string[] secrets = ["NX-SECRET-1", "AI-SECRET-2", "TOK-SECRET-3", "RT-SECRET-4", "INJ-SECRET-5", "PASS-SECRET-6"];
        File.WriteAllText(Path.Combine(src, "settings.json"), new JsonObject
        {
            ["language"] = "ru", ["nexusApiKey"] = secrets[0], ["aiKey"] = secrets[1], ["account"] = new JsonObject { ["email"] = "a@b.c" },
            ["hub"] = new JsonObject { ["sessionToken"] = secrets[2], ["likes"] = new JsonObject { ["x"] = true } },
            ["tools"] = new JsonArray(new JsonObject { ["name"] = "Tool", ["password"] = secrets[5] }),
        }.ToJsonString());
        File.WriteAllText(Path.Combine(src, "account.json"), new JsonObject { ["user"] = new JsonObject { ["refreshToken"] = new JsonObject { ["raw"] = secrets[3] } } }.ToJsonString());
        File.WriteAllText(Path.Combine(src, "friends.json"), "{}");
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(src, "injects")).FullName, ".key"), secrets[4]);
        var zipPath = Path.Combine(TestDir("secret-zip"), "b.zip");
        Write(src, zipPath, new Dictionary<string, JsonObject>(), [], "manual");
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            foreach (var e in zip.Entries)
            {
                if (e.FullName.Contains("account") || e.FullName.Contains("friends") || e.FullName.Contains("injects")) throw new Exception("copied: " + e.FullName);
                using var r = new StreamReader(e.Open());
                var text = r.ReadToEnd();
                if (secrets.FirstOrDefault(text.Contains) is { } leak) throw new Exception($"{leak} in {e.FullName}");
            }
            using var reader = new StreamReader(zip.GetEntry("data/settings.json")!.Open());
            var settings = JsonNode.Parse(reader.ReadToEnd());
            if (settings?["hub"]?["likes"].Bool("x") != true || settings.Str("language") != "ru") throw new Exception("ordinary settings lost");
        }

        // Подложенный архив: ключ в настройках и чужой account.json не перезаписывают свои.
        var dst = TestDir("secret-dst");
        File.WriteAllText(Path.Combine(dst, "settings.json"), """{"nexusApiKey":"MINE"}""");
        File.WriteAllText(Path.Combine(dst, "account.json"), "MINE");
        string Crafted(params (string Path, string Text)[] entries)
        {
            var path = Path.Combine(TestDir("secret-crafted-" + entries.Length), "c.zip");
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            var listed = new JsonArray();
            foreach (var (p, t) in entries)
            {
                var bytes = Encoding.UTF8.GetBytes(t);
                using (var s = zip.CreateEntry(p).Open()) s.Write(bytes);
                listed.Add(new JsonObject { ["path"] = p, ["size"] = bytes.Length, ["sha256"] = Sha(bytes) });
            }
            using var m = new StreamWriter(zip.CreateEntry("manifest.json").Open());
            m.Write(new JsonObject { ["modlaunchBackup"] = 1, ["files"] = listed }.ToJsonString());
            return path;
        }
        Unpack(Crafted(("data/settings.json", """{"nexusApiKey":"EVIL","theme":"x"}""")), dst, stage: false);
        var after = ReadJson(Path.Combine(dst, "settings.json"))!;
        if (after.Str("nexusApiKey") != "MINE" || after.Str("theme") != "x") throw new Exception("key overwritten: " + after.ToJsonString());
        foreach (var evil in new[] { "data/account.json", "data/injects/.key", "data/../account.json", "data/friends.json" })
        {
            try { Unpack(Crafted((evil, "EVIL")), dst, stage: false); throw new Exception("accepted " + evil); }
            catch (InvalidDataException) { }
        }
        if (File.ReadAllText(Path.Combine(dst, "account.json")) != "MINE" || Directory.Exists(Path.Combine(dst, "injects"))) throw new Exception("account or injects touched");
        // Бэкап первых сборок 11.0 с vanilla.json восстанавливается, а свой vanilla.json (что отложено в папках игр) остаётся.
        File.WriteAllText(Path.Combine(dst, "vanilla.json"), "MINE");
        Unpack(Crafted(("data/vanilla.json", "OLD"), ("data/history.json", "{}")), dst, stage: false);
        if (File.ReadAllText(Path.Combine(dst, "vanilla.json")) != "MINE" || !File.Exists(Path.Combine(dst, "history.json"))) throw new Exception("vanilla.json restored");
        return "no tokens, keys, account or injects in the backup; restore never overwrites them";
    }

    [SelfTest]
    static string BackupKeepsNewestTen()
    {
        var dir = TestDir("keep");
        var start = new DateTime(2026, 1, 1, 12, 0, 0);
        string Name(int day) => $"ModLaunch-{start.AddDays(day).ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.zip";
        // Архив с манифестом этого ПК или другого: папку облачного сервиса делят все компьютеры человека.
        void Zip(string name, string device)
        {
            using var zip = ZipFile.Open(Path.Combine(dir, name), ZipArchiveMode.Create);
            using var w = new StreamWriter(zip.CreateEntry("manifest.json").Open());
            w.Write(new JsonObject { ["modlaunchBackup"] = 1, ["device"] = device }.ToJsonString());
        }
        for (var i = 0; i < 13; i++) Zip(Name(i), Device);
        var laptop = Name(-5);
        Zip(laptop, Device + "-LAPTOP");
        File.WriteAllText(Path.Combine(dir, "notes.txt"), "mine");
        File.WriteAllText(Path.Combine(dir, "ModLaunch-old.zip"), "mine");
        // Невозможная дата и не ASCII-цифры в имени: список и чистка не падают, файл не трогается.
        File.WriteAllText(Path.Combine(dir, "ModLaunch-20261399-250000.zip"), "mine");
        File.WriteAllText(Path.Combine(dir, "ModLaunch-٢٠٢٦٠١٠١-١٢٠٠٠٠.zip"), "mine");
        if (Prune(dir, 10) != 3) throw new Exception("not 3 removed");
        var left = ListFolder("local", dir).Where(b => b.Device == Device).ToList();
        if (left.Count != 10 || left[^1].At != start.AddDays(3).ToUniversalTime() || left[0].At != start.AddDays(12).ToUniversalTime()) throw new Exception("wrong ones kept");
        foreach (var name in new[] { "notes.txt", "ModLaunch-old.zip", "ModLaunch-20261399-250000.zip", "ModLaunch-٢٠٢٦٠١٠١-١٢٠٠٠٠.zip", laptop })
            if (!File.Exists(Path.Combine(dir, name))) throw new Exception("foreign file removed: " + name);

        // Восстанавливают из самого старого: страховочный бэкап его не удаляет, удалится он потом, как обычно.
        var oldest = Path.Combine(dir, Name(3));
        Zip(Name(20), Device);
        if (Prune(dir, 10, spare: oldest) != 0 || !File.Exists(oldest)) throw new Exception("backup being restored removed");
        if (Prune(dir, 10) != 1 || File.Exists(oldest)) throw new Exception("spared backup kept forever");
        return "10 newest of this PC kept; other PCs' backups, odd names and the one being restored untouched";
    }

    [SelfTest]
    static string SyncFoldersDetectSafely()
    {
        var root = TestDir("sync");
        var home = Directory.CreateDirectory(Path.Combine(root, "home")).FullName;
        var docs = Directory.CreateDirectory(Path.Combine(home, "Documents")).FullName;
        var appData = Directory.CreateDirectory(Path.Combine(home, "AppData", "Roaming")).FullName;
        var local = Directory.CreateDirectory(Path.Combine(home, "AppData", "Local")).FullName;
        var drive = Directory.CreateDirectory(Path.Combine(root, "G")).FullName;
        var onedrive = Directory.CreateDirectory(Path.Combine(home, "OneDrive - Personal")).FullName;
        var dropbox = Directory.CreateDirectory(Path.Combine(root, "Dropbox (Личный)")).FullName;
        Directory.CreateDirectory(Path.Combine(drive, "Мой диск"));
        Directory.CreateDirectory(Path.Combine(home, "YandexDisk"));
        Directory.CreateDirectory(Path.Combine(docs, "MEGAsync"));
        Directory.CreateDirectory(Path.Combine(home, "iCloudDrive"));
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(local, "Dropbox")).FullName, "info.json"), new JsonObject { ["personal"] = new JsonObject { ["path"] = dropbox } }.ToJsonString());
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(appData, "Dropbox")).FullName, "info.json"), "{broken");
        var found = Services(n => n == "OneDriveConsumer" ? onedrive : null, home, docs, appData, local, [drive]);
        var missing = found.Where(t => !t.Available || !t.Folder!.EndsWith(SyncFolder, StringComparison.Ordinal)).Select(t => t.Id).ToList();
        if (missing.Count > 0) throw new Exception("not found: " + string.Join(", ", missing));
        if (!found.First(t => t.Id == "dropbox").Folder!.StartsWith(dropbox, StringComparison.Ordinal)) throw new Exception("dropbox path from info.json ignored");

        // Мусор вместо путей: ничего не находится и ничего не падает.
        var file = Path.Combine(root, "file.txt");
        File.WriteAllText(file, "x");
        var none = Services(_ => "relative\\path\0bad", "", " ", file, "C:\\definitely\\not\\here", ["", "Z:\\", file]);
        if (none.Count != 6 || none.Any(t => t.Available)) throw new Exception("garbage detected as a service");
        var all = Targets();
        if (all.First().Id != "local" || all.Last().Id != "cloud") throw new Exception("targets order");
        return "OneDrive, Google Drive, Dropbox, Yandex Disk, MEGA, iCloud found; garbage paths do not throw";
    }

    /// <summary>Новый облачный бэкап никогда не пишется поверх частей, на которые ссылается описание прошлого.</summary>
    [SelfTest]
    static string CloudUploadKeepsLastBackup()
    {
        CloudMeta Old(int start, int chunks) => new(DateTime.UtcNow, 1, "", chunks, start, "", "");
        foreach (var old in new[] { null, Old(0, 1), Old(0, MaxChunks), Old(MaxChunks, 1), Old(MaxChunks, MaxChunks) })
        {
            var start = NextBase(old);
            if (start is not (0 or MaxChunks)) throw new Exception($"parts from {start}");
            if (old is not null && start < old.Base + old.Chunks && old.Base < start + MaxChunks) throw new Exception($"new parts from {start} overwrite the last backup at {old.Base}");
        }
        return "new parts go to the other half; the last backup stays whole until the description switches";
    }

    /// <summary>Обычный человек не видит в ошибке слов о правилах базы и Firebase.</summary>
    [SelfTest]
    static string BackupErrorsHideServerSetup()
    {
        if (Account.Owner) return "skipped: this PC is the owner's";
        foreach (var code in new[] { "DENIED", "NOT_CONFIGURED", "SERVER" })
        {
            var text = Explain(new ServiceError(code, "PERMISSION_DENIED: Missing or insufficient permissions."));
            if (text.Contains("Firebase", StringComparison.OrdinalIgnoreCase) || text.Contains("rules", StringComparison.OrdinalIgnoreCase)
                || text.Contains("правил", StringComparison.OrdinalIgnoreCase) || text.Contains("PERMISSION", StringComparison.Ordinal)) throw new Exception(code + ": " + text);
        }
        return "server setup hidden from users";
    }
}
