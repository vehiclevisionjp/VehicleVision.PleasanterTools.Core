namespace VehicleVision.PleasanterTools.Core.Contracts.Tests;

/// <summary>仕様書 6.1 の共通の環境変数の名前を固定する。</summary>
public sealed class 環境変数の名前Tests
{
    [Fact]
    public void 共通の名前は6_1の表と一致する()
    {
        Assert.Equal("VVPT_PLEASANTER_BASE_URL", PleasanterEnvironmentNames.BaseUrl);
        Assert.Equal("VVPT_PLEASANTER_API_KEY", PleasanterEnvironmentNames.ApiKey);
        Assert.Equal("VVPT_PLEASANTER_API_KEY_FILE", PleasanterEnvironmentNames.ApiKeyFile);
        Assert.Equal("VVPT_PLEASANTER_ALLOW_INSECURE_HTTP", PleasanterEnvironmentNames.AllowInsecureHttp);
        Assert.Equal("VVPT_PLEASANTER_SERVER_VERSION", PleasanterEnvironmentNames.ServerVersion);
    }
}
