using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace ModLaunch.Setup;

/// <summary>
/// Кто держит файл: Restart Manager из Windows называет программы, которые не дают файлу
/// переписаться. Нужен, чтобы вместо «файл занят» сказать, какая именно программа держит.
/// Любая ошибка здесь — просто «не знаю»: это подсказка, а не часть установки.
/// </summary>
public static class FileLocks
{
    public sealed record Holder(int Pid, string Name);

    [StructLayout(LayoutKind.Sequential)]
    struct UniqueProcess
    {
        public int Pid;
        public System.Runtime.InteropServices.ComTypes.FILETIME StartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ProcessInfo
    {
        public UniqueProcess Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string AppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string ServiceName;
        public int ApplicationType;
        public uint AppStatus;
        public uint SessionId;
        [MarshalAs(UnmanagedType.Bool)] public bool Restartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] static extern int RmStartSession(out uint handle, int flags, StringBuilder key);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] static extern int RmRegisterResources(uint handle, uint files, string[] names, uint apps, UniqueProcess[]? processes, uint services, string[]? serviceNames);
    [DllImport("rstrtmgr.dll")] static extern int RmGetList(uint handle, out uint needed, ref uint count, [In, Out] ProcessInfo[]? info, out uint reasons);
    [DllImport("rstrtmgr.dll")] static extern int RmEndSession(uint handle);

    /// <summary>Программы, которые сейчас держат любой из файлов (кроме нашего процесса).</summary>
    public static List<Holder> Who(params string[] files)
    {
        var result = new List<Holder>();
        if (!OperatingSystem.IsWindows()) return result;
        try { Query(files.Where(File.Exists).ToArray(), result); } catch { }
        return result.Where(h => h.Pid != Environment.ProcessId).DistinctBy(h => h.Pid).ToList();
    }

    [SupportedOSPlatform("windows")]
    static void Query(string[] files, List<Holder> result)
    {
        if (files.Length == 0) return;
        if (RmStartSession(out var session, 0, new StringBuilder(33)) != 0) return;
        try
        {
            if (RmRegisterResources(session, (uint)files.Length, files, 0, null, 0, null) != 0) return;
            uint needed = 0, count = 0;
            var rc = RmGetList(session, out needed, ref count, null, out _);
            if (rc != 234 && rc != 0) return; // 234 = ERROR_MORE_DATA: теперь известно, сколько мест выделить
            if (needed == 0) return;
            var info = new ProcessInfo[needed];
            count = needed;
            if (RmGetList(session, out _, ref count, info, out _) != 0) return;
            for (var i = 0; i < count; i++) result.Add(new Holder(info[i].Process.Pid, info[i].AppName));
        }
        finally { RmEndSession(session); }
    }

    /// <summary>Закрыть держащие файл копии ModLaunch и ModHub (чужие программы не трогаем).</summary>
    public static int CloseOurs(params string[] files)
    {
        var closed = 0;
        foreach (var h in Who(files))
        {
            try
            {
                using var p = Process.GetProcessById(h.Pid);
                if (!p.ProcessName.StartsWith("ModLaunch", StringComparison.OrdinalIgnoreCase) && !p.ProcessName.StartsWith("ModHub", StringComparison.OrdinalIgnoreCase)) continue;
                p.Kill(entireProcessTree: true);
                p.WaitForExit(4000);
                closed++;
            }
            catch { }
        }
        if (closed > 0) Thread.Sleep(900);
        return closed;
    }

    public static string Describe(IEnumerable<Holder> holders) => string.Join(", ", holders.Select(h => $"{h.Name} (PID {h.Pid})"));
}

/// <summary>Журнал установки в %TEMP%\ModLaunch-setup.log: если что-то пойдёт не так, по нему видно, на каком шаге и почему.</summary>
public static class SetupLog
{
    public static string Path => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ModLaunch-setup.log");

    public static void Write(string text)
    {
        try { File.AppendAllText(Path, $"{DateTime.Now:HH:mm:ss} {text}{Environment.NewLine}"); } catch { }
    }

    public static void Error(Exception e) => Write($"ERROR {e.GetType().Name} (0x{e.HResult:X8}): {e.Message}\n{e.StackTrace}");
}
