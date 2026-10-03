using System.Reflection;
using ModLaunch.Core;

namespace ModLaunch;

/// <summary>
/// Метка «снимки экрана функции»: метод <c>static void X(Shots s)</c> вызывается при --screenshot
/// после основных снимков (или один, с --only). Так каждая функция показывает себя сама
/// и не трогает общий список в Program.cs.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class DemoShotsAttribute : Attribute;

/// <summary>
/// Метка «быстрая проверка без сети»: метод <c>static string X()</c> возвращает, что проверено,
/// или бросает исключение, если что-то не так. Запускается в --selftest и в --selfcheck.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SelfTestAttribute : Attribute;

/// <summary>Что получает метод со снимками: окно, «сохранить снимок», «подождать отрисовку».</summary>
public sealed record Shots(Views.MainWindow Window, Action<string> Save, Action<int> Pump, string OutDir);

public static class Hooks
{
    /// <summary>Все методы с меткой <typeparamref name="T"/> — в постоянном порядке (по имени класса и метода).</summary>
    public static IEnumerable<MethodInfo> Marked<T>() where T : Attribute =>
        typeof(Hooks).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Where(m => m.IsDefined(typeof(T), false))
            .OrderBy(m => m.DeclaringType!.FullName, StringComparer.Ordinal)
            .ThenBy(m => m.Name, StringComparer.Ordinal);

    static string SelfTestRoot => Path.Combine(Path.GetTempPath(), "modlaunch-selftest-" + Environment.ProcessId);

    /// <summary>
    /// Папка данных проверок — временная. Вызывать до запуска интерфейса: он сразу читает
    /// настройки, и без этого проверки писали бы в настоящую папку программы.
    /// </summary>
    public static void PrepareSelfTest()
    {
        if (Directory.Exists(SelfTestRoot)) Directory.Delete(SelfTestRoot, true);
        Environment.SetEnvironmentVariable("MODLAUNCH_DATA", Path.Combine(SelfTestRoot, "data"));
    }

    /// <summary>--selftest: все быстрые проверки без сети. Код выхода 0 — всё хорошо.</summary>
    public static int SelfTest()
    {
        if (!Paths.DataDir.StartsWith(SelfTestRoot, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("FAIL data folder is not temporary: " + Paths.DataDir);
            return 1;
        }
        var failed = 0;
        foreach (var m in Marked<SelfTestAttribute>())
        {
            var name = $"{m.DeclaringType!.Name}.{m.Name}";
            try { Console.WriteLine($"ok   {name}: {m.Invoke(null, null)}"); }
            catch (Exception e)
            {
                failed++;
                var inner = e is TargetInvocationException { InnerException: { } ie } ? ie : e;
                Console.WriteLine($"FAIL {name}: {inner.GetType().Name}: {inner.Message}");
            }
        }
        Console.WriteLine(failed == 0 ? "ALL OK" : $"{failed} FAILED");
        return failed == 0 ? 0 : 1;
    }
}
