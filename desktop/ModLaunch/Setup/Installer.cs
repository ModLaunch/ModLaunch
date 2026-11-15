using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using ModLaunch.Core;

namespace ModLaunch.Setup;

public enum SetupMode { None, Install, Update, Uninstall }

/// <summary>
/// Своя установка без отдельного установщика: ModLaunch-Setup.exe — это та же
/// программа. По имени файла (или --setup) она открывает окно установки,
/// с --update тихо обновляет установленную копию, с --uninstall — удаляет.
/// Папка, отметка и записи в реестре — те же, что у 3.x, поэтому новая версия
/// ставится поверх старой на Electron.
/// </summary>
public static class Installer
{
    public const string ExeName = "ModLaunch.exe";
    public const string Marker = ".modhub-install.json";
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ModHub";
    const string AppKey = @"Software\ModHub";

    public static SetupMode Detect(string[] args)
    {
        if (args.Contains("--uninstall")) return SetupMode.Uninstall;
        if (args.Contains("--update")) return SetupMode.Update;
        if (args.Contains("--setup") || args.Contains("--silent")) return SetupMode.Install;
        if (args.Contains("--portable")) return SetupMode.None;
        var name = Path.GetFileName(Environment.ProcessPath ?? "");
        return name.Contains("setup", StringComparison.OrdinalIgnoreCase) ? SetupMode.Install : SetupMode.None;
    }

    static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public static string DefaultDir => Path.Combine(Local, "Programs", "ModLaunch");
    static string NsisDir => Path.Combine(Local, "Programs", "modhub");

    /// <summary>Установлена ли эта копия (рядом с exe есть отметка установки).</summary>
    public static bool IsInstalledCopy => Environment.ProcessPath is string exe && File.Exists(Path.Combine(Path.GetDirectoryName(exe)!, Marker));

