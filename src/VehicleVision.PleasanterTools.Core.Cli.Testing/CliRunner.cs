namespace VehicleVision.PleasanterTools.Core.Cli.Testing;

/// <summary>
/// 契約テストが、検査するコマンドを 1 回実行するための入口。入出力と環境変数は差し替えられるので、プロセスを起動しない。
/// <see cref="CliApplication"/> を使うツールは <see cref="CliContract.RunnerFor"/> で作れる。
/// 使わないもの（例: <c>Console</c> へ直接書く）は、呼び出し側が <paramref name="stdout"/>・<paramref name="stderr"/> へ振り替えて包む。
/// </summary>
/// <param name="args">引数（実行ファイル名は含めない）。</param>
/// <param name="stdout">結果の出力先。</param>
/// <param name="stderr">診断の出力先。</param>
/// <param name="input">入力。</param>
/// <param name="getEnvironmentVariable">環境変数の取得。</param>
/// <param name="cancellationToken">取り消しの合図（Ctrl+C）。</param>
/// <returns>終了コード。</returns>
public delegate Task<int> CliRunner(
    IReadOnlyList<string> args,
    TextWriter stdout,
    TextWriter stderr,
    TextReader input,
    Func<string, string?> getEnvironmentVariable,
    CancellationToken cancellationToken);
