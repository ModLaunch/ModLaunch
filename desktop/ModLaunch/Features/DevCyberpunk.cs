using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using ModLaunch.Core;

namespace ModLaunch.Features;

/// <summary>
/// 23.2: режим разработчика для Cyberpunk 2077 (REDengine 4). Свой движок под Cyberpunk писать незачем: сообщество
/// уже сделало настоящий конструктор мира — Entity Spawner (keanuWheeze) на Cyber Engine Tweaks. ModLaunch ставит
/// всё нужное с официальных страниц GitHub (redscript, RED4ext, Cyber Engine Tweaks, Codeware, ArchiveXL, TweakXL,
/// Entity Spawner) — с докачкой и проверкой размера, распаковкой только внутрь папки игры — и кладёт своё окно
/// ModScript Studio для CET: плашку «ModScript Studio» в игре, время суток и погоду. Уже стоящее не переставляется.
/// </summary>
public static class DevCyberpunk
{
    public static bool Is(string? gamePath) => gamePath is not null && File.Exists(Path.Combine(gamePath, "bin", "x64", "Cyberpunk2077.exe"));

    /// <summary>Инструмент с GitHub: Tag — нужный выпуск (иначе последний), Into — куда распаковать относительно папки игры.</summary>
    public sealed record Tool(string Id, string Title, string Repo, Func<string, bool> Asset, string Marker, string? Tag = null, string Into = "");

    /// <summary>Что нужно конструктору, в порядке установки (сначала основы, потом сам конструктор).</summary>
    public static readonly Tool[] Tools =
    [
        new("redscript", "redscript", "jac3km4/redscript", n => n.StartsWith("redscript-v", StringComparison.Ordinal) && n.EndsWith("-windows.zip", StringComparison.Ordinal), Path.Combine("engine", "tools", "scc.exe")),
        new("red4ext", "RED4ext", "wopss/RED4ext", n => n.StartsWith("red4ext-", StringComparison.Ordinal) && !n.Contains("symbols") && n.EndsWith(".zip", StringComparison.Ordinal), Path.Combine("red4ext", "RED4ext.dll")),
        new("cet", "Cyber Engine Tweaks", "maximegmd/CyberEngineTweaks", n => n.StartsWith("cet_", StringComparison.Ordinal) && n.EndsWith(".zip", StringComparison.Ordinal), Path.Combine("bin", "x64", "plugins", "cyber_engine_tweaks.asi")),
        new("codeware", "Codeware", "psiberx/cp2077-codeware", n => n.StartsWith("Codeware-", StringComparison.Ordinal) && !n.Contains("pdb") && n.EndsWith(".zip", StringComparison.Ordinal), Path.Combine("red4ext", "plugins", "Codeware", "Codeware.dll")),
        new("archivexl", "ArchiveXL", "psiberx/cp2077-archive-xl", n => n.StartsWith("ArchiveXL-", StringComparison.Ordinal) && !n.Contains("pdb") && n.EndsWith(".zip", StringComparison.Ordinal), Path.Combine("red4ext", "plugins", "ArchiveXL", "ArchiveXL.dll")),
        new("tweakxl", "TweakXL", "psiberx/cp2077-tweak-xl", n => n.StartsWith("TweakXL-", StringComparison.Ordinal) && !n.Contains("pdb") && n.EndsWith(".zip", StringComparison.Ordinal), Path.Combine("red4ext", "plugins", "TweakXL", "TweakXL.dll")),
        new("spawner", "Entity Spawner", "justarandomguyintheinternet/CP77_entSpawner", n => n.StartsWith("worldBuilderPacked", StringComparison.Ordinal) && n.EndsWith(".zip", StringComparison.Ordinal), Path.Combine("bin", "x64", "plugins", "cyber_engine_tweaks", "mods", "entSpawner", "init.lua")),
    ];

    public static string ModDir(string gamePath) => Path.Combine(gamePath, "bin", "x64", "plugins", "cyber_engine_tweaks", "mods", "ModScriptStudio");
    public static string Flag(string gamePath) => Path.Combine(ModDir(gamePath), "dev.flag");
    public static string SpawnerData(string gamePath) => Path.Combine(gamePath, "bin", "x64", "plugins", "cyber_engine_tweaks", "mods", "entSpawner", "data", "objects");

