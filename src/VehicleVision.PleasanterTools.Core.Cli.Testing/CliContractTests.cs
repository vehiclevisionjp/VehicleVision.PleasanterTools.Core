using Xunit;

namespace VehicleVision.PleasanterTools.Core.Cli.Testing;

/// <summary>
/// CLI の契約テスト（仕様書 5.7、決定 U16）。各ツールは、検査する CLI の定義（<see cref="CliContract"/>）を返して継承する。
/// 既知の違反（<see cref="CliContract.KnownViolations"/>）は、検出されることを固定する。
/// 一覧にない違反はもちろん、一覧にあるのに検出されなくなったもの（直した）も失敗にして、一覧から外すよう促す。
/// </summary>
public abstract class CliContractTests
{
    /// <summary>検査する CLI の定義を作る。</summary>
    protected abstract CliContract CreateContract();

    private async Task VerifyAsync(string rule)
    {
        var contract = CreateContract();
        var evaluation = await CliContractChecker.EvaluateAsync(contract, rule);
        if (evaluation.Unexpected.Count > 0)
        {
            Assert.Fail(
                $"{contract.Name} が契約（{rule}）に違反している:" + Environment.NewLine
                + string.Join(Environment.NewLine, evaluation.Unexpected.Select(v => $"  [{v.Case}] {v.Message}")));
        }
        if (evaluation.Resolved.Count > 0)
        {
            Assert.Fail(
                $"{contract.Name} の既知の違反（{rule}）が解消されている。KnownViolations から外すこと:" + Environment.NewLine
                + string.Join(Environment.NewLine, evaluation.Resolved.Select(k => $"  [{k.Case}] {k.Issue}")));
        }
    }

    /// <summary>出力が ASCII だけ。</summary>
    [Fact]
    public Task 出力はASCIIだけである() => VerifyAsync(CliContractRules.Ascii);

    /// <summary>結果は stdout、診断は stderr。</summary>
    [Fact]
    public Task 結果はstdoutで診断はstderrである() => VerifyAsync(CliContractRules.Streams);

    /// <summary>失敗に [ERROR]、成功の要約に [OK]。</summary>
    [Fact]
    public Task 接頭辞を付ける() => VerifyAsync(CliContractRules.Prefixes);

    /// <summary>終了コードが表のとおり。</summary>
    [Fact]
    public Task 終了コードは表のとおりである() => VerifyAsync(CliContractRules.ExitCodes);

    /// <summary>--json の stdout が枠つきの JSON だけ。</summary>
    [Fact]
    public Task JSONはstdoutに枠つきで出る() => VerifyAsync(CliContractRules.Json);

    /// <summary>ヘルプ。</summary>
    [Fact]
    public Task ヘルプはstdoutに出て終了コード0である() => VerifyAsync(CliContractRules.Help);

    /// <summary>--version。</summary>
    [Fact]
    public Task バージョンは1行で出る() => VerifyAsync(CliContractRules.Version);

    /// <summary>旧名の検出。</summary>
    [Fact]
    public Task 旧名は検出して案内する() => VerifyAsync(CliContractRules.LegacyEnvironment);

    /// <summary>秘密を出さない。</summary>
    [Fact]
    public Task 秘密は出力に出ない() => VerifyAsync(CliContractRules.Secrets);

    /// <summary>引数の書式の違いが同じ結果になる。</summary>
    [Fact]
    public Task 引数の書式の違いは同じ結果になる() => VerifyAsync(CliContractRules.ArgumentForms);

    /// <summary>既知の違反の一覧の形（規則名・理由・Issue）。</summary>
    [Fact]
    public void 既知の違反の一覧は形が正しい()
    {
        var known = CreateContract().KnownViolations;
        Assert.All(known, k =>
        {
            Assert.Contains(k.Rule, CliContractRules.All);
            Assert.False(string.IsNullOrWhiteSpace(k.Case));
            Assert.False(string.IsNullOrWhiteSpace(k.Reason));
            Assert.Matches("^#[0-9]+$", k.Issue);
        });
        Assert.Equal(known.Count, known.Select(k => (k.Rule, k.Case)).Distinct().Count());
    }
}
