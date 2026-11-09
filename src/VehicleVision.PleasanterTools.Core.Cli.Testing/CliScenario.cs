using VehicleVision.PleasanterTools.Core.Contracts;

namespace VehicleVision.PleasanterTools.Core.Cli.Testing;

/// <summary>シナリオが期待する結末（終了コードの表。仕様書 4 章）。</summary>
public enum CliOutcome
{
    /// <summary>成功（0）。</summary>
    Success,

    /// <summary>検査の失敗（1）。<c>validate</c> で問題あり・<c>plan</c> で衝突ありなど。結果なので stdout へ出し、<c>[ERROR]</c> は要らない。</summary>
    CheckFailed,

    /// <summary>実行時の失敗（1）。stderr に <c>[ERROR]</c> を出す。</summary>
    Failed,

    /// <summary>使い方の誤り（2）。何も実行しない。stderr に <c>[ERROR]</c> を出す。</summary>
    UsageError,

    /// <summary>一部だけ完了（3）。</summary>
    Partial,

    /// <summary>中断（130）。取り消しの合図を最初から出した状態で実行する。</summary>
    Interrupted,
}

/// <summary><c>--json</c> の stdout の種類。</summary>
public enum CliJsonKind
{
    /// <summary><c>--json</c> ではない。</summary>
    None,

    /// <summary>結果の枠（<c>command</c>・<c>ok</c>・<c>exitCode</c>・<c>errors</c>・<c>warnings</c>。仕様書 3.1）。</summary>
    Result,

    /// <summary><c>--version --json</c> の枠（<c>command</c>・<c>ok</c>・<c>version</c>。仕様書 3.2）。</summary>
    Version,
}

/// <summary>生成したシナリオの役割（契約が、ヘルプ・版だけの検査を当てるために付ける）。</summary>
internal enum CliScenarioRole
{
    General,
    Help,
    Version,
}

/// <summary>
/// 検査する実行 1 回分。引数・環境変数・期待する結末を書く。結末から終了コードが決まり、
/// 出力の規則（ASCII・stdout と stderr・接頭辞・<c>--json</c> の枠・秘密）は、すべてのシナリオに共通で当たる。
/// </summary>
public sealed record CliScenario
{
    /// <summary>名前。違反の識別子（<see cref="KnownViolation.Case"/>）になるので、契約の中で一意にする。</summary>
    public required string Name { get; init; }

    /// <summary>引数。</summary>
    public required IReadOnlyList<string> Args { get; init; }

    /// <summary>期待する結末。</summary>
    public required CliOutcome Outcome { get; init; }

    /// <summary>環境変数。ここに無い名前は未設定。</summary>
    public IReadOnlyDictionary<string, string>? Environment { get; init; }

    /// <summary>入力（stdin）。</summary>
    public string Input { get; init; } = string.Empty;

    /// <summary><c>--json</c> の stdout の種類。<see cref="CliJsonKind.None"/> でなければ、stdout が JSON 1 つだけであることを検査する。</summary>
    public CliJsonKind Json { get; init; }

    /// <summary><c>--json</c> の枠の <c>command</c> に入るはずの値。省略時は検査しない。</summary>
    public string? Command { get; init; }

    /// <summary>成功のとき、stdout に <c>[OK]</c> の行を 1 つ以上出すか（<see cref="Json"/> が <see cref="CliJsonKind.None"/> のときだけ）。</summary>
    public bool RequiresOkLine { get; init; }

    /// <summary>
    /// 失敗・使い方の誤りのとき、stderr に <c>[ERROR]</c> の行を要求するか。既定は真。
    /// 引数なしで出す短い使い方のように、接頭辞を付けない決まりのときだけ偽にする。
    /// </summary>
    public bool RequiresErrorLine { get; init; } = true;

    /// <summary>出力（stdout・stderr）のどこにも現れてはならない文字列（秘密）。</summary>
    public IReadOnlyList<string> Secrets { get; init; } = [];

    /// <summary>実行の前に呼ぶ準備（DB の作り直しなど）。</summary>
    public Func<Task>? Arrange { get; init; }

    internal CliScenarioRole Role { get; init; }

    /// <summary>期待する終了コード（<see cref="Outcome"/> から決まる）。</summary>
    public int ExitCode => Outcome switch
    {
        CliOutcome.Success => ExitCodeValues.Success,
        CliOutcome.CheckFailed or CliOutcome.Failed => ExitCodeValues.Failure,
        CliOutcome.UsageError => ExitCodeValues.UsageError,
        CliOutcome.Partial => ExitCodeValues.Partial,
        CliOutcome.Interrupted => ExitCodeValues.Interrupted,
        _ => throw new InvalidOperationException("Unknown outcome."),
    };
}

/// <summary>
/// 引数の書式の違い（<c>--name=value</c>・<c>--name value</c> など）が、同じ結果になるかを調べる 1 組。
/// 組の先頭が基準で、残りの終了コードと stdout が基準と同じでなければ違反。
/// </summary>
public sealed record CliArgumentForms
{
    /// <summary>名前。</summary>
    public required string Name { get; init; }

    /// <summary>同じ意味の引数の書き方。2 つ以上。先頭が基準。</summary>
    public required IReadOnlyList<IReadOnlyList<string>> Forms { get; init; }

    /// <summary>各書き方を実行する前に呼ぶ準備。</summary>
    public Func<Task>? Arrange { get; init; }

    /// <summary>環境変数。</summary>
    public IReadOnlyDictionary<string, string>? Environment { get; init; }
}

/// <summary>
/// 旧名の検出（仕様書 6.1、決定 U11・N2）を確かめる設定。旧名が 1 つだけ設定されたとき、
/// 旧名と新名の両方が設定されたときの両方で、終了コード 2 と <c>[ERROR]</c> の案内を出すか。
/// </summary>
public sealed record CliLegacyEnvironmentProbe
{
    /// <summary>旧名の検出に行き着くコマンドの引数（環境変数を読むコマンドを 1 つ選ぶ）。</summary>
    public required IReadOnlyList<string> Args { get; init; }

    /// <summary>旧名 → 新名。省略時は <see cref="PleasanterEnvironmentNames.Legacy"/>。</summary>
    public IReadOnlyDictionary<string, string>? Legacy { get; init; }
}
