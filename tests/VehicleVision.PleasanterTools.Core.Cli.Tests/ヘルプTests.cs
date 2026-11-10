namespace VehicleVision.PleasanterTools.Core.Cli.Tests;

/// <summary>仕様書 5.7「ヘルプ」の試験。</summary>
public sealed class ヘルプTests
{
    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("-?")]
    [InlineData("/?")]
    [InlineData("--HELP")]
    [InlineData("-H")]
    [InlineData("help")]
    [InlineData("HELP")]
    public void ヘルプの形は先頭で効く(string token)
    {
        var result = TestTool.Parse(token);
        Assert.Equal(ParseOutcome.Help, result.Outcome);
        Assert.Null(result.CommandName);
        Assert.StartsWith("sample-tool - A sample tool for tests.", result.Text);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("-?")]
    [InlineData("/?")]
    public void ヘルプの形はコマンドの後ろでも効きそのコマンドのヘルプになる(string token)
    {
        var result = TestTool.Parse("run", "--dry-run", token);
        Assert.Equal(ParseOutcome.Help, result.Outcome);
        Assert.Equal("run", result.CommandName);
        Assert.StartsWith("sample-tool run - Run the sample.", result.Text);
    }

    [Fact]
    public void ヘルプはエラーより先に判定する()
    {
        // 未知のオプション・値の欠落・重複・必須の欠落・未知のコマンドがあっても、ヘルプ
        Assert.Equal(ParseOutcome.Help, TestTool.Parse("run", "--unknown", "--out", "-h").Outcome);
        Assert.Equal(ParseOutcome.Help, TestTool.Parse("run", "--out", "a", "--out", "b", "/?").Outcome);
        Assert.Equal(ParseOutcome.Help, TestTool.Parse("strict", "-?").Outcome);
        Assert.Equal(ParseOutcome.Help, TestTool.Parse("nonexistent", "--help").Outcome);
        Assert.Equal(ParseOutcome.Help, TestTool.Parse("--help", "--unknown").Outcome);
    }

    [Fact]
    public void ヘルプはコマンドの手前にあってもそのコマンドのヘルプ()
    {
        var result = TestTool.Parse("--help", "run");
        Assert.Equal(ParseOutcome.Help, result.Outcome);
        Assert.Equal("run", result.CommandName);
    }

    [Fact]
    public void 先頭のhelpは後ろのコマンドのヘルプを出す()
    {
        var result = TestTool.Parse("help", "strict");
        Assert.Equal(ParseOutcome.Help, result.Outcome);
        Assert.Equal("strict", result.CommandName);
    }

    [Fact]
    public void 終端のあとのヘルプの形は位置引数()
    {
        var result = TestTool.Parse("files", "--", "-h");
        Assert.Equal(ParseOutcome.Run, result.Outcome);
        Assert.Equal(["-h"], result.Options.Positionals);
    }

    [Fact]
    public void ツールのヘルプは定義から組み立てる()
    {
        var text = TestTool.Parse("--help").Text!;
        Assert.Contains("Usage:\n  sample-tool <command> [options]", text);
        Assert.Contains("Commands:\n  run     Run the sample.\n  strict  A command with a required option.\n", text);
        Assert.Contains("  -h, --help, -?, /?  Show this help.\n", text);
        Assert.Contains("      --version       Show the version.\n", text);
        Assert.Contains("--json", text);
        Assert.Contains("Options can also be written as -name or /name.", text);
        Assert.Contains("0 success, 1 failure, 2 usage error, 3 partially done, 130 interrupted.", text);
    }

    [Fact]
    public void コマンドのヘルプは短縮形と値の名前と必須と既定値と環境変数を出す()
    {
        var run = TestTool.Parse("run", "--help").Text!;
        Assert.Contains("-d, --dry-run", run);
        Assert.Contains("-o, --out <PATH>", run);
        Assert.Contains("Output path.", run);
        Assert.Contains("--parallel <N>", run);
        Assert.Contains("Concurrent copies, 1-16. (default: 1)", run);
        Assert.Contains("Comma separated table names. [env TEST_TABLES]", run);
        Assert.Contains("-y, --yes", run);
        var strict = TestTool.Parse("strict", "--help").Text!;
        Assert.Contains("Required value. (required) [env TEST_MUST]", strict);
        var files = TestTool.Parse("files", "--help").Text!;
        Assert.Contains("sample-tool files [options] [arguments]", files);
        var plain = TestTool.Parse("plain", "--help").Text!;
        Assert.DoesNotContain("Options for", plain);
    }

    [Fact]
    public void ヘルプ全文がすべてASCII()
    {
        var texts = new List<string> { TestTool.Parse("--help").Text!, HelpText.ShortUsage(TestTool.Spec) };
        texts.AddRange(TestTool.Spec.Commands.Select(command => TestTool.Parse(command.Name, "--help").Text!));
        Assert.All(texts, text => Assert.True(TestTool.IsAscii(text), text));
        Assert.All(texts, text => Assert.EndsWith("\n", text));
    }

    [Fact]
    public void 版は1行でツール名と版()
    {
        var result = TestTool.Parse("--version");
        Assert.Equal(ParseOutcome.Version, result.Outcome);
        Assert.Equal("sample-tool 1.2.3", result.Text);
        Assert.Equal(ParseOutcome.Version, TestTool.Parse("run", "/version").Outcome);
    }

    [Fact]
    public void 版もエラーより先で枠の判定のためにjsonを知らせる()
    {
        Assert.False(TestTool.Parse("--version").JsonRequested);
        Assert.True(TestTool.Parse("--version", "--json").JsonRequested);
        Assert.True(TestTool.Parse("--json", "--version", "--unknown").JsonRequested);
        Assert.Equal(ParseOutcome.Version, TestTool.Parse("--version", "--unknown").Outcome);
    }

    [Fact]
    public void jsonが解釈できたときだけ使い方の誤りでも知らせる()
    {
        Assert.True(TestTool.Parse("run", "--json", "--unknown").JsonRequested);
        Assert.True(TestTool.Parse("run", "--unknown", "--json").JsonRequested);
        Assert.False(TestTool.Parse("run", "--unknown").JsonRequested);
        // 値を付けた --json は解釈できていない
        Assert.False(TestTool.Parse("run", "--json=1").JsonRequested);
        // 終端のあとは位置引数
        Assert.False(TestTool.Parse("files", "--", "--json", "--x").JsonRequested);
        Assert.True(TestTool.Parse("run", "--json").JsonRequested);
    }
}
