namespace VehicleVision.PleasanterTools.Core.Web.Tests;

public sealed class InvitationTokenTests
{
    [Fact]
    public void 生成値は43文字のURL安全な乱数で毎回異なる()
    {
        var tokens = Enumerable.Range(0, 100).Select(_ => InvitationToken.Create()).ToList();
        Assert.Equal(tokens.Count, tokens.Distinct(StringComparer.Ordinal).Count());
        Assert.All(tokens, token =>
        {
            Assert.Equal(43, token.Length);
            Assert.Matches("^[A-Za-z0-9_-]+$", token);
            Assert.True(InvitationToken.LooksValid(token));
        });
    }

    [Fact]
    public void 既存の小文字16進と一致し旧Base64への照合はしない()
    {
        var hash = InvitationToken.Hash(AuthenticationCompatibilityValues.Token, trim: false);
        Assert.Equal(AuthenticationCompatibilityValues.TokenHash, hash);
        Assert.NotEqual(AuthenticationCompatibilityValues.OldTokenHash, hash);
        Assert.Matches("^[0-9a-f]{64}$", hash);
        Assert.DoesNotContain(AuthenticationCompatibilityValues.Token, hash, StringComparison.Ordinal);
        Assert.NotEqual(hash, InvitationToken.Hash(AuthenticationCompatibilityValues.Token + "a", trim: false));
    }

    [Fact]
    public void 空白除去は明示した場合だけ行い入力の受理を変えない()
    {
        var token = AuthenticationCompatibilityValues.Token;
        Assert.Equal(InvitationToken.Hash(token, trim: true), InvitationToken.Hash($" {token} ", trim: true));
        Assert.NotEqual(InvitationToken.Hash(token, trim: false), InvitationToken.Hash($" {token} ", trim: false));
    }

    [Theory]
    [InlineData(19, false)]
    [InlineData(20, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void 任意の事前検査は従来の長さ境界を維持する(int length, bool expected) =>
        Assert.Equal(expected, InvitationToken.LooksValid(new string('a', length)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0123456789abcdefghijkl!")]
    [InlineData("0123456789abcdefghijkあ")]
    public void 不正な形は事前検査で拒否する(string? token) => Assert.False(InvitationToken.LooksValid(token));
}
