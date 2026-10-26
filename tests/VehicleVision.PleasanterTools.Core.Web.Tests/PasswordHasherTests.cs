using System.Security.Cryptography;
using System.Text;

namespace VehicleVision.PleasanterTools.Core.Web.Tests;

public sealed class PasswordHasherTests
{
    private readonly PasswordHasher hasher = new();

    [Theory]
    [InlineData(AuthenticationCompatibilityValues.Hash64)]
    [InlineData(AuthenticationCompatibilityValues.Hash32)]
    public void 既存の32と64バイトの保存値を同じ入口で照合して更新を促す(string stored)
    {
        Assert.Equal((true, true), hasher.Verify(AuthenticationCompatibilityValues.Password, stored));
        Assert.Equal((false, false), hasher.Verify("wrong", stored));
        Assert.True(hasher.NeedsRehash(stored));
    }

    [Fact]
    public void 新規はSHA512と現行回数と16バイトの塩と64バイトのハッシュで保存する()
    {
        var first = hasher.Hash("password");
        var parts = first.Split('$');
        Assert.Equal(4, parts.Length);
        Assert.Equal("v1", parts[0]);
        Assert.Equal("210000", parts[1]);
        var salt = Convert.FromBase64String(parts[2]);
        var hash = Convert.FromBase64String(parts[3]);
        Assert.Equal(16, salt.Length);
        Assert.Equal(64, hash.Length);
        Assert.Equal(Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes("password"), salt,
            210_000, HashAlgorithmName.SHA512, 64), hash);
        Assert.NotEqual(first, hasher.Hash("password"));
        Assert.DoesNotContain("password", first, StringComparison.Ordinal);
        Assert.Equal((true, false), hasher.Verify("password", first));
        Assert.Equal((false, false), hasher.Verify("passworD", first));
        Assert.False(hasher.NeedsRehash(first));
        Assert.Equal(210_000, PasswordHasher.CurrentIterations);
    }

    [Theory]
    [InlineData(1, 32, true)]
    [InlineData(1, 64, true)]
    [InlineData(210000, 32, true)]
    [InlineData(210000, 64, false)]
    [InlineData(210001, 32, true)]
    [InlineData(210001, 64, false)]
    public void 照合成功時の回数と長さで更新要否を判定する(int iterations, int hashBytes, bool needsRehash)
    {
        var stored = hasher.Hash("password", iterations);
        if (hashBytes == 32)
        {
            var parts = stored.Split('$');
            parts[3] = Convert.ToBase64String(Convert.FromBase64String(parts[3])[..32]);
            stored = string.Join('$', parts);
        }

        Assert.Equal((true, needsRehash), hasher.Verify("password", stored));
        Assert.Equal((false, false), hasher.Verify("wrong", stored));
        Assert.Equal(needsRehash, hasher.NeedsRehash(stored));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("plain")]
    [InlineData("v2$1000$AAAA$AAAA")]
    [InlineData("v1$abc$AAAA$AAAA")]
    [InlineData("v1$1000$!!!$!!!")]
    [InlineData("v1$1000$AAAA$AAAA")]
    [InlineData("v1$1000$AAAA$AAAA$extra")]
    public void 不正な保存値は例外にせず不一致とする(string? stored)
    {
        Assert.Equal((false, false), hasher.Verify("anything", stored));
        Assert.True(hasher.NeedsRehash(stored));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("+210000")]
    [InlineData(" 210000")]
    [InlineData("210000 ")]
    [InlineData(" +210000 ")]
    [InlineData("210000.0")]
    [InlineData("２１００００")]
    [InlineData("2147483648")]
    public void 反復回数は符号と空白のない正の整数だけ受ける(string iterations)
    {
        var stored = AuthenticationCompatibilityValues.Hash32.Replace("$210000$", $"${iterations}$",
            StringComparison.Ordinal);
        Assert.Equal((false, false), hasher.Verify(AuthenticationCompatibilityValues.Password, stored));
        Assert.True(hasher.NeedsRehash(stored));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(63)]
    [InlineData(65)]
    public void 保存ハッシュは32か64バイト以外を拒否する(int hashBytes)
    {
        var stored = $"v1$210000$AQ==${Convert.ToBase64String(new byte[hashBytes])}";
        Assert.Equal((false, false), hasher.Verify("password", stored));
        Assert.True(hasher.NeedsRehash(stored));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void 塩は1バイト以上なら既存値を検証できる(int saltBytes, bool expected)
    {
        var salt = new byte[saltBytes];
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes("password"), salt, 1,
            HashAlgorithmName.SHA512, 64);
        var stored = $"v1$1${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        Assert.Equal((expected, expected), hasher.Verify("password", stored));
        Assert.True(hasher.NeedsRehash(stored));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void 空とnullのパスワードは生成時に例外で検証時は不一致にする(string? password)
    {
        Assert.ThrowsAny<ArgumentException>(() => hasher.Hash(password!));
        Assert.Equal((false, false), hasher.Verify(password, AuthenticationCompatibilityValues.Hash64));
        Assert.Equal((false, false), hasher.Verify(password, AuthenticationCompatibilityValues.Hash32));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void 生成時の反復回数は正数を必須にする(int iterations)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => hasher.Hash("password", iterations));
    }

    [Fact]
    public void 間違った回数で作り直しを促さない()
    {
        var stored = AuthenticationCompatibilityValues.Hash32.Replace("$210000$", "$1000$", StringComparison.Ordinal);
        Assert.Equal((false, false), hasher.Verify(AuthenticationCompatibilityValues.Password, stored));
    }
}
