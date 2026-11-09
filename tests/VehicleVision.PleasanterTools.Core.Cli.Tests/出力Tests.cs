using System.Globalization;

namespace VehicleVision.PleasanterTools.Core.Cli.Tests;

/// <summary>仕様書 5.7「出力」の試験。プロセスは起動しない。</summary>
public sealed class 出力Tests
{
    private const string Esc = "\u001b";

    private sealed class Sink : IDisposable
    {
        public StringWriter Stdout { get; } = new(CultureInfo.InvariantCulture) { NewLine = "\n" };

        public StringWriter Stderr { get; } = new(CultureInfo.InvariantCulture) { NewLine = "\n" };

        public void Dispose()
        {
            Stdout.Dispose();
            Stderr.Dispose();
        }
    }

    private static CliOutput Create(
        Sink sink, bool stdoutTty = false, bool stderrTty = false, Func<string, string?>? env = null, Func<string, string>? redact = null) =>
        new(sink.Stdout, sink.Stderr, env ?? TestTool.Env(), stdoutTty, stderrTty, redact);

    [Fact]
    public void 振り分けと接頭辞()
    {
        using var sink = new Sink();
        var output = Create(sink);
        output.Ok("done");
        output.Info("note");
        output.Result("row 1");
        output.Warn("careful");
        output.Error("failed");
        output.Progress("copied 3");
        Assert.Equal("[OK] done\n[INFO] note\nrow 1\n", sink.Stdout.ToString());
        Assert.Equal("[WARN] careful\n[ERROR] failed\ncopied 3\n", sink.Stderr.ToString());
    }

    [Theory]
    // 端末か × NO_COLOR（未設定・空・設定）
    [InlineData(true, null, true)]
    [InlineData(true, "", true)]
    [InlineData(true, "1", false)]
    [InlineData(true, "0", false)]
    [InlineData(false, null, false)]
    [InlineData(false, "", false)]
    [InlineData(false, "1", false)]
    public void 色は端末でNO_COLORが未設定か空のときだけ(bool isTerminal, string? noColor, bool expected)
    {
        var env = noColor is null ? TestTool.Env() : TestTool.Env(("NO_COLOR", noColor));
        Assert.Equal(expected, CliOutput.UseColor(isTerminal, env));

        using var sink = new Sink();
        var output = Create(sink, isTerminal, isTerminal, env);
        output.Ok("a");
        output.Warn("b");
        output.Error("c");
        output.Info("d");
        if (expected)
        {
            Assert.Equal($"{Esc}[32m[OK]{Esc}[0m a\n[INFO] d\n", sink.Stdout.ToString());
            Assert.Equal($"{Esc}[33m[WARN]{Esc}[0m b\n{Esc}[31m[ERROR]{Esc}[0m c\n", sink.Stderr.ToString());
        }
        else
        {
            Assert.Equal("[OK] a\n[INFO] d\n", sink.Stdout.ToString());
            Assert.Equal("[WARN] b\n[ERROR] c\n", sink.Stderr.ToString());
        }
    }

    [Fact]
    public void 色の判定は出力先ごと()
    {
        using var sink = new Sink();
        var output = Create(sink, stdoutTty: false, stderrTty: true);
        output.Ok("a");
        output.Error("b");
        Assert.Equal("[OK] a\n", sink.Stdout.ToString());
        Assert.Equal($"{Esc}[31m[ERROR]{Esc}[0m b\n", sink.Stderr.ToString());
    }

    [Fact]
    public void 接頭辞のない行と本文に色を付けない()
    {
        using var sink = new Sink();
        var output = Create(sink, true, true);
        output.Result("row");
        output.Progress("busy");
        Assert.Equal("row\n", sink.Stdout.ToString());
        Assert.Equal("busy\n", sink.Stderr.ToString());
    }

    [Fact]
    public void 色の指定は標準の16色の前景だけ()
    {
        using var sink = new Sink();
        var output = Create(sink, true, true);
        output.Ok("a");
        output.Warn("b");
        output.Error("c");
        var all = sink.Stdout + sink.Stderr.ToString();
        var codes = System.Text.RegularExpressions.Regex.Matches(all, "\u001b\\[([0-9;]*)m").Select(m => m.Groups[1].Value).ToHashSet();
        Assert.Equal(new HashSet<string> { "32", "33", "31", "0" }, codes);
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a\nb", "a?b")]
    [InlineData("a\r\nb\tc", "a??b?c")]
    [InlineData("日本語", "???")]
    [InlineData("\U0001F600", "?")]
    [InlineData("\u0001x\u007F", "?x?")]
    [InlineData("", "")]
    public void ToAsciiは印字可能なASCII以外を1文字ずつ疑問符にする(string input, string expected)
    {
        Assert.Equal(expected, CliOutput.ToAscii(input));
    }

    [Fact]
    public void 出力はすべてASCIIの1行になる()
    {
        using var sink = new Sink();
        var output = Create(sink);
        output.Ok("日本\nline2");
        output.Error("ファイル\r\n");
        output.Result("a\tb");
        output.Progress("✓ done");
        Assert.Equal("[OK] ???line2\na?b\n", sink.Stdout.ToString());
        Assert.Equal("[ERROR] ??????\n? done\n", sink.Stderr.ToString());
        foreach (var text in new[] { sink.Stdout.ToString(), sink.Stderr.ToString() })
        {
            Assert.True(TestTool.IsAscii(text));
        }
    }

    [Fact]
    public void 秘密を伏せてからASCIIにする()
    {
        using var sink = new Sink();
        var output = Create(sink, redact: text => text.Replace("p@ssあ", "***", StringComparison.Ordinal));
        output.Error("login with p@ssあ failed");
        output.Warn("p@ssあ");
        output.Result("p@ssあ row");
        output.Progress("p@ssあ busy");
        output.Ok("p@ssあ ok");
        output.Info("p@ssあ info");
        var all = sink.Stdout + sink.Stderr.ToString();
        Assert.DoesNotContain("p@ss", all);
        Assert.Contains("[ERROR] login with *** failed", all);
    }

    [Fact]
    public async Task 複数のスレッドから呼んでも行が混ざらない()
    {
        using var sink = new Sink();
        var output = Create(sink);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(n => Task.Run(() =>
        {
            for (var i = 0; i < 200; i++)
            {
                output.Ok($"worker {n} item {i} " + new string('x', 200));
                output.Error($"worker {n} item {i} " + new string('y', 200));
            }
        })));
        var stdoutLines = sink.Stdout.ToString().TrimEnd('\n').Split('\n');
        var stderrLines = sink.Stderr.ToString().TrimEnd('\n').Split('\n');
        Assert.Equal(1600, stdoutLines.Length);
        Assert.Equal(1600, stderrLines.Length);
        Assert.All(stdoutLines, line => Assert.Matches("^\\[OK\\] worker \\d item \\d+ x{200}$", line));
        Assert.All(stderrLines, line => Assert.Matches("^\\[ERROR\\] worker \\d item \\d+ y{200}$", line));
    }
}
