using System.Globalization;

namespace VehicleVision.PleasanterTools.Core.Cli.Testing;

/// <summary>シナリオ 1 回の実行結果。</summary>
internal sealed record CliRun(
    CliScenario Scenario,
    int? ExitCode,
    string Stdout,
    string Stderr,
    Exception? Exception)
{
    public static async Task<CliRun> ExecuteAsync(CliContract contract, CliScenario scenario)
    {
        var (exit, stdout, stderr, exception) = await InvokeAsync(
            contract,
            scenario.Args,
            scenario.Environment,
            scenario.Input,
            scenario.Outcome == CliOutcome.Interrupted,
            scenario.Arrange);
        return new CliRun(scenario, exit, stdout, stderr, exception);
    }

    public static async Task<(int? ExitCode, string Stdout, string Stderr, Exception? Exception)> InvokeAsync(
        CliContract contract,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string>? environment,
        string input,
        bool cancelled,
        Func<Task>? arrange)
    {
        if (arrange is not null)
        {
            await arrange();
        }
        using var stdout = new StringWriter(CultureInfo.InvariantCulture);
        using var stderr = new StringWriter(CultureInfo.InvariantCulture);
        using var reader = new StringReader(input);
        using var cancellation = new CancellationTokenSource();
        if (cancelled)
        {
            cancellation.Cancel();
        }
        try
        {
            var exit = await contract.Run(
                args,
                stdout,
                stderr,
                reader,
                name => environment is not null && environment.TryGetValue(name, out var value) ? value : null,
                cancellation.Token);
            return (exit, stdout.ToString(), stderr.ToString(), null);
        }
        catch (Exception ex)
        {
            return (null, stdout.ToString(), stderr.ToString(), ex);
        }
    }

    public static IReadOnlyList<string> Lines(string text) =>
        [.. text.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0)];
}
