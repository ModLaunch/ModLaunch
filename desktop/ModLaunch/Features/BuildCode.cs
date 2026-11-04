using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Minecraft;
using ModLaunch.Social;

namespace ModLaunch.Features;

/// <summary>Сборка по коду: игра, название, моды (у Minecraft — .mrpack) и zip с настройками.</summary>
public sealed record SharedBuild(string Code, string Game, string Name, int Count, byte[] Zip)
{
    public bool IsMinecraft => Game == Mc.Id;

    /// <summary>Моды сборки (у Minecraft — пусто, там .mrpack).</summary>
    public List<SetupMod> Mods
    {
        get
        {
            using var zip = new ZipArchive(new MemoryStream(Zip));
            if (zip.GetEntry(BuildCode.Entry) is not { } e) return [];
            using var r = new StreamReader(e.Open());
            return ModSetup.FromJson(JsonNode.Parse(r.ReadToEnd())?["mods"] as JsonArray);
        }
    }

    public int ConfigFiles
    {
        get
        {
            using var zip = new ZipArchive(new MemoryStream(Zip));
            return zip.Entries.Count(e => e.FullName != BuildCode.Entry && !e.FullName.EndsWith('/') && !e.FullName.StartsWith("overrides/") && e.FullName != "modrinth.index.json");
        }
    }
}

/// <summary>
/// 23.9: «Поделиться сборкой» для любой игры. Код вида ML-7K3Q-9XZD: сборка (моды с версиями и источниками,
/// настройки, у Minecraft — .mrpack без миров) лежит в Firestore builds/{код}, прочитать её можно только
/// по точному коду. Друг вставляет код в ModLaunch и получает те же моды. Коды r2modman (Thunderstore)
/// по-прежнему принимаются.
/// </summary>
public static partial class BuildCode
{
    public const string Entry = "build.json";
    const string Format = "modlaunch-build";
    const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    /// <summary>Сколько можно положить в один документ Firestore (base64 — на треть больше).</summary>
    public const int MaxBytes = 640 * 1024;
    /// <summary>Где друг возьмёт ModLaunch: последний выпуск на GitHub.</summary>
    public const string Download = "https://github.com/ModLaunch/ModLaunch/releases/latest";

    [GeneratedRegex(@"(?<![A-Za-z0-9])ML[\s\-_]?([A-HJ-NP-Za-hj-np-z2-9]{4})[\s\-_]?([A-HJ-NP-Za-hj-np-z2-9]{4})(?![A-Za-z0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex Shape();

    /// <summary>Код из текста (можно вставить всё приглашение целиком): «ML-7K3Q-9XZD» → «7K3Q9XZD».</summary>
    public static string? Find(string? text) => text is not null && Shape().Match(text) is { Success: true } m ? (m.Groups[1].Value + m.Groups[2].Value).ToUpperInvariant() : null;

    public static string Show(string code) => code.Length == 8 ? $"ML-{code[..4]}-{code[4..]}" : code;

    static string NewCode() => RandomNumberGenerator.GetString(Alphabet, 8);

    /// <summary>Текст приглашения: код и где взять ModLaunch, если у друга его ещё нет.</summary>
    public static string Invite(string code, string game, string name) =>
        I18n.T("v23.code.invite", ("game", game), ("name", name), ("code", Show(code)), ("site", Download));

    // ---------------------------------------------------------------- состав

    /// <summary>Zip сборки: build.json с модами и настройки (если влезают). У Minecraft — .mrpack без миров и настроек игры.</summary>
    public static async Task<(byte[] Zip, int Count)> Pack(GameState g, string name, CancellationToken ct = default)
    {
        if (g.Def.IsMinecraft)
        {
            var i = Mc.Active ?? throw new InvalidOperationException(I18n.T("v23.code.noBuild"));
            var temp = Path.Combine(Paths.DataDir, "temp", $"code-{Guid.NewGuid():N}.mrpack");
            Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
            try
            {
                foreach (var config in new[] { true, false })
                {
                    await McExport.Export(i, new McExport.Options(name, "1.0", "", Config: config, Packs: config, Worlds: false, GameOptions: false, Servers: true), temp, null, ct);
                    var bytes = await File.ReadAllBytesAsync(temp, ct);
                    if (bytes.Length <= MaxBytes) return (bytes, McContent.Count(i, "mod"));
                }
                throw new InvalidOperationException(I18n.T("v23.code.tooBig"));
            }
            finally { try { File.Delete(temp); } catch { } }
        }
        var registry = g.Registry ?? throw new InvalidOperationException(I18n.T("err.gameNotFound"));
        var mods = ModSetup.Capture(registry).Where(m => !Mods.Installer.IsLoader(g.Def, m.Id)).ToList();
        if (mods.Count == 0) throw new InvalidOperationException(I18n.T("v23.code.empty"));
        foreach (var withConfig in new[] { true, false })
        {
            var zip = Zip(g, name, mods, withConfig ? ModSetup.ConfigFiles(registry) : []);
            if (zip.Length <= MaxBytes) return (zip, mods.Count);
        }
        throw new InvalidOperationException(I18n.T("v23.code.tooBig"));
    }

    static byte[] Zip(GameState g, string name, List<SetupMod> mods, List<(string Path, string Entry)> configs)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            var doc = new JsonObject { ["format"] = Format, ["version"] = 1, ["app"] = Http.Version, ["game"] = g.Def.Id, ["name"] = name, ["mods"] = ModSetup.ToJson(mods) };
            using (var w = new StreamWriter(zip.CreateEntry(Entry, CompressionLevel.Optimal).Open(), new UTF8Encoding(false)))
                w.Write(doc.ToJsonString());
            foreach (var (path, entry) in configs)
                try { zip.CreateEntryFromFile(path, entry, CompressionLevel.Optimal); } catch { }
        }
        return buffer.ToArray();
    }

