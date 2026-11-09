using System.Globalization;
using VehicleVision.PleasanterTools.Core.Contracts;

namespace VehicleVision.PleasanterTools.Core.Cli.Tests;

/// <summary>仕様書 5.7「実行の入口」と、終了コード・Ctrl+C の試験。プロセスは起動しない。</summary>
public sealed class 実行の入口Tests
{
    private sealed record Outcome(int Code, string Stdout, string Stderr);

    private static Dictionary<string, CommandHandler> Handlers(CommandHandler? run = null) => new()
    {
        ["run"] = run ?? ((_, _) => Task.FromResult(ExitCodeValues.Success)),
        ["strict"] = (_, _) => Task.FromResult(ExitCodeValues.Success),
        ["files"] = (_, _) => Task.FromResult(ExitCodeValues.Success),
        ["plain"] = (_, _) => Task.FromResult(ExitCodeValues.Success),
    };

    private static async Task<Outcome> Execute(
        string[] args, CommandHandler? run = null, CancellationToken cancellationToken = default, string input = "")
    {
        using var stdout = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        using var stderr = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        using var reader = new StringReader(input);
        var code = await CliApplication.RunAsync(
            TestTool.Spec, Handlers(run), args, stdout, stderr, reader, TestTool.Env(), cancellationToken);
        return new Outcome(code, stdout.ToString(), stderr.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public async Task ハンドラーの戻り値がそのまま返る(int code)
    {
        var result = await Execute(["run"], (_, _) => Task.FromResult(code));
        Assert.Equal(code, result.Code);
        Assert.Empty(result.Stdout);
        Assert.Empty(result.Stderr);
    }

    [Fact]
    public async Task ハンドラーへ解析したオプションと入出力が渡る()
    {
        CommandContext? seen = null;
        var result = await Execute(["run", "--out", "x", "--dry-run"], (context, _) =>
        {
            seen = context;
            context.Output.Result("result line");
            context.Output.Progress("progress line");
            return Task.FromResult(context.Input.ReadLine() == "yes" ? ExitCodeValues.Success : ExitCodeValues.Failure);
        }, input: "yes\n");
        Assert.Equal(0, result.Code);
        Assert.Equal("result line\n", result.Stdout);
        Assert.Equal("progress line\n", result.Stderr);
        Assert.Equal("x", seen!.Options.Value("out"));
        Assert.True(seen.Options.Flag("dry-run"));
    }

    [Fact]
    public async Task ヘルプは標準出力へ出して0()
    {
        var result = await Execute(["run", "--help"]);
        Assert.Equal(0, result.Code);
        Assert.StartsWith("sample-tool run - Run the sample.\n", result.Stdout);
        Assert.Empty(result.Stderr);
    }

    [Fact]
    public async Task 版は1行を標準出力へ出して0()
    {
        var result = await Execute(["--version"]);
        Assert.Equal(0, result.Code);
        Assert.Equal("sample-tool 1.2.3\n", result.Stdout);
        Assert.Empty(result.Stderr);
    }

    [Fact]
    public async Task 引数なしは短い使い方を標準エラーへ出して2()
    {
        var result = await Execute([]);
        Assert.Equal(ExitCodeValues.UsageError, result.Code);
        Assert.Empty(result.Stdout);
        Assert.Contains("Usage:", result.Stderr);
        Assert.Contains("Run 'sample-tool --help' for more information.", result.Stderr);
        Assert.True(TestTool.IsAscii(result.Stderr));
    }

    [Fact]
    public async Task 解析の誤りはERRORと案内を標準エラーへ出して2でハンドラーを呼ばない()
    {
        var called = false;
        var result = await Execute(["run", "--nope"], (_, _) =>
        {
            called = true;
            return Task.FromResult(0);
        });
        Assert.Equal(2, result.Code);
        Assert.False(called);
        Assert.Empty(result.Stdout);
        Assert.Equal("[ERROR] Unknown option: --nope\nRun 'sample-tool --help' for usage.\n", result.Stderr);
    }

    [Fact]
    public async Task ハンドラーのUsageExceptionは2()
    {
        var result = await Execute(["run", "--parallel", "99"], (context, _) =>
        {
            context.Options.Int32("parallel", 1, 16, 1);
            return Task.FromResult(0);
        });
        Assert.Equal(2, result.Code);
        Assert.Equal(
            "[ERROR] Option --parallel must be an integer from 1 to 16.\nRun 'sample-tool --help' for usage.\n",
            result.Stderr);
    }

    [Fact]
    public async Task UsageExceptionの文言のASCII以外と改行は置き換える()
    {
        var result = await Execute(["run"], (_, _) => throw new UsageException("日本語\nline"));
        Assert.Equal(2, result.Code);
        Assert.True(TestTool.IsAscii(result.Stderr));
        Assert.StartsWith("[ERROR] ???" + "?line\n", result.Stderr);
    }

    [Fact]
    public async Task 想定外の例外はERRORを出して1で例外の文言を出さない()
    {
        var result = await Execute(["run"], (_, _) => throw new InvalidOperationException("Password=hunter2"));
        Assert.Equal(1, result.Code);
        Assert.Empty(result.Stdout);
        Assert.Equal("[ERROR] An unexpected error occurred.\n", result.Stderr);
        Assert.DoesNotContain("hunter2", result.Stderr);
    }

    [Fact]
    public async Task 取り消しは130()
    {
        using var cancellation = new CancellationTokenSource();
        var result = await Execute(["run"], async (_, token) =>
        {
            await cancellation.CancelAsync();
            token.ThrowIfCancellationRequested();
            return ExitCodeValues.Success;
        }, cancellation.Token);
        Assert.Equal(130, result.Code);
        Assert.Empty(result.Stderr);
    }

    [Fact]
    public async Task 取り消しの合図が出ていればハンドラーの戻り値より優先して130()
    {
        foreach (var code in new[] { 0, 1, 3 })
        {
            using var cancellation = new CancellationTokenSource();
            var result = await Execute(["run"], async (_, _) =>
            {
                await cancellation.CancelAsync();
                return code;
            }, cancellation.Token);
            Assert.Equal(130, result.Code);
        }
    }

    [Fact]
    public async Task 取り消しの合図が出ていない取り消し例外は想定外の1()
    {
        var result = await Execute(["run"], (_, _) => throw new OperationCanceledException());
        Assert.Equal(1, result.Code);
        Assert.Contains("[ERROR]", result.Stderr);
    }

    [Fact]
    public async Task ハンドラーが足りなければ呼び出し側の誤りとして例外()
    {
        using var stdout = new StringWriter();
        await Assert.ThrowsAsync<ArgumentException>(() => CliApplication.RunAsync(
            TestTool.Spec,
            new Dictionary<string, CommandHandler> { ["run"] = (_, _) => Task.FromResult(0) },
            ["run"], stdout, stdout, TextReader.Null, TestTool.Env(), CancellationToken.None));
    }

    [Fact]
    public async Task コマンド名は定義の綴りでハンドラーに引き当てる()
    {
        var called = string.Empty;
        var handlers = Handlers();
        handlers["strict"] = (_, _) =>
        {
            called = "strict";
            return Task.FromResult(0);
        };
        using var stdout = new StringWriter();
        var code = await CliApplication.RunAsync(
            TestTool.Spec, handlers, ["STRICT", "--must", "v"], stdout, stdout, TextReader.Null, TestTool.Env(),
            CancellationToken.None);
        Assert.Equal(0, code);
        Assert.Equal("strict", called);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 3, 3)]
    [InlineData(3, 0, 3)]
    [InlineData(3, 1, 1)]
    [InlineData(1, 3, 1)]
    [InlineData(1, 130, 130)]
    [InlineData(130, 1, 130)]
    [InlineData(0, 130, 130)]
    [InlineData(3, 130, 130)]
    public void 終了コードの優先は130_1_3_0(int first, int second, int expected)
    {
        Assert.Equal(expected, ExitCodes.Combine(first, second));
    }

    [Fact]
    public void 未知の終了コードの合成は例外()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ExitCodes.Combine(0, 7));
    }

    [Fact]
    public void Ctrl_Cの1回目は取り消しの合図を出し2回目はすぐ終了する()
    {
        using var cancellation = new CancellationTokenSource();
        var first = 0;
        var forced = 0;
        var coordinator = new InterruptCoordinator(cancellation, () => first++, () => forced++);

        Assert.False(cancellation.IsCancellationRequested);
        coordinator.Interrupt();
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(1, first);
        Assert.Equal(0, forced);

        coordinator.Interrupt();
        Assert.Equal(1, first);
        Assert.Equal(1, forced);

        coordinator.Interrupt();
        Assert.Equal(2, forced);
    }

    [Fact]
    public async Task Ctrl_Cの1回目で安全に止めて130を返す()
    {
        using var cancellation = new CancellationTokenSource();
        var coordinator = new InterruptCoordinator(cancellation, () => { }, () => throw new InvalidOperationException());
        var started = new TaskCompletionSource();
        var running = Execute(["run"], async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return ExitCodeValues.Success;
        }, cancellation.Token);

        await started.Task;
        coordinator.Interrupt();
        var result = await running;
        Assert.Equal(130, result.Code);
    }
}
