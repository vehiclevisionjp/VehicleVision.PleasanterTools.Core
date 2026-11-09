namespace VehicleVision.PleasanterTools.Core.Cli;

/// <summary>終了コード（仕様書 4 章）。各ツールは数字を直書きしない。</summary>
public static class ExitCodes
{
    /// <summary>成功。</summary>
    public const int Success = 0;

    /// <summary>処理の失敗。想定外の例外も。</summary>
    public const int Failure = 1;

    /// <summary>使い方の誤り。何も実行していない。</summary>
    public const int UsageError = 2;

    /// <summary>一部だけ完了（件数の上限で、残りがある）。</summary>
    public const int Partial = 3;

    /// <summary>Ctrl+C などの中断。</summary>
    public const int Interrupted = 130;

    /// <summary>
    /// 複数の結果から、優先の高い終了コードを選ぶ。優先は 130 &gt; 2 &gt; 1 &gt; 3 &gt; 0
    /// （2 は実行前の誤りで、実行結果とは並ばないが、順序を決めておく）。
    /// 5 つの定数以外は <see cref="ArgumentOutOfRangeException"/>。
    /// </summary>
    public static int Combine(int first, int second) => Rank(first) >= Rank(second) ? first : second;

    private static int Rank(int code) => code switch
    {
        Success => 0,
        Partial => 1,
        Failure => 2,
        UsageError => 3,
        Interrupted => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(code), "Unknown exit code."),
    };
}