    // ---------------------------------------------------------------- Firestore

    /// <summary>Выложить сборку и получить код. Совпадение кода с чужим — пробуем другой.</summary>
    public static async Task<string> Upload(GameState g, string name, CancellationToken ct = default)
    {
        var (zip, count) = await Pack(g, name, ct);
        if (Program.Demo || Program.Screenshot) return "7K3Q9XZD";
        var (uid, token) = await Account.Token();
        for (var attempt = 0; ; attempt++)
        {
            var code = NewCode();
            var write = new JsonObject
            {
                ["update"] = new JsonObject
                {
                    ["name"] = Firebase.Doc($"builds/{code}"),
                    ["fields"] = Firebase.ToFields(new Dictionary<string, object?>
                    {
                        ["uid"] = uid, ["game"] = g.Def.Id, ["name"] = Clip(name, 80), ["mods"] = (long)count,
                        ["data"] = Convert.ToBase64String(zip), ["app"] = Http.Version,
                    }),
                },
                ["updateTransforms"] = new JsonArray(new JsonObject { ["fieldPath"] = "created", ["setToServerValue"] = "REQUEST_TIME" }),
                ["currentDocument"] = new JsonObject { ["exists"] = false },
            };
            try { await Hub.Commit(new JsonArray(write), token); return code; }
            catch (ServiceError e) when (e.Code is "EXISTS" or "PRECONDITION" && attempt < 3) { }
        }
    }

    /// <summary>Сборка по коду друга.</summary>
    public static async Task<SharedBuild> Fetch(string code, CancellationToken ct = default)
    {
        code = Find(code) ?? (code.Length == 8 ? code.ToUpperInvariant() : throw new InvalidDataException(I18n.T("code.bad")));
        var (_, token) = await Account.Token();
        JsonObject doc;
        try { doc = await Hub.Call(Firebase.Url($"/builds/{code}"), HttpMethod.Get, null, token) as JsonObject ?? throw new InvalidDataException(I18n.T("code.notFound")); }
        catch (ServiceError e) when (e.Code == "NOT_FOUND") { throw new InvalidDataException(I18n.T("code.notFound")); }
        var f = Firebase.FromFields(doc["fields"] as JsonObject);
        byte[] zip;
        try { zip = Convert.FromBase64String(f.S("data")); }
        catch { throw new InvalidDataException(I18n.T("code.bad")); }
        return Read(code, f.S("game"), f.S("name"), (int)f.L("mods"), zip);
    }

