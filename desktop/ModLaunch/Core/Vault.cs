using System.Security.Cryptography;
using System.Text;

namespace ModLaunch.Core;

/// <summary>
/// 11.0: шифрование купленных инжектов на диске (AES-GCM). Ключ — случайный, свой у каждого
/// компьютера и пользователя Windows: файл с инжектом, скопированный на другой ПК, не откроется.
/// Исходник покупателю не показывается, а в папке игры появляется только на время игры.
/// </summary>
public static class Vault
{
    static byte[]? _key;

    static byte[] Key()
    {
        if (_key is not null) return _key;
        var dir = Directory.CreateDirectory(Path.Combine(Paths.DataDir, "injects")).FullName;
        var file = Path.Combine(dir, ".key");
        byte[] secret;
        try { secret = File.Exists(file) ? File.ReadAllBytes(file) : []; } catch { secret = []; }
        if (secret.Length != 32)
        {
            secret = RandomNumberGenerator.GetBytes(32);
            File.WriteAllBytes(file, secret);
            try { File.SetAttributes(file, FileAttributes.Hidden); } catch { }
        }
        var bind = Encoding.UTF8.GetBytes(Environment.MachineName + "|" + Environment.UserName);
        return _key = SHA256.HashData([.. secret, .. bind]);
    }

    /// <summary>nonce (12) + tag (16) + шифротекст.</summary>
    public static byte[] Seal(byte[] plain)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[plain.Length];
        using var gcm = new AesGcm(Key(), 16);
        gcm.Encrypt(nonce, plain, cipher, tag);
        return [.. nonce, .. tag, .. cipher];
    }

    public static byte[] Open(byte[] sealedData)
    {
        if (sealedData.Length < 28) throw new CryptographicException("too short");
        var plain = new byte[sealedData.Length - 28];
        using var gcm = new AesGcm(Key(), 16);
        gcm.Decrypt(sealedData.AsSpan(0, 12), sealedData.AsSpan(28), sealedData.AsSpan(12, 16), plain);
        return plain;
    }

    [SelfTest]
    static string VaultRoundTrip()
    {
        var text = Encoding.UTF8.GetBytes("hook start {\n  add gold 1\n}");
        var box = Seal(text);
        if (box.AsSpan(28).SequenceEqual(text)) throw new Exception("not encrypted");
        if (!Open(box).SequenceEqual(text)) throw new Exception("round trip");
        box[^1] ^= 1;
        try { Open(box); throw new Exception("tampered data opened"); }
        catch (CryptographicException) { }
        return "inject sealed with AES-GCM; tampering is detected";
    }
}