    public static bool Installed(string gamePath, Tool t) => File.Exists(Path.Combine(gamePath, t.Into, t.Marker));

    static readonly HttpClient Client = CreateClient();

    static HttpClient CreateClient()
    {
        var c = new HttpClient(new Offline.Gate(new SocketsHttpHandler { AutomaticDecompression = System.Net.DecompressionMethods.All, ConnectTimeout = TimeSpan.FromSeconds(15) })) { Timeout = TimeSpan.FromMinutes(3) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd($"ModLaunch/{Http.Version}");
        return c;
    }

    /// <summary>Последний выпуск инструмента: ссылка на архив, имя, размер и версия.</summary>
    public static async Task<(string Url, string Name, long Size, string Version)> Latest(Tool t, CancellationToken ct)
    {
        var url0 = t.Tag is null ? $"https://api.github.com/repos/{t.Repo}/releases/latest" : $"https://api.github.com/repos/{t.Repo}/releases/tags/{t.Tag}";
        var json = await Http.GetJson(url0, ct, 30) as JsonObject ?? throw new InvalidOperationException("no release: " + t.Repo);
        var version = json.Str("tag_name") ?? "?";
        foreach (var a in json.Arr("assets").OfType<JsonObject>())
            if (a.Str("name") is { } name && t.Asset(name) && a.Str("browser_download_url") is { } url)
            {
                // 23.8: GitHub сообщает SHA-256 каждого файла выпуска — скачанное сверяется с ней (Download).
                if (a.Str("digest") is { } d && System.Text.RegularExpressions.Regex.IsMatch(d, "^sha256:[0-9a-fA-F]{64}$")) Digests[url] = d[7..].ToLowerInvariant();
                return (url, name, a.Long("size"), version);
            }
        throw new InvalidOperationException($"{t.Title} {version}: no archive in the release");
    }

    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> Digests = new();

    /// <summary>
    /// Скачать с докачкой и проверить: размер, а если GitHub сообщил SHA-256 — и её (в том числе у файла, уже лежащего
    /// в кэше). Не совпало — файл удаляется, и в игру ничего не ставится.
    /// </summary>
    public static async Task Download(string url, long size, string file, CancellationToken ct)
    {
        await DownloadRaw(url, size, file, ct);
        if (!Digests.TryGetValue(url, out var want)) return;
        string got;
        await using (var s = File.OpenRead(file)) got = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(s, ct)).ToLowerInvariant();
        if (got == want) return;
        File.Delete(file);
        throw new IOException($"checksum mismatch: {Path.GetFileName(file)}");
    }

