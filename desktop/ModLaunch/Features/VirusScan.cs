using System.Diagnostics;
using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Social;

namespace ModLaunch.Features;

public enum ScanResult { Clean, Threat, Blocked, Unavailable }

/// <summary>Итог проверки файла: что нашли, отпечаток и подробности (имя угрозы, код Защитника).</summary>
public sealed record ScanReport(ScanResult Result, string Sha256, string Detail)
{
    public bool Bad => Result is ScanResult.Threat or ScanResult.Blocked;
}

/// <summary>
/// 31.0: проверка файлов, которые выкладывают и скачивают люди (Hub, рынок, инди-игры): сначала чёрный список
/// отпечатков ModLaunch (config/blocklist, снятое из-за вируса), затем Защитник Windows (MpCmdRun, без удаления —
/// решаем сами). Опасное не ставится и не выкладывается, а уходит в карантин (DataDir/quarantine), откуда его
/// можно только удалить. Защитник выключен или стоит другой антивирус — «не проверено», не «чисто».
/// </summary>
public static class VirusScan
{
    public static bool Enabled => Settings.Data.Bool("scanFiles", true);

    public static string QuarantineDir => Path.Combine(Paths.DataDir, "quarantine");

    /// <summary>MpCmdRun.exe: свежая платформа Защитника (ProgramData), иначе из Program Files.</summary>
    public static string? DefenderPath()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var platform = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows Defender", "Platform");
            if (Directory.Exists(platform))
                foreach (var dir in Directory.GetDirectories(platform).OrderByDescending(d => Version.TryParse(Path.GetFileName(d).Split('-')[0], out var v) ? v : new Version()))
                    if (File.Exists(Path.Combine(dir, "MpCmdRun.exe"))) return Path.Combine(dir, "MpCmdRun.exe");
        }
        catch { }
        var classic = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender", "MpCmdRun.exe");
        return File.Exists(classic) ? classic : null;
    }

    /// <summary>Проверить файл: чёрный список, затем Защитник Windows (до 3 минут).</summary>
    public static async Task<ScanReport> Check(string file, CancellationToken ct = default)
    {
        var sha = await Task.Run(() => Safety.Sha256(file), ct);
        try { await Safety.Refresh(); } catch { }
        if (Safety.IsBlocked(sha)) return new ScanReport(ScanResult.Blocked, sha, "ModLaunch blocklist");
        if (!Enabled || DefenderPath() is not { } exe) return new ScanReport(ScanResult.Unavailable, sha, "no Defender");
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var a in new[] { "-Scan", "-ScanType", "3", "-File", Path.GetFullPath(file), "-DisableRemediation" }) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("MpCmdRun did not start");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            var output = p.StandardOutput.ReadToEndAsync(timeout.Token);
            try { await p.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                try { p.Kill(true); } catch { }
                return new ScanReport(ScanResult.Unavailable, sha, "Defender timeout");
            }
            var text = await output;
            // 0 — угроз нет, 2 — найдена угроза (с -DisableRemediation файл не трогается). Остальное — Защитник
            // недоступен (выключен, стоит другой антивирус, служба не отвечает): это не «чисто».
            return p.ExitCode switch
            {
                0 => new ScanReport(ScanResult.Clean, sha, "Microsoft Defender"),
                2 => new ScanReport(ScanResult.Threat, sha, ThreatName(text)),
                _ => new ScanReport(ScanResult.Unavailable, sha, $"Defender exit {p.ExitCode}"),
            };
        }
        catch (Exception e) when (e is not OperationCanceledException) { return new ScanReport(ScanResult.Unavailable, sha, e.Message); }
    }

    /// <summary>Имя угрозы из вывода MpCmdRun («Threat  : Virus:DOS/EICAR_Test_File»).</summary>
    public static string ThreatName(string output)
    {
        foreach (var line in output.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith("Threat ", StringComparison.OrdinalIgnoreCase) && t.Contains(':'))
                return t[(t.IndexOf(':') + 1)..].Trim();
        }
        return "threat";
    }

    /// <summary>Убрать файл в карантин (переименовать в .quarantine, рядом — заметка). Возвращает путь в карантине.</summary>
    public static string Quarantine(string file, ScanReport report, string source)
    {
        Directory.CreateDirectory(QuarantineDir);
        var target = Path.Combine(QuarantineDir, $"{DateTime.UtcNow:yyyyMMddHHmmss}_{report.Sha256[..12]}.quarantine");
        try { File.Move(file, target, true); }
        catch { try { File.Delete(file); } catch { } return ""; }
        File.WriteAllText(target + ".json", new JsonObject
        {
            ["file"] = Path.GetFileName(file), ["sha256"] = report.Sha256, ["result"] = report.Result.ToString(),
            ["detail"] = report.Detail, ["source"] = source, ["at"] = DateTime.UtcNow.ToString("o"),
        }.ToJsonString());
        return target;
    }

    /// <summary>
    /// Проверить скачанный файл перед установкой: опасный — в карантин и ошибка с понятным текстом.
    /// Возвращает отчёт (Clean или Unavailable), чтобы можно было показать «проверено Защитником».
    /// </summary>
    public static async Task<ScanReport> BeforeInstall(string file, string source, CancellationToken ct = default)
    {
        var r = await Check(file, ct);
        if (!r.Bad) return r;
        Quarantine(file, r, source);
        throw new InvalidOperationException(r.Result == ScanResult.Blocked ? I18n.T("v31.scan.blocked") : I18n.T("v31.scan.threat", ("name", r.Detail)));
    }

    /// <summary>Проверить свой файл перед публикацией: опасное не выкладываем (файл человека не трогаем).</summary>
    public static async Task BeforePublish(string file, CancellationToken ct = default)
    {
        var r = await Check(file, ct);
        if (r.Result == ScanResult.Blocked) throw new Social.ServiceError("BLOCKED_FILE");
        if (r.Result == ScanResult.Threat) throw new Social.ServiceError("VIRUS", r.Detail);
    }

    /// <summary>Файлы в карантине (новые сверху).</summary>
    public static List<(string File, JsonObject Note)> Quarantined()
    {
        if (!Directory.Exists(QuarantineDir)) return [];
        return Directory.GetFiles(QuarantineDir, "*.quarantine").OrderByDescending(f => f)
            .Select(f => (f, (File.Exists(f + ".json") ? JsonNode.Parse(File.ReadAllText(f + ".json")) as JsonObject : null) ?? new JsonObject())).ToList();
    }

    public static void DeleteQuarantined(string file)
    {
        if (!Path.GetFullPath(file).StartsWith(Path.GetFullPath(QuarantineDir), StringComparison.OrdinalIgnoreCase)) return;
        try { File.Delete(file); } catch { }
        try { File.Delete(file + ".json"); } catch { }
    }

    [SelfTest]
    static string VirusScanBasics()
    {
        if (ThreatName("Scanning C:\\x\nThreat                  : Virus:DOS/EICAR_Test_File\nResources               : 1 total") != "Virus:DOS/EICAR_Test_File")
            throw new Exception("threat name not parsed");
        // Чистый файл: Защитник (если он есть) говорит «чисто»; без него — «не проверено», но не «угроза».
        var dir = Directory.CreateDirectory(Path.Combine(Paths.DataDir, "scan-test")).FullName;
        var clean = Path.Combine(dir, "clean.txt");
        File.WriteAllText(clean, "just a harmless mod readme");
        var r = Task.Run(() => Check(clean)).GetAwaiter().GetResult();
        if (r.Bad) throw new Exception("clean file flagged: " + r);
        // Карантин: файл уезжает, заметка рядом, удаляется только внутри карантина.
        var q = Quarantine(clean, r with { Result = ScanResult.Threat, Detail = "test" }, "selftest");
        if (File.Exists(clean) || !File.Exists(q) || !File.Exists(q + ".json") || Quarantined().Count == 0) throw new Exception("quarantine");
        DeleteQuarantined(q);
        if (File.Exists(q)) throw new Exception("quarantined file not deleted");
        return $"clean file → {r.Result} ({r.Detail}); quarantine moves and deletes; threat name parsed";
    }
}
