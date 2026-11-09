namespace VehicleVision.PleasanterTools.Core.Cli;

/// <summary>
/// コマンドの実行。終了コード（<see cref="ExitCodes"/>）を返す。
/// 使い方の誤りは <see cref="UsageException"/> を投げる。長い処理は <paramref name="cancellationToken"/> を見て、
/// 安全な所で止める（<see cref="CliApplication"/> が 130 にする）。
/// </summary>
public delegate Task<int> CommandHandler(CommandContext context, CancellationToken cancellationToken);
