using System.Net;
using OtpNet;

namespace VehicleVision.PleasanterTools.Core.Web;

/// <summary>TOTP の入力と登録 URI の互換方式。方式を暗黙に切り替えない。</summary>
public enum TotpCompatibility
{
    /// <summary>入力から数字だけを抽出する。URI は要素を個別にエスケープする。</summary>
    DigitsOnly,

    /// <summary>入力の前後の空白だけを除く。URI はラベル全体を URL エンコードする。</summary>
    Trimmed,
}

/// <summary>Otp.NET による 6 桁・30 秒・SHA-1・前後 1 枠の照合。</summary>
public sealed class TotpService
{
    private static readonly VerificationWindow Window = new(previous: 1, future: 1);
    private readonly TotpCompatibility compatibility;

    public TotpService(TotpCompatibility compatibility)
    {
        if (!Enum.IsDefined(compatibility))
        {
            throw new ArgumentOutOfRangeException(nameof(compatibility));
        }

        this.compatibility = compatibility;
    }

    public string GenerateSecret() => Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));

    public string BuildUri(string issuer, string loginId, string secret) =>
        compatibility == TotpCompatibility.DigitsOnly
            ? $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(loginId)}"
                + $"?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits=6&period=30"
            : "otpauth://totp/" + WebUtility.UrlEncode($"{issuer}:{loginId}")
                + "?secret=" + secret + "&issuer=" + WebUtility.UrlEncode(issuer)
                + "&algorithm=SHA1&digits=6&period=30";

    /// <summary>時刻は呼び出し側から受ける。同じ時間枠の再使用はストアで原子的に拒否する。</summary>
    public (bool Verified, long TimeStep) Verify(string secret, string code, DateTime utcNow)
    {
        if (compatibility == TotpCompatibility.DigitsOnly)
        {
            var normalized = new string(code.Where(char.IsDigit).ToArray());
            if (normalized.Length != 6 || string.IsNullOrEmpty(secret))
            {
                return (false, 0);
            }

            var verified = new Totp(Base32Encoding.ToBytes(secret))
                .VerifyTotp(utcNow, normalized, out var step, Window);
            return (verified, step);
        }

        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code))
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

        var result = new Totp(bytes).VerifyTotp(utcNow, code.Trim(), out var timeStep, Window);
        return (result, timeStep);
    }
}
