using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Games;

namespace ModLaunch.Creator;

/// <summary>Что нашлось в папке (или .zip) мода: описание из манифеста и файлы.</summary>
public sealed class ImportedMod
{
    /// <summary>thunderstore, smapi или files (манифеста нет — просто файлы).</summary>
    public string Kind = "files";
    public string Name = "";
    public string Version = "1.0.0";
    public string Author = "";
    public string About = "";
    public string Website = "";
    /// <summary>Игра из манифеста (у SMAPI — всегда Stardew Valley), иначе null — решает человек.</summary>
    public string? Game;
    public readonly List<string> Needs = [];
    /// <summary>Файлы мода — пути внутри папки, через «/».</summary>
    public readonly List<string> Files = [];
    /// <summary>Пути, которые не взяли: выход за папку (../, абсолютные), ссылки.</summary>
    public readonly List<string> Skipped = [];
    /// <summary>Подсказки для человека — ключи строк (v12h2.imp.warn.*).</summary>
    public readonly List<string> Warnings = [];
    public long Bytes;
    /// <summary>Мод SMAPI с кодом (EntryDll): его manifest.json оставляем как есть.</summary>
    public bool KeepManifest;
    internal string Prefix = "";
}

/// <summary>
/// 12.0: импорт готового мода в проект Creator Hub. Манифест Thunderstore (name, version_number,
/// dependencies) или SMAPI (Name, Version, Dependencies) становится шапкой main.mls и строками needs,
/// файлы — копируются в files/ и получают строки copy. Пути проверяются: ничего не пишется за пределы проекта.
/// </summary>
public static class ProjectImport
{
    public const int MaxFiles = 2000;
    public const long MaxBytes = 512L * 1024 * 1024;
    /// <summary>Сколько строк copy писать в скрипт; остальные файлы лежат в files/, строки — вручную.</summary>
    public const int MaxCopyLines = 300;