    /// <summary>Скачать с докачкой (сервер GitHub часто рвёт связь посреди файла) и проверкой размера.</summary>
    static async Task DownloadRaw(string url, long size, string file, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var have = File.Exists(file) ? new FileInfo(file).Length : 0;
            if (size > 0 && have == size) return;
            if (size > 0 && have > size) { File.Delete(file); have = 0; }
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                if (have > 0) req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(have, null);
                using var res = await Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!res.IsSuccessStatusCode) throw new HttpRequestException($"{(int)res.StatusCode} {url}");
                var append = have > 0 && res.StatusCode == System.Net.HttpStatusCode.PartialContent;
                await using var input = await res.Content.ReadAsStreamAsync(ct);
                await using var output = new FileStream(file, append ? FileMode.Append : FileMode.Create, FileAccess.Write);
                await input.CopyToAsync(output, ct);
            }
            catch (Exception e) when (e is IOException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested) { await Task.Delay(800, ct); }
            if (size <= 0) return;
        }
        if (size > 0 && (!File.Exists(file) || new FileInfo(file).Length != size)) throw new IOException($"download incomplete: {Path.GetFileName(file)}");
    }

    /// <summary>Распаковать архив в папку игры: только внутрь неё, поверх старых файлов. Возвращает список файлов.</summary>
    public static List<string> Extract(string zip, string gamePath)
    {
        var root = Path.GetFullPath(gamePath).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        var files = new List<string>();
        using var archive = ZipFile.OpenRead(zip);
        foreach (var e in archive.Entries)
        {
            if (e.FullName.EndsWith('/') || e.FullName.EndsWith('\\')) continue;
            var target = Path.GetFullPath(Path.Combine(root, e.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("unsafe path in archive: " + e.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            e.ExtractToFile(target, overwrite: true);
            files.Add(e.FullName);
        }
        return files;
    }

    /// <summary>
    /// Поставить недостающее и своё окно, включить режим разработчика. progress — что делаем сейчас (для подсказки).
    /// null — готово, иначе — текст ошибки.
    /// </summary>
    public static async Task<string?> Prepare(string gamePath, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var temp = Directory.CreateDirectory(Path.Combine(Paths.CacheDir, "dev-cyberpunk")).FullName;
        foreach (var t in Tools)
        {
            if (Installed(gamePath, t)) continue;
            try
            {
                progress?.Report(t.Title);
                var (url, name, size, _) = await Latest(t, ct);
                var file = Path.Combine(temp, name);
                await Download(url, size, file, ct);
                Extract(file, Path.Combine(gamePath, t.Into));
                if (!Installed(gamePath, t)) return $"{t.Title}: {I18n.T("v23.dev.cp.badArchive")}";
            }
            catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException or InvalidOperationException or TaskCanceledException or UnauthorizedAccessException)
            {
                return $"{t.Title}: {e.Message}";
            }
        }
        WriteStudio(gamePath);
        File.WriteAllText(Flag(gamePath), DateTime.UtcNow.ToString("o"));
        return null;
    }

    /// <summary>Строки окна ModScript Studio в игре (на языке программы).</summary>
    static readonly string[] Labels = ["hint", "open", "title", "builder", "time", "weather", "weather.reset", "credit", "sunny", "rain", "fog", "clouds", "storm"];

    public static IReadOnlyList<string> LabelKeys => Labels;

    static string Lua(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n") + "\"";

    /// <summary>Своё окно для CET: init.lua (в программе) и labels.lua (строки на языке программы).</summary>
    public static void WriteStudio(string gamePath)
    {
        var dir = Directory.CreateDirectory(ModDir(gamePath)).FullName;
        using (var s = typeof(DevCyberpunk).Assembly.GetManifestResourceStream("studio-cyberpunk") ?? throw new InvalidOperationException("Cyberpunk studio is missing from the build"))
        using (var r = new StreamReader(s, Encoding.UTF8))
            File.WriteAllText(Path.Combine(dir, "init.lua"), r.ReadToEnd(), new UTF8Encoding(false));
        var lines = new StringBuilder("return {\n");
        foreach (var k in Labels) lines.Append("    [").Append(Lua(k)).Append("] = ").Append(Lua(I18n.T("v23.cp." + k))).Append(",\n");
        lines.Append("}\n");
        File.WriteAllText(Path.Combine(dir, "labels.lua"), lines.ToString(), new UTF8Encoding(false));
        CetFont(gamePath);
    }

    /// <summary>
    /// 23.3: у шрифта CET по умолчанию нет кириллицы и иероглифов — надписи на русском были бы «?». Если игрок не выбрал
    /// свой шрифт, ставим шрифт Windows и нужные буквы. Только в уже созданный CET файл настроек (свой он пишет сам при первом запуске).
    /// </summary>
    public static void CetFont(string gamePath)
    {
        if (I18n.Lang is not ("ru" or "uk" or "zh-CN")) return;
        var file = Path.Combine(gamePath, "bin", "x64", "plugins", "cyber_engine_tweaks", "config.json");
        if (!File.Exists(file) || !File.Exists(DevEngines.SystemFont())) return;
        try
        {
            if (System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file)) is not System.Text.Json.Nodes.JsonObject cfg) return;
            var font = cfg["font"] as System.Text.Json.Nodes.JsonObject ?? new System.Text.Json.Nodes.JsonObject();
            if (!string.IsNullOrEmpty((string?)font["path"])) return;
            font["path"] = DevEngines.SystemFont();
            font["glyph_ranges"] = I18n.Lang == "zh-CN" ? "ChineseFull" : "Cyrillic";
            cfg["font"] = font;
            File.WriteAllText(file, cfg.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or IOException or UnauthorizedAccessException or InvalidOperationException) { }
    }

    /// <summary>Сохранённые группы конструктора мира (Entity Spawner) — для «Карт из игр».</summary>
    public static List<(string Level, int Objects, DateTime At)> Worlds(string gamePath)
    {
        var list = new List<(string, int, DateTime)>();
        var dir = SpawnerData(gamePath);
        if (!Directory.Exists(dir)) return list;
        foreach (var f in Directory.EnumerateFiles(dir, "*.json"))
            try
            {
                var text = File.ReadAllText(f);
                var objects = System.Text.RegularExpressions.Regex.Matches(text, "\"spawnable\"\\s*:").Count;
                list.Add((Path.GetFileNameWithoutExtension(f), Math.Max(1, objects), File.GetLastWriteTimeUtc(f)));
            }
            catch (IOException) { }
        return list;
    }

    [SelfTest]
    static string CyberpunkStudioIsComplete()
    {
        using var s = typeof(DevCyberpunk).Assembly.GetManifestResourceStream("studio-cyberpunk") ?? throw new Exception("init.lua not embedded");
        var lua = new StreamReader(s).ReadToEnd();
        foreach (var k in Labels)
            if (!lua.Contains($"T(\"{k}\"") && !lua.Contains($"key = \"{k}\"")) throw new Exception("label not used in init.lua: " + k);
        foreach (var must in new[] { "registerForEvent(\"onDraw\"", "registerForEvent(\"onOverlayOpen\"", "dev.flag", "SetWeather", "SetGameTimeByHMS" })
            if (!lua.Contains(must)) throw new Exception("init.lua lacks " + must);
        // Блоки и их end сходятся (без строк и комментариев): файл цел и не обрезан.
        var code = System.Text.RegularExpressions.Regex.Replace(lua, "--[^\n]*", "");
        code = System.Text.RegularExpressions.Regex.Replace(code, "\"(\\\\.|[^\"\\\\])*\"", "\"\"");
        var opens = System.Text.RegularExpressions.Regex.Matches(code, @"\b(function|if|for|while)\b").Count;
        var ends = System.Text.RegularExpressions.Regex.Matches(code, @"\bend\b").Count;
        if (code.Count(c => c == '(') != code.Count(c => c == ')')) throw new Exception("init.lua brackets");
        if (opens != ends) throw new Exception($"init.lua blocks {opens} vs end {ends}");
        if (Tools.Select(t => t.Id).Distinct().Count() != Tools.Length) throw new Exception("tools repeat");
        // Игру находит обычный поиск ModLaunch: папка с bin/x64/Cyberpunk2077.exe (Steam, GOG, Epic или своя).
        var dir = Path.Combine(Paths.DataDir, "cp-probe", "Cyberpunk 2077");
        Directory.CreateDirectory(Path.Combine(dir, "bin", "x64"));
        File.WriteAllText(Path.Combine(dir, "bin", "x64", "Cyberpunk2077.exe"), "");
        try
        {
            var def = Games.GameCatalog.ById("cyberpunk-2077") ?? throw new Exception("Cyberpunk is not in the catalog");
            if (!def.MatchesSignature(dir) || !Is(dir) || DevMode.KindOf(dir) != "cyberpunk" || DevMode.Unsupported(dir) is not null) throw new Exception("Cyberpunk folder not recognised");
            if (def.LaunchExe(dir) != Path.Combine(dir, "bin", "x64", "Cyberpunk2077.exe")) throw new Exception("launch exe " + def.LaunchExe(dir));
        }
        finally { try { Directory.Delete(Path.Combine(Paths.DataDir, "cp-probe"), true); } catch (IOException) { } }
        return $"{Tools.Length} tools from GitHub, studio window with {Labels.Length} strings";
    }
}
