namespace VehicleVision.PleasanterTools.Core.Cli.Testing;

/// <summary>ツールの定義から作る、共通のシナリオ（ヘルプ・版・使い方の誤り）。</summary>
internal static class GeneratedScenarios
{
    /// <summary>秘密に見立てる値。使い方の誤りの文言に出てはならない。</summary>
    public const string SecretMarker = "contract-secret-value-0123";

    private const string UnknownOption = "--no-such-option-xyz";

    public static IReadOnlyList<CliScenario> For(ToolSpec tool)
    {
        var first = tool.Commands[0].Name;
        var hasJson = tool.CommonOptions?.Any(option => option.Name == StandardOptions.JsonName) == true;
        var list = new List<CliScenario>();

        foreach (var form in new[] { "--help", "-h", "-?", "/?" })
        {
            list.Add(Help($"help {form}", [form]));
        }
        foreach (var command in tool.Commands)
        {
            list.Add(Help($"help {command.Name} --help", [command.Name, "--help"]));
        }
        list.Add(Help("help before error", [first, UnknownOption, "--help"]));

        list.Add(new CliScenario
        {
            Name = "version",
            Args = ["--version"],
            Outcome = CliOutcome.Success,
            Role = CliScenarioRole.Version,
        });
        if (hasJson)
        {
            list.Add(new CliScenario
            {
                Name = "version json",
                Args = ["--version", "--json"],
                Outcome = CliOutcome.Success,
                Json = CliJsonKind.Version,
            });
        }

        list.Add(Usage("usage no arguments", [], requiresErrorLine: false));
        list.Add(Usage("usage unknown command", ["no-such-command-xyz"]));
        list.Add(Usage("usage unknown option", [UnknownOption]));
        list.Add(Usage("usage unknown option of command", [first, UnknownOption]));
        list.Add(Usage("usage secret in unknown option", [first, $"{UnknownOption}={SecretMarker}"], secret: true));
        if (hasJson)
        {
            list.Add(Usage("usage json", [first, "--json", UnknownOption], json: true, command: first));
            list.Add(Usage("usage json without command", ["--json", UnknownOption], json: true, command: tool.Name));
            list.Add(Usage(
                "usage json secret", [first, "--json", $"{UnknownOption}={SecretMarker}"],
                json: true, command: first, secret: true));
        }

        return list;
    }

    private static CliScenario Help(string name, string[] args) => new()
    {
        Name = name,
        Args = args,
        Outcome = CliOutcome.Success,
        Role = CliScenarioRole.Help,
    };

    private static CliScenario Usage(
        string name, string[] args, bool json = false, string? command = null, bool secret = false, bool requiresErrorLine = true) => new()
    {
        Name = name,
        Args = args,
        Outcome = CliOutcome.UsageError,
        Json = json ? CliJsonKind.Result : CliJsonKind.None,
        Command = command,
        RequiresErrorLine = requiresErrorLine,
        Secrets = secret ? [SecretMarker] : [],
    };
}
