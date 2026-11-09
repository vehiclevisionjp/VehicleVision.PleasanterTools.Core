namespace VehicleVision.PleasanterTools.Core.Cli;

/// <summary>値を取るオプションの、値の補完の方式（仕様書 5.5）。列挙の値は <see cref="OptionSpec.Values"/> で指定する。</summary>
public enum OptionValueCompletion
{
    /// <summary>補完しない（<see cref="OptionSpec.Values"/> があればその候補）。</summary>
    None,

    /// <summary>ファイル名（シェルの標準の補完に任せる。パスの区切り・引用はシェルが扱う）。</summary>
    File,

    /// <summary>
    /// 動的な値。<c>ツール名 __complete</c> を呼び戻して <see cref="CompletionProvider"/> から候補を得る
    /// （接続できないときなどは何も出さない）。
    /// </summary>
    Dynamic,
}
