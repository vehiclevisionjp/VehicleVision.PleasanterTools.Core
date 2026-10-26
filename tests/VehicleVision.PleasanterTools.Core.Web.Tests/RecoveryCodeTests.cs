namespace VehicleVision.PleasanterTools.Core.Web.Tests;

public sealed class RecoveryCodeTests
{
    [Fact]
    public void 十本を毎回異なる組で紛らわしい文字を使わず生成する()
    {
        var codes = RecoveryCode.Generate();
        Assert.Equal(10, RecoveryCode.Count);
        Assert.Equal(10, codes.Count);
        Assert.Equal(10, codes.Distinct().Count());
        Assert.NotEqual(codes, RecoveryCode.Generate());
        Assert.All(codes, code => Assert.Matches("^[ABCDEFGHJKMNPQRSTUVWXYZ23456789]{5}-[ABCDEFGHJKMNPQRSTUVWXYZ23456789]{5}$", code));
    }

    [Theory]
    [InlineData("abcde-fghjk")]
    [InlineData("ABCDE FGHJK")]
    [InlineData("  ABCDE-FGHJK  ")]
    public void 既存Delimited32の復旧コードを入力の揺れを吸収して照合する(string code)
    {
        Assert.Equal(32, Convert.FromBase64String(AuthenticationCompatibilityValues.Recovery.Split('$')[3]).Length);
        Assert.Equal("ABCDEFGHJK", RecoveryCode.Normalize(code));
        Assert.True(RecoveryCode.Verify(code, AuthenticationCompatibilityValues.Recovery));
        Assert.False(RecoveryCode.Verify("ABCDE-FGHJL", AuthenticationCompatibilityValues.Recovery));
    }

    [Fact]
    public void 新しい保存値は塩つきPBKDF2で毎回異なり旧SHA256は受けない()
    {
        var first = RecoveryCode.Hash("ABCDE-FGHJK");
        Assert.NotEqual(first, RecoveryCode.Hash("ABCDE-FGHJK"));
        Assert.StartsWith("v1$210000$", first, StringComparison.Ordinal);
        Assert.Equal(64, Convert.FromBase64String(first.Split('$')[3]).Length);
        Assert.True(RecoveryCode.Verify("abcde fghjk", first));
        Assert.False(RecoveryCode.Verify("ABCDE-FGHJK", AuthenticationCompatibilityValues.OldRecoveryHash));
        Assert.False(RecoveryCode.Verify("", AuthenticationCompatibilityValues.Recovery));
    }
}
