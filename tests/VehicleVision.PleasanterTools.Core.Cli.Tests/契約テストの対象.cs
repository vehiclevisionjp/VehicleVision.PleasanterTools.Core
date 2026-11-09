using VehicleVision.PleasanterTools.Core.Cli.Testing;
using VehicleVision.PleasanterTools.Core.Contracts;

namespace VehicleVision.PleasanterTools.Core.Cli.Tests;

/// <summary>契約テストを Core.Cli 自身に当てるための、試験用のツール（正しく動くもの）。</summary>
internal static class ContractTestTool
{
    public static ToolSpec Spec { get; } = new(
        "contract-tool",
        "A tool for contract tests.",
        "2.0.1",
        [
            new CommandSpec("run", "Run it.", []),
            new CommandSpec("check", "Check it; fails when told to.", []),
            new CommandSpec("partial", "Finish only a part.", []),
            new CommandSpec("boom", "Throw an unexpected exception.", []),
        ],
        [StandardOptions.Json]);

    public static IReadOnlyDictionary<string, CommandHandler> Handlers { get; } = new Dictionary<string, CommandHandler>
    {
        ["run"] = (context, _) =>
        {
            var legacy = LegacyEnvironmentCheck.Report(context.Output, context.GetEnvironmentVariable);
            if (legacy != ExitCodeValues.Success)
            {
                return Task.FromResult(legacy);
            }
            if (context.Options.Has(StandardOptions.JsonName))
            {
                context.Output.WriteJson("run", true, ExitCodeValues.Success, [], []);
            }
            else
            {
                context.Output.Progress("Working...");
                context.Output.Ok("Done.");
            }
            return Task.FromResult(ExitCodeValues.Success);
        },
        ["check"] = (context, _) =>
        {
            if (context.Options.Has(StandardOptions.JsonName))
            {
                context.Output.WriteJson(
                    "check", false, ExitCodeValues.Failure, [new CliIssue("ConfigInvalid", "sourceId is duplicated.", "sources[1].id")], [],
                    writer => writer.WriteNumber("checkedCount", 2));
            }
            else
            {
                context.Output.Result("1 problem found.");
            }
            return Task.FromResult(ExitCodeValues.Failure);
        },
        ["partial"] = (context, _) =>
        {
            if (context.Options.Has(StandardOptions.JsonName))
            {
                context.Output.WriteJson(
                    "partial", false, ExitCodeValues.Partial, [], [new CliIssue("MoreRemain", "The limit was reached.")]);
            }
            return Task.FromResult(ExitCodeValues.Partial);
        },
        ["boom"] = (_, _) => throw new InvalidOperationException(Secret),
    };

    public const string Secret = "boom-secret-9876";

    public static CliRunner Runner { get; } = CliContract.RunnerFor(Spec, Handlers);
}

/// <summary>契約テストを、Core.Cli 自身（<see cref="CliApplication"/> と試験用の <see cref="ToolSpec"/>）に当てる。</summary>
public sealed class CliApplicationContractTests : CliContractTests
{
    protected override CliContract CreateContract() => new()
    {
        Name = "Core.Cli",
        Run = ContractTestTool.Runner,
        Tool = ContractTestTool.Spec,
        LegacyEnvironment = new CliLegacyEnvironmentProbe { Args = ["run"] },
        Scenarios =
        [
            new CliScenario { Name = "run", Args = ["run"], Outcome = CliOutcome.Success, RequiresOkLine = true },
            new CliScenario { Name = "run json", Args = ["run", "--json"], Outcome = CliOutcome.Success, Json = CliJsonKind.Result, Command = "run" },
            new CliScenario { Name = "check", Args = ["check"], Outcome = CliOutcome.CheckFailed },
            new CliScenario { Name = "check json", Args = ["check", "--json"], Outcome = CliOutcome.CheckFailed, Json = CliJsonKind.Result, Command = "check" },
            new CliScenario { Name = "partial", Args = ["partial"], Outcome = CliOutcome.Partial },
            new CliScenario { Name = "partial json", Args = ["partial", "--json"], Outcome = CliOutcome.Partial, Json = CliJsonKind.Result, Command = "partial" },
            new CliScenario
            {
                Name = "unexpected exception", Args = ["boom"], Outcome = CliOutcome.Failed, Secrets = [ContractTestTool.Secret],
            },
            new CliScenario
            {
                Name = "unexpected exception json", Args = ["boom", "--json"], Outcome = CliOutcome.Failed,
                Json = CliJsonKind.Result, Command = "boom", Secrets = [ContractTestTool.Secret],
            },
            new CliScenario { Name = "interrupted", Args = ["run"], Outcome = CliOutcome.Interrupted },
            new CliScenario
            {
                Name = "api key in environment", Args = ["run"], Outcome = CliOutcome.Success, RequiresOkLine = true,
                Environment = new Dictionary<string, string> { [PleasanterEnvironmentNames.ApiKey] = "api-key-secret-5555" },
                Secrets = ["api-key-secret-5555"],
            },
        ],
        ArgumentForms =
        [
            new CliArgumentForms { Name = "json", Forms = [["run", "--json"], ["run", "-json"], ["run", "/json"]] },
        ],
    };
}
