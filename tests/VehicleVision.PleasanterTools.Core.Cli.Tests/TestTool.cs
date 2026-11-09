namespace VehicleVision.PleasanterTools.Core.Cli.Tests;

/// <summary>試験用のツールの定義と、環境変数・実行の差し替え。</summary>
internal static class TestTool
{
    public static ToolSpec Spec { get; } = new(
        "sample-tool",
        "A sample tool for tests.",
        "1.2.3",
        [
            new CommandSpec(
                "run",
                "Run the sample.",
                [
                    new OptionSpec { Name = "dry-run", ShortName = "d", Description = "Show what would be done." },
                    new OptionSpec
                    {
                        Name = "out", ShortName = "o", Kind = OptionKind.Value, ValueName = "PATH",
                        Description = "Output path.",
                    },
                    new OptionSpec { Name = "offset", Kind = OptionKind.Value, Description = "Offset." },
                    new OptionSpec
                    {
                        Name = "parallel", Kind = OptionKind.Value, ValueName = "N", DefaultValue = "1",
                        Description = "Concurrent copies, 1-16.",
                    },
                    new OptionSpec
                    {
                        Name = "tables", Kind = OptionKind.Value, EnvironmentVariable = "TEST_TABLES",
                        Description = "Comma separated table names.",
                    },
                    new OptionSpec { Name = "exclude", Kind = OptionKind.Value, Description = "Excluded names." },
                    new OptionSpec
                    {
                        Name = "limit", Kind = OptionKind.Value, EnvironmentVariable = "TEST_LIMIT",
                        Description = "Limit.",
                    },
                    new OptionSpec
                    {
                        Name = "quiet", EnvironmentVariable = "TEST_QUIET", Description = "Be quiet.",
                    },
                    StandardOptions.Yes,
                ]),
            new CommandSpec(
                "strict",
                "A command with a required option.",
                [
                    new OptionSpec
                    {
                        Name = "must", Kind = OptionKind.Value, Required = true, EnvironmentVariable = "TEST_MUST",
                        Description = "Required value.",
                    },
                ]),
            new CommandSpec(
                "files",
                "A command that takes positional arguments.",
                [new OptionSpec { Name = "verbose", ShortName = "v", Description = "Verbose." }],
                AcceptsPositionals: true,
                MaxPositionals: 2),
            new CommandSpec("plain", "A command without options.", []),
        ],
        [StandardOptions.Json]);

    public static Func<string, string?> Env(params (string Name, string Value)[] values)
    {
        var map = values.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);
        return name => map.GetValueOrDefault(name);
    }

    public static ParseResult Parse(params string[] args) => CommandLineParser.Parse(Spec, args, Env());

    public static ParseResult Parse(Func<string, string?> env, params string[] args) =>
        CommandLineParser.Parse(Spec, args, env);

    /// <summary>成功することを確かめて、解析したオプションを返す。</summary>
    public static ParsedOptions Run(params string[] args)
    {
        var result = Parse(args);
        Assert.True(result.Outcome == ParseOutcome.Run, $"{string.Join(' ', args)} => {result.Outcome}: {result.Text}");
        return result.Options;
    }

    /// <summary>誤りになることを確かめて、エラー文を返す。</summary>
    public static string Error(params string[] args)
    {
        var result = Parse(args);
        Assert.Equal(ParseOutcome.Error, result.Outcome);
        return result.Text!;
    }

    public static bool IsAscii(string text) => text.All(c => c == '\n' || c is >= ' ' and <= '~');
}
