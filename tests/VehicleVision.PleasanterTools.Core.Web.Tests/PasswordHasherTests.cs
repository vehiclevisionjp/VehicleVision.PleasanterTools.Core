namespace VehicleVision.PleasanterTools.Core.Web.Tests;

public sealed class PasswordHasherTests
{
    [Theory]
    [InlineData(PasswordHashCompatibility.Strict64, AuthenticationCompatibilityValues.Hash64, true)]
    [InlineData(PasswordHashCompatibility.Delimited32, AuthenticationCompatibilityValues.Hash32, false)]
    public void 既存実装が生成した保存値で正しい入力だけ通る(
        PasswordHashCompatibility compatibility, string stored, bool needsRehash)
    {
        var hasher = new PasswordHasher(compatibility);
        Assert.Equal((true, needsRehash), hasher.Verify(AuthenticationCompatibilityValues.Password, stored));
        Assert.Equal((false, false), hasher.Verify("wrong", stored));
    }

    [Theory]
    [InlineData(PasswordHashCompatibility.Strict64, 64)]
    [InlineData(PasswordHashCompatibility.Delimited32, 32)]
    public void 生成は版と回数と塩を持ち形式とランダム性を維持する(
        PasswordHashCompatibility compatibility, int hashBytes)
    {
        var hasher = new PasswordHasher(compatibility);
        var first = hasher.Hash("password", 1000);
        var second = hasher.Hash("password", 1000);
        var parts = first.Split('$');
        Assert.StartsWith("v1$1000$", first, StringComparison.Ordinal);
        Assert.Equal(16, Convert.FromBase64String(parts[2]).Length);
        Assert.Equal(hashBytes, Convert.FromBase64String(parts[3]).Length);
        Assert.NotEqual(first, second);
        Assert.DoesNotContain("password", first, StringComparison.Ordinal);
        Assert.Equal((true, true), hasher.Verify("password", first));
        Assert.Equal((false, false), hasher.Verify("passworD", first));
        Assert.True(hasher.NeedsRehash(first));
        Assert.Equal(210_000, PasswordHasher.CurrentIterations);
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain")]
    [InlineData("v2$1000$AAAA$AAAA")]
    [InlineData("v1$abc$AAAA$AAAA")]
    [InlineData("v1$1000$!!!$!!!")]
    [InlineData("v1$1000$AAAA$AAAA")]
    public void 壊れた書式は通さず失敗時に更新を促さない(string stored)
    {
        foreach (var compatibility in Enum.GetValues<PasswordHashCompatibility>())
        {
            Assert.Equal((false, false), new PasswordHasher(compatibility).Verify("anything", stored));
        }
    }

    [Fact]
    public void 方式をまたいで受け入れない()
    {
        Assert.False(new PasswordHasher(PasswordHashCompatibility.Strict64)
            .Verify(AuthenticationCompatibilityValues.Password, AuthenticationCompatibilityValues.Hash32).Verified);
        Assert.False(new PasswordHasher(PasswordHashCompatibility.Delimited32)
            .Verify(AuthenticationCompatibilityValues.Password, AuthenticationCompatibilityValues.Hash64).Verified);
    }

    [Fact]
    public void 空の入力と回数の解釈の差を維持する()
    {
        var strict = new PasswordHasher(PasswordHashCompatibility.Strict64);
        var delimited = new PasswordHasher(PasswordHashCompatibility.Delimited32);
        Assert.True(strict.Verify("", strict.Hash("", 1000)).Verified);
        Assert.Throws<ArgumentException>(() => delimited.Hash(""));
        Assert.Equal((false, false), delimited.Verify("", AuthenticationCompatibilityValues.Hash32));
        Assert.Throws<ArgumentNullException>(() => strict.Verify(null!, AuthenticationCompatibilityValues.Hash64));
        Assert.Equal((false, false), delimited.Verify(null!, AuthenticationCompatibilityValues.Hash32));
        Assert.False(strict.Verify("x", "v1$0$$").Verified);
        Assert.Throws<ArgumentOutOfRangeException>(() => delimited.Verify("x", "v1$0$$"));
        Assert.Throws<ArgumentOutOfRangeException>(() => strict.Hash("x", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PasswordHasher((PasswordHashCompatibility)99));
    }

    [Fact]
    public void 符号と空白と塩の受理条件を維持する()
    {
        var strict = new PasswordHasher(PasswordHashCompatibility.Strict64);
        var delimited = new PasswordHasher(PasswordHashCompatibility.Delimited32);
        Assert.Equal((true, false), delimited.Verify(AuthenticationCompatibilityValues.Password,
            AuthenticationCompatibilityValues.Hash32.Replace("$210000$", "$ +210000 $", StringComparison.Ordinal)));
        Assert.False(strict.Verify(AuthenticationCompatibilityValues.Password,
            AuthenticationCompatibilityValues.Hash64.Replace("$1000$", "$ +1000 $", StringComparison.Ordinal)).Verified);
        Assert.True(strict.NeedsRehash("broken"));
        Assert.False(delimited.NeedsRehash(AuthenticationCompatibilityValues.Hash32));
    }

    [Fact]
    public void 間違った回数で作り直しを促さない()
    {
        var hasher = new PasswordHasher(PasswordHashCompatibility.Delimited32);
        var stored = AuthenticationCompatibilityValues.Hash32.Replace("$210000$", "$1000$", StringComparison.Ordinal);
        Assert.Equal((false, false), hasher.Verify(AuthenticationCompatibilityValues.Password, stored));
    }
}
