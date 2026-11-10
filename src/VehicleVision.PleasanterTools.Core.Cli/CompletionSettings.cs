namespace VehicleVision.PleasanterTools.Core.Cli;

/// <summary>
/// 隠しコマンド <c>__complete</c>（動的な値の呼び戻し）の設定。<see cref="CliApplication"/> へ渡すと、
/// <c>ツール名 __complete 〈オプション名〉 〈入力中の語〉</c> を処理する（ヘルプ・補完の候補には出ない）。
/// </summary>
public sealed record CompletionSettings
{
    /// <summary>時間制限の既定値（2 秒）。</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);

    private readonly TimeSpan _timeout = DefaultTimeout;

    /// <summary>動的な値の候補を返す処理。</summary>
    public required CompletionProvider Provider { get; init; }

    /// <summary>
    /// 時間制限。超えたら候補を出さずに戻る。ツールごとに変えられる（正の値）。
    /// </summary>
    public TimeSpan Timeout
    {
        get => _timeout;
        init
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Timeout must be positive.");
            }
            _timeout = value;
        }
    }
}
