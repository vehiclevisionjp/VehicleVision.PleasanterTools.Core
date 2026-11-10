namespace VehicleVision.PleasanterTools.Core.Contracts.Tests;

/// <summary>仕様書 3 章（<c>--json</c> の枠）のキー名とエラーコード名を固定する。</summary>
public sealed class JSONの枠のキーTests
{
    [Fact]
    public void 枠のキー名は3章のとおり()
    {
        Assert.Equal("command", JsonKeys.Command);
        Assert.Equal("ok", JsonKeys.Ok);
        Assert.Equal("exitCode", JsonKeys.ExitCode);
        Assert.Equal("errors", JsonKeys.Errors);
        Assert.Equal("warnings", JsonKeys.Warnings);
        Assert.Equal("version", JsonKeys.Version);
    }

    [Fact]
    public void issueのキー名は3章のとおり()
    {
        Assert.Equal("code", JsonKeys.Code);
        Assert.Equal("message", JsonKeys.Message);
        Assert.Equal("path", JsonKeys.Path);
    }

    [Fact]
    public void 共通のエラーコードは3章のとおり()
    {
        Assert.Equal("UnexpectedError", ErrorCodes.UnexpectedError);
        Assert.Equal("Usage", ErrorCodes.Usage);
    }
}
