using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Mods;

namespace ModLaunch.Features;

/// <summary>Мод в сейфе: папка записи, его номер и версия, когда и почему туда попал, размер и запись из списка модов.</summary>
public sealed record SafeItem(string Dir, string Id, string Name, string Version, DateTime At, string Reason, long Bytes, string RelPath, JsonObject? Record);

/// <summary>
/// 27.0: сейф модов. Раньше обновление, переустановка и удаление стирали старую папку мода насовсем — вместе с правками
/// человека, модами, поставленными руками, и версиями, которых уже нет в каталоге. Теперь папка не стирается, а переезжает
/// в &lt;игра&gt;/ModHub/safe (тот же диск — переезд мгновенный и не копирует гигабайты) вместе с записью из списка модов.
/// «Вернуть» снимка берёт нужную версию отсюда первой: ровно те файлы, что были, без интернета.
/// Хранится не больше трёх версий одного мода и не больше 2 ГБ на игру (старые уходят первыми).
/// </summary>
public static partial class ModSafe
{
    const int KeepPerMod = 3;
    const string MetaFile = "safe.json", FilesDir = "files";

    public static bool Enabled => Settings.Data.Bool("modSafe", true);

    static long LimitBytes => (long)((Settings.Data["modSafeGb"] is JsonValue v && v.TryGetValue<double>(out var gb) && gb > 0 ? gb : 2) * (1L << 30));

    public static string Root(ModRegistry registry) => Path.Combine(registry.StorageDir, "safe");

    /// <summary>
    /// Убрать папку в сейф, а не получилось (сейф выключен, папка больше половины лимита, другой диск, файл занят) —
    /// удалить, как раньше. record — запись мода, которому папка принадлежит (null — папка без записи, например мод, поставленный руками).
    /// </summary>
    public static void Remove(ModRegistry registry, string folder, JsonObject? record, string reason)
    {
        if (!Directory.Exists(folder)) return;
        if (!Stash(registry, folder, record, reason)) Directory.Delete(folder, true);
    }

    static bool Stash(ModRegistry registry, string folder, JsonObject? record, string reason)
    {
        if (!Enabled) return false;
        string? dir = null;
        try
        {
            var bytes = Size(folder);
            if (bytes > LimitBytes / 2) return false;
            var id = record?.Str("id") ?? Path.GetFileName(folder.TrimEnd('\\', '/'));
            dir = Path.Combine(Root(registry), DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Slug(id));
            Directory.CreateDirectory(dir);
            // Directory.Move на том же диске — переименование: или целиком, или ничего. На другой диск — исключение, тогда удаляем как раньше.
            Directory.Move(folder, Path.Combine(dir, FilesDir));
            File.WriteAllText(Path.Combine(dir, MetaFile), new JsonObject
            {
                ["id"] = id,
                ["name"] = record?.Str("displayName") ?? record?.Str("name") ?? id,
                ["version"] = record?.Str("version") ?? "",
                ["reason"] = reason,
                ["at"] = DateTime.UtcNow.ToString("o"),
                ["bytes"] = bytes,
                ["path"] = Path.GetRelativePath(registry.GamePath, folder),
                ["record"] = record?.DeepClone(),
            }.ToJsonString());
            Prune(registry);
            return true;
        }
        catch
        {
            // Папка не переехала — убираем пустую запись сейфа, и вызывающий удалит папку как раньше.
            // Переехала, но описание не записалось — папки на месте уже нет, удалять нечего.
            if (dir is not null && Directory.Exists(folder)) try { Directory.Delete(dir, true); } catch { }
            return dir is not null && !Directory.Exists(folder);
        }
    }

