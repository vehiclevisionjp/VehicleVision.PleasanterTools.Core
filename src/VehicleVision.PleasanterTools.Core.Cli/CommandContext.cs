namespace VehicleVision.PleasanterTools.Core.Cli;

/// <summary>
/// コマンドの実行に渡す文脈。入出力と環境変数を差し替えられるので、プロセスを起動せずに試験できる。
/// </summary>
/// <param name="Options">解析したオプションと位置引数。</param>
/// <param name="Output">出力（<see cref="CliOutput"/>）。結果は stdout、診断（進行・警告・エラー）は stderr（仕様書 2.2）。</param>
/// <param name="Input">入力（stdin）。確認の質問の答えなどを読む。</param>
/// <param name="GetEnvironmentVariable">環境変数の取得。</param>
public sealed record CommandContext(
    ParsedOptions Options,
    CliOutput Output,
    TextReader Input,
    Func<string, string?> GetEnvironmentVariable);
