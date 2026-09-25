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
        if (args.Contains("--setup")) return SetupMode.Install;
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
                if (Registry.CurrentUser.OpenSubKey(AppKey)?.GetValue("InstallDir") is string known && Inspect(known).Kind == TargetKind.Ours) return Normalize(known);
            }
            catch { }
        }
        return Normalize(Inspect(NsisDir).Kind == TargetKind.Ours ? NsisDir : DefaultDir);
    }

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

    public static async Task<Result> Install(string targetInput, bool desktop, IProgress<(string Step, double Ratio)> progress)
    {
        var dir = Normalize(targetInput);
        var source = Environment.ProcessPath ?? throw new InvalidOperationException("no exe");
        if (Inside(source, dir)) throw new SetupError("SETUP_TARGET_SELF");
        var (kind, version) = Inspect(dir);
        if (kind == TargetKind.Foreign) throw new SetupError("SETUP_TARGET_FOREIGN");

        try
        {
            progress.Report(("close", 0));
            await Task.Run(() => CloseRunning(dir));
            if (kind == TargetKind.Ours)
            {
                progress.Report(("clean", 0));
                await Retry(() => { Directory.Delete(dir, true); });
            }
            progress.Report(("copy", 0));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, Marker), new JsonObject { ["version"] = Http.Version, ["installedAt"] = DateTime.UtcNow.ToString("o") }.ToJsonString());
            var exe = Path.Combine(dir, ExeName);
            await CopyWithProgress(source, exe, r => progress.Report(("copy", r)));

            progress.Report(("integrate", 1));
            if (OperatingSystem.IsWindows())
            {
                Register(dir, exe, (int)(new FileInfo(exe).Length / 1024));
                Shortcuts(exe, dir, desktop);
            }
            progress.Report(("done", 1));
            return new Result(dir, exe, kind == TargetKind.Ours, version);
        }
        catch (IOException) { throw new SetupError("SETUP_BUSY"); }
        catch (UnauthorizedAccessException) { throw new SetupError("SETUP_ACCESS"); }
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
            try
            {
                var command = Registry.CurrentUser.OpenSubKey(@"Software\Classes\nxm\shell\open\command")?.GetValue(null) as string;
                if (command is not null && command.Contains(exe, StringComparison.OrdinalIgnoreCase)) Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\nxm", false);
            }
            catch { }
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
