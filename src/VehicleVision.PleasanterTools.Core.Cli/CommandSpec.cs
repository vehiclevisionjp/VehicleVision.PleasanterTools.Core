namespace VehicleVision.PleasanterTools.Core.Cli;

/// <summary>コマンドの定義。名前は大文字小文字を区別せずに照合する。</summary>
/// <param name="Name">コマンド名（小文字・ハイフン区切り）。<c>help</c>・<c>version</c> は予約語。</param>
/// <param name="Summary">ヘルプの 1 行の説明（ASCII）。</param>
/// <param name="Options">このコマンドのオプション。共通のオプションとは名前が重ならないこと。</param>
/// <param name="AcceptsPositionals">位置引数を受けるか（新しいコマンドでは使わない方針。仕様書 5.1）。</param>
/// <param name="MaxPositionals">受ける位置引数の最大数。<paramref name="AcceptsPositionals"/> が偽のときは無視する。</param>
public sealed record CommandSpec(
    string Name,
    string Summary,
    IReadOnlyList<OptionSpec> Options,
    bool AcceptsPositionals = false,
    int MaxPositionals = int.MaxValue);
