using System.Security.Cryptography;

namespace VehicleVision.PleasanterTools.Core.Web.Tests;

public sealed class SecretProtectorTests
{
    private static SecretProtector Create(SecretProtectionCompatibility compatibility) =>
        new(AuthenticationCompatibilityValues.Key, compatibility);

    [Theory]
    [InlineData(SecretProtectionCompatibility.PackedV1, AuthenticationCompatibilityValues.Packed)]
    [InlineData(SecretProtectionCompatibility.DelimitedV2, AuthenticationCompatibilityValues.DelimitedV2)]
    public void 既存の文脈つき暗号文を同じ鍵と文脈だけで読める(
        SecretProtectionCompatibility compatibility, string stored)
    {
        var protector = Create(compatibility);
        Assert.Equal(AuthenticationCompatibilityValues.Secret,
            protector.Unprotect(stored, AuthenticationCompatibilityValues.Context));
        if (compatibility == SecretProtectionCompatibility.PackedV1)
        {
            Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(stored, "wrong"));
            Assert.Throws<ArgumentNullException>(() => protector.Unprotect(stored, null));
        }
        else
        {
            Assert.Null(protector.Unprotect(stored, "wrong"));
            Assert.Null(protector.Unprotect(stored, null));
        }
    }

    [Fact]
    public void 旧v1の文脈なし暗号文は文脈を問わず読めるが別方式には渡さない()
    {
        var protector = Create(SecretProtectionCompatibility.DelimitedV2);
        Assert.Equal(AuthenticationCompatibilityValues.Secret,
            protector.Unprotect(AuthenticationCompatibilityValues.DelimitedV1));
        Assert.Equal(AuthenticationCompatibilityValues.Secret,
            protector.Unprotect(AuthenticationCompatibilityValues.DelimitedV1, "different"));
        Assert.ThrowsAny<CryptographicException>(() => Create(SecretProtectionCompatibility.PackedV1)
            .Unprotect(AuthenticationCompatibilityValues.DelimitedV1, "ctx"));
        Assert.Null(protector.Unprotect(AuthenticationCompatibilityValues.Packed, "ctx"));
    }

    [Theory]
    [InlineData(SecretProtectionCompatibility.PackedV1, "v1:")]
    [InlineData(SecretProtectionCompatibility.DelimitedV2, "v2$")]
    public void 日本語と空を含め暗号化復号でき毎回違う値になる(
        SecretProtectionCompatibility compatibility, string prefix)
    {
        var protector = Create(compatibility);
        foreach (var plain in new[] { "日本語🗓️のキー", "", "secret" })
        {
            var cipher = protector.Protect(plain, "ctx");
            Assert.StartsWith(prefix, cipher, StringComparison.Ordinal);
            Assert.Equal(plain, protector.Unprotect(cipher, "ctx"));
            Assert.NotEqual(cipher, protector.Protect(plain, "ctx"));
        }
    }

    [Fact]
    public void 文脈を省略できるのは既存の区切り書式だけ()
    {
        var delimited = Create(SecretProtectionCompatibility.DelimitedV2);
        var cipher = delimited.Protect("secret");
        Assert.StartsWith("v1$", cipher, StringComparison.Ordinal);
        Assert.Equal("secret", delimited.Unprotect(cipher));
        Assert.Throws<InvalidOperationException>(() => Create(SecretProtectionCompatibility.PackedV1).Protect("x"));
        Assert.Throws<InvalidOperationException>(() => Create(SecretProtectionCompatibility.PackedV1).Unprotect("x"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain")]
    [InlineData("v2:AAAA")]
    [InlineData("v1:!!!")]
    [InlineData("v1:AAAA")]
    [InlineData("v1$AA==$AA==$AA==")]
    [InlineData("v2$AA==$AA==$AA==")]
    public void 壊れた値は既存方式どおりの失敗になる(string stored)
    {
        var packed = Create(SecretProtectionCompatibility.PackedV1);
        if (stored.Length == 0)
        {
            Assert.Throws<ArgumentException>(() => packed.Unprotect(stored, "ctx"));
        }
        else
        {
            Assert.ThrowsAny<CryptographicException>(() => packed.Unprotect(stored, "ctx"));
        }

        Assert.Null(Create(SecretProtectionCompatibility.DelimitedV2).Unprotect(stored, "ctx"));
    }

    [Theory]
    [InlineData(SecretProtectionCompatibility.PackedV1)]
    [InlineData(SecretProtectionCompatibility.DelimitedV2)]
    public void 別の鍵と改ざんを拒否する(SecretProtectionCompatibility compatibility)
    {
        var protector = Create(compatibility);
        var cipher = protector.Protect("secret", "ctx");
        var differentKey = new SecretProtector(SecretProtector.GenerateKey(), compatibility);
        string tampered;
        if (compatibility == SecretProtectionCompatibility.PackedV1)
        {
            var raw = Convert.FromBase64String(cipher[3..]);
            raw[^1] ^= 1;
            tampered = "v1:" + Convert.ToBase64String(raw);
            Assert.ThrowsAny<CryptographicException>(() => differentKey.Unprotect(cipher, "ctx"));
            Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(tampered, "ctx"));
        }
        else
        {
            var parts = cipher.Split('$');
            var raw = Convert.FromBase64String(parts[3]);
            raw[^1] ^= 1;
            parts[3] = Convert.ToBase64String(raw);
            tampered = string.Join('$', parts);
            Assert.Null(differentKey.Unprotect(cipher, "ctx"));
            Assert.Null(protector.Unprotect(tampered, "ctx"));
        }
    }

    [Theory]
    [InlineData("not base64 !!")]
    [InlineData("AAAA")]
    [InlineData("")]
    public void 未設定や短い鍵を自動生成で補わない(string key)
    {
        foreach (var compatibility in Enum.GetValues<SecretProtectionCompatibility>())
        {
            Assert.ThrowsAny<ArgumentException>(() => new SecretProtector(key, compatibility));
        }
    }

    [Fact]
    public void 明示生成した鍵は32バイトで毎回違う()
    {
        var key = SecretProtector.GenerateKey();
        Assert.Equal(32, SecretProtector.DecodeKey(key).Length);
        Assert.NotEqual(key, SecretProtector.GenerateKey());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SecretProtector(key, (SecretProtectionCompatibility)99));
    }
}
