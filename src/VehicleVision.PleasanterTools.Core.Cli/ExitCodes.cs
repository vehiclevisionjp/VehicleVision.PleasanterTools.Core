using VehicleVision.PleasanterTools.Core.Contracts;

namespace VehicleVision.PleasanterTools.Core.Cli;

/// <summary>終了コードの合成（仕様書 4 章）。値そのものは <see cref="ExitCodeValues"/>（Core.Contracts）にある。</summary>
public static class ExitCodes
{
    /// <summary>
    /// 複数の結果から、優先の高い終了コードを選ぶ。優先は 130 &gt; 2 &gt; 1 &gt; 3 &gt; 0
    /// （2 は実行前の誤りで、実行結果とは並ばないが、順序を決めておく）。
    /// <see cref="ExitCodeValues"/> の 5 つの値以外は <see cref="ArgumentOutOfRangeException"/>。
    /// </summary>
    public static int Combine(int first, int second) => Rank(first) >= Rank(second) ? first : second;

    private static int Rank(int code) => code switch
    {
        ExitCodeValues.Success => 0,
        ExitCodeValues.Partial => 1,
        ExitCodeValues.Failure => 2,
        ExitCodeValues.UsageError => 3,
        ExitCodeValues.Interrupted => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(code), "Unknown exit code."),
    };
}
