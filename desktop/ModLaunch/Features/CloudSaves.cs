using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Games;

namespace ModLaunch.Features;

/// <summary>Одна сторона сохранений: на этом ПК или в облаке. Hash — по содержимому файлов (не по датам).</summary>
public sealed record SaveSide(string Hash, DateTime At, long Bytes, int Files, string Device);

/// <summary>
/// Что делать с сохранениями перед игрой. State: same — совпадают; push — новее здесь; pull — новее в облаке
/// (другой ПК); conflict — поменялись и тут, и там; empty — сохранений нет нигде; noCloud — папка облака недоступна;
/// noSaves — у игры нет известной папки сохранений.
/// </summary>
public sealed record SaveCheck(string State, SaveSide? Local, SaveSide? Cloud);

/// <summary>
/// 28.0: облачные сохранения, как Steam Cloud, но через папку облачного сервиса, который уже стоит на ПК (OneDrive,
/// Google Диск, Dropbox, Яндекс Диск, MEGA, iCloud или своя/сетевая папка) — свой сервер не нужен. На каждую игру:
/// «&lt;сервис&gt;\ModLaunch Backups\Saves\&lt;игра&gt;\saves.zip» и state.json (отпечаток, когда, с какого ПК).
/// Перед игрой сверяем: новее в облаке — берём (копия здешних — в резервных копиях), новее здесь — отправляем,
/// поменялись обе — спрашиваем, какую оставить; вторая не пропадает, а ложится в резервные копии игры.
/// После игры отправляем свежие. Ничего не стирается без копии.
/// </summary>
public static class CloudSaves
{
    static readonly JsonFile Store = new(Path.Combine(Paths.DataDir, "cloud-saves.json"), () => new JsonObject());
    const string Zip = "saves.zip";
    const string State = "state.json";

    public static event Action<string>? Changed;

    /// <summary>Куда можно класть: найденные облачные сервисы и своя папка (облако ModLaunch мало для сохранений).</summary>
    public static List<BackupTarget> Places() => OneClickBackup.Targets().Where(t => t.Kind is "service" or "custom").ToList();

    public static string PlaceId
    {
        get => Settings.Data.Str("savesCloud") is { Length: > 0 } id ? id : Places().FirstOrDefault(t => t.Available)?.Id ?? "onedrive";
        set { Settings.Data["savesCloud"] = value; Settings.Save(); Changed?.Invoke(""); }
    }

    public static BackupTarget? Place() => Places().FirstOrDefault(t => t.Id == PlaceId && t.Available && t.Folder is not null);

    public static bool Enabled(string gameId) => Store.Data[gameId] is JsonObject o && o.Bool("on");

    public static void SetEnabled(string gameId, bool on)
    {
        if (Store.Data[gameId] is not JsonObject o) Store.Data[gameId] = o = new JsonObject();
        o["on"] = on;
        Store.Save();
        Changed?.Invoke(gameId);
    }

    static string? Synced(string gameId) => (Store.Data[gameId] as JsonObject)?.Str("synced");

