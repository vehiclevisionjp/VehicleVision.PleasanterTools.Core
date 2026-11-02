namespace VehicleVision.PleasanterTools.Core.Web.Tests;

public sealed class AcceptLanguageTests
{
    private static readonly IReadOnlySet<string> Supported = new HashSet<string> { "ja", "en", "de" };

    [Theory]
    [InlineData("ja", "ja")]
    [InlineData("en-US,en;q=0.9", "en")]
    [InlineData("fr,de;q=0.8,en;q=0.5", "de")]
    [InlineData("en;q=0.5,de;q=0.9", "de")]
    [InlineData("en,de", "en")]
    [InlineData("ja_JP", "ja")]
    [InlineData("JA", "ja")]
    [InlineData("en;q=0,de;q=0.1", "de")]
    [InlineData("en;q=abc", "en")]
    [InlineData("fr,zh", null)]
    [InlineData("", null)]
    [InlineData("*", null)]
    public void 品質値の高い順に対応する最初の言語を返す(string header, string? expected)
    {
        Assert.Equal(expected, AcceptLanguage.Resolve([header], Supported));
    }

    [Fact]
    public void 複数行のヘッダとnullを扱う()
    {
        Assert.Null(AcceptLanguage.Resolve(null, Supported));
        Assert.Equal("de", AcceptLanguage.Resolve([null, "fr", "de;q=0.5"], Supported));
    }

    [Fact]
    public void 同率は出現順()
    {
        Assert.Equal("de", AcceptLanguage.Resolve(["de;q=0.5,en;q=0.5"], Supported));
    }
}
