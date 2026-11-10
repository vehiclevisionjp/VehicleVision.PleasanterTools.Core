using System.Globalization;
using System.Text.Json;
using VehicleVision.PleasanterTools.Core.Contracts;

namespace VehicleVision.PleasanterTools.Core.Cli.Tests;

/// <summary>仕様書 3.2 の <c>--version --json</c>・<c>--json</c> の使い方の誤り（決定 U4）と、色つき出力の入口の試験。</summary>
public sealed class JSONの出力Tests
{
    private sealed record Outcome(int Code, string Stdout, string Stderr);

    private static async Task<Outcome> Execute(string[] args, CommandHandler? run = null, bool tty = false, Func<string, string?>? env = null)
    {
        var handlers = new Dictionary<string, CommandHandler>
        {
            ["run"] = run ?? ((_, _) => Task.FromResult(ExitCodeValues.Success)),
            ["strict"] = (_, _) => Task.FromResult(ExitCodeValues.Success),
            ["files"] = (_, _) => Task.FromResult(ExitCodeValues.Success),
            ["plain"] = (_, _) => Task.FromResult(ExitCodeValues.Success),
        };
        using var stdout = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        using var stderr = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        using var reader = new StringReader(string.Empty);
        var environment = env ?? TestTool.Env();
        var output = new CliOutput(stdout, stderr, environment, tty, tty);
        var code = await CliApplication.RunAsync(TestTool.Spec, handlers, args, output, reader, environment, CancellationToken.None);
        return new Outcome(code, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public async Task 版のjsonは枠の3項目()
    {
        var result = await Execute(["--version", "--json"]);
        Assert.Equal(0, result.Code);
        Assert.Equal("{\"command\":\"version\",\"ok\":true,\"version\":\"1.2.3\"}\n", result.Stdout);
        Assert.Empty(result.Stderr);
    }

    [Fact]
    public async Task 版はjsonなしなら従来の1行()
    {
        var result = await Execute(["--version"]);
        Assert.Equal("sample-tool 1.2.3\n", result.Stdout);
    }

    [Fact]
    public async Task jsonのときの使い方の誤りは枠をstdoutへ_文章をstderrへ出して2()
    {
        var result = await Execute(["run", "--nope", "--json"]);
        Assert.Equal(2, result.Code);
        Assert.Equal(
            "{\"command\":\"run\",\"ok\":false,\"exitCode\":2,\"errors\":[{\"code\":\"Usage\",\"message\":\"Unknown option: --nope\"}],\"warnings\":[]}\n",
            result.Stdout);
        Assert.Equal("[ERROR] Unknown option: --nope\nRun 'sample-tool --help' for usage.\n", result.Stderr);
    }

    [Fact]
    public async Task コマンドが特定できない誤りは_commandがツール名()
    {
        var result = await Execute(["nope", "--json"]);
        Assert.Equal(2, result.Code);
        using var document = JsonDocument.Parse(result.Stdout);
        Assert.Equal("sample-tool", document.RootElement.GetProperty("command").GetString());
        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(2, document.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal("Usage", document.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task jsonがないときの使い方の誤りは枠を出さない()
    {
        var result = await Execute(["run", "--nope"]);
        Assert.Empty(result.Stdout);
    }

    [Fact]
    public async Task jsonが解釈できないときは文章だけ()
    {
        // --json に値を付けると解釈できない
        var result = await Execute(["run", "--json=1"]);
        Assert.Equal(2, result.Code);
        Assert.Empty(result.Stdout);
        Assert.StartsWith("[ERROR] ", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ハンドラーのUsageExceptionもjsonなら枠でstdoutへ()
    {
        var result = await Execute(["run", "--json"], (_, _) => throw new UsageException("Bad 日本 value."));
        Assert.Equal(2, result.Code);
        Assert.Equal(
            "{\"command\":\"run\",\"ok\":false,\"exitCode\":2,\"errors\":[{\"code\":\"Usage\",\"message\":\"Bad ?? value.\"}],\"warnings\":[]}\n",
            result.Stdout);
        Assert.Contains("[ERROR] Bad ?? value.", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task jsonのときstdoutはJSONだけで色のコードも混ざらない()
    {
        var result = await Execute(["run", "--nope", "--json"], tty: true);
        Assert.DoesNotContain("\u001b", result.Stdout, StringComparison.Ordinal);
        Assert.Single(result.Stdout.TrimEnd('\n').Split('\n'));
        using var document = JsonDocument.Parse(result.Stdout);
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        // stderr は端末なので接頭辞に色が付く
        Assert.StartsWith("\u001b[31m[ERROR]\u001b[0m ", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 端末でNO_COLORが設定されていれば入口の出力に色が付かない()
    {
        var result = await Execute(["run", "--nope"], tty: true, env: TestTool.Env(("NO_COLOR", "1")));
        Assert.StartsWith("[ERROR] ", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 想定外の例外のERRORは端末なら赤()
    {
        var result = await Execute(["run"], (_, _) => throw new InvalidOperationException("x"), tty: true);
        Assert.Equal(1, result.Code);
        Assert.Equal("\u001b[31m[ERROR]\u001b[0m An unexpected error occurred.\n", result.Stderr);
    }

    [Fact]
    public async Task ハンドラーはContextのOutputで書き分けられる()
    {
        var result = await Execute(["run"], (context, _) =>
        {
            context.Output.Ok("finished");
            context.Output.Warn("careful");
            return Task.FromResult(ExitCodeValues.Success);
        });
        Assert.Equal("[OK] finished\n", result.Stdout);
        Assert.Equal("[WARN] careful\n", result.Stderr);
    }

    [Fact]
    public async Task jsonで想定外の例外が出力前なら枠をstdoutへ出して1()
    {
        var result = await Execute(["run", "--json"], (_, _) => throw new InvalidOperationException("Password=hunter2"));
        Assert.Equal(1, result.Code);
        Assert.Equal(
            "{\"command\":\"run\",\"ok\":false,\"exitCode\":1,\"errors\":[{\"code\":\"UnexpectedError\",\"message\":\"An unexpected error occurred.\"}],\"warnings\":[]}\n",
            result.Stdout);
        Assert.Equal("[ERROR] An unexpected error occurred.\n", result.Stderr);
        Assert.DoesNotContain("hunter2", result.Stdout + result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task jsonで想定外の例外が出力済みなら枠を足さない()
    {
        var result = await Execute(["run", "--json"], (context, _) =>
        {
            context.Output.WriteJson("run", true, 0, [], []);
            throw new InvalidOperationException("x");
        });
        Assert.Equal(1, result.Code);
        Assert.Single(result.Stdout.TrimEnd('\n').Split('\n'));
        Assert.Contains("\"ok\":true", result.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 結果の行を出した後の想定外の例外も枠を足さない()
    {
        var result = await Execute(["run", "--json"], (context, _) =>
        {
            context.Output.Result("row");
            throw new InvalidOperationException("x");
        });
        Assert.Equal("row\n", result.Stdout);
    }

    [Fact]
    public async Task jsonがなければ想定外の例外で枠を出さない()
    {
        var result = await Execute(["run"], (_, _) => throw new InvalidOperationException("x"));
        Assert.Empty(result.Stdout);
    }

    [Fact]
    public void 出力済みの記録はstdoutだけが対象()
    {
        using var stdout = new StringWriter(CultureInfo.InvariantCulture);
        using var stderr = new StringWriter(CultureInfo.InvariantCulture);
        var output = new CliOutput(stdout, stderr, TestTool.Env(), false, false);
        Assert.False(output.HasWrittenToStdout);
        output.Error("e");
        output.Warn("w");
        output.Progress("p");
        Assert.False(output.HasWrittenToStdout);
        output.Info("i");
        Assert.True(output.HasWrittenToStdout);
    }
}