    public static DateTime? SyncedAt(string gameId) => DateTime.TryParse((Store.Data[gameId] as JsonObject)?.Str("syncedAt"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : null;

    static void MarkSynced(string gameId, string hash)
    {
        if (Store.Data[gameId] is not JsonObject o) Store.Data[gameId] = o = new JsonObject();
        o["synced"] = hash;
        o["syncedAt"] = DateTime.UtcNow.ToString("o");
        Store.Save();
        Changed?.Invoke(gameId);
    }

    static string Safe(string id) => string.Concat(id.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_'));

    public static string? CloudDir(string gameId) => Place()?.Folder is { } f ? Path.Combine(f, "Saves", Safe(gameId)) : null;

    // ---------------------------------------------------------------- сверка

    /// <summary>Что делать: по отпечаткам здесь, в облаке и при последней сверке.</summary>
    internal static string Decide(string? local, string? cloud, string? synced)
    {
        if (local is null && cloud is null) return "empty";
        if (cloud is null) return "push";
        if (local is null) return "pull";
        if (local == cloud) return "same";
        if (synced == local) return "pull";
        if (synced == cloud) return "push";
        return "conflict";
    }

    /// <summary>Сохранения в папке: отпечаток по путям и содержимому, последнее изменение, размер. Пусто — null.</summary>
    public static SaveSide? Describe(string? dir)
    {
        if (dir is null || !Directory.Exists(dir)) return null;
        var files = new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories)
            .Select(f => (File: f, Rel: Path.GetRelativePath(dir, f.FullName).Replace('\\', '/')))
            .OrderBy(x => x.Rel, StringComparer.Ordinal).ToList();
        if (files.Count == 0) return null;
        using var all = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (file, rel) in files)
        {
            all.AppendData(Encoding.UTF8.GetBytes(rel + "\n"));
            using var s = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            all.AppendData(SHA256.HashData(s));
        }
        return new SaveSide(Convert.ToHexString(all.GetHashAndReset()).ToLowerInvariant(), files.Max(x => x.File.LastWriteTimeUtc),
            files.Sum(x => x.File.Length), files.Count, Environment.MachineName);
    }

    static SaveSide? ReadCloud(string? cloudDir)
    {
        if (cloudDir is null) return null;
        try
        {
            var state = Path.Combine(cloudDir, State);
            if (!File.Exists(state) || !File.Exists(Path.Combine(cloudDir, Zip))) return null;
            var o = JsonNode.Parse(File.ReadAllText(state));
            if (o.Str("hash") is not { Length: > 0 } hash) return null;
            return new SaveSide(hash, DateTime.TryParse(o.Str("at"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var at) ? at : DateTime.MinValue,
                o.Long("bytes"), (int)o.Long("files"), o.Str("device") ?? "?");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { return null; }
    }

    public static SaveCheck Check(GameDef def, string gamePath)
    {
        var savesDir = Launcher.SavesDir(def, gamePath);
        if (savesDir is null) return new("noSaves", null, null);
        var cloudDir = CloudDir(def.Id);
        if (cloudDir is null) return new("noCloud", Describe(savesDir), null);
        var local = Describe(savesDir);
        var cloud = ReadCloud(cloudDir);
        return new(Decide(local?.Hash, cloud?.Hash, Synced(def.Id)), local, cloud);
    }

    // ---------------------------------------------------------------- отправить и взять

    /// <summary>Отправить сохранения этого ПК в облако (прошлая облачная копия остаётся рядом как saves.prev.zip).</summary>
    public static SaveSide? Push(GameDef def, string gamePath)
    {
        var savesDir = Launcher.SavesDir(def, gamePath) ?? throw new InvalidOperationException(I18n.T("bak.unsupported"));
        var cloudDir = CloudDir(def.Id) ?? throw new InvalidOperationException(I18n.T("v28.cs.noPlace"));
        var local = Describe(savesDir);
        if (local is null) return null;
        Directory.CreateDirectory(cloudDir);
        var zip = Path.Combine(cloudDir, Zip);
        var tmp = zip + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);
        ZipFile.CreateFromDirectory(savesDir, tmp, CompressionLevel.Optimal, includeBaseDirectory: false);
        if (File.Exists(zip)) File.Copy(zip, Path.Combine(cloudDir, "saves.prev.zip"), overwrite: true);
        File.Move(tmp, zip, overwrite: true);
        var state = new JsonObject
        {
            ["hash"] = local.Hash, ["at"] = local.At.ToString("o"), ["bytes"] = local.Bytes, ["files"] = local.Files,
            ["device"] = local.Device, ["game"] = def.Name, ["app"] = Http.Version, ["pushed"] = DateTime.UtcNow.ToString("o"),
        };
        File.WriteAllText(Path.Combine(cloudDir, State + ".tmp"), state.ToJsonString());
        File.Move(Path.Combine(cloudDir, State + ".tmp"), Path.Combine(cloudDir, State), overwrite: true);
        MarkSynced(def.Id, local.Hash);
        return local;
    }

    /// <summary>
    /// Положить облачную копию в резервные копии игры (reason cloud) — так её можно и восстановить, и сохранить
    /// при споре. Возвращает имя копии.
    /// </summary>
    static string KeepCloudCopy(GameDef def, string cloudDir)
    {
        var folder = Backups.FolderFor(def.Id);
        Directory.CreateDirectory(folder);
        var at = DateTime.Now;
        var name = $"{at:yyyyMMdd-HHmmss}-cloud.zip";
        for (var i = 1; File.Exists(Path.Combine(folder, name)) && i < 60; i++) name = $"{at.AddSeconds(i):yyyyMMdd-HHmmss}-cloud.zip";
        File.Copy(Path.Combine(cloudDir, Zip), Path.Combine(folder, name));
        return name;
    }

    /// <summary>Взять сохранения из облака: здешние сначала уходят в резервные копии (Backups.Restore делает копию сам).</summary>
    public static SaveSide? Pull(GameDef def, string gamePath)
    {
        var savesDir = Launcher.SavesDir(def, gamePath) ?? throw new InvalidOperationException(I18n.T("bak.unsupported"));
        var cloudDir = CloudDir(def.Id) ?? throw new InvalidOperationException(I18n.T("v28.cs.noPlace"));
        var cloud = ReadCloud(cloudDir) ?? throw new FileNotFoundException(I18n.T("v28.cs.nothingInCloud"));
        var name = KeepCloudCopy(def, cloudDir);
        Backups.Restore(def.Id, name, savesDir);
        // Отпечаток считаем заново: если облачный архив собран другой версией, всё равно сверимся с тем, что лежит.
        MarkSynced(def.Id, Describe(savesDir)?.Hash ?? cloud.Hash);
        return cloud;
    }

    /// <summary>Спор: оставить здешние (облачная копия — в резервные копии) или облачные (здешние — в резервные копии).</summary>
    public static void Resolve(GameDef def, string gamePath, bool keepLocal)
    {
        if (keepLocal)
        {
            if (CloudDir(def.Id) is { } cloudDir && File.Exists(Path.Combine(cloudDir, Zip))) KeepCloudCopy(def, cloudDir);
            Push(def, gamePath);
        }
        else Pull(def, gamePath);
    }

    /// <summary>После игры: отправить, если сохранения поменялись. Возвращает, отправили ли.</summary>
    public static bool AfterExit(GameDef def, string gamePath)
    {
        if (!Enabled(def.Id) || CloudDir(def.Id) is null) return false;
        var check = Check(def, gamePath);
        if (check.State != "push") return false;
        return Push(def, gamePath) is not null;
    }

    [SelfTest]
    static string CloudSavesDecideAndRoundTrip()
    {
        if (Decide(null, null, null) != "empty" || Decide("a", null, null) != "push" || Decide(null, "b", "x") != "pull") throw new Exception("missing sides");
        if (Decide("a", "a", "z") != "same") throw new Exception("same");
        if (Decide("a", "b", "a") != "pull") throw new Exception("only cloud changed should pull");
        if (Decide("a", "b", "b") != "push") throw new Exception("only local changed should push");
        if (Decide("a", "b", "c") != "conflict" || Decide("a", "b", null) != "conflict") throw new Exception("both changed should conflict");

        var root = Path.Combine(Path.GetTempPath(), "modlaunch-cloudsaves-test");
        try { Directory.Delete(root, true); } catch { }
        try
        {
            var a = Directory.CreateDirectory(Path.Combine(root, "a")).FullName;
            var b = Directory.CreateDirectory(Path.Combine(root, "b")).FullName;
            File.WriteAllText(Path.Combine(a, "slot1.sav"), "level 7");
            Directory.CreateDirectory(Path.Combine(a, "sub"));
            File.WriteAllText(Path.Combine(a, "sub", "opts.ini"), "x=1");
            File.WriteAllText(Path.Combine(b, "slot1.sav"), "level 7");
            Directory.CreateDirectory(Path.Combine(b, "sub"));
            File.WriteAllText(Path.Combine(b, "sub", "opts.ini"), "x=1");
            File.SetLastWriteTimeUtc(Path.Combine(b, "slot1.sav"), DateTime.UtcNow.AddDays(-3));
            var da = Describe(a)!;
            var db = Describe(b)!;
            if (da.Hash != db.Hash || da.Files != 2) throw new Exception("same content, different dates must give the same hash");
            File.WriteAllText(Path.Combine(b, "sub", "opts.ini"), "x=2");
            if (Describe(b)!.Hash == da.Hash) throw new Exception("changed file not noticed");
            File.Move(Path.Combine(a, "slot1.sav"), Path.Combine(a, "slot2.sav"));
            if (Describe(a)!.Hash == da.Hash) throw new Exception("renamed file not noticed");
            if (Describe(Path.Combine(root, "missing")) is not null) throw new Exception("missing folder should be null");

            // Круг: отправить → поменять здесь → взять из облака (здешнее — в резервных копиях) → спор → оставить здешнее.
            var game = GameCatalog.ById("subnautica") ?? throw new Exception("no subnautica def");
            var gamePath = Directory.CreateDirectory(Path.Combine(root, "game")).FullName;
            var saves = Launcher.SavesDir(game, gamePath)!;
            Directory.CreateDirectory(saves);
            File.WriteAllText(Path.Combine(saves, "slot.sav"), "day 1");
            var cloudRoot = Directory.CreateDirectory(Path.Combine(root, "cloud")).FullName;
            var wasPlace = Settings.Data.Str("savesCloud");
            OneClickBackup.DemoTargets = [new("custom", "custom", "test", true, cloudRoot)];
            try
            {
                PlaceId = "custom";
                Store.Data.Remove(game.Id);
                if (Check(game, gamePath).State != "push") throw new Exception("first check should push");
                var pushed = Push(game, gamePath)!;
                if (Check(game, gamePath).State != "same") throw new Exception("after push should be same");
                File.WriteAllText(Path.Combine(saves, "slot.sav"), "day 2");
                if (Check(game, gamePath).State != "push") throw new Exception("local change should push");
                Pull(game, gamePath);
                if (File.ReadAllText(Path.Combine(saves, "slot.sav")) != "day 1") throw new Exception("pull did not restore cloud saves");
                if (!Backups.List(game.Id).Any(b => b.Reason == "cloud") || !Backups.List(game.Id).Any(b => b.Reason == "restore")) throw new Exception("pull must keep both copies in backups");
                // Другой ПК отправил своё, а здесь тоже играли — спор.
                File.WriteAllText(Path.Combine(saves, "slot.sav"), "day 3 here");
                var state = JsonNode.Parse(File.ReadAllText(Path.Combine(CloudDir(game.Id)!, State)))!.AsObject();
                state["hash"] = "other-pc";
                File.WriteAllText(Path.Combine(CloudDir(game.Id)!, State), state.ToJsonString());
                if (Check(game, gamePath).State != "conflict") throw new Exception("both changed should conflict");
                var cloudCopies = Backups.List(game.Id).Count(b => b.Reason == "cloud");
                Resolve(game, gamePath, keepLocal: true);
                if (File.ReadAllText(Path.Combine(saves, "slot.sav")) != "day 3 here" || Check(game, gamePath).State != "same") throw new Exception("keep local failed");
                if (Backups.List(game.Id).Count(b => b.Reason == "cloud") != cloudCopies + 1) throw new Exception("losing cloud copy not kept");
                if (pushed.Files != 1) throw new Exception("pushed files");
            }
            finally
            {
                OneClickBackup.DemoTargets = null;
                Store.Data.Remove(game.Id);
                if (wasPlace is null) Settings.Data.Remove("savesCloud"); else Settings.Data["savesCloud"] = wasPlace;
            }
            return "decide: empty/push/pull/same/conflict; hash follows content, not dates; push → pull → conflict → keep local, losing side kept in backups";
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
