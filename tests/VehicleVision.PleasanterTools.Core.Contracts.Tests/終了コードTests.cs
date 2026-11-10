namespace VehicleVision.PleasanterTools.Core.Contracts.Tests;

/// <summary>仕様書 4 章の終了コードの値を固定する。</summary>
public sealed class 終了コードTests
{
    [Fact]
    public void 終了コードの値は4章のとおり()
    {
        Assert.Equal(0, ExitCodeValues.Success);
        Assert.Equal(1, ExitCodeValues.Failure);
        Assert.Equal(2, ExitCodeValues.UsageError);
        Assert.Equal(3, ExitCodeValues.Partial);
        Assert.Equal(130, ExitCodeValues.Interrupted);
    }
}
