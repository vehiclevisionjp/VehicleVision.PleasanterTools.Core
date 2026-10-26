using System.Security.Cryptography;
using System.Text;

namespace VehicleVision.PleasanterTools.Core.Web;

/// <summary>256 bit の招待トークン。保存は U5 の SHA-256 小文字 16 進に限定する。</summary>
public static class InvitationToken
{
    public static string Create() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>空白の扱いは呼び出し側が明示する。旧 Base64 ハッシュへのフォールバックはしない。</summary>
    public static string Hash(string token, bool trim) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(trim ? token.Trim() : token)));

    /// <summary>任意の事前検査。ハッシュ計算では自動的に適用しない。</summary>
    public static bool LooksValid(string? token) =>
        token is { Length: >= 20 and <= 100 } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
