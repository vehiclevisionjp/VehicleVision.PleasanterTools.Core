using VehicleVision.PleasanterTools.Core.Cli.Testing;

namespace VehicleVision.PleasanterTools.Core.Cli.Tests;

/// <summary>契約が働いていること。わざと違反させた偽のツールを、契約が検出する。</summary>
public sealed class 契約テストが違反を検出するTests
{
    private const string Frame = """{"command":"run","ok":true,"exitCode":0,"errors":[],"warnings":[]}""";

    private static CliContract Fake(
        Func<IReadOnlyList<string>, TextWriter, TextWriter, Func<string, string?>, int> body,
        CliOutcome outcome = CliOutcome.Success,
        CliJsonKind json = CliJsonKind.None,
        IReadOnlyList<string>? secrets = null,
        bool requiresOk = false) => new()
        {
            Name = "fake",
            Run = (args, stdout, stderr, _, env, _) => Task.FromResult(body(args, stdout, stderr, env)),
            Scenarios =
            [
                new CliScenario
                {
                    Name = "case", Args = ["run"], Outcome = outcome, Json = json,
                    Secrets = secrets ?? [], RequiresOkLine = requiresOk,
                },
            ],
        };

    private static async Task AssertDetectedAsync(CliContract contract, string rule)
    {
        var violations = await CliContractChecker.CheckAsync(contract, rule);
        Assert.NotEmpty(violations);
        Assert.All(violations, v => Assert.Equal(rule, v.Rule));
    }

    private static async Task AssertCleanAsync(CliContract contract, string rule) =>
        Assert.Empty(await CliContractChecker.CheckAsync(contract, rule));

    [Fact]
    public async Task ASCIIでない出力を検出する()
    {
        await AssertDetectedAsync(Fake((_, o, _, _) => { o.WriteLine("完了しました"); return 0; }), CliContractRules.Ascii);
        await AssertDetectedAsync(Fake((_, _, e, _) => { e.WriteLine("[ERROR] ✓"); return 0; }), CliContractRules.Ascii);
        await AssertCleanAsync(Fake((_, o, _, _) => { o.WriteLine("[OK] done"); return 0; }), CliContractRules.Ascii);
    }

    [Fact]
    public async Task stdoutとstderrの取り違えを検出する()
    {
        await AssertDetectedAsync(Fake((_, o, _, _) => { o.WriteLine("[ERROR] x"); return 0; }), CliContractRules.Streams);
        await AssertDetectedAsync(Fake((_, _, e, _) => { e.WriteLine("[OK] x"); return 0; }), CliContractRules.Streams);
        await AssertDetectedAsync(
            Fake((_, o, _, _) => { o.WriteLine("ran"); return 2; }, CliOutcome.UsageError), CliContractRules.Streams);
    }

    [Fact]
    public async Task 誤った終了コードを検出する()
    {
        await AssertDetectedAsync(Fake((_, _, _, _) => 2, CliOutcome.Failed), CliContractRules.ExitCodes);
        await AssertDetectedAsync(Fake((_, _, _, _) => 7), CliContractRules.ExitCodes);
        await AssertDetectedAsync(Fake((_, _, _, _) => throw new InvalidOperationException("x")), CliContractRules.ExitCodes);
        await AssertCleanAsync(Fake((_, _, _, _) => 1, CliOutcome.CheckFailed), CliContractRules.ExitCodes);
    }

    [Fact]
    public async Task 接頭辞の欠落を検出する()
    {
        await AssertDetectedAsync(
            Fake((_, _, e, _) => { e.WriteLine("failed"); return 1; }, CliOutcome.Failed), CliContractRules.Prefixes);
        await AssertDetectedAsync(
            Fake((_, o, _, _) => { o.WriteLine("done"); return 0; }, requiresOk: true), CliContractRules.Prefixes);
    }

