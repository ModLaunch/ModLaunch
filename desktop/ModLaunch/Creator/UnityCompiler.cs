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
        if (refs.Count == 0) return "no game assemblies found (*_Data/Managed)";
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
        AddDir(Directory.Exists(gameDir) ? Directory.EnumerateDirectories(gameDir, "*_Data").Select(d => Path.Combine(d, "Managed")).FirstOrDefault(Directory.Exists) : null);
        if (list.Count == 0) return list;
        AddDir(Path.Combine(gameDir, "BepInEx", "core"));
        if (!names.Contains("Microsoft.CSharp") && Embedded() is { } csharp)
        {
            bundleCSharp = csharp;
            list.Add(new Ref("Microsoft.CSharp", MetadataReference.CreateFromImage(csharp)));
        }
        return list;
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
