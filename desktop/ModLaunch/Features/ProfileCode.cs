using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Mods;
using ModLaunch.Sources;

namespace ModLaunch.Features;

/// <summary>Мод в коде сборки: Автор-Название, версия и включён ли.</summary>
public sealed record CodeMod(string Id, string Version, bool Enabled);

/// <summary>Сборка из кода или файла .r2z: название, моды и zip (в нём и настройки).</summary>
public sealed record CodeProfile(string Name, List<CodeMod> Mods, byte[] Zip)
{
    public int ConfigFiles => Count(Zip);

    static int Count(byte[] zip)
    {
        using var archive = new ZipArchive(new MemoryStream(zip));
        return archive.Entries.Count(e => e.FullName.StartsWith("config/", StringComparison.OrdinalIgnoreCase) && !e.FullName.EndsWith('/'));
    }
}

/// <summary>
/// Код сборки, как в r2modman и Gale: zip с export.r2x (список модов с точными версиями) и настройками
/// из BepInEx/config. Код получает Thunderstore — он работает в обе стороны: друг с r2modman вводит
/// наш код, мы — его. Для игр без Thunderstore остаётся файл .modhub.json.
/// </summary>
public static partial class ProfileCode
{
    const string Api = "https://thunderstore.io/api/experimental/legacyprofile/";
    const string Prefix = "#r2modman";

    [GeneratedRegex(@"^[A-Za-z0-9_]+-[A-Za-z0-9_]+$")]
    private static partial Regex PackageId();

    [GeneratedRegex(@"^[0-9a-fA-F-]{8,64}$")]
    private static partial Regex CodeShape();

    public static bool Supported(GameDef game) => game.ThunderstoreCommunity is not null;

    public static bool LooksLikeCode(string text) => CodeShape().IsMatch(text.Trim());

    /// <summary>Моды, которые попадут в код: только с Thunderstore (Автор-Название).</summary>
    public static (List<CodeMod> In, List<string> Left) Pick(ModRegistry registry)
    {
        var inCode = new List<CodeMod>();
        var left = new List<string>();
        foreach (var r in registry.List().Where(r => !r.Bool("missing") && r.Str("target") is null && r.Str("kind") != "preset"))
        {
            var id = r.Str("id")!;
            if (id.Contains('#')) continue; // вторая папка мода — едет вместе с ним
            if (PackageId().IsMatch(id) && r.Str("source") == "thunderstore" && r.Str("version") is { Length: > 0 } v) inCode.Add(new CodeMod(id, v, r.Bool("enabled", true)));
            else left.Add(r.Str("name") ?? id);
        }
        return (inCode, left);
    }

    // ---------------------------------------------------------------- export.r2x (YAML)

    public static string WriteYaml(string name, IEnumerable<CodeMod> mods)
    {
        var s = new StringBuilder();
        s.Append("profileName: '").Append(name.Replace("'", "''")).Append("'\n");
        s.Append("mods:\n");
        foreach (var m in mods)
        {
            var (major, minor, patch) = Numbers(m.Version);
            s.Append($"  - name: {m.Id}\n    version:\n      major: {major}\n      minor: {minor}\n      patch: {patch}\n    enabled: {(m.Enabled ? "true" : "false")}\n");
        }
        return s.ToString();
    }

    static (int, int, int) Numbers(string version)
    {
        var parts = version.TrimStart('v', 'V').Split('.', '-', '+');
        int At(int i) => i < parts.Length && int.TryParse(parts[i], out var n) ? n : 0;
        return (At(0), At(1), At(2));
    }

