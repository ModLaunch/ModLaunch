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

    /// <summary>Закрыть запущенный ModLaunch (и старый ModHub) из этой папки.</summary>
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
                p.Kill(entireProcessTree: true);
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
    /// Установка «с запасным выходом»: новая копия сначала целиком ложится рядом и проверяется,
    /// потом папки меняются местами. Если что-то сорвалось — старая версия возвращается на место,
    /// и у человека всегда остаётся рабочий ModLaunch.
    /// </summary>
    public static async Task<Result> Install(string targetInput, bool desktop, IProgress<(string Step, double Ratio)> progress, bool autostart = false)
    {
        var dir = Normalize(targetInput);
        var source = Environment.ProcessPath ?? throw new InvalidOperationException("no exe");
        if (Inside(source, dir)) throw new SetupError("SETUP_TARGET_SELF");
        var (kind, version) = Inspect(dir);
        if (kind == TargetKind.Foreign) throw new SetupError("SETUP_TARGET_FOREIGN");
        Log($"install {Http.Version} to {dir} (was {kind} {version})");
        CheckSpace(dir, new FileInfo(source).Length);

        var staging = dir + ".new";
        var backup = dir + ".old";
        var swapped = false;
        try
        {
            progress.Report(("close", 0));
            await Task.Run(() => CloseRunning(dir));

            progress.Report(("copy", 0));
            if (Directory.Exists(staging)) await Retry(() => Directory.Delete(staging, true));
            Directory.CreateDirectory(staging);
            File.WriteAllText(Path.Combine(staging, Marker), new JsonObject { ["version"] = Http.Version, ["installedAt"] = DateTime.UtcNow.ToString("o") }.ToJsonString());
            var stagedExe = Path.Combine(staging, ExeName);
            await CopyWithProgress(source, stagedExe, r => progress.Report(("copy", r)));

            progress.Report(("verify", 1));
            if (!await Task.Run(() => SameFile(source, stagedExe))) throw new SetupError("SETUP_VERIFY");

            progress.Report(("clean", 1));
            if (Directory.Exists(backup)) await Retry(() => Directory.Delete(backup, true));
            var hadOld = Directory.Exists(dir);
            if (hadOld) await Retry(() => Directory.Move(dir, backup));
            try { await Retry(() => Directory.Move(staging, dir)); swapped = true; }
            catch { if (hadOld) try { Directory.Move(backup, dir); } catch { } throw; }

            progress.Report(("integrate", 1));
            var exe = Path.Combine(dir, ExeName);
            try
            {
                if (OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("MODLAUNCH_SETUP_TEST") != "1") // проверки установщика не трогают реестр и ярлыки
                {
                    Register(dir, exe, (int)(new FileInfo(exe).Length / 1024));
                    Shortcuts(exe, dir, desktop);
                    if (autostart) Features.Autostart.Set(true, exe);
                }
            }
            catch
            {
                // Ярлыки или реестр не записались — возвращаем прежнюю версию целиком.
                if (hadOld) { try { Directory.Delete(dir, true); Directory.Move(backup, dir); } catch { } }
                throw;
            }
            if (Directory.Exists(backup)) { try { await Retry(() => Directory.Delete(backup, true)); } catch { Log("old copy left at " + backup); } }
            Log("done");
            progress.Report(("done", 1));
            return new Result(dir, exe, kind == TargetKind.Ours, version);
        }
        catch (SetupError e) { Log("error " + e.Code); throw; }
        catch (IOException e) { Log("io error " + e.Message); throw new SetupError("SETUP_BUSY"); }
        catch (UnauthorizedAccessException e) { Log("access error " + e.Message); throw new SetupError("SETUP_ACCESS"); }
        finally
        {
            if (!swapped) try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
        }
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

    [SelfTest]
    static string NormalizeAddsFolder()
    {
        var p = Installer.Normalize(Path.Combine(Path.GetTempPath(), "Games"));
        if (!p.EndsWith("ModLaunch")) throw new Exception(p);
        if (Installer.Normalize(p) != p) throw new Exception("not idempotent");
        return "target folder always ends in ModLaunch, never doubled";
    }
}
