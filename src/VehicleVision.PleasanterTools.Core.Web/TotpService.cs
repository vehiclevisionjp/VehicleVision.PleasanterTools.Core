using OtpNet;

namespace VehicleVision.PleasanterTools.Core.Web;

/// <summary>Otp.NET による 6 桁・30 秒・SHA-1・前後 1 枠の照合。</summary>
public sealed class TotpService
{
    private static readonly VerificationWindow Window = new(previous: 1, future: 1);

    public string GenerateSecret() => Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));

    public string BuildUri(string issuer, string loginId, string secret) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(loginId)}"
        + $"?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits=6&period=30";

    /// <summary>時刻は呼び出し側から受ける。同じ時間枠の再使用はストアで原子的に拒否する。</summary>
    public (bool Verified, long TimeStep) Verify(string? secret, string? code, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrEmpty(code))
        {
            return (false, 0);
        }

        var normalized = new string(code.Where(character => !char.IsWhiteSpace(character) && character != '-').ToArray());
        if (normalized.Length != 6 || normalized.Any(character => character is < '0' or > '9'))
        {
            return (false, 0);
        }

        byte[] bytes;
        try
        {
            bytes = Base32Encoding.ToBytes(secret);
        }
        catch (ArgumentException)
        {
            return (false, 0);
        }

        if (bytes.Length == 0)
        {
            return (false, 0);
        }

        // Otp.NET は余分なパディングや不完全な末尾も受けるため、正しい Base32 表現か確かめる。
        var encoded = Base32Encoding.ToString(bytes);
        if (!secret.Equals(encoded, StringComparison.OrdinalIgnoreCase)
            && !secret.Equals(encoded.TrimEnd('='), StringComparison.OrdinalIgnoreCase))
        {
            return (false, 0);
        }

        var result = new Totp(bytes).VerifyTotp(utcNow, normalized, out var timeStep, Window);
        return result ? (true, timeStep) : (false, 0);
    }
}
