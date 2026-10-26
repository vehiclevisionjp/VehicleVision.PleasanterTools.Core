using OtpNet;

namespace VehicleVision.PleasanterTools.Core.Web.Tests;

public sealed class TotpServiceTests
{
    private static readonly DateTime Now = AuthenticationCompatibilityValues.TotpUtcNow;
    private const string Secret = AuthenticationCompatibilityValues.Secret;
    private const string Code = AuthenticationCompatibilityValues.TotpCode;
    private readonly TotpService service = new();

    [Theory]
    [InlineData(-30, true)]
    [InlineData(0, true)]
    [InlineData(30, true)]
    [InlineData(-60, false)]
    [InlineData(60, false)]
    public void 旧3アプリと共通の固定コードを前後1枠だけ受け時間枠を返す(int seconds, bool expected)
    {
        var result = service.Verify(Secret, Code, Now.AddSeconds(seconds));
        Assert.Equal((expected, expected ? new DateTimeOffset(Now).ToUnixTimeSeconds() / 30 : 0), result);
    }

    [Theory]
    [InlineData("221236")]
    [InlineData(" 221236 ")]
    [InlineData("221-236")]
    [InlineData("221 236")]
    [InlineData("\t22-1\r\n2\u30003 6-")]
    public void 空白とASCIIハイフンだけを除いて照合する(string code)
    {
        Assert.True(service.Verify(Secret, code, Now).Verified);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" - \t")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("abcdef")]
    [InlineData("a2b2c1d2e3f6")]
    [InlineData("２２１２３６")]
    [InlineData("٢٢١٢٣٦")]
    [InlineData("221–236")]
    [InlineData("221_236")]
    [InlineData("000000")]
    public void ASCII数字6桁以外や不一致は時間枠を返さず拒否する(string? code)
    {
        Assert.Equal((false, 0L), service.Verify(Secret, code, Now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("これは Base32 ではない")]
    [InlineData("!!!")]
    [InlineData("JBSWY3DPEHPK3PX0")]
    [InlineData("A")]
    [InlineData("ABC")]
    [InlineData("ABCDEF")]
    [InlineData("=")]
    [InlineData("========")]
    [InlineData("JBSWY3DPEHPK3PXP=")]
    [InlineData("MZ======")]
    public void 空や不正なBase32の共有鍵は例外にせず不一致にする(string? secret)
    {
        Assert.Equal((false, 0L), service.Verify(secret, Code, Now));
    }

    [Theory]
    [InlineData("MY======")]
    [InlineData("MY")]
    [InlineData("my======")]
    [InlineData("jbswy3dpehpk3pxp")]
    public void 正しいパディング付きと無しと小文字のBase32を受ける(string secret)
    {
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(Now);
        Assert.True(service.Verify(secret, code, Now).Verified);
    }

    [Fact]
    public void 共有鍵は20バイトで毎回異なり生成した数字を照合できる()
    {
        var secret = service.GenerateSecret();
        Assert.Equal(20, Base32Encoding.ToBytes(secret).Length);
        Assert.NotEqual(secret, service.GenerateSecret());
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(Now);
        Assert.True(service.Verify(secret, code, Now).Verified);
    }

    [Fact]
    public void 登録URIは発行者とログインIDを個別にエスケープする()
    {
        Assert.Equal("otpauth://totp/Example%20App:admin%40example.com"
            + "?secret=KEY&issuer=Example%20App&algorithm=SHA1&digits=6&period=30",
            service.BuildUri("Example App", "admin@example.com", "KEY"));
        Assert.Equal("otpauth://totp/Example%3A%2F%26:user%3A%2F%26"
            + "?secret=KEY&issuer=Example%3A%2F%26&algorithm=SHA1&digits=6&period=30",
            service.BuildUri("Example:/&", "user:/&", "KEY"));
    }
}