    [Fact]
    public async Task JSON以外の混入と枠の欠落を検出する()
    {
        await AssertDetectedAsync(
            Fake((_, o, _, _) => { o.WriteLine("Processing..."); o.WriteLine(Frame); return 0; }, json: CliJsonKind.Result),
            CliContractRules.Json);
        await AssertDetectedAsync(
            Fake((_, o, _, _) => { o.WriteLine("""{"command":"run","ok":true,"exitCode":0,"errors":[]}"""); return 0; }, json: CliJsonKind.Result),
            CliContractRules.Json);
        await AssertDetectedAsync(
            Fake((_, o, _, _) => { o.WriteLine("""{"command":"run","ok":true,"exitCode":1,"errors":[],"warnings":[]}"""); return 0; }, json: CliJsonKind.Result),
            CliContractRules.Json);
        await AssertDetectedAsync(
            Fake((_, o, _, _) => { o.WriteLine("plain text"); return 0; }, json: CliJsonKind.Result), CliContractRules.Json);
        await AssertCleanAsync(
            Fake((_, o, _, _) => { o.WriteLine(Frame); return 0; }, json: CliJsonKind.Result), CliContractRules.Json);
    }

    [Fact]
    public async Task 秘密の出力を検出する()
    {
        await AssertDetectedAsync(
            Fake((_, _, e, _) => { e.WriteLine("[ERROR] bad token abc123"); return 1; }, CliOutcome.Failed, secrets: ["abc123"]),
            CliContractRules.Secrets);
    }

    [Fact]
    public async Task 旧名の検出漏れを検出する()
    {
        var contract = Fake((_, _, _, _) => 0) with
        {
            LegacyEnvironment = new CliLegacyEnvironmentProbe
            {
                Args = ["run"],
                Legacy = new Dictionary<string, string> { ["OLD_NAME"] = "NEW_NAME" },
            },
        };
        await AssertDetectedAsync(contract, CliContractRules.LegacyEnvironment);
    }

    [Fact]
    public async Task 引数の書式の食い違いを検出する()
    {
        var contract = Fake((args, _, _, _) => args.Contains("--n=1") ? 0 : 1) with
        {
            ArgumentForms = [new CliArgumentForms { Name = "n", Forms = [["--n=1"], ["--n", "1"]] }],
        };
        await AssertDetectedAsync(contract, CliContractRules.ArgumentForms);
    }

    [Fact]
    public async Task ヘルプと版の誤りを検出する()
    {
        var tool = ContractTestTool.Spec;
        var toStderr = new CliContract
        {
            Name = "fake",
            Tool = tool,
            Run = (args, o, e, _, _, _) =>
            {
                e.WriteLine("contract-tool help");
                return Task.FromResult(0);
            },
        };
        await AssertDetectedAsync(toStderr, CliContractRules.Help);
        var wrongVersion = toStderr with
        {
            Run = (args, o, e, _, _, _) =>
            {
                o.WriteLine("contract-tool 9.9.9");
                return Task.FromResult(0);
            },
        };
        await AssertDetectedAsync(wrongVersion, CliContractRules.Version);
    }

    [Fact]
    public async Task 既知の違反は検出されることを固定し_解消したら外させる()
    {
        var contract = Fake((_, _, _, _) => 2, CliOutcome.Failed);
        var known = new KnownViolation(CliContractRules.ExitCodes, "case", "理由", "#1");
        var withKnown = contract with { KnownViolations = [known] };
        var pinned = await CliContractChecker.EvaluateAsync(withKnown, CliContractRules.ExitCodes);
        Assert.Empty(pinned.Unexpected);
        Assert.Empty(pinned.Resolved);

        var fixedTool = withKnown with { Run = (_, _, _, _, _, _) => Task.FromResult(1) };
        var resolved = await CliContractChecker.EvaluateAsync(fixedTool, CliContractRules.ExitCodes);
        Assert.Equal([known], resolved.Resolved);

        var unlisted = await CliContractChecker.EvaluateAsync(contract, CliContractRules.ExitCodes);
        Assert.Single(unlisted.Unexpected);
    }
}