    /// <summary>
    /// Прочитать export.r2x или mods.yml профиля r2modman. Формат один и тот же по сути:
    /// список модов с name, version/versionNumber (major, minor, patch) и enabled. Разбираем по отступам.
    /// </summary>
    public static (string Name, List<CodeMod> Mods) ReadYaml(string text)
    {
        var name = "";
        var mods = new List<CodeMod>();
        int? itemIndent = null;
        string? id = null;
        int major = 0, minor = 0, patch = 0;
        var enabled = true;
        var inVersion = false;
        void Flush()
        {
            if (id is not null && PackageId().IsMatch(id)) mods.Add(new CodeMod(id, $"{major}.{minor}.{patch}", enabled));
            id = null; major = minor = patch = 0; enabled = true; inVersion = false;
        }
        static (string Key, string Value) Split(string line)
        {
            var at = line.IndexOf(':');
            return at < 0 ? (line.Trim(), "") : (line[..at].Trim(), line[(at + 1)..].Trim().Trim('\'', '"'));
        }
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            if (raw.Trim() == "" || raw.TrimStart().StartsWith('#')) continue;
            var indent = raw.Length - raw.TrimStart().Length;
            var line = raw.Trim();
            if (line.StartsWith("- ") && line.Contains(':') && (itemIndent is null || indent == itemIndent))
            {
                itemIndent ??= indent;
                Flush();
                line = line[2..];
                indent += 2;
            }
            else if (itemIndent is null)
            {
                var (k, v) = Split(line);
                if (k == "profileName") name = v.Replace("''", "'");
                continue;
            }
            if (indent <= itemIndent) continue;
            var (key, value) = Split(line);
            if (indent == itemIndent + 2)
            {
                inVersion = key is "version" or "versionNumber" && value == "";
                if (key == "name") id = value;
                else if (key == "enabled") enabled = value != "false";
            }
            else if (inVersion && int.TryParse(value, out var n))
            {
                if (key == "major") major = n;
                else if (key == "minor") minor = n;
                else if (key == "patch") patch = n;
            }
        }
        Flush();
        return (name, mods);
    }

    // ---------------------------------------------------------------- .r2z

    /// <summary>Собрать .r2z: export.r2x и (по желанию) настройки. Загрузчик (BepInExPack) — первым, как у r2modman.</summary>
    public static async Task<byte[]> Build(GameState g, string name, bool withConfig, CancellationToken ct = default)
    {
        var registry = g.Registry ?? throw new InvalidOperationException(I18n.T("err.gameNotFound"));
        var (mods, _) = Pick(registry);
        if (g.Def.ThunderstorePackage is { } pack && g.Def.ThunderstoreCommunity is { } community && !mods.Any(m => m.Id.Equals(pack, StringComparison.OrdinalIgnoreCase)))
        {
            string? version = null;
            if (Program.Demo) version = "5.4.2100";
            else try { version = (await Thunderstore.Latest(community, pack, ct)).Version; } catch { }
            if (version is not null) mods.Insert(0, new CodeMod(pack, version, true));
        }
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            using (var w = new StreamWriter(zip.CreateEntry("export.r2x").Open(), new UTF8Encoding(false)))
                w.Write(WriteYaml(name, mods));
            if (withConfig)
                foreach (var (path, entry) in ModSetup.ConfigFiles(registry).Where(f => f.Entry.StartsWith("config/")))
                    try { zip.CreateEntryFromFile(path, entry, CompressionLevel.Optimal); } catch { }
        }
        return buffer.ToArray();
    }

    public static CodeProfile Read(byte[] zipBytes)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(zipBytes));
            var entry = zip.Entries.FirstOrDefault(e => e.FullName.Equals("export.r2x", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException();
            using var reader = new StreamReader(entry.Open());
            var (name, mods) = ReadYaml(reader.ReadToEnd());
            return new CodeProfile(name == "" ? "Profile" : name, mods, zipBytes);
        }
        catch (Exception e) when (e is not InvalidDataException) { throw new InvalidDataException(I18n.T("code.bad")); }
        catch (InvalidDataException) { throw new InvalidDataException(I18n.T("code.bad")); }
    }

    // ---------------------------------------------------------------- код через Thunderstore

    /// <summary>Выложить сборку и получить код (как «Share profile» в r2modman).</summary>
    public static async Task<string> Upload(byte[] zip, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Api + "create/")
        {
            Content = new StringContent(Prefix + "\n" + Convert.ToBase64String(zip), Encoding.UTF8),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(40));
        using var response = await Http.Client.SendAsync(request, cts.Token);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase}");
        var key = System.Text.Json.Nodes.JsonNode.Parse(await response.Content.ReadAsStringAsync(cts.Token)).Str("key");
        return key ?? throw new HttpRequestException("no key");
    }

    /// <summary>Сборка по коду друга.</summary>
    public static async Task<CodeProfile> Fetch(string code, CancellationToken ct = default)
    {
        code = code.Trim();
        if (!LooksLikeCode(code)) throw new InvalidDataException(I18n.T("code.bad"));
        string text;
        try { text = await Http.GetString(Api + "get/" + code + "/", ct, 40); }
        catch (HttpRequestException e) when (e.Message.StartsWith("404")) { throw new InvalidDataException(I18n.T("code.notFound")); }
        text = text.Trim();
        if (!text.StartsWith(Prefix)) throw new InvalidDataException(I18n.T("code.bad"));
        byte[] zip;
        try { zip = Convert.FromBase64String(text[Prefix.Length..].Trim()); }
        catch { throw new InvalidDataException(I18n.T("code.bad")); }
        return Read(zip);
    }

    [SelfTest]
    static string R2zRoundTrip()
    {
        var mods = new List<CodeMod> { new("BepInEx-BepInExPack", "5.4.2100", true), new("Evaisa-LethalLib", "0.16.1", true), new("notnotnotswipez-MoreCompany", "1.10.1", false) };
        var (name, back) = ReadYaml(WriteYaml("Ночь с друзьями's", mods));
        if (name != "Ночь с друзьями's" || !back.SequenceEqual(mods)) throw new Exception("export.r2x round trip failed");
        // mods.yml из профиля r2modman: список в корне, есть вложенные списки зависимостей.
        var yml = "- manifestVersion: 1\n  name: Evaisa-LethalLib\n  authorName: Evaisa\n  dependencies:\n    - BepInEx-BepInExPack-5.4.2100\n  versionNumber:\n    major: 0\n    minor: 16\n    patch: 1\n  enabled: true\n" +
                  "- manifestVersion: 1\n  name: x753-More_Suits\n  versionNumber:\n    major: 1\n    minor: 4\n    patch: 3\n  enabled: false\n";
        var (_, fromYml) = ReadYaml(yml);
        if (fromYml.Count != 2 || fromYml[0] != new CodeMod("Evaisa-LethalLib", "0.16.1", true) || fromYml[1] != new CodeMod("x753-More_Suits", "1.4.3", false))
            throw new Exception("mods.yml not read: " + string.Join("; ", fromYml));
        if (!LooksLikeCode("018f2c3a-6b1e-4f1b-9c55-2d4e8a1f3b77") || LooksLikeCode("hello world")) throw new Exception("code shape check wrong");
        return "export.r2x and r2modman mods.yml read back exactly";
    }
}
