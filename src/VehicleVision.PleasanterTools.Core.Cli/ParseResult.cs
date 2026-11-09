namespace VehicleVision.PleasanterTools.Core.Cli;

/// <summary>解析の結果。引数の誤りは普通の入力なので、例外にせず <see cref="ParseOutcome.Error"/> で返す。</summary>
public sealed class ParseResult
{
    internal ParseResult(ParseOutcome outcome, string? commandName, ParsedOptions options, string? text, bool jsonRequested)
    {
        Outcome = outcome;
        CommandName = commandName;
        Options = options;
        Text = text;
        JsonRequested = jsonRequested;
    }

    /// <summary>結果の種類。</summary>
    public ParseOutcome Outcome { get; }

    /// <summary>コマンド名（定義の綴り）。<see cref="ParseOutcome.Run"/> のとき。<see cref="ParseOutcome.Help"/> は、コマンドのヘルプのときだけ。</summary>
    public string? CommandName { get; }

    /// <summary><see cref="ParseOutcome.Run"/> のときのオプション。それ以外は空。</summary>
    public ParsedOptions Options { get; }

    /// <summary>
    /// <see cref="ParseOutcome.Help"/>: ヘルプ全文（<c>\n</c> 区切り・ASCII）。
    /// <see cref="ParseOutcome.Version"/>: <c>ツール名 バージョン</c> の 1 行。
    /// <see cref="ParseOutcome.Error"/>: ASCII のエラー文（<c>[ERROR]</c> は付けない）。
    /// </summary>
    public string? Text { get; }

    /// <summary>
    /// <c>--json</c> が解釈できたか（<see cref="ParseOutcome.Version"/>・<see cref="ParseOutcome.Error"/> のときに、
    /// 枠つきの JSON で出すかの判定に使う。仕様書 3.2、決定 U4）。
    /// 定義に <see cref="StandardOptions.JsonName"/> があり、値なしのフラグとして指定されたときだけ真。
    /// </summary>
    public bool JsonRequested { get; }
}