    public static string Normalize(string? dir)
    {
        var full = Path.GetFullPath(string.IsNullOrWhiteSpace(dir) ? DefaultDir : dir.Trim());
        var name = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar));
        return name.Equals("ModLaunch", StringComparison.OrdinalIgnoreCase) || name.Equals("ModHub", StringComparison.OrdinalIgnoreCase) ? full : Path.Combine(full, "ModLaunch");
    }

    public enum TargetKind { Missing, Empty, Ours, Foreign }

    public static (TargetKind Kind, string? Version) Inspect(string dir)
    {
        if (!Directory.Exists(dir)) return (TargetKind.Missing, null);
        var entries = Directory.EnumerateFileSystemEntries(dir).Select(Path.GetFileName).ToList();
        if (entries.Count == 0) return (TargetKind.Empty, null);
        if (entries.Contains(Marker))
        {
            try { return (TargetKind.Ours, JsonNode.Parse(File.ReadAllText(Path.Combine(dir, Marker))).Str("version")); } catch { return (TargetKind.Ours, null); }
        }
        if ((entries.Contains(ExeName) || entries.Contains("ModHub.exe")) && File.Exists(Path.Combine(dir, "resources", "app.asar"))) return (TargetKind.Ours, null);
        return (TargetKind.Foreign, null);
    }

    /// <summary>Куда ставить: туда, где уже стоит (из реестра или старая папка NSIS), иначе по умолчанию.</summary>
    public static string SuggestedDir()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                if (Registry.CurrentUser.OpenSubKey(AppKey)?.GetValue("InstallDir") is string known && !InTemp(known) && Inspect(known).Kind == TargetKind.Ours) return Normalize(known);
            }
            catch { }
        }
        return Normalize(Inspect(NsisDir).Kind == TargetKind.Ours ? NsisDir : DefaultDir);
    }

    /// <summary>Временная папка (например, от проверок) — ставить туда программу нельзя: Windows её чистит.</summary>
    static bool InTemp(string path) { try { return Inside(path, Path.GetTempPath()); } catch { return false; } }

    static bool Inside(string child, string parent)
    {
        var rel = Path.GetRelativePath(Path.GetFullPath(parent), Path.GetFullPath(child));
        return rel == "." || (!rel.StartsWith("..") && !Path.IsPathRooted(rel));
    }

    /// <summary>
    /// Закрыть запущенный ModLaunch (и старый ModHub) из этой папки. 30.0: только сам ModLaunch, без дочерних процессов —
    /// раньше закрывалось всё дерево, и игра, запущенная из ModLaunch, закрывалась посреди игры без сохранения.
    /// </summary>
    public static void CloseRunning(string dir)
    {
        var me = Environment.ProcessId;
        var closed = false;
        foreach (var name in new[] { "ModLaunch", "ModHub" })
        foreach (var p in Process.GetProcessesByName(name))
        {
            try
            {
                if (p.Id == me) continue;
                var path = p.MainModule?.FileName;
                if (path is null || !Inside(path, dir)) continue;
                p.Kill(entireProcessTree: false);
                p.WaitForExit(3000);
                closed = true;
            }
            catch { }
            finally { p.Dispose(); }
        }
        if (closed) Thread.Sleep(900);
    }

    public sealed record Result(string Target, string Exe, bool Updated, string? From);

    /// <summary>Журнал установки: лежит во временной папке, по нему можно понять, что пошло не так.</summary>
    public static string LogPath => Path.Combine(Path.GetTempPath(), "modlaunch-setup.log");

    public static void Log(string line)
    {
        try { File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {line}{Environment.NewLine}"); } catch { }
    }

    /// <summary>Хватит ли места: копия, запасная копия на время замены и немного про запас.</summary>
    public static void CheckSpace(string dir, long needBytes)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(dir));
            if (root is null) return;
            if (new DriveInfo(root).AvailableFreeSpace < needBytes * 2 + 50L * 1024 * 1024) throw new SetupError("SETUP_SPACE");
        }
        catch (SetupError) { throw; }
        catch { /* диск не опросился — пробуем так */ }
    }

    internal static bool SameFile(string a, string b)
    {
        using var x = File.OpenRead(a);
        using var y = File.OpenRead(b);
        if (x.Length != y.Length) return false;
        return System.Security.Cryptography.SHA256.HashData(x).AsSpan().SequenceEqual(System.Security.Cryptography.SHA256.HashData(y));
    }

    /// <summary>
    /// Установка «с запасным выходом»: новый exe сначала целиком ложится рядом со старым и проверяется,
    /// потом встаёт на его место. Если что-то сорвалось — старая версия возвращается, и у человека всегда
    /// остаётся рабочий ModLaunch.
    /// 27.0: раньше менялась вся папка (ModLaunch → ModLaunch.old). Это срывалось с «файлы заняты», если папку
    /// кто-то держал (открытый в ней терминал, проводник, редактор), а когда получалось — стирало вместе со
    /// старой версией всё, что человек положил рядом (резервные копии и т. п.). Теперь меняется только exe и отметка.
    /// </summary>
    public static async Task<Result> Install(string targetInput, bool desktop, IProgress<(string Step, double Ratio)> progress, bool autostart = false, string? sourceExe = null, bool integrate = true)
    {
        var dir = Normalize(targetInput);
        var source = sourceExe ?? Environment.ProcessPath ?? throw new InvalidOperationException("no exe");
        if (Inside(source, dir)) throw new SetupError("SETUP_TARGET_SELF");
        var (kind, version) = Inspect(dir);
        if (kind == TargetKind.Foreign) throw new SetupError("SETUP_TARGET_FOREIGN");
        Log($"install {Http.Version} to {dir} (was {kind} {version})");
        CheckSpace(dir, new FileInfo(source).Length);

        // Старая версия на Electron (3.x) — целая папка ненужных теперь файлов: её по-прежнему меняем целиком.
        var electron = kind == TargetKind.Ours && File.Exists(Path.Combine(dir, "resources", "app.asar"));
        try
        {
            progress.Report(("close", 0));
            await Task.Run(() => CloseRunning(dir));

            var swap = (electron ? await SwapFolder(dir, source, progress) : null) ?? await SwapExe(dir, source, progress);

            progress.Report(("integrate", 1));
            var exe = Path.Combine(dir, ExeName);
            try
            {
                if (integrate && OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("MODLAUNCH_SETUP_TEST") != "1") // проверки установщика не трогают реестр и ярлыки
                {
                    Register(dir, exe, (int)(new FileInfo(exe).Length / 1024));
                    Shortcuts(exe, dir, desktop);
                    if (autostart) Features.Autostart.Set(true, exe);
                }
            }
            catch
            {
                // Ярлыки или реестр не записались — возвращаем прежнюю версию.
                swap.Undo();
                throw;
            }
            await swap.Commit();
            Log("done");
            progress.Report(("done", 1));
            return new Result(dir, exe, kind == TargetKind.Ours, version);
        }
        catch (SetupError e) { Log("error " + e.Code); throw; }
        catch (IOException e) { Log("io error " + e.Message); throw new SetupError("SETUP_BUSY"); }
        catch (UnauthorizedAccessException e) { Log("access error " + e.Message); throw new SetupError("SETUP_ACCESS"); }
    }

    /// <summary>Как вернуть прежнюю версию, если дальше что-то сорвалось, и как убрать её, когда всё получилось.</summary>
    sealed record Swap(Action Undo, Func<Task> Commit);

    static void WriteMarker(string dir) =>
        File.WriteAllText(Path.Combine(dir, Marker), new JsonObject { ["version"] = Http.Version, ["installedAt"] = DateTime.UtcNow.ToString("o") }.ToJsonString());

    /// <summary>
    /// Замена на месте: новый exe ложится в ту же папку под временным именем и сверяется, старый переименовывается
    /// (это Windows разрешает, даже когда папку держит другая программа), новый встаёт на его место.
    /// Всё остальное в папке не трогаем.
    /// </summary>
    static async Task<Swap> SwapExe(string dir, string source, IProgress<(string Step, double Ratio)> progress)
    {
        Directory.CreateDirectory(dir);
        ClearLeftovers(dir);
        var exe = Path.Combine(dir, ExeName);
        var staged = $"{exe}.{Environment.ProcessId}.new";
        var old = $"{exe}.{Environment.ProcessId}.old";
        var marker = Path.Combine(dir, Marker);
        var oldMarker = File.Exists(marker) ? await File.ReadAllTextAsync(marker) : null;
        var hadOld = File.Exists(exe);
        try
        {
            progress.Report(("copy", 0));
            await CopyWithProgress(source, staged, r => progress.Report(("copy", r)));

            progress.Report(("verify", 1));
            if (!await Task.Run(() => SameFile(source, staged))) throw new SetupError("SETUP_VERIFY");

            progress.Report(("clean", 1));
            if (hadOld) await Retry(() => File.Move(exe, old));
            void Undo()
            {
                try
                {
                    if (hadOld) File.Move(old, exe, overwrite: true);
                    else File.Delete(exe);
                    if (oldMarker is null) File.Delete(marker);
                    else File.WriteAllText(marker, oldMarker);
                }
                catch (Exception e) { Log("rollback failed: " + e.Message); }
            }
            try
            {
                await Retry(() => File.Move(staged, exe));
                WriteMarker(dir);
            }
            catch { Undo(); throw; }
            return new Swap(Undo, async () =>
            {
                if (!hadOld) return;
                // 31.0: прежний exe — рядом как ModLaunch.exe.prev: если новая версия не запустится, UpdateGuard вернёт его.
                try { UpdateGuard.KeepPrevious(dir, old, (oldMarker is null ? null : JsonNode.Parse(oldMarker) as JsonObject).Str("version")); }
                catch (Exception e) { Log("prev not kept: " + e.Message); }
                try { await Retry(() => File.Delete(old)); } catch { Log("old exe left at " + old); }
            });
        }
        finally { try { File.Delete(staged); } catch { } }
    }

    /// <summary>Хвосты прошлых сорвавшихся установок (ModLaunch.exe.1234.new / .old). Занятые пропускаем.</summary>
    static void ClearLeftovers(string dir)
    {
        foreach (var file in Directory.EnumerateFiles(dir))
            if (System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(file), @"^ModLaunch\.exe\.(\d+\.(new|old)|[\d.]+\.bad)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                try { File.Delete(file); } catch { }
    }

    /// <summary>
    /// Старая версия на Electron: новая папка рядом, потом папки меняются местами, старая удаляется целиком.
    /// Папку держит другая программа — null: тогда просто ставим новый exe рядом со старыми файлами.
    /// </summary>
    static async Task<Swap?> SwapFolder(string dir, string source, IProgress<(string Step, double Ratio)> progress)
    {
        var staging = dir + ".new";
        var backup = dir + ".old";
        try
        {
            progress.Report(("copy", 0));
            if (Directory.Exists(staging)) await Retry(() => Directory.Delete(staging, true));
            Directory.CreateDirectory(staging);
            WriteMarker(staging);
            var stagedExe = Path.Combine(staging, ExeName);
            await CopyWithProgress(source, stagedExe, r => progress.Report(("copy", r)));

            progress.Report(("verify", 1));
            if (!await Task.Run(() => SameFile(source, stagedExe))) throw new SetupError("SETUP_VERIFY");

            progress.Report(("clean", 1));
            if (Directory.Exists(backup)) await Retry(() => Directory.Delete(backup, true));
            try { await Retry(() => Directory.Move(dir, backup)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log("old folder is busy, replacing the exe only: " + e.Message);
                return null;
            }
            try { await Retry(() => Directory.Move(staging, dir)); }
            catch { try { Directory.Move(backup, dir); } catch { } throw; }
            return new Swap(
                () => { try { Directory.Delete(dir, true); Directory.Move(backup, dir); } catch (Exception e) { Log("rollback failed: " + e.Message); } },
                async () => { try { await Retry(() => Directory.Delete(backup, true)); } catch { Log("old copy left at " + backup); } });
        }
        finally { try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { } }
    }

    /// <summary>Установка без окна, для скриптов и администраторов: --silent [--dir путь] [--no-desktop] [--no-launch] [--autostart]; с --uninstall — удаление (--wipe — вместе с данными).</summary>
    public static int RunSilent(string[] args)
    {
        string? Arg(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        try
        {
            if (args.Contains("--uninstall")) { Uninstall(args.Contains("--wipe")); return 0; }
            var result = Install(Arg("--dir") ?? SuggestedDir(), !args.Contains("--no-desktop"), new Progress<(string, double)>(), args.Contains("--autostart")).GetAwaiter().GetResult();
            if (!args.Contains("--no-launch")) Launch(result.Exe);
            return 0;
        }
        catch (Exception e) { Log("silent failed: " + e.Message); return 1; }
    }

    static async Task Retry(Action action)
    {
        for (var i = 0; ; i++)
        {
            try { action(); return; }
            catch (IOException) when (i < 8) { await Task.Delay(250); }
            catch (UnauthorizedAccessException) when (i < 8) { await Task.Delay(250); }
        }
    }

    static async Task CopyWithProgress(string from, string to, Action<double> report)
    {
        await using var input = File.OpenRead(from);
        await using var output = File.Create(to);
        var buffer = new byte[1 << 20];
        long done = 0;
        int read;
        while ((read = await input.ReadAsync(buffer)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read));
            done += read;
            report((double)done / input.Length);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    static void Register(string dir, string exe, int sizeKb)
    {
        using (var u = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            u.SetValue("DisplayName", "ModLaunch");
            u.SetValue("DisplayVersion", Http.Version);
            u.SetValue("Publisher", "ModLaunch");
            u.SetValue("DisplayIcon", $"{exe},0");
            u.SetValue("InstallLocation", dir);
            u.SetValue("UninstallString", $"\"{exe}\" --uninstall");
            u.SetValue("NoModify", 1, RegistryValueKind.DWord);
            u.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            u.SetValue("EstimatedSize", sizeKb, RegistryValueKind.DWord);
        }
        using (var m = Registry.CurrentUser.CreateSubKey(AppKey))
        {
            m.SetValue("InstallDir", dir);
            m.SetValue("Version", Http.Version);
        }
        using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\nxm"))
        {
            key.SetValue(null, "URL:Nexus Mods Protocol");
            key.SetValue("URL Protocol", "");
            using (var icon = key.CreateSubKey("DefaultIcon")) icon.SetValue(null, $"\"{exe}\",0");
            using var command = key.CreateSubKey(@"shell\open\command");
            command.SetValue(null, $"\"{exe}\" \"%1\"");
        }
    }

    static string StartLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "ModLaunch.lnk");
    static string DesktopLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "ModLaunch.lnk");

    static string Ps(string value) => "'" + value.Replace("'", "''") + "'";

    /// <summary>Ярлыки через WScript.Shell (PowerShell есть в любой Windows).</summary>
    static void Shortcuts(string exe, string dir, bool desktop)
    {
        var links = new List<string> { StartLink };
        if (desktop) links.Add(DesktopLink);
        else try { File.Delete(DesktopLink); } catch { }
        var script = "$w=New-Object -ComObject WScript.Shell\n" + string.Join("\n", links.Select(l =>
            $"$s=$w.CreateShortcut({Ps(l)});$s.TargetPath={Ps(exe)};$s.WorkingDirectory={Ps(dir)};$s.IconLocation={Ps(exe + ",0")};$s.Description='ModLaunch';$s.Save()"));
        Directory.CreateDirectory(Path.GetDirectoryName(StartLink)!);
        RunPowerShell(script);
        // Иконки в проводнике обновятся сразу, а не после перезагрузки.
        try { Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "ie4uinit.exe"), "-show") { CreateNoWindow = true, UseShellExecute = false }); } catch { }
    }

    static void RunPowerShell(string script)
    {
        var file = Path.Combine(Path.GetTempPath(), $"modlaunch-setup-{Environment.ProcessId}.ps1");
        File.WriteAllText(file, "﻿$ErrorActionPreference='Stop'\r\n" + script, System.Text.Encoding.UTF8);
        try
        {
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
            };
            foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", file }) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            var err = p.StandardError.ReadToEnd();
            p.WaitForExit(30000);
            if (p.ExitCode != 0) throw new SetupError("SETUP_PS", err.Split('\n')[0].Trim());
        }
        finally { try { File.Delete(file); } catch { } }
    }

    /// <summary>Удаление: ярлыки, реестр, а папку стираем после выхода (сами себя не удалить).</summary>
    public static void Uninstall(bool wipeData)
    {
        var exe = Environment.ProcessPath!;
        var dir = Path.GetDirectoryName(exe)!;
        CloseRunning(dir);
        foreach (var link in new[] { StartLink, DesktopLink }) try { File.Delete(link); } catch { }
        if (OperatingSystem.IsWindows())
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(AppKey, false); } catch { }
            // Ссылки nxm://, modlaunch:// и ror2mm://, если они наши: отдаём прежней программе или убираем,
            // иначе браузер звал бы уже удалённый ModLaunch.exe.
            foreach (var scheme in new[] { "nxm", "modlaunch", "ror2mm" })
                try { Features.Nxm.Unregister(scheme); } catch { }
        }
        var remove = new List<string>();
        if (File.Exists(Path.Combine(dir, Marker))) remove.Add(dir);
        if (wipeData && Path.GetFileName(Paths.DataDir).Equals("ModHub", StringComparison.OrdinalIgnoreCase)) remove.Add(Paths.DataDir);
        if (remove.Count == 0) return;
        var cmd = Environment.GetEnvironmentVariable("ComSpec") ?? Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var parts = string.Join(" & ", remove.Select(d => $"rmdir /s /q \"{d.Replace("\"", "")}\""));
        Process.Start(new ProcessStartInfo(cmd, $"/d /s /c \"ping 127.0.0.1 -n 4 >nul & {parts}\"") { CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Path.GetTempPath() });
    }

    public static void Launch(string exe)
    {
        try { Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = false }); } catch { }
    }
}

