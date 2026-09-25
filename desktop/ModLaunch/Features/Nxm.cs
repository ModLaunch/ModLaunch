using System.IO.Pipes;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ModLaunch.Features;

public sealed record NxmLink(string Domain, string ModId, long FileId, string? Key, string? Expires);

/// <summary>
/// Ссылки nxm:// (кнопка «Mod Manager Download» на Nexus): регистрация в Windows,
/// один экземпляр программы (второй запуск передаёт ссылку первому) и разбор ссылки.
/// </summary>
public static partial class Nxm
{
    const string PipeName = "ModLaunch-4-single-instance";
    static Mutex? _mutex;

    public static event Action<string>? Received;

    [GeneratedRegex(@"^nxm://([^/]+)/mods/(\d+)/files/(\d+)", RegexOptions.IgnoreCase)] private static partial Regex LinkRe();

    public static NxmLink? Parse(string link)
    {
        var m = LinkRe().Match(link.Trim());
        if (!m.Success) return null;
        string? Q(string name) => Regex.Match(link, $@"[?&]{name}=([^&]+)") is { Success: true } q ? Uri.UnescapeDataString(q.Groups[1].Value) : null;
        return new NxmLink(m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value, long.Parse(m.Groups[3].Value), Q("key"), Q("expires"));
    }

    /// <summary>true — мы первый экземпляр. Иначе ссылка передана уже открытому окну.</summary>
    public static bool Claim(string[] args)
    {
        _mutex = new Mutex(true, "Local\\" + PipeName, out var first);
        if (first)
        {
            _ = Task.Run(Listen);
            return true;
        }
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(3000);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(args.FirstOrDefault(a => a.StartsWith("nxm://", StringComparison.OrdinalIgnoreCase)) ?? "--show");
        }
        catch { }
        return false;
    }

    static async Task Listen()
    {
        while (true)
        {
            try
            {
                using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync();
                using var reader = new StreamReader(server);
                var line = await reader.ReadLineAsync();
                if (!string.IsNullOrWhiteSpace(line)) Avalonia.Threading.Dispatcher.UIThread.Post(() => Received?.Invoke(line));
            }
            catch { await Task.Delay(500); }
        }
    }

    public static bool IsRegistered()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            var command = Registry.CurrentUser.OpenSubKey(@"Software\Classes\nxm\shell\open\command")?.GetValue(null) as string;
            return command is not null && command.Contains(Environment.ProcessPath ?? "\0", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>Сделать ModLaunch обработчиком nxm:// для текущего пользователя (без прав администратора).</summary>
    public static void Register()
    {
        if (!OperatingSystem.IsWindows() || Environment.ProcessPath is not string exe) return;
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\nxm");
        key.SetValue(null, "URL:NXM Protocol");
        key.SetValue("URL Protocol", "");
        using (var icon = key.CreateSubKey("DefaultIcon")) icon.SetValue(null, $"\"{exe}\",0");
        using var command = key.CreateSubKey(@"shell\open\command");
        command.SetValue(null, $"\"{exe}\" \"%1\"");
    }
}
