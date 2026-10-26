using System.Security.Cryptography;

namespace VehicleVision.PleasanterTools.Core.Web;

/// <summary>U12 の復旧コード。旧 SHA-256 は検証せず、本人確認を伴う再発行で移行する。</summary>
public static class RecoveryCode
{
    public const int Count = 10;
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private static readonly PasswordHasher Hasher = new();

    public static IReadOnlyList<string> Generate()
    {
        var codes = new List<string>(Count);
        for (var index = 0; index < Count; index++)
        {
            var buffer = new char[11];
            for (var position = 0; position < buffer.Length; position++)
            {
                buffer[position] = position == 5 ? '-' : Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
            }

            codes.Add(new string(buffer));
        }

        return codes;
    }

    public static string Normalize(string code) =>
        new(code.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    public static string Hash(string code) => Hasher.Hash(Normalize(code));

    /// <summary>未使用候補を照合する。使い切りの原子的な更新はアプリのストアが担う。</summary>
    public static bool Verify(string code, string stored) => Hasher.Verify(Normalize(code), stored).Verified;
}
