using System.Globalization;
using System.Text;
using VehicleVision.PleasanterTools.Core.Contracts;

namespace VehicleVision.PleasanterTools.Core.Cli.Tests;

/// <summary>仕様書 5.5・5.7「補完」の、生成物と入口（<c>completion</c> コマンド・定義の検査）の試験。</summary>
public sealed class 補完の生成Tests
{
    /// <summary>期待値を作り直すときに、書き出し先のディレクトリを入れる環境変数（通常の試験では未設定）。</summary>
    private const string UpdateGoldenKey = "CLI_COMPLETION_UPDATE_GOLDEN_DIR";

    [Theory]
    [InlineData(CompletionShell.Bash, "completion.bash")]
    [InlineData(CompletionShell.PowerShell, "completion.ps1")]
    [InlineData(CompletionShell.Zsh, "completion.zsh")]
    public void 生成物の文字列が期待値と一致する(CompletionShell shell, string fileName)
    {
        var actual = Completion.Generate(CompletionTestTool.Spec, shell);
        var update = Environment.GetEnvironmentVariable(UpdateGoldenKey);
        if (!string.IsNullOrEmpty(update))
        {
            File.WriteAllText(Path.Combine(update, fileName), actual, new UTF8Encoding(false));
        }
        var expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", fileName)).ReplaceLineEndings("\n");
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(CompletionShell.Bash)]
    [InlineData(CompletionShell.PowerShell)]
    [InlineData(CompletionShell.Zsh)]
    public void 生成物はASCIIでLFだけ(CompletionShell shell)
    {
        var text = Completion.Generate(CompletionTestTool.Spec, shell);
        Assert.DoesNotContain('\r', text);
        Assert.True(TestTool.IsAscii(text));
        Assert.EndsWith("\n", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CompletionShell.Bash)]
    [InlineData(CompletionShell.PowerShell)]
    [InlineData(CompletionShell.Zsh)]
    public void 動的な値があるときだけ呼び戻しを含む(CompletionShell shell)
    {
        Assert.Contains("__complete", Completion.Generate(CompletionTestTool.Spec, shell), StringComparison.Ordinal);
        Assert.DoesNotContain("__complete", Completion.Generate(CompletionTestTool.WithoutDynamic, shell), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CompletionShell.Bash)]
    [InlineData(CompletionShell.PowerShell)]
    [InlineData(CompletionShell.Zsh)]
    public void 秘密になり得る環境変数名や既定値は生成物へ出さない(CompletionShell shell)
    {
        var tool = new ToolSpec(
            "secret-tool", "Tool.", "1.0.0",
            [
                new CommandSpec("go", "Go.",
                [
                    new OptionSpec
                    {
                        Name = "connection", Kind = OptionKind.Value, Description = "Connection.",
                        EnvironmentVariable = "SECRET_TOOL_CONNECTION", DefaultValue = "Server=x;Password=hunter2",
                    },
                ]),
            ]);
        var text = Completion.Generate(tool, shell);
        Assert.DoesNotContain("hunter2", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET_TOOL_CONNECTION", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("powershell", CompletionShell.PowerShell)]
    [InlineData("PWSH", CompletionShell.PowerShell)]
    [InlineData("bash", CompletionShell.Bash)]
    [InlineData("Zsh", CompletionShell.Zsh)]
    public void シェル名を解釈する(string name, CompletionShell expected)
    {
        Assert.True(Completion.TryParseShell(name, out var shell));
        Assert.Equal(expected, shell);
    }

    [Theory]
    [InlineData("fish")]
    [InlineData("")]
    [InlineData(null)]
    public void 未知のシェル名は解釈しない(string? name)
    {
        Assert.False(Completion.TryParseShell(name, out _));
    }

    [Theory]
    [InlineData("tool'x")]
    [InlineData("tool x")]
    [InlineData("to$ol")]
    [InlineData("-tool")]
    public void スクリプトへ安全に埋め込めないツール名は生成しない(string name)
    {
        foreach (var shell in Enum.GetValues<CompletionShell>())
        {
            // ToolSpec 自体が受けない名前（空白）も、受ける名前（引用符・$）も、生成の前に弾かれる
            Assert.Throws<ArgumentException>(() =>
                Completion.Generate(new ToolSpec(name, "Tool.", "1.0.0", [new CommandSpec("go", "Go.", [])]), shell));
        }
    }

    [Fact]
    public void 候補の値は定義の時点で検査する()
    {
        static void Check(OptionSpec option) =>
            Assert.Throws<ArgumentException>(() => new ToolSpec("t", "T.", "1", [new CommandSpec("go", "Go.", [option])]));

        var mode = new OptionSpec { Name = "mode", Kind = OptionKind.Value, Description = "Mode." };
        Check(mode with { Values = [] });
        Check(mode with { Values = ["a b"] });
        Check(mode with { Values = ["a'b"] });
        Check(mode with { Values = ["-a"] });
        Check(mode with { Values = ["a", "a"] });
        Check(mode with { Values = ["日本"] });
        Check(mode with { Values = ["a"], ValueCompletion = OptionValueCompletion.File });
        Check(new OptionSpec { Name = "flag", Description = "Flag.", Values = ["a"] });
        Check(new OptionSpec { Name = "flag", Description = "Flag.", ValueCompletion = OptionValueCompletion.Dynamic });

        Assert.Throws<ArgumentException>(() =>
            new ToolSpec("t", "T.", "1", [new CommandSpec("go", "Go.", [], PositionalValues: ["a"])]));
        Assert.Throws<ArgumentException>(() =>
            new ToolSpec("t", "T.", "1", [new CommandSpec("go", "Go.", [], true, 1, ["a b"])]));
    }

    [Fact]
    public void 補完のコマンドは通常のコマンドで隠しコマンドはヘルプに出ず定義にもできない()
    {
        var help = CommandLineParser.Parse(CompletionTestTool.Spec, ["--help"]).Text!;
        Assert.Contains("completion", help, StringComparison.Ordinal);
        Assert.DoesNotContain("__complete", help, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() =>
            new ToolSpec("t", "T.", "1", [new CommandSpec("__complete", "Hidden.", [])]));
    }

    private sealed record Outcome(int Code, string Stdout, string Stderr);

    private static async Task<Outcome> Execute(params string[] args)
    {
        // 改行は LF のまま出る必要があるので、既定の改行が CRLF の書き込み先で確かめる
        using var stdout = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\r\n" };
        using var stderr = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\r\n" };
        var handlers = new Dictionary<string, CommandHandler>
        {
            ["run"] = (_, _) => Task.FromResult(0),
            ["files"] = (_, _) => Task.FromResult(0),
            ["plain"] = (_, _) => Task.FromResult(0),
            [CompletionCommand.Name] = CompletionCommand.CreateHandler(CompletionTestTool.Spec),
        };
        var code = await CliApplication.RunAsync(
            CompletionTestTool.Spec, handlers, args, stdout, stderr, TextReader.Null, TestTool.Env(), CancellationToken.None);
        return new Outcome(code, stdout.ToString(), stderr.ToString());
    }

    [Theory]
    [InlineData("bash", CompletionShell.Bash)]
    [InlineData("zsh", CompletionShell.Zsh)]
    [InlineData("powershell", CompletionShell.PowerShell)]
    [InlineData("pwsh", CompletionShell.PowerShell)]
    public async Task completionコマンドはスクリプトをLFでstdoutへ出す(string name, CompletionShell shell)
    {
        var result = await Execute("completion", name);
        Assert.Equal(ExitCodeValues.Success, result.Code);
        Assert.Equal(Completion.Generate(CompletionTestTool.Spec, shell), result.Stdout);
        Assert.Empty(result.Stderr);
    }

    [Theory]
    [InlineData("fish")]
    [InlineData("")]
    public async Task completionコマンドは未知のシェルを使い方の誤りにする(string name)
    {
        var result = await Execute("completion", name);
        Assert.Equal(ExitCodeValues.UsageError, result.Code);
        Assert.Empty(result.Stdout);
        Assert.Contains("Unknown shell", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task completionコマンドはシェル名が無ければ使い方の誤り()
    {
        var result = await Execute("completion");
        Assert.Equal(ExitCodeValues.UsageError, result.Code);
        Assert.Empty(result.Stdout);
        Assert.Contains("shell name is required", result.Stderr, StringComparison.Ordinal);
    }
}
