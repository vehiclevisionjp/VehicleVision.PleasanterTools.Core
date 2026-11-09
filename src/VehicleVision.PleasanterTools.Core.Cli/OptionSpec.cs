namespace VehicleVision.PleasanterTools.Core.Cli;

/// <summary>
/// オプションの定義。名前は接頭辞（<c>--</c>・<c>-</c>・<c>/</c>）なし・小文字・ハイフン区切りの 2 文字以上。
/// ヘルプに出す文字列（説明・値の名前・既定値の表示）は、ASCII の 1 行にする（定義の時点で検査する）。
/// </summary>
public sealed record OptionSpec
{
    /// <summary>名前。例: <c>dry-run</c>。</summary>
    public required string Name { get; init; }

    /// <summary>ヘルプの説明。ASCII の英語 1 行。</summary>
    public required string Description { get; init; }

    /// <summary>種類。既定は <see cref="OptionKind.Flag"/>。</summary>
    public OptionKind Kind { get; init; } = OptionKind.Flag;

    /// <summary>1 文字の短縮形（英小文字 1 文字。例: <c>y</c>）。定義したものだけが使える。</summary>
    public string? ShortName { get; init; }

    /// <summary>ヘルプの値の名前（山括弧なし。例: <c>N</c> は <c>&lt;N&gt;</c> と表示）。省略時は <c>VALUE</c>。</summary>
    public string? ValueName { get; init; }

    /// <summary>必須か。引数か環境変数のどちらにも無ければ使い方の誤り。<see cref="OptionKind.Value"/> だけ。</summary>
    public bool Required { get; init; }

    /// <summary>呼び出し側が決めた環境変数の名前。引数 &gt; 環境変数 &gt; 既定値の順で使う。</summary>
    public string? EnvironmentVariable { get; init; }

    /// <summary>
    /// 既定値。ヘルプの <c>(default: ...)</c> に出し、<see cref="ParsedOptions.Value"/>・<see cref="ParsedOptions.List"/> の
    /// 最後の候補にする。<see cref="OptionKind.Value"/> だけ。
    /// </summary>
    public string? DefaultValue { get; init; }
}
