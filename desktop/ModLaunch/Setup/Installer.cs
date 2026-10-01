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
/// Ставит всегда в папку «ModLaunch». Если рядом найдена старая установка
/// (3.x на Electron в Programs\modhub), программа переезжает в «ModLaunch»,
/// а старая папка, её ярлыки и записи в реестре убираются; настройки и моды
/// лежат отдельно (%APPDATA%\ModHub) и остаются на месте.
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

    /// <summary>%LOCALAPPDATA%; для проверок установщика подменяется переменной MODLAUNCH_LOCAL.</summary>
    static string Local => Environment.GetEnvironmentVariable("MODLAUNCH_LOCAL") is { Length: > 0 } fake ? fake : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public const string FolderName = "ModLaunch";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string UninstallRoot = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
    public static string DefaultDir => Path.Combine(Local, "Programs", FolderName);
    static string LegacyDir => Path.Combine(Local, "Programs", "modhub");

    /// <summary>Установлена ли эта копия (рядом с exe есть отметка установки).</summary>
    public static bool IsInstalledCopy => Environment.ProcessPath is string exe && File.Exists(Path.Combine(Path.GetDirectoryName(exe)!, Marker));

    /// <summary>
    /// Ставим всегда в свою папку: выбрали «D:\Games» — получится «D:\Games\ModLaunch».
    /// Прежняя «ModHub» превращается в «ModLaunch» рядом с ней.
    /// </summary>
    public static string Normalize(string? dir)
    {
        var full = Path.GetFullPath(string.IsNullOrWhiteSpace(dir) ? DefaultDir : dir.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(full);
        if (name.Equals(FolderName, StringComparison.OrdinalIgnoreCase)) return full;
        if (name.Equals("ModHub", StringComparison.OrdinalIgnoreCase)) return Path.Combine(Path.GetDirectoryName(full) ?? full, FolderName);
        return Path.Combine(full, FolderName);
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

    static string? KnownDir()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try { return Registry.CurrentUser.OpenSubKey(AppKey)?.GetValue("InstallDir") as string; } catch { return null; }
    }

    /// <summary>Куда ставить: где уже стоит ModLaunch (если выбирали свою папку), иначе Programs\ModLaunch.</summary>
    public static string SuggestedDir()
    {
        var known = KnownDir();
        if (known is not null && Inspect(known).Kind == TargetKind.Ours) return Normalize(known);
        return DefaultDir;
    }

    static bool Same(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    /// <summary>Старые установки ModLaunch/ModHub, которые не совпадают с целевой папкой и переедут в неё.</summary>
    public static List<string> FindLegacy(string target)
    {
        var found = new List<string>();
        foreach (var candidate in new[] { LegacyDir, KnownDir() })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            try
            {
                var full = Path.GetFullPath(candidate.Trim());
                if (Same(full, target) || found.Any(f => Same(f, full))) continue;
                if (Inspect(full).Kind == TargetKind.Ours) found.Add(full);
            }
            catch { }
        }
        return found;
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

    public sealed record Result(string Target, string Exe, bool Updated, string? From, string? Moved = null);

    public static long SourceSize => Environment.ProcessPath is string p && File.Exists(p) ? new FileInfo(p).Length : 0;

    /// <summary>Свободное место на диске папки; null, если диск не определился (сетевой путь и т. п.).</summary>
    public static long? FreeBytes(string dir)
    {
        try { return Path.GetPathRoot(Path.GetFullPath(dir)) is string root ? new DriveInfo(root).AvailableFreeSpace : null; }
        catch { return null; }
    }

    static bool NoSpace(IOException e) => (e.HResult & 0xFFFF) is 112 or 39;

    /// <summary>
    /// Установка не оставляет полурабочую копию: сначала новый exe пишется рядом как .part,
    /// и только когда он целиком на диске, старые файлы заменяются. Не хватило места или
    /// файл занят — прежняя установка остаётся как была.
    /// </summary>
    public static async Task<Result> Install(string targetInput, bool desktop, IProgress<(string Step, double Ratio)> progress)
    {
        var dir = Normalize(targetInput);
        var source = Environment.ProcessPath ?? throw new InvalidOperationException("no exe");
        if (Inside(source, dir)) throw new SetupError("SETUP_TARGET_SELF");
        var (kind, version) = Inspect(dir);
        if (kind == TargetKind.Foreign) throw new SetupError("SETUP_TARGET_FOREIGN");
        var legacy = FindLegacy(dir).Where(l => !Inside(source, l)).ToList();
        var size = new FileInfo(source).Length;
        var exe = Path.Combine(dir, ExeName);
        var part = exe + ".part";

        try
        {
            progress.Report(("close", 0));
            await Task.Run(() => { CloseRunning(dir); foreach (var l in legacy) CloseRunning(l); });

            if (FreeBytes(dir) is long free && free < size * 2 + (50L << 20)) throw new SetupError("SETUP_SPACE");
            Directory.CreateDirectory(dir);
            progress.Report(("copy", 0.04));
            try { await CopyWithProgress(source, part, r => progress.Report(("copy", 0.04 + r * 0.8))); }
            catch { try { File.Delete(part); } catch { } throw; }

            progress.Report(("clean", 0.86));
            if (kind == TargetKind.Ours)
                foreach (var entry in Directory.EnumerateFileSystemEntries(dir).Where(e => !Same(e, part)).ToList())
                    await Retry(() => { if (Directory.Exists(entry)) Directory.Delete(entry, true); else File.Delete(entry); });
            await Retry(() => File.Move(part, exe, true));
            File.WriteAllText(Path.Combine(dir, Marker), new JsonObject { ["version"] = Http.Version, ["installedAt"] = DateTime.UtcNow.ToString("o") }.ToJsonString());

            progress.Report(("integrate", 0.92));
            if (OperatingSystem.IsWindows())
            {
                Register(dir, exe, (int)(new FileInfo(exe).Length / 1024));
                Shortcuts(exe, dir, desktop);
                RepointAutostart(exe);
            }

            string? moved = null;
            if (legacy.Count > 0)
            {
                progress.Report(("legacy", 0.96));
                foreach (var l in legacy) if (await Task.Run(() => RemoveLegacy(l))) moved ??= l;
            }
            progress.Report(("done", 1));
            return new Result(dir, exe, kind == TargetKind.Ours, version, moved);
        }
        catch (IOException e) when (NoSpace(e)) { throw new SetupError("SETUP_SPACE"); }
        catch (IOException) { throw new SetupError("SETUP_BUSY"); }
        catch (UnauthorizedAccessException) { throw new SetupError("SETUP_ACCESS"); }
    }

    /// <summary>
    /// Убрать прежнюю установку: папку, ярлыки «ModHub» и запись в «Приложениях Windows».
    /// Ошибки не мешают установке — новая копия уже стоит и работает.
    /// </summary>
    static bool RemoveLegacy(string dir)
    {
        var ok = true;
        try { Directory.Delete(dir, true); }
        catch { ok = false; }
        foreach (var folder in new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.Programs })
            try { File.Delete(Path.Combine(Environment.GetFolderPath(folder), "ModHub.lnk")); } catch { }
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var root = Registry.CurrentUser.OpenSubKey(UninstallRoot, writable: true);
                foreach (var name in root?.GetSubKeyNames() ?? [])
                {
                    if (name == "ModHub") continue; // эту запись только что записали мы
                    using var key = root!.OpenSubKey(name);
                    var text = string.Join("|", new[] { "InstallLocation", "UninstallString", "DisplayIcon" }.Select(v => key?.GetValue(v) as string ?? ""));
                    if (text.Contains(dir, StringComparison.OrdinalIgnoreCase)) root.DeleteSubKeyTree(name, false);
                }
            }
            catch { }
        }
        return ok;
    }

    /// <summary>Автозапуск с Windows, включённый из старой папки, переводим на новый exe.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    static void RepointAutostart(string exe)
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (run?.GetValue("ModLaunch") is string current) run.SetValue("ModLaunch", $"\"{exe}\"" + (current.Contains("--autostart") ? " --autostart" : ""));
        }
        catch { }
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
                using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
                if (run?.GetValue("ModLaunch") is string auto && auto.Contains(exe, StringComparison.OrdinalIgnoreCase)) run.DeleteValue("ModLaunch", false);
            }
            catch { }
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

    public static void OpenFolder(string dir)
    {
        try { Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true }); } catch { }
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
