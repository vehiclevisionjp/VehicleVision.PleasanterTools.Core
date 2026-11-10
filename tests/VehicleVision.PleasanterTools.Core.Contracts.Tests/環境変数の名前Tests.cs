namespace VehicleVision.PleasanterTools.Core.Contracts.Tests;

/// <summary>仕様書 6.2 の表（共通の環境変数の名前と旧名の対応）を固定する。</summary>
public sealed class 環境変数の名前Tests
{
    [Fact]
    public void 共通の名前は6_2の表と一致する()
    {
        Assert.Equal("VVPT_PLEASANTER_BASE_URL", PleasanterEnvironmentNames.BaseUrl);
        Assert.Equal("VVPT_PLEASANTER_API_KEY", PleasanterEnvironmentNames.ApiKey);
        Assert.Equal("VVPT_PLEASANTER_API_KEY_FILE", PleasanterEnvironmentNames.ApiKeyFile);
        Assert.Equal("VVPT_PLEASANTER_ALLOW_INSECURE_HTTP", PleasanterEnvironmentNames.AllowInsecureHttp);
        Assert.Equal("VVPT_PLEASANTER_SERVER_VERSION", PleasanterEnvironmentNames.ServerVersion);
    }

    [Fact]
    public void 旧名の対応は6_2の表と一致する()
    {
        var expected = new Dictionary<string, string>
        {
            ["PLEASANTER_BASE_URL"] = "VVPT_PLEASANTER_BASE_URL",
            ["PLEASANTER_API_KEY"] = "VVPT_PLEASANTER_API_KEY",
            ["PLEASANTER_API_KEY_FILE"] = "VVPT_PLEASANTER_API_KEY_FILE",
            ["PLEASANTER_ALLOW_INSECURE_HTTP"] = "VVPT_PLEASANTER_ALLOW_INSECURE_HTTP",
            ["PLEASANTER_SERVER_VERSION"] = "VVPT_PLEASANTER_SERVER_VERSION",
            ["FEEDINGEST_PLEASANTER_BASE_URL"] = "VVPT_PLEASANTER_BASE_URL",
            ["FEEDINGEST_PLEASANTER_API_KEY"] = "VVPT_PLEASANTER_API_KEY",
            ["FEEDINGEST_PLEASANTER_API_KEY_FILE"] = "VVPT_PLEASANTER_API_KEY_FILE",
            ["CALENDARSTYLE_PLEASANTER_API_KEY"] = "VVPT_PLEASANTER_API_KEY",
        };
        Assert.Equal(expected.OrderBy(p => p.Key), PleasanterEnvironmentNames.Legacy.OrderBy(p => p.Key));
    }

    [Fact]
    public void 旧名の行き先はすべて共通の名前()
    {
        var names = new[]
        {
            PleasanterEnvironmentNames.BaseUrl,
            PleasanterEnvironmentNames.ApiKey,
            PleasanterEnvironmentNames.ApiKeyFile,
            PleasanterEnvironmentNames.AllowInsecureHttp,
            PleasanterEnvironmentNames.ServerVersion,
        };
        Assert.All(PleasanterEnvironmentNames.Legacy.Values, v => Assert.Contains(v, names));
    }

    [Fact]
    public void 共通の名前に旧名は含まれない()
    {
        Assert.All(PleasanterEnvironmentNames.Legacy.Keys, k => Assert.DoesNotContain("VVPT_", k));
    }
}
