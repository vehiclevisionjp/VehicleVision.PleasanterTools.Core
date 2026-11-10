namespace VehicleVision.PleasanterTools.Core.Cli.Tests;

/// <summary>補完の試験用のツールの定義。列挙・ファイル・動的な値・位置引数・引用が要る説明を持つ。</summary>
internal static class CompletionTestTool
{
    public static ToolSpec Spec { get; } = new(
        "sample-tool",
        "A sample tool for completion tests.",
        "1.0.0",
        [
            new CommandSpec(
                "run",
                "Run the sample. It's [fancy]: yes",
                [
                    new OptionSpec { Name = "dry-run", ShortName = "d", Description = "Show what would be done." },
                    new OptionSpec
                    {
                        Name = "out", ShortName = "o", Kind = OptionKind.Value, ValueName = "PATH",
                        Description = "Output path.", ValueCompletion = OptionValueCompletion.File,
                    },
                    new OptionSpec
                    {
                        Name = "mode", Kind = OptionKind.Value,
                        Description = "Mode: fast or slow [x].", Values = ["fast", "slow", "Mixed.1"],
                    },
                    new OptionSpec
                    {
                        Name = "table", Kind = OptionKind.Value, ValueName = "TABLE",
                        Description = "Table name.", ValueCompletion = OptionValueCompletion.Dynamic,
                    },
                    new OptionSpec { Name = "limit", Kind = OptionKind.Value, Description = "Limit." },
                ]),
            new CommandSpec(
                "files",
                "Takes files.",
                [new OptionSpec { Name = "verbose", ShortName = "v", Description = "Verbose." }],
                AcceptsPositionals: true),
            new CommandSpec("plain", "Nothing.", []),
            CompletionCommand.Spec,
        ],
        [StandardOptions.Json]);

    /// <summary>動的な値を持たないツール（スクリプトに呼び戻しが出ないこと）。</summary>
    public static ToolSpec WithoutDynamic { get; } = new(
        "plain-tool",
        "No dynamic values.",
        "1.0.0",
        [new CommandSpec("go", "Go.", [new OptionSpec { Name = "mode", Kind = OptionKind.Value, Description = "Mode.", Values = ["a", "b"] }])]);
}
