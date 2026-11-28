namespace VehicleVision.PleasanterTools.Core.Web.Tests;

/// <summary>
/// 暗号の本体の試験は Core.Security.Tests にある。ここは、Core.Web の <c>SecretProtector</c>（互換の委譲）が
/// 従来どおりの API・書式で動くことだけを確かめる。
/// </summary>
public sealed class SecretProtectorCompatibilityTests
{
    private readonly SecretProtector protector = new(AuthenticationCompatibilityValues.Key);
    private const string Context = AuthenticationCompatibilityValues.Context;

    [Theory]
    [InlineData(AuthenticationCompatibilityValues.Packed)]
    [InlineData(AuthenticationCompatibilityValues.DelimitedV2)]
    public void 既存の暗号文を同じ鍵と文脈で読める(string stored)
    {
        Assert.Equal(AuthenticationCompatibilityValues.Secret, protector.Unprotect(stored, Context));
        Assert.Null(protector.Unprotect(stored, "wrong"));
    }

    [Fact]
    public void 旧v1区切りを文脈なしで読める()
    {
        Assert.Equal(AuthenticationCompatibilityValues.Secret,
            protector.Unprotect(AuthenticationCompatibilityValues.DelimitedV1, null));
    }

    [Fact]
    public void 書いたv2を新しい置き場の型でも読める()
    {
        var cipher = protector.Protect("secret", Context);
        Assert.StartsWith("v2$", cipher, StringComparison.Ordinal);

        var viaSecurity = new Core.Security.SecretProtector(AuthenticationCompatibilityValues.Key);
        Assert.Equal("secret", viaSecurity.Unprotect(cipher, Context));
        Assert.Equal("secret", protector.Unprotect(viaSecurity.Protect("secret", Context), Context));
    }

    [Fact]
    public void 鍵の生成と検査は従来どおり()
    {
        Assert.Equal(32, SecretProtector.DecodeKey(SecretProtector.GenerateKey()).Length);
        Assert.ThrowsAny<ArgumentException>(() => new SecretProtector("AAAA"));
    }
}