    public static SharedBuild Read(string code, string game, string name, int count, byte[] zip)
    {
        if (zip.Length > MaxBytes * 2) throw new InvalidDataException(I18n.T("code.bad"));
        try
        {
            using var archive = new ZipArchive(new MemoryStream(zip));
            var ok = game == Mc.Id ? archive.GetEntry("modrinth.index.json") is not null : archive.GetEntry(Entry) is not null;
            if (!ok) throw new InvalidDataException(I18n.T("code.bad"));
        }
        catch (InvalidDataException) { throw new InvalidDataException(I18n.T("code.bad")); }
        return new SharedBuild(code, game, name == "" ? "Build" : name, count, zip);
    }

    static string Clip(string s, int max) => s.Length > max ? s[..max] : s;

    /// <summary>Коды, которые уже показывали или открывали: из буфера обмена их второй раз не предлагаем.</summary>
    public static bool Seen(string code) => Settings.Data.Arr("seenCodes").Any(n => n?.ToString() == code);

    public static void Remember(string code)
    {
        if (Seen(code)) return;
        var list = Settings.Data["seenCodes"] as JsonArray ?? [];
        list.Add((JsonNode)code);
        while (list.Count > 50) list.RemoveAt(0);
        Settings.Data["seenCodes"] = list;
        Settings.Save();
    }

    /// <summary>Сборка для снимков и демо — без сети.</summary>
    public static SharedBuild Demo(Games.GameDef def, string name, List<SetupMod> mods) =>
        Read("7K3Q9XZD", def.Id, name, mods.Count, Zip(new GameState { Def = def }, name, mods, []));

    // ---------------------------------------------------------------- самопроверка

    [SelfTest]
    static string CodesAreFoundInText()
    {
        if (Find("Заходи! Код сборки: ML-7K3Q-9XZD, вставь в ModLaunch") != "7K3Q9XZD") throw new Exception("code in invite text not found");
        if (Find("ml 7k3q 9xzd") != "7K3Q9XZD" || Find("ML7K3Q9XZD") != "7K3Q9XZD") throw new Exception("loose code not found");
        if (Find("HTML-ABCD-EFGH") is not null || Find("ML-ABCD-EFG") is not null || Find("ML-ABCD-EFG1") is not null) throw new Exception("not a code but found");
        if (Show("7K3Q9XZD") != "ML-7K3Q-9XZD") throw new Exception("show");
        if (!Regex.IsMatch(NewCode(), "^[A-HJ-NP-Z2-9]{8}$")) throw new Exception("new code does not match firestore.rules");
        return "ML-XXXX-XXXX found in invite text, codes match firestore.rules";
    }

    [SelfTest]
    static string BuildZipRoundTrip()
    {
        var def = Games.GameCatalog.ById("stardew-valley")!;
        var g = new GameState { Def = def, Path = Path.Combine(Paths.DataDir, "code-test", "Stardew Valley") };
        try
        {
            var mods = new List<SetupMod>
            {
                new("nexus:stardewvalley:1915", "Content Patcher", "2.4.4", "nexus", true),
                new("nexus:stardewvalley:1348", "SpaceCore", "1.27.0", "nexus", false),
            };
            var cfg = Path.Combine(Paths.DataDir, "code-test", "config.json");
            Directory.CreateDirectory(Path.GetDirectoryName(cfg)!);
            File.WriteAllText(cfg, "{\"Speed\": 2}");
            var zip = Zip(g, "Кооп с друзьями", mods, [(cfg, "Mods/ContentPatcher/config.json")]);
            var back = Read("7K3Q9XZD", def.Id, "Кооп с друзьями", 2, zip);
            if (!back.Mods.SequenceEqual(mods)) throw new Exception("mods changed: " + string.Join("; ", back.Mods));
            if (back.ConfigFiles != 1) throw new Exception("config files: " + back.ConfigFiles);
            try { Read("X", Mc.Id, "", 0, zip); throw new Exception("stardew zip accepted as minecraft"); } catch (InvalidDataException) { }
            return "Stardew build with 2 mods (one off) and a config survives the code";
        }
        finally { try { Directory.Delete(Path.Combine(Paths.DataDir, "code-test"), true); } catch { } }
    }
}
