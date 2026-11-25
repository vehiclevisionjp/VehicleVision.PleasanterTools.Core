using VehicleVision.PleasanterTools.Core.Contracts;

namespace VehicleVision.PleasanterTools.Core.Cli.Tests;

/// <summary>仕様書 6 章の試験。接頭辞・真偽・旧名を読まないこと（名前そのものは Core.Contracts.Tests）。</summary>
public sealed class 環境変数の名前Tests
{
    private static Func<string, string?> Env(params (string Name, string Value)[] values)
    {
        var map = values.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);
        return name => map.GetValueOrDefault(name);
    }

    [Fact]
    public void 旧名だけが設定されていても新名の未設定として扱う()
    {
        var env = Env(
            ("PLEASANTER_API_KEY", "old-value"),
            ("FEEDINGEST_PLEASANTER_API_KEY", "old-value"),
            ("CALENDARSTYLE_PLEASANTER_API_KEY", "old-value"));

        Assert.Null(PleasanterApiKey.Resolve(env));
    }

    [Theory]
    [InlineData("binary-migrator", "BINARY_MIGRATOR_")]
    [InlineData("feedingest", "FEEDINGEST_")]
    [InlineData("calendar-style-creator", "CALENDAR_STYLE_CREATOR_")]
    public void Prefixはツール_IDから接頭辞を導く(string toolId, string expected)
    {
        Assert.Equal(expected, EnvironmentNames.Prefix(toolId));
    }

    [Fact]
    public void Prefixは空を拒む()
    {
        Assert.Throws<ArgumentException>(() => EnvironmentNames.Prefix(""));
        Assert.Throws<ArgumentNullException>(() => EnvironmentNames.Prefix(null!));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData("1", false)]
    [InlineData("yes", false)]
    [InlineData(" true", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsTrueはtrueだけを真とする(string? value, bool expected)
    {
        Assert.Equal(expected, EnvironmentNames.IsTrue(value));
    }
}
