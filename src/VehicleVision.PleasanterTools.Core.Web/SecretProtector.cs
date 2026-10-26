using System.Security.Cryptography;
using System.Text;

namespace VehicleVision.PleasanterTools.Core.Web;

/// <summary>AES-GCM の保存書式と復号失敗時の互換方式。</summary>
public enum SecretProtectionCompatibility
{
    /// <summary>v1: の後へ nonce・tag・cipher をまとめる。文脈必須、復号失敗は例外。</summary>
    PackedV1,

    /// <summary>$ 区切り。文脈ありは v2、旧 v1 は文脈なし。復号失敗は null。</summary>
    DelimitedV2,
}

/// <summary>明示的に渡された鍵で AES-256-GCM を使う。鍵の自動生成・保存・設定解決はしない。</summary>
public sealed class SecretProtector
{
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private readonly byte[] key;
    private readonly SecretProtectionCompatibility compatibility;

    public SecretProtector(string base64Key, SecretProtectionCompatibility compatibility)
    {
        if (!Enum.IsDefined(compatibility))
        {
            throw new ArgumentOutOfRangeException(nameof(compatibility));
        }

        key = DecodeKey(base64Key);
        this.compatibility = compatibility;
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

    /// <summary>文脈なしの旧書式。DelimitedV2 の既存用途に限定する。</summary>
    public string Protect(string plaintext)
    {
        EnsureDelimited();
        return Encrypt(plaintext, context: null);
    }

    public string Protect(string plaintext, string context)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentException.ThrowIfNullOrEmpty(context);
        return Encrypt(plaintext, context);
    }

    private string Encrypt(string plaintext, string? context)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagBytes];
        using var aes = new AesGcm(key, TagBytes);
        aes.Encrypt(nonce, plain, cipher, tag, context is null ? null : Encoding.UTF8.GetBytes(context));

        return compatibility == SecretProtectionCompatibility.PackedV1
            ? $"v1:{Convert.ToBase64String([.. nonce, .. tag, .. cipher])}"
            : string.Join('$', context is null ? "v1" : "v2", Convert.ToBase64String(nonce),
                Convert.ToBase64String(tag), Convert.ToBase64String(cipher));
    }

    public string? Unprotect(string protectedValue)
    {
        EnsureDelimited();
        return Unprotect(protectedValue, context: null);
    }

    /// <summary>指定方式の書式だけ読む。DelimitedV2 の旧 v1 は従来どおり文脈を要求しない。</summary>
    public string? Unprotect(string protectedValue, string? context)
    {
        if (compatibility == SecretProtectionCompatibility.PackedV1)
        {
            ArgumentException.ThrowIfNullOrEmpty(protectedValue);
            ArgumentException.ThrowIfNullOrEmpty(context);
            if (!protectedValue.StartsWith("v1:", StringComparison.Ordinal))
            {
                throw new CryptographicException("暗号文の書式（版）が不明です。");
            }

            byte[] raw;
            try
            {
                raw = Convert.FromBase64String(protectedValue[3..]);
            }
            catch (FormatException exception)
            {
                throw new CryptographicException("暗号文が壊れています。", exception);
            }

            if (raw.Length < NonceBytes + TagBytes)
            {
                throw new CryptographicException("暗号文が短すぎます。");
            }

            return Decrypt(raw.AsSpan(0, NonceBytes), raw.AsSpan(NonceBytes, TagBytes),
                raw.AsSpan(NonceBytes + TagBytes), Encoding.UTF8.GetBytes(context));
        }

        if (string.IsNullOrEmpty(protectedValue))
        {
            return null;
        }

        var parts = protectedValue.Split('$');
        if (parts.Length != 4 || (parts[0] != "v1" && parts[0] != "v2")
            || (parts[0] == "v2" && string.IsNullOrEmpty(context)))
        {
            return null;
        }

        try
        {
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

    private void EnsureDelimited()
    {
        if (compatibility != SecretProtectionCompatibility.DelimitedV2)
        {
            throw new InvalidOperationException("この書式では文脈を省略できません。");
        }
    }
}
