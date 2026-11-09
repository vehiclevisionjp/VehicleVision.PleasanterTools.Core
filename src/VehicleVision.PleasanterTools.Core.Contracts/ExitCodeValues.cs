namespace VehicleVision.PleasanterTools.Core.Contracts;

/// <summary>
/// 終了コードの値（仕様書 4 章）。各ツール・ライブラリは数字を直書きしない。
/// 複数の結果から選ぶ規則は処理なので、<c>Core.Cli</c> の <c>ExitCodes.Combine</c> が持つ。
/// </summary>
public static class ExitCodeValues
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
}
