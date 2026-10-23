using OtpNet;

namespace VehicleVision.PleasanterTools.Core.Web.Tests;

public sealed class TotpServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 22, 9, 0, 0, DateTimeKind.Utc);
    private const string Secret = AuthenticationCompatibilityValues.Secret;

    [Theory]
    [InlineData(TotpCompatibility.DigitsOnly)]
    [InlineData(TotpCompatibility.Trimmed)]
    public void 既存実装の固定時刻の数字と時間枠が一致する(TotpCompatibility compatibility)
    {
        var service = new TotpService(compatibility);
        var result = service.Verify(Secret, "221236", Now);
        Assert.True(result.Verified);
        Assert.Equal(new DateTimeOffset(Now).ToUnixTimeSeconds() / 30, result.TimeStep);
        Assert.True(service.Verify(Secret, "221236", Now.AddSeconds(-30)).Verified);
        Assert.True(service.Verify(Secret, "221236", Now.AddSeconds(30)).Verified);
        Assert.False(service.Verify(Secret, "221236", Now.AddSeconds(-90)).Verified);
        Assert.False(service.Verify(Secret, "221236", Now.AddSeconds(90)).Verified);
        Assert.True(service.Verify(Secret, " 221236 ", Now).Verified);
    }

    [Theory]
    [InlineData("221-236")]
    [InlineData("221 236")]
    [InlineData("a2b2c1d2e3f6")]
    public void 数字以外を除く方式と空白だけ除く方式の差を維持する(string code)
    {
        Assert.True(new TotpService(TotpCompatibility.DigitsOnly).Verify(Secret, code, Now).Verified);
        Assert.False(new TotpService(TotpCompatibility.Trimmed).Verify(Secret, code, Now).Verified);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("abcdef")]
    public void 不正な入力は通さない(string code)
    {
        foreach (var compatibility in Enum.GetValues<TotpCompatibility>())
        {
            Assert.False(new TotpService(compatibility).Verify(Secret, code, Now).Verified);
        }
    }

    [Fact]
    public void 壊れた共有鍵の失敗の差を維持する()
    {
        Assert.False(new TotpService(TotpCompatibility.Trimmed).Verify("これは Base32 ではない", "221236", Now).Verified);
        Assert.Throws<ArgumentException>(() => new TotpService(TotpCompatibility.DigitsOnly)
            .Verify("これは Base32 ではない", "221236", Now));
    }

    [Theory]
    [InlineData(TotpCompatibility.DigitsOnly)]
    [InlineData(TotpCompatibility.Trimmed)]
    public void 共有鍵は20バイトで毎回異なり生成した数字を照合できる(TotpCompatibility compatibility)
    {
        var service = new TotpService(compatibility);
        var secret = service.GenerateSecret();
        Assert.Equal(20, Base32Encoding.ToBytes(secret).Length);
        Assert.NotEqual(secret, service.GenerateSecret());
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(Now);
        Assert.True(service.Verify(secret, code, Now).Verified);
    }

    [Fact]
    public void 登録URIの符号化の差を維持する()
    {
        const string query = "?secret=KEY&issuer=Example";
        Assert.Equal("otpauth://totp/Example%20App:admin%40example.com"
            + query + "%20App&algorithm=SHA1&digits=6&period=30",
            new TotpService(TotpCompatibility.DigitsOnly).BuildUri("Example App", "admin@example.com", "KEY"));
        Assert.Equal("otpauth://totp/Example+App%3Aadmin%40example.com"
            + query + "+App&algorithm=SHA1&digits=6&period=30",
            new TotpService(TotpCompatibility.Trimmed).BuildUri("Example App", "admin@example.com", "KEY"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TotpService((TotpCompatibility)99));
    }
}
