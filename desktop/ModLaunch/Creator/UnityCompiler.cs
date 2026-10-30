using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ModLaunch.Core;

namespace ModLaunch.Creator;

/// <summary>
/// 15.0: сборка C#-мода ModScript X для Unity прямо в ModLaunch (Roslyn). Раньше инжект в Unity-игру требовал
/// .NET SDK у игрока (dotnet build) — у большинства его нет, и инжект молча не ставился. Ссылки — сборки самой
/// игры (*_Data/Managed) и BepInEx/core; Microsoft.CSharp (для dynamic) кладётся рядом с плагином, если в игре его нет.
/// </summary>
public static class UnityCompiler
{
    public static void Register() => ModScriptX.Target.CompileCSharp = Compile;

    /// <summary>Собрать исходник в outDir/assembly.dll. null — получилось, иначе — ошибки компилятора (до 20 строк).</summary>
    public static string? Compile(string source, string assembly, string outDir, string gameDir)
    {
        var refs = References(gameDir, out var bundleCSharp);
        if (refs.Count == 0) return "no game assemblies found (*_Data/Managed or ScriptHook .NET)";
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest), path: assembly + ".cs");
        var compilation = CSharpCompilation.Create(assembly, [tree], refs.Select(r => r.Reference),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release, allowUnsafe: true,
                nullableContextOptions: NullableContextOptions.Disable, concurrentBuild: true));
        using var dll = new MemoryStream();
        var result = compilation.Emit(dll);
        if (!result.Success)
            return string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(20).Select(d =>
            {
                var at = d.Location.GetLineSpan();
                return $"{assembly}.cs({at.StartLinePosition.Line + 1},{at.StartLinePosition.Character + 1}): {d.Id}: {d.GetMessage()}";
            }));
        Directory.CreateDirectory(outDir);
        File.WriteAllBytes(Path.Combine(outDir, assembly + ".dll"), dll.ToArray());
        if (bundleCSharp is not null) File.WriteAllBytes(Path.Combine(outDir, "Microsoft.CSharp.dll"), bundleCSharp);
        return null;
    }

    sealed record Ref(string Name, MetadataReference Reference);

    static List<Ref> References(string gameDir, out byte[]? bundleCSharp)
    {
        bundleCSharp = null;
        var list = new List<Ref>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddDir(string? dir)
        {
            if (dir is null || !Directory.Exists(dir)) return;
            foreach (var file in Directory.EnumerateFiles(dir, "*.dll"))
                if (names.Add(Path.GetFileNameWithoutExtension(file)) && IsManaged(file))
                    try { list.Add(new Ref(Path.GetFileNameWithoutExtension(file), MetadataReference.CreateFromFile(file))); } catch { }
        }
        void AddFile(string file)
        {
            if (File.Exists(file) && names.Add(Path.GetFileNameWithoutExtension(file)) && IsManaged(file))
                try { list.Add(new Ref(Path.GetFileNameWithoutExtension(file), MetadataReference.CreateFromFile(file))); } catch { }
        }
        AddDir(Directory.Exists(gameDir) ? Directory.EnumerateDirectories(gameDir, "*_Data").Select(d => Path.Combine(d, "Managed")).FirstOrDefault(Directory.Exists) : null);
        if (list.Count == 0 && ScriptHooks(gameDir) is { Count: > 0 } hooks)
        {
            // 22.0: GTA V и RDR2 — скрипты ScriptHook .NET живут в .NET Framework 4.8 из Windows; на Linux (проверки) — свой рантайм.
            var fw = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319");
            if (Directory.Exists(fw))
                foreach (var n in new[] { "mscorlib", "System", "System.Core", "Microsoft.CSharp", "System.Drawing", "System.Windows.Forms", "System.Xml" }) AddFile(Path.Combine(fw, n + ".dll"));
            else AddDir(Path.GetDirectoryName(typeof(object).Assembly.Location));
            foreach (var h in hooks) AddFile(h);
            return list;
        }
        if (list.Count == 0) return list;
        AddDir(Path.Combine(gameDir, "BepInEx", "core"));
        if (!names.Contains("Microsoft.CSharp") && Embedded() is { } csharp)
        {
            bundleCSharp = csharp;
            list.Add(new Ref("Microsoft.CSharp", MetadataReference.CreateFromImage(csharp)));
        }
        return list;
    }

    /// <summary>
    /// 22.0: dll ScriptHook .NET в корне игры (ScriptHookVDotNet3.dll, ScriptHookRDR2DotNet.dll). Если рядом лежат версии 2 и 3
    /// для GTA V — только 3: у обеих пространство имён GTA, вместе компилятор не различит Game и Script.
    /// </summary>
    static List<string> ScriptHooks(string gameDir)
    {
        if (!Directory.Exists(gameDir)) return [];
        var all = Directory.EnumerateFiles(gameDir, "ScriptHook*DotNet*.dll").Where(IsManaged).ToList();
        var v3 = all.Where(f => Path.GetFileNameWithoutExtension(f).EndsWith("DotNet3", StringComparison.OrdinalIgnoreCase)).ToList();
        return v3.Count > 0 ? v3 : all;
    }

    /// <summary>Нативные dll (в Managed их не бывает, в BepInEx/core — бывают) компилятору не нужны.</summary>
    static bool IsManaged(string file)
    {
        try
        {
            using var s = File.OpenRead(file);
            using var pe = new PEReader(s);
            return pe.HasMetadata;
        }
        catch { return false; }
    }

    static byte[]? Embedded()
    {
        using var s = typeof(UnityCompiler).Assembly.GetManifestResourceStream("modscript-Microsoft.CSharp.dll");
        if (s is null) return null;
        using var m = new MemoryStream();
        s.CopyTo(m);
        return m.ToArray();
    }

    /// <summary>
    /// Весь путь инжекта в Unity без .NET SDK: заглушки UnityEngine и BepInEx, пример «Arena» (dynamic, функции,
    /// переменные игры) → движок узнан, C# собран в plugins/ModScript_&lt;id&gt;/, eject снимает всё.
    /// </summary>
    [SelfTest]
    static string UnityInjectEndToEnd()
    {
        var core = typeof(object).Assembly.Location;
        if (core == "") return "skipped: single-file build has no assembly paths";
        var game = Path.Combine(Path.GetTempPath(), "modlaunch-unity-" + Environment.ProcessId);
        try
        {
            var managed = Directory.CreateDirectory(Path.Combine(game, "Probe_Data", "Managed")).FullName;
            foreach (var f in Directory.EnumerateFiles(Path.GetDirectoryName(core)!, "*.dll"))
                if (IsManaged(f)) File.Copy(f, Path.Combine(managed, Path.GetFileName(f)));
            File.WriteAllText(Path.Combine(game, "UnityPlayer.dll"), "");
            Directory.CreateDirectory(Path.Combine(game, "BepInEx", "core"));
            var unity = Stub("UnityEngine.CoreModule", """
                namespace UnityEngine { public class Object {} public class Component : Object {} public class Behaviour : Component {} public class MonoBehaviour : Behaviour {} }
                namespace UnityEngine.Events { public delegate void UnityAction<T0, T1>(T0 a, T1 b); }
                namespace UnityEngine.SceneManagement
                {
                    public struct Scene {}
                    public enum LoadSceneMode { Single, Additive }
                    public static class SceneManager { public static event UnityEngine.Events.UnityAction<Scene, LoadSceneMode> sceneLoaded { add {} remove {} } }
                }
                """, managed, []);
            Stub("BepInEx", """
                namespace BepInEx.Logging { public class ManualLogSource { public void LogMessage(object o) {} public void LogInfo(object o) {} } }
                namespace BepInEx
                {
                    [System.AttributeUsage(System.AttributeTargets.Class)] public class BepInPlugin : System.Attribute { public BepInPlugin(string guid, string name, string version) {} }
                    public abstract class BaseUnityPlugin : UnityEngine.MonoBehaviour { protected BepInEx.Logging.ManualLogSource Logger { get; } = new BepInEx.Logging.ManualLogSource(); }
                }
                """, Path.Combine(game, "BepInEx", "core"), [unity]);
            var arena = Views.CreatorPage.ArenaExample();
            var r = ModScriptX.Injector.Inject(arena, game, null);
            if (r.Target?.Id != "unity") throw new Exception("engine: " + r.Target?.Id + " " + string.Join("; ", r.Doc.Diags.Select(d => d.Message)));
            if (!Features.Inject.Built(game, r.Doc.Id)) throw new Exception("not built: " + r.Note);
            ModScriptX.Injector.Eject(r.Doc.Id, game);
            if (Directory.Exists(Path.Combine(game, "BepInEx", "plugins", "ModScript_" + r.Doc.Id))) throw new Exception("eject left the plugin");
            return $"{r.Doc.Name}: C# built in-process into the plugin folder, eject removes it";
        }
        finally { try { Directory.Delete(game, true); } catch { } }
    }

    static string Stub(string name, string code, string dir, string[] extra)
    {
        var refs = Directory.EnumerateFiles(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "*.dll").Where(IsManaged).Concat(extra)
            .Select(f => (MetadataReference)MetadataReference.CreateFromFile(f));
        var c = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(code)], refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var path = Path.Combine(dir, name + ".dll");
        var emit = c.Emit(path);
        if (!emit.Success) throw new Exception("stub " + name + ": " + string.Join("; ", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return path;
    }

    /// <summary>
    /// 22.0: GTA V и RDR2 по-настоящему: заглушки ScriptHookVDotNet3 и ScriptHookRDR2DotNet с тем же API (Game.Player, Script.Tick,
    /// GTA.UI.Notification, RDR2.UI.Screen) → мост узнаёт игру, мод на простых словах собирается в scripts/ModScript_&lt;id&gt;.dll,
    /// загруженная dll на первом кадре меняет деньги, здоровье и розыск игрока и показывает текст; eject снимает всё.
    /// </summary>
    [SelfTest]
    static string RageInjectRunsInGame()
    {
        var core = typeof(object).Assembly.Location;
        if (core == "") return "skipped: single-file build has no assembly paths";
        var root = Path.Combine(Path.GetTempPath(), "modlaunch-rage-" + Environment.ProcessId);
        const string mod = "мод \"Богач\"\nкогда игра началась {\n    дать деньги 500\n    забрать здоровье 50\n    поставить розыск = 0\n    сказать \"Деньги: {деньги}\"\n}\n";
        try
        {
            var results = new List<string>();
            foreach (var (ns, exe, hook, stubName, sayType, sayMethod) in new[]
            {
                ("GTA", "GTA5.exe", "ScriptHookV.dll", "ScriptHookVDotNet3", "GTA.UI.Notification", "public static int Show(string message, bool blinking = false) { Last = message; return 0; }"),
                ("RDR2", "RDR2.exe", "ScriptHookRDR2.dll", "ScriptHookRDR2DotNet", "RDR2.UI.Screen", "public static void DisplaySubtitle(string text) { Last = text; }"),
            })
            {
                var game = Directory.CreateDirectory(Path.Combine(root, ns)).FullName;
                File.WriteAllText(Path.Combine(game, exe), "");
                File.WriteAllText(Path.Combine(game, hook), "");
                var uiNs = sayType[..sayType.LastIndexOf('.')];
                var uiClass = sayType[(sayType.LastIndexOf('.') + 1)..];
                var stub = Stub(stubName, $$"""
                    namespace {{ns}}
                    {
                        public abstract class Script { public event System.EventHandler Tick; public void Fire() { if (Tick != null) Tick(this, System.EventArgs.Empty); } }
                        public class Ped { public int Health { get; set; } = 200; public int MaxHealth { get; set; } = 200; public int Armor { get; set; } }
                        public class Player { public int Money { get; set; } = 1000; public int WantedLevel { get; set; } = 3; public Ped Character { get; } = new Ped(); }
                        public static class Game { public static Player Player { get; } = new Player(); }
                    }
                    namespace {{uiNs}} { public static class {{uiClass}} { public static string Last; {{sayMethod}} } }
                    """, game, []);
                var bridge = ModScriptX.Bridge.Scan(game);
                if (!bridge.Code || !bridge.LoaderReady) throw new Exception($"{ns} bridge: {bridge.Engine} {bridge.Path} loader {bridge.LoaderReady}");
                var r = ModScriptX.Injector.Inject(mod, game, null);
                if (r.Target is null || !Features.Inject.Built(game, r.Doc.Id, r.Target)) throw new Exception($"{ns} not built: {r.Target?.Id} {r.Note} {string.Join("; ", r.Doc.Diags.Select(d => d.Message))}");

                // Как ScriptHook .NET: загрузить dll, создать скрипт, дать кадр.
                var alc = new System.Runtime.Loader.AssemblyLoadContext("rage-" + ns, isCollectible: true);
                try
                {
                    var api = alc.LoadFromStream(new MemoryStream(File.ReadAllBytes(stub)));
                    alc.Resolving += (_, name) => name.Name == stubName ? api : null;
                    var dll = alc.LoadFromStream(new MemoryStream(File.ReadAllBytes(Path.Combine(game, r.Target.Dll(r.Doc.Id)!))));
                    var script = Activator.CreateInstance(dll.GetType("ModScript_" + r.Doc.Id)!)!;
                    script.GetType().GetMethod("Fire")!.Invoke(script, null);
                    var player = api.GetType(ns + ".Game")!.GetProperty("Player")!.GetValue(null)!;
                    var money = player.GetType().GetProperty("Money")!.GetValue(player);
                    var health = player.GetType().GetProperty("Character")!.GetValue(player)!.GetType().GetProperty("Health")!.GetValue(player.GetType().GetProperty("Character")!.GetValue(player));
                    var wanted = player.GetType().GetProperty("WantedLevel")!.GetValue(player);
                    var said = api.GetType(sayType)!.GetField("Last")!.GetValue(null);
                    // RDR2: розыск не привязан (у порта нет такого свойства) — остаётся числом мода, игрок не меняется.
                    var wantWanted = ns == "GTA" ? 0 : 3;
                    if ((int)money! != 1500 || (int)health! != 150 || (int)wanted! != wantWanted || (string?)said != "Деньги: 1500")
                        throw new Exception($"{ns} in game: money {money}, health {health}, wanted {wanted}, said '{said}'");
                }
                finally { alc.Unload(); }

                ModScriptX.Injector.Eject(r.Doc.Id, game);
                if (File.Exists(Path.Combine(game, r.Target.Dll(r.Doc.Id)!)) || Directory.Exists(Path.Combine(game, "ModScriptX"))) throw new Exception(ns + ": eject left files");
                results.Add(r.Target.Id);
            }
            return string.Join(" and ", results) + ": plain words built into scripts/*.dll, money, health and wanted change in game, eject cleans up";
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    /// <summary>Сборка без SDK: «игра» с базовой библиотекой .NET вместо Unity — dll появляется, ошибка — с номером строки.</summary>
    [SelfTest]
    static string CompilesWithoutSdk()
    {
        var core = typeof(object).Assembly.Location;
        if (core == "") return "skipped: single-file build has no assembly paths";
        var game = Path.Combine(Path.GetTempPath(), "modlaunch-roslyn-" + Environment.ProcessId);
        try
        {
            var managed = Directory.CreateDirectory(Path.Combine(game, "Probe_Data", "Managed")).FullName;
            foreach (var name in new[] { "System.Private.CoreLib.dll", "System.Runtime.dll", "netstandard.dll" })
                if (Path.Combine(Path.GetDirectoryName(core)!, name) is var f && File.Exists(f)) File.Copy(f, Path.Combine(managed, name));
            var outDir = Path.Combine(game, "BepInEx", "plugins", "ModScript_Probe");
            var ok = Compile("public class ModScript_Probe { public static int Gold = 5; public int Add(int x) { Gold += x; return Gold; } }", "ModScript_Probe", outDir, game);
            if (ok is not null) throw new Exception("compile: " + ok);
            if (!File.Exists(Path.Combine(outDir, "ModScript_Probe.dll"))) throw new Exception("dll missing");
            var bad = Compile("public class B { void F() { int x = \"text\"; } }", "B", outDir, game);
            if (bad is null || !bad.Contains("B.cs(1,") || !bad.Contains("CS0029")) throw new Exception("error text: " + bad);
            if (Compile("class C {}", "C", outDir, Path.Combine(game, "nowhere")) is not { } none || !none.Contains("Managed")) throw new Exception("no game");
            return "plugin dll built in-process, errors carry line numbers";
        }
        finally { try { Directory.Delete(game, true); } catch { } }
    }
}
