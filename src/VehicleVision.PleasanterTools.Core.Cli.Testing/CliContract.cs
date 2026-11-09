namespace VehicleVision.PleasanterTools.Core.Cli.Testing;

/// <summary>契約の規則の名前（<see cref="ContractViolation.Rule"/>・<see cref="KnownViolation.Rule"/>）。</summary>
public static class CliContractRules
{
    /// <summary>stdout・stderr がすべて ASCII（仕様書 2.1）。</summary>
    public const string Ascii = "Ascii";

    /// <summary>結果は stdout、診断は stderr（仕様書 2.2・2.3）。</summary>
    public const string Streams = "Streams";

    /// <summary>失敗に <c>[ERROR]</c>、成功の要約に <c>[OK]</c>（仕様書 2.3）。</summary>
    public const string Prefixes = "Prefixes";

    /// <summary>終了コードが表（0・1・2・3・130）のとおり（仕様書 4 章）。</summary>
    public const string ExitCodes = "ExitCodes";

    /// <summary><c>--json</c> の stdout が枠つきの JSON だけ（仕様書 3 章）。</summary>
    public const string Json = "Json";

    /// <summary>ヘルプ（仕様書 5.3）。</summary>
    public const string Help = "Help";

    /// <summary><c>--version</c>（仕様書 5.3、決定 U15）。</summary>
    public const string Version = "Version";

    /// <summary>旧名の検出（仕様書 6.1）。</summary>
    public const string LegacyEnvironment = "LegacyEnvironment";

    /// <summary>秘密を出さない（仕様書 2.1）。</summary>
    public const string Secrets = "Secrets";

    /// <summary>引数の書式の違いが同じ結果になる（仕様書 5.1）。</summary>
    public const string ArgumentForms = "ArgumentForms";

    /// <summary>すべての規則。</summary>
    public static IReadOnlyList<string> All { get; } =
        [Ascii, Streams, Prefixes, ExitCodes, Json, Help, Version, LegacyEnvironment, Secrets, ArgumentForms];
}

/// <summary>契約に違反した 1 件。</summary>
/// <param name="Rule">規則（<see cref="CliContractRules"/>）。</param>
/// <param name="Case">シナリオの名前など、どの場面での違反か。</param>
/// <param name="Message">内容。</param>
public sealed record ContractViolation(string Rule, string Case, string Message);

/// <summary>
/// 既知の違反。直す Issue がある違反を、契約テストが「検出すること」を固定して一覧にする。
/// 直したら一覧から外す（外さないと、解消済みなのに残っているとしてテストが失敗する）。
/// </summary>
/// <param name="Rule">規則（<see cref="CliContractRules"/>）。</param>
/// <param name="Case">場面（<see cref="ContractViolation.Case"/> と同じ）。</param>
/// <param name="Reason">なぜ違反か（日本語の 1 文）。</param>
/// <param name="Issue">直す Issue（例: <c>#175</c>）。</param>
public sealed record KnownViolation(string Rule, string Case, string Reason, string Issue);

/// <summary>契約テストに渡す、検査する CLI の定義。</summary>
public sealed record CliContract
{
    /// <summary>名前（失敗の文言に出す）。</summary>
    public required string Name { get; init; }

    /// <summary>1 回の実行の入口。</summary>
    public required CliRunner Run { get; init; }

    /// <summary>
    /// ツールの定義。渡すと、定義から次のシナリオを作って検査する。
    /// ヘルプ（<c>--help</c>・<c>-h</c>・<c>-?</c>・<c>/?</c>・コマンドごと）、<c>--version</c>（<c>--json</c> を含む）、
    /// 使い方の誤り（引数なし・未知のコマンド・未知のオプション・値に秘密を含む場合・<c>--json</c> つき）。
    /// 渡さないもの（<c>--help</c> などを持たない、Web アプリの起動引数など）は、<see cref="Scenarios"/> だけを検査する。
    /// </summary>
    public ToolSpec? Tool { get; init; }

    /// <summary>ツール固有のシナリオ（成功・失敗・<c>--json</c>・秘密など）。</summary>
    public IReadOnlyList<CliScenario> Scenarios { get; init; } = [];

    /// <summary>引数の書式の同値。</summary>
    public IReadOnlyList<CliArgumentForms> ArgumentForms { get; init; } = [];

    /// <summary>旧名の検出。環境変数を読まないツールは省略する。</summary>
    public CliLegacyEnvironmentProbe? LegacyEnvironment { get; init; }

    /// <summary>既知の違反（直す Issue があるもの）。</summary>
    public IReadOnlyList<KnownViolation> KnownViolations { get; init; } = [];

    /// <summary>
    /// <see cref="CliApplication"/> で動くツールの <see cref="CliRunner"/> を作る。
    /// ハンドラーは、各ツールが自分のテストで依存を差し替えたものを渡す。
    /// </summary>
    /// <param name="tool">ツールの定義。</param>
    /// <param name="handlers">コマンド名からハンドラーへの対応。</param>
    /// <param name="completion">動的な値の補完の設定（省略可）。</param>
    public static CliRunner RunnerFor(
        ToolSpec tool,
        IReadOnlyDictionary<string, CommandHandler> handlers,
        CompletionSettings? completion = null)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(handlers);
        return (args, stdout, stderr, input, getEnvironmentVariable, cancellationToken) =>
            CliApplication.RunAsync(
                tool,
                handlers,
                args,
                new CliOutput(stdout, stderr, getEnvironmentVariable, false, false),
                input,
                getEnvironmentVariable,
                cancellationToken,
                completion);
    }
}
