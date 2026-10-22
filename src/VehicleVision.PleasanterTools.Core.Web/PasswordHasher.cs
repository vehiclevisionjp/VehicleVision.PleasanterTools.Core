using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;

namespace VehicleVision.PleasanterTools.Core.Web;

/// <summary>既存の保存値と入力検査を維持する互換方式。</summary>
public enum PasswordHashCompatibility
{
    /// <summary>64 バイト。反復回数は符号なし、塩は空を拒否する。</summary>
    Strict64,

    /// <summary>32 バイト。従来の KeyDerivation と整数の解釈を維持する。</summary>
    Delimited32,
}

/// <summary>版と反復回数を持つ PBKDF2-HMAC-SHA512。互換方式は呼び出し側が明示する。</summary>
public sealed class PasswordHasher
{
    public const int CurrentIterations = 210_000;
    private readonly PasswordHashCompatibility compatibility;

    public PasswordHasher(PasswordHashCompatibility compatibility)
    {
        if (!Enum.IsDefined(compatibility))
        {
            throw new ArgumentOutOfRangeException(nameof(compatibility));
        }

        this.compatibility = compatibility;
    }

    public string Hash(string password, int iterations = CurrentIterations)
    {
        if (compatibility == PasswordHashCompatibility.Strict64)
        {
            ArgumentNullException.ThrowIfNull(password);
        }
        else
        {
            ArgumentException.ThrowIfNullOrEmpty(password);
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);
        var salt = RandomNumberGenerator.GetBytes(16);
        return string.Create(CultureInfo.InvariantCulture,
            $"v1${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(Derive(password, salt, iterations))}");
    }

    /// <summary>再ハッシュの合図は照合成功時だけ返す。方式をまたいだ検証はしない。</summary>
    public (bool Verified, bool NeedsRehash) Verify(string password, string stored)
    {
        if (compatibility == PasswordHashCompatibility.Strict64)
        {
            ArgumentNullException.ThrowIfNull(password);
        }
        else if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(stored))
        {
            return (false, false);
        }

        if (!TryParse(stored, out var iterations, out var salt, out var expected))
        {
            return (false, false);
        }

        var verified = CryptographicOperations.FixedTimeEquals(Derive(password, salt, iterations), expected);
        return (verified, verified && iterations < CurrentIterations);
    }

    /// <summary>保存値だけで更新の要否を見る。更新は必ず照合成功後に行う。</summary>
    public bool NeedsRehash(string stored) =>
        !TryParse(stored, out var iterations, out _, out _) || iterations < CurrentIterations;

    private byte[] Derive(string password, byte[] salt, int iterations) =>
        compatibility == PasswordHashCompatibility.Strict64
            ? Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations,
                HashAlgorithmName.SHA512, 64)
            : KeyDerivation.Pbkdf2(password, salt, KeyDerivationPrf.HMACSHA512, iterations, 32);

    private bool TryParse(string stored, out int iterations, out byte[] salt, out byte[] hash)
    {
        iterations = 0;
        salt = [];
        hash = [];
        var parts = stored?.Split('$');
        if (parts is not { Length: 4 } || parts[0] != "v1")
        {
            return false;
        }

        if (compatibility == PasswordHashCompatibility.Strict64)
        {
            if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out iterations)
                || iterations < 1)
            {
                return false;
            }
        }
        else if (!int.TryParse(parts[1], out iterations))
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

        return compatibility != PasswordHashCompatibility.Strict64 || (salt.Length > 0 && hash.Length == 64);
    }
}
