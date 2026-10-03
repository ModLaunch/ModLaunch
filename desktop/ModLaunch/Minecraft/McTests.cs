using System.IO.Compression;
using System.Text.Json.Nodes;
using ModLaunch.Core;

namespace ModLaunch.Minecraft;

/// <summary>Проверки Minecraft (--selftest): версии загрузчиков, логи, .mrpack, установки лаунчера, файлы сборки.</summary>
static class McTests
{
    static void Expect(bool ok, string what) { if (!ok) throw new Exception(what); }

    [SelfTest]
    static string NeoForgeVersionsMapToMinecraft()
    {
        foreach (var (neo, mc) in new[]
        {
            ("21.1.77", "1.21.1"), ("20.4.12", "1.20.4"), ("21.0.5", "1.21"), ("20.6.119", "1.20.6"),
            ("26.1.0.5-beta", "26.1"), ("26.1.2.3", "26.1.2"), ("26.2.0.7", "26.2"),
        })
            Expect(McMeta.NeoGameVersion(neo) == mc, $"{neo} → {McMeta.NeoGameVersion(neo)}, want {mc}");
        return "NeoForge → Minecraft: старая и новая схема номеров";
    }

    [SelfTest]
    static string ReadsLoaderFromLogs()
    {
        var fabric = McImport.Parse("[12:00:01] [main/INFO]: Loading Minecraft 1.21.1 with Fabric Loader 0.17.2\n");
        Expect(fabric == ("1.21.1", "fabric", "0.17.2"), "fabric log");
        var quilt = McImport.Parse("[main/INFO]: Loading Minecraft 1.20.1 with Quilt Loader 0.26.4");
        Expect(quilt == ("1.20.1", "quilt", "0.26.4"), "quilt log");
        var neo = McImport.Parse("ModLauncher running: args [--launchTarget, forgeclient, --fml.neoForgeVersion, 21.1.77, --fml.fmlVersion, 4.0.24, --fml.mcVersion, 1.21.1, --fml.neoFormVersion, 20240808.144430]");
        Expect(neo == ("1.21.1", "neoforge", "21.1.77"), "neoforge log");
        var forge = McImport.Parse("args [--fml.forgeVersion, 47.3.0, --fml.mcVersion, 1.20.1, --fml.forgeGroup, net.minecraftforge]");
        Expect(forge == ("1.20.1", "forge", "47.3.0"), "forge log");
        Expect(McImport.Parse("nothing here") is null, "empty log");
        Expect(McImport.ParseCurseLoader("neoforge-21.1.77") == ("neoforge", "21.1.77"), "curse neoforge");
        Expect(McImport.ParseCurseLoader("fabric-0.15.7-1.20.1") == ("fabric", "0.15.7"), "curse fabric");
        Expect(McImport.ParseCurseLoader("vanilla") == ("vanilla", ""), "curse vanilla");
        return "Версия и загрузчик из логов Fabric, Quilt, Forge, NeoForge и CurseForge";
    }

    [SelfTest]
    static string ReadsMrpackIndex()
    {
        var file = Path.Combine(Paths.DataDir, "mc-test.mrpack");
        Directory.CreateDirectory(Paths.DataDir);
        using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
        {
            var e = zip.CreateEntry("modrinth.index.json");
            using var w = new StreamWriter(e.Open());
            w.Write(new JsonObject
            {
                ["formatVersion"] = 1, ["game"] = "minecraft", ["name"] = "Test Pack", ["versionId"] = "1.0",
                ["dependencies"] = new JsonObject { ["minecraft"] = "1.21.1", ["fabric-loader"] = "0.17.2" },
                ["files"] = new JsonArray(
                    new JsonObject
                    {
                        ["path"] = "mods/sodium.jar", ["hashes"] = new JsonObject { ["sha1"] = "abc" },
                        ["downloads"] = new JsonArray(JsonValue.Create("https://cdn.modrinth.com/data/x/sodium.jar")), ["fileSize"] = 100,
                        ["env"] = new JsonObject { ["client"] = "required", ["server"] = "unsupported" },
                    },
                    new JsonObject
                    {
                        ["path"] = "mods/server-only.jar", ["downloads"] = new JsonArray(JsonValue.Create("https://cdn.modrinth.com/data/y/s.jar")),
                        ["env"] = new JsonObject { ["client"] = "unsupported", ["server"] = "required" },
                    }),
            }.ToJsonString());
        }
        try
        {
            var index = McModrinth.ReadIndex(file);
            Expect(index.Name == "Test Pack" && index.GameVersion == "1.21.1", "name/version");
            Expect(index.Loader == "fabric" && index.LoaderVersion == "0.17.2", "loader");
            Expect(index.Files.Count == 2 && index.Files[0].Client && !index.Files[1].Client, "client files");
            Expect(index.Files[0].Sha1 == "abc" && index.Files[0].Urls.Length == 1, "hash/urls");
        }
        finally { File.Delete(file); }
        return "Модпак .mrpack: версия, загрузчик, файлы только для игры";
    }

