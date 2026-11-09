namespace VehicleVision.PleasanterTools.Core.Cli.Tests;

/// <summary>仕様書 5.7「環境変数」の試験。引数 &gt; 環境変数 &gt; 既定値。</summary>
public sealed class 環境変数Tests
{
    [Fact]
    public void 引数が環境変数より優先される()
    {
        var env = TestTool.Env(("TEST_TABLES", "from-env"));
        var options = TestTool.Parse(env, "run", "--tables", "from-arg").Options;
        Assert.Equal("from-arg", options.Value("tables"));
    }

    [Fact]
    public void 引数が無ければ環境変数を使う()
    {
        var env = TestTool.Env(("TEST_TABLES", "from-env"));
        Assert.Equal("from-env", TestTool.Parse(env, "run").Options.Value("tables"));
        Assert.True(TestTool.Parse(env, "run").Options.Has("tables"));
    }

    [Theory]
    [InlineData("")]
    public void 空の環境変数は未設定として扱う(string empty)
    {
        var options = TestTool.Parse(TestTool.Env(("TEST_TABLES", empty)), "run").Options;
        Assert.Null(options.Value("tables"));
        Assert.False(options.Has("tables"));
    }

    [Fact]
    public void 環境変数も無ければ既定値で既定値も無ければnull()
    {
        var options = TestTool.Run("run");
        Assert.Equal("1", options.Value("parallel"));
        Assert.Null(options.Value("out"));
        Assert.True(options.Has("parallel"));
        Assert.False(options.Has("out"));
    }

    [Fact]
    public void 環境変数の数も範囲を検査する()
    {
        var options = TestTool.Parse(TestTool.Env(("TEST_LIMIT", "5")), "run").Options;
        Assert.Equal(5, options.Int32("limit", 1, 10, 3));
        var bad = TestTool.Parse(TestTool.Env(("TEST_LIMIT", "50")), "run").Options;
        Assert.Throws<UsageException>(() => bad.Int32("limit", 1, 10, 3));
        Assert.Equal(3, TestTool.Run("run").Int32("limit", 1, 10, 3));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("True", true)]
    [InlineData("1", false)]
    [InlineData("yes", false)]
    [InlineData("false", false)]
    public void フラグの環境変数はtrueだけを真とする(string value, bool expected)
    {
        var options = TestTool.Parse(TestTool.Env(("TEST_QUIET", value)), "run").Options;
        Assert.Equal(expected, options.Flag("quiet"));
    }

    [Fact]
    public void 必須は環境変数でも満たせる()
    {
        var result = TestTool.Parse(TestTool.Env(("TEST_MUST", "v")), "strict");
        Assert.Equal(ParseOutcome.Run, result.Outcome);
        Assert.Equal("v", result.Options.Value("must"));
        var empty = TestTool.Parse(TestTool.Env(("TEST_MUST", "")), "strict");
        Assert.Equal(ParseOutcome.Error, empty.Outcome);
    }

    [Fact]
    public void 環境変数の取得を省くと実際の環境から読む()
    {
        var name = "VVPT_CLI_TEST_" + Guid.NewGuid().ToString("N");
        var tool = new ToolSpec("t", "d", "1", [
            new CommandSpec("c", "s", [
                new OptionSpec { Name = "val", Kind = OptionKind.Value, EnvironmentVariable = name, Description = "d" },
            ]),
        ]);
        Environment.SetEnvironmentVariable(name, "real");
        try
        {
            Assert.Equal("real", CommandLineParser.Parse(tool, ["c"]).Options.Value("val"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }
}
