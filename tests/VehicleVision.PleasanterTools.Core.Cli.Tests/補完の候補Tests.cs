using System.Diagnostics;
using System.Globalization;
using VehicleVision.PleasanterTools.Core.Contracts;

namespace VehicleVision.PleasanterTools.Core.Cli.Tests;

/// <summary>
/// 動的な値の候補（<c>__complete</c>）の試験。候補のロジック、時間超過・例外・取り消しのとき何も出さないこと、
/// 秘密が出力に出ないこと、ヘルプ・補完の候補に出ないこと。
/// </summary>
public sealed class 補完の候補Tests
{
    private static CompletionSettings Settings(CompletionProvider provider, TimeSpan? timeout = null) =>
        timeout is null
            ? new CompletionSettings { Provider = provider }
            : new CompletionSettings { Provider = provider, Timeout = timeout.Value };

    private static CompletionProvider Returns(params string[] values) =>
        (_, _, _) => Task.FromResult<IReadOnlyList<string>>(values);

    private static Task<IReadOnlyList<string>> Complete(CompletionSettings settings, string option = "table", string word = "") =>
        Completion.CompleteAsync(CompletionTestTool.Spec, settings, option, word, CancellationToken.None);

    [Fact]
    public async Task 提供元へオプション名と入力中の語が渡る()
    {
        (string Option, string Word)? seen = null;
        var result = await Complete(Settings((option, word, _) =>
        {
            seen = (option, word);
            return Task.FromResult<IReadOnlyList<string>>(["orders"]);
        }), "TABLE", "or");
        Assert.Equal(("table", "or"), seen);
        Assert.Equal(["orders"], result);
    }

    [Fact]
    public async Task 入力中の語で始まる候補だけを大文字小文字を区別せず重複なしで返す()
    {
        var result = await Complete(Settings(Returns("Orders", "orders", "Orders", "users", "OTHER")), word: "o");
        Assert.Equal(["Orders", "orders", "OTHER"], result);
    }

    [Fact]
    public async Task シェルへ安全に渡せない候補は返さない()
    {
        var result = await Complete(Settings(Returns(
            "ok_1", "a b", "a'b", "a\"b", "a$b", "a`b", "a;b", "a|b", "a\\b", "", "日本", "a\nb", "path/x:y@z+1,2=3%~")));
        Assert.Equal(["ok_1", "path/x:y@z+1,2=3%~"], result);
    }

    [Theory]
    [InlineData("limit")]
    [InlineData("mode")]
    [InlineData("out")]
    [InlineData("unknown")]
    [InlineData("")]
    public async Task 動的でないオプションや未知のオプションは提供元を呼ばず空(string option)
    {
        var called = false;
        var result = await Complete(Settings((_, _, _) =>
        {
            called = true;
            return Task.FromResult<IReadOnlyList<string>>(["x"]);
        }), option);
        Assert.False(called);
        Assert.Empty(result);
    }

    [Fact]
    public async Task 提供元の例外は何も出さずに空を返す()
    {
        Assert.Empty(await Complete(Settings((_, _, _) => throw new InvalidOperationException("Server=x;Password=hunter2"))));
        Assert.Empty(await Complete(Settings((_, _, _) => Task.FromException<IReadOnlyList<string>>(new TimeoutException("hunter2")))));
        Assert.Empty(await Complete(Settings((_, _, _) => Task.FromResult<IReadOnlyList<string>>(null!))));
    }

