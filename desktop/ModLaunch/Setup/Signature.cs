using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace ModLaunch.Setup;

/// <summary>
/// 31.0: цифровая подпись (Authenticode) exe. Если запущенная программа подписана, обновление обязано быть
/// подписано тем же издателем и подпись должна проверяться Windows (WinVerifyTrust) — подменённый на сайте
/// установщик с верной контрольной суммой в update.json всё равно не запустится. Неподписанная программа
/// (до покупки сертификата) проверяет только SHA-256, как раньше.
/// </summary>
public static class Signature
{
    /// <summary>Издатель (Subject сертификата), если подпись есть и Windows считает её действительной; иначе null.</summary>
    public static string? Publisher(string file)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(file) || !Trusted(file)) return null;
        try
        {
#pragma warning disable SYSLIB0057
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(file));
#pragma warning restore SYSLIB0057
            return cert.Subject;
        }
        catch { return null; }
    }

    /// <summary>Издатель запущенной программы (null — не подписана).</summary>
    public static string? Mine => _mine ??= Environment.ProcessPath is { } p ? Publisher(p) ?? "" : "";
    static string? _mine;

    /// <summary>Можно ли запускать скачанное обновление: программа не подписана — да; подписана — только тем же издателем.</summary>
    public static bool UpdateAllowed(string file, out string? reason)
    {
        reason = null;
        if (string.IsNullOrEmpty(Mine)) return true;
        var theirs = Publisher(file);
        if (theirs is null) { reason = "unsigned"; return false; }
        if (!string.Equals(theirs, Mine, StringComparison.Ordinal)) { reason = "publisher: " + theirs; return false; }
        return true;
    }

    // ---------------------------------------------------------------- WinVerifyTrust

    static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    sealed class FileInfo(string path) : IDisposable
    {
        public uint cbStruct = (uint)Marshal.SizeOf<FileInfo>();
        public IntPtr pcwszFilePath = Marshal.StringToCoTaskMemUni(path);
        public IntPtr hFile = IntPtr.Zero;
        public IntPtr pgKnownSubject = IntPtr.Zero;
        public void Dispose() { if (pcwszFilePath != IntPtr.Zero) { Marshal.FreeCoTaskMem(pcwszFilePath); pcwszFilePath = IntPtr.Zero; } }
    }

    [StructLayout(LayoutKind.Sequential)]
    sealed class TrustData : IDisposable
    {
        public uint cbStruct = (uint)Marshal.SizeOf<TrustData>();
        public IntPtr pPolicyCallbackData = IntPtr.Zero;
        public IntPtr pSIPClientData = IntPtr.Zero;
        public uint dwUIChoice = 2;            // WTD_UI_NONE
        public uint fdwRevocationChecks = 0;   // WTD_REVOKE_NONE (сеть может быть недоступна)
        public uint dwUnionChoice = 1;         // WTD_CHOICE_FILE
        public IntPtr pFile;
        public uint dwStateAction = 0;
        public IntPtr hWVTStateData = IntPtr.Zero;
        public IntPtr pwszURLReference = IntPtr.Zero;
        public uint dwProvFlags = 0x1000;      // WTD_CACHE_ONLY_URL_RETRIEVAL
        public uint dwUIContext = 0;
        public IntPtr pSignatureSettings = IntPtr.Zero;

        public TrustData(FileInfo file)
        {
            pFile = Marshal.AllocCoTaskMem(Marshal.SizeOf<FileInfo>());
            Marshal.StructureToPtr(file, pFile, false);
        }

        public void Dispose() { if (pFile != IntPtr.Zero) { Marshal.FreeCoTaskMem(pFile); pFile = IntPtr.Zero; } }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
    static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, [In] TrustData data);

    /// <summary>Подпись есть и цепочка доверенная (без проверки отзыва — она может требовать сети).</summary>
    public static bool Trusted(string file)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var info = new FileInfo(file);
            using var data = new TrustData(info);
            return WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, data) == 0;
        }
        catch { return false; }
    }

    [SelfTest]
    static string SignatureChecks()
    {
        // Файл с вложенной подписью Microsoft (dotnet.exe; системные exe подписаны каталогом — их WinVerifyTrust по файлу не видит).
        var system = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
        var signed = File.Exists(system) && Trusted(system);
        if (File.Exists(system) && (!signed || Publisher(system)?.Contains("Microsoft") != true)) throw new Exception("signed dotnet.exe not trusted: " + Publisher(system));
        var plain = Path.Combine(Core.Paths.DataDir, "unsigned.exe");
        File.WriteAllBytes(plain, [0x4D, 0x5A, 0, 0]);
        if (Trusted(plain) || Publisher(plain) is not null) throw new Exception("unsigned file reported as signed");
        // Неподписанная программа пускает любое обновление (контрольную сумму проверяет загрузчик).
        var mine = string.IsNullOrEmpty(Mine) ? "unsigned build" : "signed build: " + Mine;
        return $"dotnet.exe signature trusted: {signed}; unsigned file rejected; {mine}";
    }
}