    [SelfTest]
    static string LauncherProfileKeepsOthers()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Paths.DataDir, "mc-profiles-test")).FullName;
        var file = Path.Combine(dir, "launcher_profiles.json");
        File.WriteAllText(file, """{"profiles":{"abc":{"name":"Latest release","type":"latest-release"}},"settings":{"crashAssistance":true},"version":3}""");
        var i = new McInstance { Id = "test-build", Name = "Тест", GameVersion = "1.21.1", Loader = "fabric", LoaderVersion = "0.17.2", Dir = Path.Combine(dir, "inst"), MemoryMb = 6144 };
        try
        {
            McProfiles.Write(file, i, played: true);
            var root = JsonNode.Parse(File.ReadAllText(file))!;
            Expect(root["profiles"]?["abc"]?.Str("name") == "Latest release", "other profile lost");
            Expect(root["settings"]?.Bool("crashAssistance") == true, "settings lost");
            var mine = root["profiles"]?["modlaunch-test-build"];
            Expect(mine is not null, "no profile");
            Expect(mine.Str("lastVersionId") == "fabric-loader-0.17.2-1.21.1", "version id " + mine.Str("lastVersionId"));
            Expect(mine.Str("gameDir") == i.Dir && mine.Str("type") == "custom", "dir/type");
            Expect(mine.Str("javaArgs")?.StartsWith("-Xmx6144M") == true, "memory");
            Expect(mine.Str("name") == "ModLaunch · Тест", "name");
            // Второй раз — та же установка, не копия.
            McProfiles.Write(file, i, played: false);
            Expect((JsonNode.Parse(File.ReadAllText(file))!["profiles"] as JsonObject)!.Count == 2, "duplicate profile");
        }
        finally { Directory.Delete(dir, true); }
        return "Установка в лаунчере: своя запись, чужие и настройки на месте";
    }

    [SelfTest]
    static string VersionIdsMatchLauncher()
    {
        McInstance I(string loader, string gv, string lv) => new() { Id = "x", GameVersion = gv, Loader = loader, LoaderVersion = lv };
        Expect(I("fabric", "1.21.1", "0.17.2").VersionId == "fabric-loader-0.17.2-1.21.1", "fabric");
        Expect(I("quilt", "1.20.1", "0.26.4").VersionId == "quilt-loader-0.26.4-1.20.1", "quilt");
        Expect(I("forge", "1.20.1", "47.3.0").VersionId == "1.20.1-forge-47.3.0", "forge");
        Expect(I("neoforge", "1.21.1", "21.1.77").VersionId == "neoforge-21.1.77", "neoforge");
        Expect(I("vanilla", "26.2", "").VersionId == "26.2", "vanilla");
        Expect(I("vanilla", "26.2", "").Label == "Minecraft 26.2" && I("neoforge", "26.2", "26.2.0.7").Label == "NeoForge 26.2", "labels");
        Expect(Mc.NewId("Выживание с друзьями").StartsWith("vyzhivanie-s-druzyami"), "slug " + Mc.NewId("Выживание с друзьями"));
        Expect(Mc.NewId("!!!") .StartsWith("build"), "empty slug");
        return "Номера версий как у установщиков Fabric, Quilt, Forge, NeoForge; имена папок сборок";
    }

    [SelfTest]
    static string BuildContentToggles()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Paths.DataDir, "mc-content-test")).FullName;
        var i = new McInstance { Id = "content-test", Name = "T", GameVersion = "1.21.1", Loader = "neoforge", LoaderVersion = "21.1.77", Dir = dir };
        var mods = Directory.CreateDirectory(i.Folder("mod")).FullName;
        var jar = Path.Combine(mods, "create-1.21.1-6.0.jar");
        using (var zip = ZipFile.Open(jar, ZipArchiveMode.Create))
        {
            using var w = new StreamWriter(zip.CreateEntry("META-INF/neoforge.mods.toml").Open());
            w.Write("modLoader=\"javafml\"\n[[mods]]\nmodId=\"create\"\nversion=\"6.0.4\"\ndisplayName=\"Create\"\ndescription='''\nGears and contraptions.\n'''\n[[dependencies.create]]\nmodId=\"neoforge\"\nversionRange=\"[21,)\"\n");
        }
        File.WriteAllText(Path.Combine(mods, "notes.txt"), "not a mod");
        Directory.CreateDirectory(i.Folder("resourcepack"));
        File.WriteAllBytes(Path.Combine(i.Folder("resourcepack"), "Faithful.zip"), []);
        try
        {
            var list = McContent.List(i, "mod");
            Expect(list.Count == 1, "count " + list.Count);
            var create = list[0];
            Expect(create.Name == "Create" && create.Version == "6.0.4" && create.Description == "Gears and contraptions.", $"jar meta: {create.Name} {create.Version} {create.Description}");
            McContent.SetEnabled(i, create, false);
            Expect(File.Exists(jar + ".disabled") && !File.Exists(jar), "disable");
            var off = McContent.List(i, "mod")[0];
            Expect(!off.Enabled && off.Key == "create-1.21.1-6.0.jar" && off.Name == "Create", "disabled item");
            McContent.SetEnabled(i, off, true);
            Expect(File.Exists(jar), "enable");
            Expect(McContent.Count(i, "resourcepack") == 1 && McContent.Count(i, "shader") == 0, "kinds");
            McContent.Delete(i, McContent.List(i, "mod")[0]);
            Expect(!File.Exists(jar) && McContent.Count(i, "mod") == 0, "delete");
        }
        finally
        {
            Directory.Delete(dir, true);
            try { File.Delete(McContent.MetaPath(i)); } catch { }
        }
        return "Файлы сборки: описание из .jar NeoForge, выключение (.disabled), удаление";
    }

    [SelfTest]
    static string JavaMatchesGameVersion()
    {
        foreach (var (gv, java) in new[] { ("26.2", 25), ("26.1.1", 25), ("1.21.1", 21), ("1.20.5", 21), ("1.20.4", 17), ("1.18.2", 17), ("1.16.5", 8), ("1.12.2", 8), ("26.3-snapshot-4", 25) })
            Expect(McInstall.JavaFor(gv) == java, $"{gv} → Java {McInstall.JavaFor(gv)}, want {java}");
        var home = Directory.CreateDirectory(Path.Combine(Paths.DataDir, "java-test", "bin")).Parent!.FullName;
        try
        {
            File.WriteAllText(Path.Combine(home, "release"), "IMPLEMENTOR=\"Eclipse Adoptium\"\nJAVA_VERSION=\"21.0.7\"\n");
            Expect(McInstall.JavaMajor(Path.Combine(home, "bin", "java.exe")) == 21, "release 21");
            File.WriteAllText(Path.Combine(home, "release"), "JAVA_VERSION=\"1.8.0_402\"");
            Expect(McInstall.JavaMajor(Path.Combine(home, "bin", "java.exe")) == 8, "release 8");
        }
        finally { Directory.Delete(home, true); }
        return "Java под версию игры: 8, 17, 21, 25";
    }
}