    static readonly string[] Junk = [".git/", "__MACOSX/", ".DS_Store", "Thumbs.db", "desktop.ini"];
    static readonly JsonDocumentOptions Loose = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>
    /// Куда положить файл: полный путь внутри filesDir или null, если путь выходит за неё
    /// (../, абсолютный, с диском или «:»). Так же проверяются записи .zip («zip slip»).
    /// </summary>
    public static string? Target(string filesDir, string relative)
    {
        var rel = relative.Replace('\\', '/');
        if (rel == "" || rel.StartsWith('/') || rel.Contains(':') || rel.Contains('\0') || Path.IsPathRooted(rel)) return null;
        if (rel.Split('/').Any(part => part is "." or "..")) return null;
        var root = Path.GetFullPath(filesDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && full.Length > root.Length ? full : null;
    }

    /// <summary>Прочитать мод: папку или .zip. Ничего не копирует — для предпросмотра в мастере.</summary>
    public static ImportedMod Read(string path)
    {
        // Имя по умолчанию — имя папки или архива; манифест его уточнит.
        var trimmed = path.TrimEnd('/', '\\');
        var m = new ImportedMod { Name = Directory.Exists(path) ? Path.GetFileName(trimmed) : Path.GetFileNameWithoutExtension(trimmed) };
        Walk(path, m, null);
        return m;
    }

    /// <summary>Создать проект из мода: main.mls по манифесту, файлы — в files/. При ошибке проект убирается.</summary>
    public static Project Create(string path, string name, string game, string author)
    {
        var m = Read(path);
        if (Templates.Word(game) == "") game = m.Game ?? "";
        if (name.Trim() == "") name = m.Name.Trim() is { Length: > 0 } n ? n : Templates.DefaultName();
        if (author.Trim() == "") author = m.Author.Trim() is { Length: > 0 } a ? a : Templates.DefaultAuthor();
        var project = Projects.Create(name, Script(m, name, game, author));
        try { Walk(path, m, Path.Combine(project.Dir, "files")); }
        catch { Projects.Delete(project); throw; }
        return project;
    }

    /// <summary>Шапка и строки скрипта по прочитанному моду.</summary>
    public static string Script(ImportedMod m, string name, string game, string author)
    {
        var sb = new StringBuilder();
        void Line(string s = "") => sb.Append(s).Append('\n');
        Line("# " + Templates.Text(I18n.T("v12h2.imp.script.head", ("kind", KindTitle(m.Kind)))));
        Line($"mod \"{Templates.Text(name)}\"");
        Line($"version {(Regex.IsMatch(m.Version, @"^[\w.\-+]+$") ? m.Version : "1.0.0")}");
        Line($"author \"{Templates.Text(author)}\"");
        if (m.About.Trim() != "") Line($"about \"{Templates.Text(m.About)}\"");
        Line($"game {Templates.Word(game)}");
        if (m.Website.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) Line($"website \"{Templates.Text(m.Website)}\"");
        var icon = m.Kind == "thunderstore" && m.Files.Contains("icon.png");
        if (icon) Line("icon \"icon.png\"");
        if (m.Needs.Count > 0)
        {
            Line();
            // По четыре в строку — как в шаблонах сборок.
            foreach (var chunk in m.Needs.Chunk(4)) Line("needs " + string.Join(' ', chunk));
        }
        if (m.KeepManifest) { Line(); Line("# " + Templates.Text(I18n.T("v12h2.imp.script.manifest"))); }

        var copies = m.Files.Where(f => !Generated(m, f, icon)).ToList();
        if (copies.Count == 0) return sb.ToString();
        Line();
        Line("# " + Templates.Text(I18n.T("v12h2.imp.script.files")));
        var written = 0;
        foreach (var f in copies)
        {
            // «$» в строке ModScript — подстановка переменной: такой путь строкой copy не выразить.
            if (f.Contains('$') || written >= MaxCopyLines) continue;
            var q = f.Replace("\\", "\\\\").Replace("\"", "\\\"");
            Line($"copy \"{q}\" to \"{q}\"");
            written++;
        }
        if (written < copies.Count) Line("# " + Templates.Text(I18n.T("v12h2.imp.script.more", ("n", copies.Count - written))));
        return sb.ToString();
    }

    /// <summary>Файлы, которые сборка делает сама: манифест, README и значок Thunderstore, манифест SMAPI.</summary>
    static bool Generated(ImportedMod m, string f, bool icon) => m.Kind switch
    {
        "thunderstore" => f is "manifest.json" or "README.md" or "CHANGELOG.md" || icon && f == "icon.png",
        "smapi" => f == "manifest.json" && !m.KeepManifest,
        _ => false,
    };

    public static string KindTitle(string kind) => kind switch { "thunderstore" => "Thunderstore", "smapi" => "SMAPI", _ => I18n.T("v12h2.imp.kind.files") };

    // ---------------------------------------------------------------- обход папки и архива

    sealed record Entry(string Rel, long Size, Func<Stream> Open);

    /// <summary>Обойти мод: собрать описание и (если copyTo не null) скопировать файлы.</summary>
    static void Walk(string path, ImportedMod m, string? copyTo)
    {
        if (Directory.Exists(path))
        {
            var root = Path.GetFullPath(path);
            // Ссылки (symlink, junction) не идём: они могут вести за пределы папки мода.
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            var entries = new List<Entry>();
            foreach (var file in Directory.EnumerateFiles(root, "*", options))
            {
                if (entries.Count > MaxFiles) break;
                var info = new FileInfo(file);
                entries.Add(new Entry(Path.GetRelativePath(root, file).Replace('\\', '/'), info.Length, () => File.OpenRead(file)));
            }
            Use(entries, m, copyTo);
            return;
        }
        if (File.Exists(path) && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = ZipFile.OpenRead(path);
            Use(zip.Entries.Where(e => !e.FullName.EndsWith('/') && !e.FullName.EndsWith('\\'))
                .Select(e => new Entry(e.FullName.Replace('\\', '/'), e.Length, e.Open)).ToList(), m, copyTo);
            return;
        }
        throw new InvalidOperationException(I18n.T("v12h2.imp.err.notFound"));
    }

    static void Use(List<Entry> all, ImportedMod m, string? copyTo)
    {
        if (all.Count > MaxFiles) throw new InvalidOperationException(I18n.T("v12h2.imp.err.tooMany", ("n", MaxFiles)));
        // Проверка путей — до всего остального: «../» и абсолютные не берём вовсе.
        var probe = Path.Combine(Path.GetTempPath(), "modlaunch-import-probe");
        var safe = new List<Entry>();
        foreach (var e in all)
        {
            if (Junk.Any(j => j.EndsWith('/') ? e.Rel.StartsWith(j, StringComparison.OrdinalIgnoreCase) || e.Rel.Contains("/" + j, StringComparison.OrdinalIgnoreCase) : e.Rel.EndsWith(j, StringComparison.OrdinalIgnoreCase))) continue;
            if (Target(probe, e.Rel) is null) { if (copyTo is null) m.Skipped.Add(e.Rel); continue; }
            safe.Add(e);
        }
        if (copyTo is null) Describe(safe, m);
        var mine = safe.Where(e => e.Rel.StartsWith(m.Prefix, StringComparison.Ordinal) && e.Rel.Length > m.Prefix.Length).ToList();
        if (copyTo is null)
        {
            m.Bytes = mine.Sum(e => e.Size);
            if (m.Bytes > MaxBytes) throw new InvalidOperationException(I18n.T("v12h2.imp.err.tooBig", ("mb", MaxBytes / 1024 / 1024)));
            m.Files.AddRange(mine.Select(e => e.Rel[m.Prefix.Length..]).OrderBy(f => f, StringComparer.Ordinal));
            if (m.Files.Count == 0) throw new InvalidOperationException(I18n.T("v12h2.imp.err.empty"));
            if (m.Skipped.Count > 0) m.Warnings.Add("v12h2.imp.warn.skipped");
            if (m.Files.Count(f => !f.Contains('$')) > MaxCopyLines) m.Warnings.Add("v12h2.imp.warn.manyFiles");
            return;
        }
        Directory.CreateDirectory(copyTo);
        // Размер из архива может врать («zip-бомба»): считаем то, что реально распаковали.
        var total = 0L;
        var buffer = new byte[81920];
        foreach (var e in mine)
        {
            var target = Target(copyTo, e.Rel[m.Prefix.Length..]);
            if (target is null) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = e.Open();
            using var output = File.Create(target);
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
                if (total > MaxBytes) throw new InvalidOperationException(I18n.T("v12h2.imp.err.tooBig", ("mb", MaxBytes / 1024 / 1024)));
                output.Write(buffer, 0, read);
            }
        }
    }

