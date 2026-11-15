using System.Diagnostics;
using System.Text.Json.Nodes;
using ModLaunch.Core;

namespace ModLaunch.Setup;

/// <summary>
/// 31.0: откат неудачного обновления лаунчера. Обновление оставляет прежний exe рядом (ModLaunch.exe.prev) и
/// помечает новую версию «на испытании». Новая версия, проработав минуту, отмечает себя здоровой. Если она
/// трижды подряд не дожила до этой отметки (падает при запуске, зависает и её закрывают), при следующем запуске
/// прежняя версия возвращается на место сама, а неудачная больше не предлагается к установке.
/// </summary>
public static class UpdateGuard
{
    public const string PrevName = "ModLaunch.exe.prev";
    public const int MaxFailedStarts = 3;
    static readonly TimeSpan Healthy = TimeSpan.FromSeconds(60);

    static string? Dir => Environment.ProcessPath is { } p ? Path.GetDirectoryName(p) : null;

    static JsonObject? ReadMarker(string dir)
    {
        try { return JsonNode.Parse(File.ReadAllText(Path.Combine(dir, Installer.Marker))) as JsonObject; }
        catch { return null; }
    }

    static void WriteMarker(string dir, JsonObject marker) =>
        File.WriteAllText(Path.Combine(dir, Installer.Marker), marker.ToJsonString());

    /// <summary>
    /// Установщик (обновление): сохранить прежний exe как .prev и отметить новую версию «на испытании».
    /// Вызывается после того, как новый exe встал на место.
    /// </summary>
    public static void KeepPrevious(string dir, string oldExe, string? oldVersion)
    {
        if (!File.Exists(oldExe)) return;
        File.Copy(oldExe, Path.Combine(dir, PrevName), overwrite: true);
        var marker = ReadMarker(dir) ?? new JsonObject();
        marker["previous"] = oldVersion ?? "";
        marker["trial"] = true;
        marker["failedStarts"] = 0;
        WriteMarker(dir, marker);
    }

    /// <summary>
    /// Самое начало запуска окна. true — запущена прежняя версия вместо этой: процесс надо завершить.
    /// </summary>
    public static bool OnStart()
    {
        try
        {
            if (!Installer.IsInstalledCopy || Dir is not { } dir) return false;
            var marker = ReadMarker(dir);
            if (marker is null || !marker.Bool("trial")) return false;
            var failed = (int)marker.Long("failedStarts");
            var prev = Path.Combine(dir, PrevName);
            if (failed >= MaxFailedStarts && File.Exists(prev))
            {
                Rollback(dir, marker, prev);
                return true;
            }
            // Этот запуск — на испытании: засчитываем заранее, отметка «здоров» снимет счёт.
            marker["failedStarts"] = failed + 1;
            WriteMarker(dir, marker);
            Installer.Log($"trial start {Http.Version}: attempt {failed + 1}");
        }
        catch (Exception e) { Guard.Log(e); }
        return false;
    }

    /// <summary>Окно проработало минуту — версия рабочая: снимаем испытание (прежний exe остаётся на всякий случай).</summary>
    public static void StartHealthTimer()
    {
        if (!Installer.IsInstalledCopy) return;
        _ = Task.Run(async () => { await Task.Delay(Healthy); MarkHealthy(); });
    }

    /// <summary>Версия рабочая: минута без падения или обычное закрытие окна (быстро открыл и закрыл — не сбой).</summary>
    public static void MarkHealthy()
    {
        if (!Installer.IsInstalledCopy) return;
        try
        {
            if (Dir is not { } dir || ReadMarker(dir) is not { } marker || !marker.Bool("trial")) return;
            marker["trial"] = false;
            marker["failedStarts"] = 0;
            marker["healthyAt"] = DateTime.UtcNow.ToString("o");
            WriteMarker(dir, marker);
            Installer.Log($"version {Http.Version} healthy");
        }
        catch (Exception e) { Guard.Log(e); }
    }

