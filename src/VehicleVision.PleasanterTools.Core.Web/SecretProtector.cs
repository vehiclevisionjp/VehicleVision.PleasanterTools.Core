using System.Security.Cryptography;
using System.Text;

namespace VehicleVision.PleasanterTools.Core.Web;

/// <summary>明示的に渡された鍵で AES-256-GCM を使う。鍵の自動生成・保存・設定解決はしない。</summary>
public sealed class SecretProtector
{
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private readonly byte[] key;

    public SecretProtector(string base64Key)
    {
        key = DecodeKey(base64Key);
    }

    public static string GenerateKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public static byte[] DecodeKey(string base64Key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(base64Key);
        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(base64Key);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("秘密の鍵は Base64 で与えてください。", nameof(base64Key), exception);
        }

        return decoded.Length == 32
            ? decoded
            : throw new ArgumentException(
                $"秘密の鍵は 32 バイト（256 ビット）で与えてください。実際は {decoded.Length} バイトです。",
                nameof(base64Key));
    }

    /// <summary>文脈を AAD とする v2$ 書式だけを書き込む。</summary>
    public string Protect(string plaintext, string context)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentException.ThrowIfNullOrEmpty(context);
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagBytes];
        using var aes = new AesGcm(key, TagBytes);
        aes.Encrypt(nonce, plain, cipher, tag, Encoding.UTF8.GetBytes(context));

        return string.Join('$', "v2", Convert.ToBase64String(nonce),
            Convert.ToBase64String(tag), Convert.ToBase64String(cipher));
    }

    /// <summary>旧 v1:・v1$ と v2$ を読む。入力・文脈の不正や復号失敗は null を返す。</summary>
    public string? Unprotect(string? protectedValue, string? context)
    {
        if (string.IsNullOrEmpty(protectedValue))
        {
            return null;
        }

        try
        {
            if (protectedValue.StartsWith("v1:", StringComparison.Ordinal))
            {
                if (string.IsNullOrEmpty(context))
                {
                    return null;
                }

                var raw = Convert.FromBase64String(protectedValue[3..]);
                if (raw.Length < NonceBytes + TagBytes)
                {
                    return null;
                }

                return Decrypt(raw.AsSpan(0, NonceBytes), raw.AsSpan(NonceBytes, TagBytes),
                    raw.AsSpan(NonceBytes + TagBytes), Encoding.UTF8.GetBytes(context));
            }

            var parts = protectedValue.Split('$');
            if (parts.Length != 4 || (parts[0] != "v1" && parts[0] != "v2")
                || (parts[0] == "v2" && string.IsNullOrEmpty(context)))
            {
                return null;
            }

            return Decrypt(Convert.FromBase64String(parts[1]), Convert.FromBase64String(parts[2]),
                Convert.FromBase64String(parts[3]),
                parts[0] == "v2" ? Encoding.UTF8.GetBytes(context!) : null);
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException or ArgumentException)
        {
            return null;
        }
    }

    private string Decrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> tag, ReadOnlySpan<byte> cipher, byte[]? aad)
    {
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, TagBytes);
        aes.Decrypt(nonce, cipher, tag, plain, aad);
        return Encoding.UTF8.GetString(plain);
    }
}
