namespace VehicleVision.PleasanterTools.Core.Contracts;

/// <summary><c>--json</c> の <c>errors</c> の <c>code</c> のうち、全ツールで共通のもの（仕様書 3.2）。ツール固有のコードは各ツールが持つ。</summary>
public static class ErrorCodes
{
    /// <summary>想定外の例外。文言は固定で、例外の文言は出さない。</summary>
    public const string UnexpectedError = "UnexpectedError";

    /// <summary>使い方の誤り（終了コード 2）。</summary>
    public const string Usage = "Usage";
}