    /// <summary>Найти манифест (в корне или в единственной папке внутри) и прочитать его.</summary>
    static void Describe(List<Entry> files, ImportedMod m)
    {
        var manifest = files.FirstOrDefault(e => e.Rel.Equals("manifest.json", StringComparison.OrdinalIgnoreCase));
        if (manifest is null)
        {
            // Архив SMAPI обычно — одна папка «[CP] Мод/manifest.json»: её содержимое и есть мод.
            // Без манифеста внутри папку не снимаем: «plugins/…» — это уже раскладка мода.
            var tops = files.Select(e => e.Rel.Split('/')[0]).Distinct(StringComparer.Ordinal).ToList();
            manifest = tops.Count == 1 && files.All(e => e.Rel.Contains('/'))
                ? files.FirstOrDefault(e => e.Rel.Equals(tops[0] + "/manifest.json", StringComparison.OrdinalIgnoreCase))
                : null;
            if (manifest is not null) { m.Prefix = tops[0] + "/"; m.Name = tops[0]; }
        }
        if (manifest is null) return;
        JsonObject? json;
        try
        {
            using var s = manifest.Open();
            using var reader = new StreamReader(s, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            json = JsonNode.Parse(reader.ReadToEnd(), new JsonNodeOptions { PropertyNameCaseInsensitive = true }, Loose) as JsonObject;
        }
        catch (Exception e) when (e is JsonException or IOException or InvalidDataException) { m.Warnings.Add("v12h2.imp.warn.badManifest"); return; }
        if (json is null) return;
        if (json.ContainsKey("version_number")) Thunderstore(json, m);
        else if (json.ContainsKey("UniqueID")) Smapi(json, m);
    }

    static string Str(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s.Trim() : "";

    /// <summary>Зависимость как слово ModScript (needs): без пробелов, кавычек и скобок.</summary>
    static bool NeedOk(string s) => Regex.IsMatch(s, @"^[A-Za-z0-9_.\-]+$");

    static void Thunderstore(JsonObject o, ImportedMod m)
    {
        m.Kind = "thunderstore";
        if (Str(o, "name") is { Length: > 0 } name) m.Name = name.Replace('_', ' ');
        if (Str(o, "version_number") is { Length: > 0 } v) m.Version = v;
        m.About = Str(o, "description");
        m.Website = Str(o, "website_url");
        foreach (var dep in (o["dependencies"] as JsonArray ?? []).Select(d => d is JsonValue jv && jv.TryGetValue<string>(out var s) ? s.Trim() : ""))
        {
            // BepInExPack сборка добавляет сама — своей свежей версии для выбранной игры.
            if (!NeedOk(dep) || dep.Contains("BepInExPack", StringComparison.OrdinalIgnoreCase)) continue;
            if (!m.Needs.Contains(dep)) m.Needs.Add(dep);
        }
    }

    static void Smapi(JsonObject o, ImportedMod m)
    {
        m.Kind = "smapi";
        m.Game = "stardew-valley";
        if (Str(o, "Name") is { Length: > 0 } name) m.Name = name;
        m.Author = Str(o, "Author");
        if (Str(o, "Version") is { Length: > 0 } v) m.Version = v;
        m.About = Str(o, "Description");
        foreach (var dep in (o["Dependencies"] as JsonArray ?? []).OfType<JsonObject>())
        {
            if (dep["IsRequired"] is JsonValue req && req.TryGetValue<bool>(out var required) && !required) continue;
            var id = Str(dep, "UniqueID");
            if (NeedOk(id) && !m.Needs.Contains(id)) m.Needs.Add(id);
        }
        // Пакет Content Patcher: свой content.json идёт в архив вместо собранного из команд.
        var cp = o["ContentPackFor"] is JsonObject pack && Str(pack, "UniqueID").Equals("Pathoschild.ContentPatcher", StringComparison.OrdinalIgnoreCase);
        if (cp) m.Warnings.Add("v12h2.imp.warn.content");
        if (Str(o, "EntryDll") != "") { m.KeepManifest = true; m.Warnings.Add("v12h2.imp.warn.entryDll"); }
    }

    // ---------------------------------------------------------------- проверки

    [SelfTest]
    static string ImportThunderstoreAndSmapi()
    {
        var temp = Path.Combine(Path.GetTempPath(), "modlaunch-import-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            // Thunderstore: манифест, значок, плагин; BepInExPack в needs не попадает.
            var ts = Directory.CreateDirectory(Path.Combine(temp, "ts")).FullName;
            File.WriteAllText(Path.Combine(ts, "manifest.json"), "\uFEFF{\"name\":\"More_Slots\",\"version_number\":\"1.2.3\",\"website_url\":\"https://example.com\",\"description\":\"Eight more slots\"," +
                "\"dependencies\":[\"BepInEx-BepInExPack-5.4.2100\",\"RandyKnapp-EquipmentAndQuickSlots-2.1.0\",\"bad dep\"]}");
            File.WriteAllText(Path.Combine(ts, "README.md"), "# readme");
            Directory.CreateDirectory(Path.Combine(ts, "plugins"));
            File.WriteAllText(Path.Combine(ts, "plugins", "MoreSlots.dll"), "dll");
            File.WriteAllText(Path.Combine(ts, "config.cfg"), "a=1");
            var p = Create(ts, "", "valheim", "Mira");
            var b = ModScript.Compile(File.ReadAllText(p.Script));
            if (!b.Ok) throw new Exception("ts: " + string.Join("; ", b.Diags.Select(d => d.Line + " " + d.Message)));
            if (b.Name != "More Slots" || b.Version != "1.2.3" || b.Game != "valheim" || b.Author != "Mira") throw new Exception($"ts head: {b.Name}|{b.Version}|{b.Game}|{b.Author}");
            if (b.Needs.Count != 1 || b.Needs[0] != "RandyKnapp-EquipmentAndQuickSlots-2.1.0") throw new Exception("ts needs: " + string.Join(",", b.Needs));
            if (b.Copies.Count != 2 || b.Copies.Any(c => c.From is "manifest.json" or "README.md")) throw new Exception("ts copies: " + string.Join(",", b.Copies.Select(c => c.From)));
            if (!File.Exists(Path.Combine(p.Dir, "files", "plugins", "MoreSlots.dll"))) throw new Exception("ts files not copied");
            Projects.Delete(p);

            // SMAPI в архиве: одна папка внутри, необязательная зависимость пропускается, игра — Stardew.
            var zip = Path.Combine(temp, "cp.zip");
            using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                void Add(string name, string text) { using var w = new StreamWriter(z.CreateEntry(name).Open()); w.Write(text); }
                Add("[CP] Cheap Seeds/manifest.json", "{ // SMAPI любит комментарии\n\"Name\": \"Cheap Seeds\", \"Author\": \"Abi\", \"Version\": \"2.0.0\", \"UniqueID\": \"Abi.CheapSeeds\"," +
                    "\"ContentPackFor\": {\"UniqueID\": \"Pathoschild.ContentPatcher\"}, \"Dependencies\": [{\"UniqueID\": \"spacechase0.JsonAssets\", \"IsRequired\": false}, {\"UniqueID\": \"Abi.Core\"}],}");
                Add("[CP] Cheap Seeds/content.json", "{\"Format\":\"2.0.0\",\"Changes\":[]}");
                Add("[CP] Cheap Seeds/assets/seeds.png", "png");
                // Записи «zip slip»: за пределы проекта не попадают.
                Add("[CP] Cheap Seeds/../../evil.txt", "evil");
                Add("/abs.txt", "evil");
            }
            var read = Read(zip);
            if (read.Kind != "smapi" || read.Game != "stardew-valley" || read.Skipped.Count != 2) throw new Exception($"smapi read: {read.Kind}|{read.Game}|{read.Skipped.Count}");
            var sp = Create(zip, "", "", "");
            var sb = ModScript.Compile(File.ReadAllText(sp.Script));
            if (!sb.Ok || sb.Name != "Cheap Seeds" || sb.Author != "Abi" || sb.Version != "2.0.0" || sb.Game != "stardew-valley") throw new Exception($"smapi head: {sb.Name}|{sb.Author}|{sb.Version}|{sb.Game}");
            if (sb.Needs.Count != 1 || sb.Needs[0] != "Abi.Core") throw new Exception("smapi needs: " + string.Join(",", sb.Needs));
            if (sb.Copies.Any(c => c.From == "manifest.json") || !sb.Copies.Any(c => c.From == "content.json")) throw new Exception("smapi copies");
            if (!File.Exists(Path.Combine(sp.Dir, "files", "assets", "seeds.png"))) throw new Exception("smapi files not copied");
            var escaped = Directory.EnumerateFiles(Projects.Root, "evil.txt", SearchOption.AllDirectories).Any() || File.Exists(Path.Combine(temp, "evil.txt"));
            Projects.Delete(sp);
            if (escaped) throw new Exception("zip slip wrote outside the project");
            return "Thunderstore and SMAPI imported, 2 unsafe paths rejected";
        }
        finally { try { Directory.Delete(temp, true); } catch { } }
    }

    [SelfTest]
    static string ImportRejectsTraversal()
    {
        var root = Path.Combine(Path.GetTempPath(), "modlaunch-files");
        string[] bad = ["../x.dll", "a/../../x.dll", "/etc/passwd", "C:/Windows/x.dll", "C:x.dll", "a/./b", "..", ""];
        if (bad.FirstOrDefault(r => Target(root, r) is not null) is { } leak) throw new Exception("accepted " + leak);
        if (Target(root, "plugins/My Mod/x.dll") is not { } ok || !ok.StartsWith(Path.GetFullPath(root), StringComparison.Ordinal)) throw new Exception("rejected a normal path");
        return $"{bad.Length} unsafe paths rejected";
    }
}