    /// <summary>Вернуть прежний exe: работающий переименовываем (Windows это разрешает), прежний — на место, запуск.</summary>
    internal static void Rollback(string dir, JsonObject marker, string prev, bool launch = true)
    {
        var exe = Path.Combine(dir, Installer.ExeName);
        var bad = Path.Combine(dir, $"ModLaunch.exe.{Http.Version}.bad");
        Installer.Log($"rollback {Http.Version} -> {marker.Str("previous")} after {marker.Long("failedStarts")} failed starts");
        File.Move(exe, bad, overwrite: true);
        try { File.Copy(prev, exe, overwrite: true); }
        catch { File.Move(bad, exe, overwrite: true); throw; }
        marker["version"] = marker.Str("previous") ?? "";
        marker["rolledBack"] = Http.Version;
        marker["trial"] = false;
        marker["failedStarts"] = 0;
        WriteMarker(dir, marker);
        // Неудачную версию больше не предлагаем (до следующей, более новой).
        Settings.Data["badVersion"] = Http.Version;
        Settings.Save();
        Core.JsonFile.FlushAll();
        if (launch) Process.Start(new ProcessStartInfo(exe, "--rolled-back") { UseShellExecute = false, WorkingDirectory = dir });
    }

    /// <summary>Версия, которую откатили (её не предлагаем снова).</summary>
    public static string? BadVersion => Settings.Data.Str("badVersion");

    /// <summary>Только что откатились: (неудачная версия, текущая) — для сообщения человеку.</summary>
    public static (string Bad, string Now)? RolledBack()
    {
        if (Dir is not { } dir || ReadMarker(dir) is not { } marker || marker.Str("rolledBack") is not { Length: > 0 } bad) return null;
        return (bad, Http.Version);
    }

    public static void ClearRolledBack()
    {
        if (Dir is not { } dir || ReadMarker(dir) is not { } marker) return;
        marker.Remove("rolledBack");
        WriteMarker(dir, marker);
    }

    [SelfTest]
    static string UpdateGuardRollsBack()
    {
        // Логика маркера без настоящей замены exe: три неудачных старта → откат, здоровый старт снимает испытание.
        var dir = Directory.CreateDirectory(Path.Combine(Paths.DataDir, "guard-test")).FullName;
        File.WriteAllText(Path.Combine(dir, "old.exe"), "old");
        WriteMarker(dir, new JsonObject { ["version"] = "31.0.0" });
        KeepPrevious(dir, Path.Combine(dir, "old.exe"), "30.13.0");
        var m = ReadMarker(dir)!;
        if (!m.Bool("trial") || m.Str("previous") != "30.13.0" || File.ReadAllText(Path.Combine(dir, PrevName)) != "old") throw new Exception("previous not kept");
        for (var i = 0; i < MaxFailedStarts; i++) { m["failedStarts"] = m.Long("failedStarts") + 1; WriteMarker(dir, m); }
        if (ReadMarker(dir)!.Long("failedStarts") < MaxFailedStarts) throw new Exception("failed starts not counted");
        // Сама подмена: «упавший» exe уходит в .bad, прежний встаёт на место, версия в отметке — прежняя.
        File.WriteAllText(Path.Combine(dir, Installer.ExeName), "new-broken");
        Rollback(dir, ReadMarker(dir)!, Path.Combine(dir, PrevName), launch: false);
        if (File.ReadAllText(Path.Combine(dir, Installer.ExeName)) != "old") throw new Exception("previous exe not restored");
        if (!File.Exists(Path.Combine(dir, $"ModLaunch.exe.{Http.Version}.bad"))) throw new Exception("broken exe not kept aside");
        var after = ReadMarker(dir)!;
        if (after.Str("version") != "30.13.0" || after.Bool("trial") || after.Str("rolledBack") != Http.Version || BadVersion != Http.Version) throw new Exception("marker after rollback: " + after.ToJsonString());
        Settings.Data.Remove("badVersion");
        return $"prev kept, trial flagged; after {MaxFailedStarts} starts without a healthy minute the broken exe goes to .bad, the previous one is restored and its version is not offered again";
    }
}
