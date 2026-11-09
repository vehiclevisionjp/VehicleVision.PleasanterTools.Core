namespace VehicleVision.PleasanterTools.Core.Cli;

/// <summary>
/// コマンドの実行に渡す文脈。入出力と環境変数を差し替えられるので、プロセスを起動せずに試験できる。
/// </summary>
/// <param name="Options">解析したオプションと位置引数。</param>
/// <param name="Stdout">結果の出力先（stdout。仕様書 2.2）。</param>
/// <param name="Stderr">診断（進行・警告・エラー・確認の質問）の出力先（stderr）。</param>
/// <param name="Input">入力（stdin）。確認の質問の答えなどを読む。</param>
/// <param name="GetEnvironmentVariable">環境変数の取得。</param>
/// <remarks>
/// 出力の Issue（#171）で <c>CliOutput</c>（接頭辞・色・ASCII 化・秘密の伏せ）が入ったら、
/// <paramref name="Stdout"/>・<paramref name="Stderr"/> を <c>CliOutput</c> に置き換える。
/// </remarks>
public sealed record CommandContext(
    ParsedOptions Options,
    TextWriter Stdout,
    TextWriter Stderr,
    TextReader Input,
    Func<string, string?> GetEnvironmentVariable);