public sealed class SetupError(string code, string? detail = null) : Exception(detail ?? code)
{
    public string Code { get; } = code;
}

static class InstallerChecks
{
    [SelfTest]
    static string VerifyCatchesBadCopy()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "modlaunch-installer-test")).FullName;
        try
        {
            var a = Path.Combine(dir, "a.bin");
            var b = Path.Combine(dir, "b.bin");
            File.WriteAllBytes(a, [1, 2, 3, 4]);
            File.WriteAllBytes(b, [1, 2, 3, 4]);
            if (!Installer.SameFile(a, b)) throw new Exception("equal files reported different");
            File.WriteAllBytes(b, [1, 2, 3, 5]);
            if (Installer.SameFile(a, b)) throw new Exception("flipped byte not noticed");
            File.WriteAllBytes(b, [1, 2, 3]);
            if (Installer.SameFile(a, b)) throw new Exception("short copy not noticed");
            Installer.CheckSpace(dir, 1024); // место есть — не бросает
            return "identical copy passes; flipped byte and truncated copy are caught";
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    /// <summary>
    /// 27.0: обновление поверх, когда папку программы кто-то держит (в ней открыт терминал, файл, редактор),
    /// а рядом с exe лежат чужие файлы. Раньше установщик переносил всю папку и падал с «файлы заняты»,
    /// а если перенос удавался — стирал вместе со старой версией всё, что лежало рядом.
    /// </summary>
    [SelfTest]
    static string UpdateWithBusyFolderKeepsOtherFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "modlaunch-installer-busy");
        try { Directory.Delete(root, true); } catch { }
        var dir = Path.Combine(root, "ModLaunch");
        var v1 = Path.Combine(root, "v1.exe");
        var v2 = Path.Combine(root, "v2.exe");
        var quiet = new Progress<(string, double)>(_ => { });
        // Самотесты идут в потоке окна: ждать установку прямо в нём — взаимная блокировка, поэтому через пул потоков.
        static Installer.Result Run(Func<Task<Installer.Result>> install) => Task.Run(install).GetAwaiter().GetResult();
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllBytes(v1, [1, 2, 3]);
            File.WriteAllBytes(v2, [4, 5, 6, 7]);
            var first = Run(() => Installer.Install(dir, false, quiet, sourceExe: v1, integrate: false));
            if (first.Updated) throw new Exception("fresh install reported as update");

            var bak = Path.Combine(dir, "ModLaunch-25.0.0.exe.bak");
            var own = Path.Combine(dir, ".claude", "settings.json");
            File.WriteAllText(bak, "backup");
            Directory.CreateDirectory(Path.GetDirectoryName(own)!);
            File.WriteAllText(own, "{}");
            Installer.Result second;
            using (new FileStream(own, FileMode.Open, FileAccess.Read, FileShare.Read)) // папку держат — как терминал, открытый в ней
                second = Run(() => Installer.Install(dir, false, quiet, sourceExe: v2, integrate: false));

            if (!second.Updated) throw new Exception("second install should be an update");
            if (!File.ReadAllBytes(second.Exe).AsSpan().SequenceEqual(new byte[] { 4, 5, 6, 7 })) throw new Exception("exe not replaced");
            if (!File.Exists(bak) || !File.Exists(own)) throw new Exception("files next to the exe were lost");
            var left = Directory.EnumerateFileSystemEntries(root).Select(Path.GetFileName).Where(n => n is not ("ModLaunch" or "v1.exe" or "v2.exe"))
                .Concat(Directory.EnumerateFiles(dir).Select(Path.GetFileName).Where(n => n!.StartsWith(Installer.ExeName + ".", StringComparison.OrdinalIgnoreCase) && n != UpdateGuard.PrevName)).ToList();
            if (left.Count > 0) throw new Exception("leftovers: " + string.Join(", ", left));
            // 31.0: прежняя версия — рядом для отката, новая — «на испытании».
            if (!File.ReadAllBytes(Path.Combine(dir, UpdateGuard.PrevName)).AsSpan().SequenceEqual(new byte[] { 1, 2, 3 }))
                throw new Exception("previous exe not kept");
            if (JsonNode.Parse(File.ReadAllText(Path.Combine(dir, Installer.Marker))) is not JsonObject m || !m.Bool("trial")) throw new Exception("update not on trial");
            if (Installer.Inspect(dir) is not (Installer.TargetKind.Ours, var version) || version != Http.Version) throw new Exception("marker not updated");
            return "exe replaced in place while the folder is held open; backups and other files next to it kept, no leftovers";
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [SelfTest]
    static string NormalizeAddsFolder()
    {
        var p = Installer.Normalize(Path.Combine(Path.GetTempPath(), "Games"));
        if (!p.EndsWith("ModLaunch")) throw new Exception(p);
        if (Installer.Normalize(p) != p) throw new Exception("not idempotent");
        return "target folder always ends in ModLaunch, never doubled";
    }
}