    public static List<SafeItem> List(ModRegistry registry)
    {
        var root = Root(registry);
        if (!Directory.Exists(root)) return [];
        var result = new List<SafeItem>();
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            try
            {
                var meta = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, MetaFile)));
                if (meta.Str("id") is not { Length: > 0 } id || !Directory.Exists(Path.Combine(dir, FilesDir))) continue;
                result.Add(new SafeItem(dir, id, meta.Str("name") ?? id, meta.Str("version") ?? "",
                    DateTime.TryParse(meta.Str("at"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var at) ? at : Directory.GetCreationTimeUtc(dir),
                    meta.Str("reason") ?? "", meta.Long("bytes"), meta.Str("path") ?? "", meta?["record"] as JsonObject));
            }
            catch { }
        }
        return [.. result.OrderByDescending(i => i.At)];
    }

    /// <summary>Самая свежая копия мода нужной версии (пустая версия — любая).</summary>
    public static SafeItem? Find(ModRegistry registry, string id, string version) =>
        List(registry).FirstOrDefault(i => i.Id == id && (version == "" || Versions.Same(i.Version, version)));

    /// <summary>
    /// Вернуть мод из сейфа на место. То, что стоит сейчас (другая версия), само уходит в сейф — возврат тоже можно отменить.
    /// Вместе с модом возвращаются его части (патчеры BepInEx «мод#patchers»), убранные тем же делом.
    /// </summary>
    public static bool Restore(ModRegistry registry, SafeItem item)
    {
        var files = Path.Combine(item.Dir, FilesDir);
        if (!Directory.Exists(files)) return false;
        var record = item.Record?.DeepClone() as JsonObject;
        string target;
        if (record is not null)
        {
            record["enabled"] = true;
            record["missing"] = false;
            target = registry.FolderFor(record);
        }
        else target = Path.GetFullPath(Path.Combine(registry.GamePath, item.RelPath));
        // Только внутрь папки игры: путь из safe.json — данные с диска.
        var game = Path.GetFullPath(registry.GamePath).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(target).StartsWith(game, StringComparison.OrdinalIgnoreCase)) return false;

        if (registry.Get(item.Id) is { } current && registry.OwnFolder(current) is { } now) Remove(registry, now, current, "restore");
        if (Directory.Exists(target)) Remove(registry, target, null, "restore");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        Directory.Move(files, target);
        if (record is not null) registry.Add(record);
        try { Directory.Delete(item.Dir, true); } catch { }
        foreach (var part in List(registry).Where(p => p.Id.StartsWith(item.Id + "#", StringComparison.Ordinal) && Math.Abs((p.At - item.At).TotalMinutes) < 2))
            Restore(registry, part);
        return true;
    }

    /// <summary>Сколько всего лежит в сейфе игры (штук и байт).</summary>
    public static (int Count, long Bytes) Totals(ModRegistry registry)
    {
        var list = List(registry);
        return (list.Count, list.Sum(i => i.Bytes));
    }

    /// <summary>Очистить сейф игры. Возвращает освобождённые байты.</summary>
    public static long Clear(ModRegistry registry)
    {
        long freed = 0;
        foreach (var item in List(registry))
        {
            try { Directory.Delete(item.Dir, true); freed += item.Bytes; } catch { }
        }
        return freed;
    }

    /// <summary>Не больше трёх копий одного мода и не больше лимита на игру — старые уходят первыми.</summary>
    static void Prune(ModRegistry registry)
    {
        var list = List(registry);
        var drop = list.GroupBy(i => i.Id).SelectMany(g => g.Skip(KeepPerMod)).ToHashSet();
        long total = 0;
        foreach (var item in list.Where(i => !drop.Contains(i)))
        {
            total += item.Bytes;
            if (total > LimitBytes) drop.Add(item);
        }
        foreach (var item in drop) try { Directory.Delete(item.Dir, true); } catch { }
    }

    static long Size(string folder)
    {
        long sum = 0;
        foreach (var f in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)) try { sum += new FileInfo(f).Length; } catch { }
        return sum;
    }

    [GeneratedRegex(@"[^\w.-]+")] private static partial Regex NotSlug();
    static string Slug(string id) { var s = NotSlug().Replace(id, "_"); return s.Length > 60 ? s[..60] : s; }

    [SelfTest]
    static string ModSafeKeepsAndRestores()
    {
        var game = Path.Combine(Paths.DataDir, "safe-test", "Lethal Company");
        if (Directory.Exists(game)) Directory.Delete(game, true);
        var registry = new ModRegistry(Games.GameCatalog.ById("lethal-company")!, game);

        // v1 с правкой человека → обновление на v2 кладёт v1 в сейф.
        var folder = Directory.CreateDirectory(Path.Combine(registry.ModsDir, "MoreCompany")).FullName;
        File.WriteAllText(Path.Combine(folder, "MoreCompany.dll"), "v1");
        File.WriteAllText(Path.Combine(folder, "my-edit.cfg"), "mine");
        registry.Add(new JsonObject { ["id"] = "notnotnotswipez-MoreCompany", ["name"] = "MoreCompany", ["folder"] = "MoreCompany", ["version"] = "1.9.0", ["source"] = "thunderstore" });
        Remove(registry, folder, registry.Get("notnotnotswipez-MoreCompany"), "replace");
        if (Directory.Exists(folder)) throw new Exception("old folder left in place");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "MoreCompany.dll"), "v2");
        registry.Add(new JsonObject { ["id"] = "notnotnotswipez-MoreCompany", ["name"] = "MoreCompany", ["folder"] = "MoreCompany", ["version"] = "1.9.1", ["source"] = "thunderstore" });

        // Мод, поставленный руками (без записи), и удаление через список — тоже в сейф.
        var manual = Directory.CreateDirectory(Path.Combine(registry.ModsDir, "HandMade")).FullName;
        File.WriteAllText(Path.Combine(manual, "a.dll"), "x");
        Remove(registry, manual, null, "replace");
        var gone = Directory.CreateDirectory(Path.Combine(registry.ModsDir, "LethalLib")).FullName;
        File.WriteAllText(Path.Combine(gone, "LethalLib.dll"), "lib");
        registry.Add(new JsonObject { ["id"] = "Evaisa-LethalLib", ["name"] = "LethalLib", ["folder"] = "LethalLib", ["version"] = "0.16.1", ["source"] = "thunderstore" });
        registry.Remove("Evaisa-LethalLib");
        if (Totals(registry).Count != 3) throw new Exception("safe has " + Totals(registry).Count);

        // Возврат v1: файлы с правкой на месте, запись снова 1.9.0, а v2 — теперь в сейфе.
        if (Find(registry, "notnotnotswipez-MoreCompany", "1.9.0") is not { } v1 || !Restore(registry, v1)) throw new Exception("v1 not restored");
        if (File.ReadAllText(Path.Combine(folder, "MoreCompany.dll")) != "v1" || File.ReadAllText(Path.Combine(folder, "my-edit.cfg")) != "mine") throw new Exception("v1 files wrong");
        if (registry.Get("notnotnotswipez-MoreCompany").Str("version") != "1.9.0" || Find(registry, "notnotnotswipez-MoreCompany", "1.9.1") is null) throw new Exception("v2 not kept");
        if (Find(registry, "Evaisa-LethalLib", "0.16.1") is not { } lib || !Restore(registry, lib) || !registry.Has("Evaisa-LethalLib") || !File.Exists(Path.Combine(gone, "LethalLib.dll")))
            throw new Exception("removed mod not restored");

        // Не больше трёх копий одного мода.
        for (var i = 0; i < 5; i++)
        {
            var f = Directory.CreateDirectory(Path.Combine(registry.ModsDir, "Spam")).FullName;
            File.WriteAllText(Path.Combine(f, "s.dll"), i.ToString());
            Remove(registry, f, new JsonObject { ["id"] = "spam", ["version"] = "1." + i }, "replace");
            Thread.Sleep(2);
        }
        if (List(registry).Count(x => x.Id == "spam") != KeepPerMod || Find(registry, "spam", "")?.Version != "1.4") throw new Exception("prune");
        var freed = Clear(registry);
        if (Totals(registry).Count != 0 || freed <= 0) throw new Exception("clear");
        return "update, manual mod and removal kept; v1 restored with the user's edit, v2 kept; 3 copies per mod; clear";
    }
}
