using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace VehicleVision.PleasanterTools.Core.Web;

/// <summary>PBKDF2-HMAC-SHA512。既存の 32・64 バイトを読み、新規は 64 バイトで保存する。</summary>
public sealed class PasswordHasher
{
    public const int CurrentIterations = 210_000;
    private const int HashBytes = 64;

    public string Hash(string password, int iterations = CurrentIterations)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);
        var salt = RandomNumberGenerator.GetBytes(16);
        return string.Create(CultureInfo.InvariantCulture,
            $"v1${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(Derive(password, salt, iterations, HashBytes))}");
    }

    /// <summary>保存されたハッシュ長で照合し、更新の合図は成功時だけ返す。</summary>
    public (bool Verified, bool NeedsRehash) Verify(string? password, string? stored)
    {
        if (string.IsNullOrEmpty(password))
        {
            return (false, false);
        }

        if (!TryParse(stored, out var iterations, out var salt, out var expected))
        {
            return (false, false);
        }

        var verified = CryptographicOperations.FixedTimeEquals(
            Derive(password, salt, iterations, expected.Length), expected);
        return (verified, verified && (iterations < CurrentIterations || expected.Length != HashBytes));
    }

    /// <summary>保存値だけで更新の要否を見る。更新は必ず照合成功後に行う。</summary>
    public bool NeedsRehash(string? stored) =>
        !TryParse(stored, out var iterations, out _, out var hash)
        || iterations < CurrentIterations || hash.Length != HashBytes;

    private static byte[] Derive(string password, byte[] salt, int iterations, int hashBytes) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations,
            HashAlgorithmName.SHA512, hashBytes);

    private static bool TryParse(string? stored, out int iterations, out byte[] salt, out byte[] hash)
    {
        iterations = 0;
        salt = [];
        hash = [];
        var parts = stored?.Split('$');
        if (parts is not { Length: 4 } || parts[0] != "v1")
        {
            return false;
        }

        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out iterations)
            || iterations < 1)
        {
            return false;
        }

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            hash = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        return salt.Length > 0 && hash.Length is 32 or HashBytes;
    }
}