    [Fact]
    public async Task 時間を超えたら取り消しの合図を出して空を返す()
    {
        var token = CancellationToken.None;
        var stopwatch = Stopwatch.StartNew();
        var result = await Complete(Settings(async (_, _, cancellationToken) =>
        {
            token = cancellationToken;
            await Task.Delay(TimeSpan.FromSeconds(30), CancellationToken.None);
            return ["late"];
        }, TimeSpan.FromMilliseconds(200)));
        stopwatch.Stop();
        Assert.Empty(result);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"took {stopwatch.Elapsed}");
        Assert.True(token.IsCancellationRequested);
    }

    [Fact]
    public async Task 取り消しを無視して同期的に待つ提供元でも制限内に戻る()
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await Complete(Settings((_, _, _) =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(5));
            return Task.FromResult<IReadOnlyList<string>>(["late"]);
        }, TimeSpan.FromMilliseconds(200)));
        stopwatch.Stop();
        Assert.Empty(result);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(4), $"took {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task 呼び出し側の取り消しでも何も出さずに戻る()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var result = await Completion.CompleteAsync(
            CompletionTestTool.Spec, Settings(Returns("orders")), "table", "", cancellation.Token);
        Assert.Empty(result);
    }

    [Fact]
    public void 時間制限の既定は2秒でツールごとに変えられ正の値だけ()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), CompletionSettings.DefaultTimeout);
        Assert.Equal(CompletionSettings.DefaultTimeout, Settings(Returns()).Timeout);
        Assert.Equal(TimeSpan.FromSeconds(5), Settings(Returns(), TimeSpan.FromSeconds(5)).Timeout);
        Assert.Throws<ArgumentOutOfRangeException>(() => Settings(Returns(), TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => Settings(Returns(), TimeSpan.FromSeconds(-1)));
    }

    private sealed record Outcome(int Code, string Stdout, string Stderr);

    private static async Task<Outcome> Hidden(
        CompletionSettings? settings, string[] args, Func<string, string>? redact = null)
    {
        using var stdout = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\r\n" };
        using var stderr = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\r\n" };
        var output = new CliOutput(stdout, stderr, TestTool.Env(), false, false, redact);
        var handlers = new Dictionary<string, CommandHandler>
        {
            ["run"] = (_, _) => Task.FromResult(0),
            ["files"] = (_, _) => Task.FromResult(0),
            ["plain"] = (_, _) => Task.FromResult(0),
            ["completion"] = CompletionCommand.CreateHandler(CompletionTestTool.Spec),
        };
        var code = await CliApplication.RunAsync(
            CompletionTestTool.Spec, handlers, args, output, TextReader.Null, TestTool.Env(), CancellationToken.None, settings);
        return new Outcome(code, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public async Task 隠しコマンドは候補を1行1つでLFのまま出す()
    {
        var result = await Hidden(Settings(Returns("orders", "users")), ["__complete", "table", ""]);
        Assert.Equal(0, result.Code);
        Assert.Equal("orders\nusers\n", result.Stdout);
        Assert.Empty(result.Stderr);
    }

    [Fact]
    public async Task 隠しコマンドは入力中の語を省略しても動く()
    {
        // Windows PowerShell 5.1 は、空の引数をネイティブコマンドへ渡さない
        var result = await Hidden(Settings(Returns("orders", "users")), ["__complete", "table"]);
        Assert.Equal("orders\nusers\n", result.Stdout);
    }

    [Theory]
    [InlineData("throw")]
    [InlineData("timeout")]
    [InlineData("unknown-option")]
    [InlineData("no-args")]
    public async Task 隠しコマンドは失敗しても何も出さず終了コード0(string kind)
    {
        var settings = kind switch
        {
            "throw" => Settings((_, _, _) => throw new InvalidOperationException("Server=x;Password=hunter2")),
            "timeout" => Settings(
                async (_, _, _) =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), CancellationToken.None);
                    return [];
                },
                TimeSpan.FromMilliseconds(100)),
            _ => Settings(Returns("orders")),
        };
        string[] args = kind switch
        {
            "unknown-option" => ["__complete", "nothing", ""],
            "no-args" => ["__complete"],
            _ => ["__complete", "table", ""],
        };
        var result = await Hidden(settings, args);
        Assert.Equal(0, result.Code);
        Assert.Empty(result.Stdout);
        Assert.Empty(result.Stderr);
    }

    [Fact]
    public async Task 秘密を伏せる関数で値が変わる候補は出さない()
    {
        var result = await Hidden(
            Settings(Returns("orders", "hunter2-token", "users")),
            ["__complete", "table", ""],
            text => text.Replace("hunter2", "***", StringComparison.Ordinal));
        Assert.Equal("orders\nusers\n", result.Stdout);
        Assert.DoesNotContain("hunter2", result.Stdout + result.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("***", result.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 設定を渡さないツールでは隠しコマンドは未知のコマンドになる()
    {
        var result = await Hidden(null, ["__complete", "table", ""]);
        Assert.Equal(ExitCodeValues.UsageError, result.Code);
        Assert.Contains("Unknown", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 隠しコマンドはヘルプと使い方の誤りに現れない()
    {
        var settings = Settings(Returns("orders"));
        var help = await Hidden(settings, ["--help"]);
        Assert.DoesNotContain("__complete", help.Stdout, StringComparison.Ordinal);
        var noArgs = await Hidden(settings, []);
        Assert.DoesNotContain("__complete", noArgs.Stderr, StringComparison.Ordinal);
        var unknown = await Hidden(settings, ["nope"]);
        Assert.DoesNotContain("__complete", unknown.Stderr, StringComparison.Ordinal);
    }
}
