namespace VehicleVision.PleasanterTools.Core.Web.Tests;

public sealed class SecretProtectorTests
{
    private readonly SecretProtector protector = new(AuthenticationCompatibilityValues.Key);
    private const string Context = AuthenticationCompatibilityValues.Context;

    [Theory]
    [InlineData(AuthenticationCompatibilityValues.Packed)]
    [InlineData(AuthenticationCompatibilityValues.DelimitedV2)]
    public void 既存の文脈つき暗号文を同じ鍵と文脈だけで読める(string stored)
    {
        Assert.Equal(AuthenticationCompatibilityValues.Secret, protector.Unprotect(stored, Context));
        Assert.Null(protector.Unprotect(stored, "wrong"));
        Assert.Null(protector.Unprotect(stored, null));
        Assert.Null(protector.Unprotect(stored, ""));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("different")]
    [InlineData(Context)]
    public void 旧v1区切り暗号文だけは文脈を問わず読める(string? context)
    {
        Assert.Equal(AuthenticationCompatibilityValues.Secret,
            protector.Unprotect(AuthenticationCompatibilityValues.DelimitedV1, context));
    }

    [Theory]
    [InlineData("日本語🗓️のキー")]
    [InlineData("")]
    [InlineData("secret")]
    public void 新規は文脈つきv2だけで書き毎回異なる値になる(string plain)
    {
        var cipher = protector.Protect(plain, Context);
        var parts = cipher.Split('$');
        Assert.Equal(4, parts.Length);
        Assert.Equal("v2", parts[0]);
        Assert.Equal(12, Convert.FromBase64String(parts[1]).Length);
        Assert.Equal(16, Convert.FromBase64String(parts[2]).Length);
        Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(plain), Convert.FromBase64String(parts[3]).Length);
        Assert.Equal(plain, protector.Unprotect(cipher, Context));
        Assert.NotEqual(cipher, protector.Protect(plain, Context));
        Assert.Null(protector.Unprotect(cipher, "different"));
        Assert.Null(protector.Unprotect(cipher, null));
        Assert.Null(protector.Unprotect(cipher, ""));
        Assert.Null(protector.Unprotect(cipher.Replace("v2$", "v1$", StringComparison.Ordinal), Context));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void 新規の暗号化は文脈を必須にする(string? context)
    {
        Assert.ThrowsAny<ArgumentException>(() => protector.Protect("secret", context!));
    }

    [Fact]
    public void nullの平文は拒否する()
    {
        Assert.Throws<ArgumentNullException>(() => protector.Protect(null!, Context));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("plain")]
    [InlineData("v2:AAAA")]
    [InlineData("v1:!!!")]
    [InlineData("v1:AAAA")]
    [InlineData("v1:")]
    [InlineData("v1$AA==$AA==$AA==")]
    [InlineData("v2$AA==$AA==$AA==")]
    [InlineData("v1$!!!$!!!$!!!")]
    [InlineData("v2$!!!$!!!$!!!")]
    [InlineData("v1$$$")]
    [InlineData("v2$$$")]
    [InlineData("v2$AA==$AA==$AA==$extra")]
    [InlineData("v3$AA==$AA==$AA==")]
    public void 不正な保存値は例外にせずnullを返す(string? stored)
    {
        Assert.Null(protector.Unprotect(stored, Context));
    }

    [Theory]
    [InlineData(AuthenticationCompatibilityValues.Packed)]
    [InlineData(AuthenticationCompatibilityValues.DelimitedV1)]
    [InlineData(AuthenticationCompatibilityValues.DelimitedV2)]
    public void 旧3書式すべてで別の鍵とnonceとtagと暗号文の改ざんを拒否する(string stored)
    {
        var differentKey = new SecretProtector(SecretProtector.GenerateKey());
        Assert.Null(differentKey.Unprotect(stored, Context));
        for (var component = 0; component < 3; component++)
        {
            string tampered;
            if (stored.StartsWith("v1:", StringComparison.Ordinal))
            {
                var raw = Convert.FromBase64String(stored[3..]);
                raw[component switch { 0 => 0, 1 => 12, _ => 28 }] ^= 1;
                tampered = "v1:" + Convert.ToBase64String(raw);
            }
            else
            {
                var parts = stored.Split('$');
                var raw = Convert.FromBase64String(parts[component + 1]);
                raw[0] ^= 1;
                parts[component + 1] = Convert.ToBase64String(raw);
                tampered = string.Join('$', parts);
            }

            Assert.Null(protector.Unprotect(tampered, Context));
        }
    }

    [Theory]
    [InlineData(AuthenticationCompatibilityValues.Packed)]
    [InlineData(AuthenticationCompatibilityValues.DelimitedV1)]
    [InlineData(AuthenticationCompatibilityValues.DelimitedV2)]
    public void 旧保存値を読み直して保存すると文脈必須のv2になる(string stored)
    {
        var plain = protector.Unprotect(stored, Context);
        Assert.NotNull(plain);
        var updated = protector.Protect(plain, Context);
        Assert.StartsWith("v2$", updated, StringComparison.Ordinal);
        Assert.Equal(AuthenticationCompatibilityValues.Secret, protector.Unprotect(updated, Context));
        Assert.Null(protector.Unprotect(updated, "different"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not base64 !!")]
    [InlineData("AAAA")]
    [InlineData("")]
    [InlineData(" ")]
    public void 未設定や短い鍵を自動生成で補わない(string? key)
    {
        Assert.ThrowsAny<ArgumentException>(() => new SecretProtector(key!));
    }

    [Fact]
    public void 明示生成した鍵は32バイトで毎回違う()
    {
        var key = SecretProtector.GenerateKey();
        Assert.Equal(32, SecretProtector.DecodeKey(key).Length);
        Assert.NotEqual(key, SecretProtector.GenerateKey());
    }
}
