namespace VehicleVision.PleasanterTools.Core.Cli.Tests;

/// <summary>仕様書 6 章の試験。共通の名前・旧名の検出・接頭辞・真偽。</summary>
public sealed class 環境変数の名前Tests
{
    private static Func<string, string?> Env(params (string Name, string Value)[] values)
    {
        var map = values.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);
        return name => map.GetValueOrDefault(name);
    }

    [Fact]
    public void 共通の名前は6_2の表と一致する()
    {
        Assert.Equal("VVPT_PLEASANTER_BASE_URL", PleasanterEnvironmentNames.BaseUrl);
        Assert.Equal("VVPT_PLEASANTER_API_KEY", PleasanterEnvironmentNames.ApiKey);
        Assert.Equal("VVPT_PLEASANTER_API_KEY_FILE", PleasanterEnvironmentNames.ApiKeyFile);
        Assert.Equal("VVPT_PLEASANTER_ALLOW_INSECURE_HTTP", PleasanterEnvironmentNames.AllowInsecureHttp);
        Assert.Equal("VVPT_PLEASANTER_SERVER_VERSION", PleasanterEnvironmentNames.ServerVersion);
    }

    [Fact]
    public void 旧名の対応は6_2の表と一致する()
    {
        var expected = new Dictionary<string, string>
        {
            ["PLEASANTER_BASE_URL"] = "VVPT_PLEASANTER_BASE_URL",
            ["PLEASANTER_API_KEY"] = "VVPT_PLEASANTER_API_KEY",
            ["PLEASANTER_API_KEY_FILE"] = "VVPT_PLEASANTER_API_KEY_FILE",
            ["PLEASANTER_ALLOW_INSECURE_HTTP"] = "VVPT_PLEASANTER_ALLOW_INSECURE_HTTP",
            ["PLEASANTER_SERVER_VERSION"] = "VVPT_PLEASANTER_SERVER_VERSION",
            ["FEEDINGEST_PLEASANTER_BASE_URL"] = "VVPT_PLEASANTER_BASE_URL",
            ["FEEDINGEST_PLEASANTER_API_KEY"] = "VVPT_PLEASANTER_API_KEY",
            ["FEEDINGEST_PLEASANTER_API_KEY_FILE"] = "VVPT_PLEASANTER_API_KEY_FILE",
            ["CALENDARSTYLE_PLEASANTER_API_KEY"] = "VVPT_PLEASANTER_API_KEY",
        };
        Assert.Equal(expected.OrderBy(p => p.Key), PleasanterEnvironmentNames.Legacy.OrderBy(p => p.Key));
    }

    [Fact]
    public void 旧名の行き先はすべて共通の名前()
    {
        var names = new[]
        {
            PleasanterEnvironmentNames.BaseUrl,
            PleasanterEnvironmentNames.ApiKey,
            PleasanterEnvironmentNames.ApiKeyFile,
            PleasanterEnvironmentNames.AllowInsecureHttp,
            PleasanterEnvironmentNames.ServerVersion,
        };
        Assert.All(PleasanterEnvironmentNames.Legacy.Values, v => Assert.Contains(v, names));
    }

    [Fact]
    public void 旧名のみなら検出する()
    {
        var found = LegacyEnvironmentCheck.Find(Env(("PLEASANTER_BASE_URL", "https://example.test")));
        Assert.Equal(["Environment variable PLEASANTER_BASE_URL is no longer supported. Use VVPT_PLEASANTER_BASE_URL."], found);
    }

    [Fact]
    public void 新名のみなら検出しない()
    {
        Assert.Empty(LegacyEnvironmentCheck.Find(Env((PleasanterEnvironmentNames.BaseUrl, "https://example.test"))));
    }

    [Fact]
    public void 新名との併用でも検出する()
    {
        var found = LegacyEnvironmentCheck.Find(Env(
            (PleasanterEnvironmentNames.ApiKey, "new"),
            ("PLEASANTER_API_KEY", "old")));
        Assert.Equal(["Environment variable PLEASANTER_API_KEY is no longer supported. Use VVPT_PLEASANTER_API_KEY."], found);
    }

    [Fact]
    public void 複数の旧名はすべて対応表の順に返す()
    {
        var found = LegacyEnvironmentCheck.Find(Env(
            ("CALENDARSTYLE_PLEASANTER_API_KEY", "x"),
            ("PLEASANTER_BASE_URL", "x")));
        Assert.Equal(
            [
                "Environment variable PLEASANTER_BASE_URL is no longer supported. Use VVPT_PLEASANTER_BASE_URL.",
                "Environment variable CALENDARSTYLE_PLEASANTER_API_KEY is no longer supported. Use VVPT_PLEASANTER_API_KEY.",
            ],
            found);
    }

    [Fact]
    public void 空の旧名は未設定として扱う()
    {
        Assert.Empty(LegacyEnvironmentCheck.Find(Env(("PLEASANTER_API_KEY", ""))));
    }

    [Fact]
    public void 対応表は差し替えられる()
    {
        var table = new Dictionary<string, string> { ["TOOL_OLD"] = "TOOL_NEW" };
        var found = LegacyEnvironmentCheck.Find(Env(("TOOL_OLD", "x"), ("PLEASANTER_API_KEY", "x")), table);
        Assert.Equal(["Environment variable TOOL_OLD is no longer supported. Use TOOL_NEW."], found);
    }

    [Fact]
    public void Reportは旧名があればERRORと終了コード2()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var env = Env(("PLEASANTER_BASE_URL", "x"));
        var output = new CliOutput(stdout, stderr, env, false, false);

        var code = LegacyEnvironmentCheck.Report(output, env);

        Assert.Equal(ExitCodes.UsageError, code);
        Assert.Equal(
            "[ERROR] Environment variable PLEASANTER_BASE_URL is no longer supported. Use VVPT_PLEASANTER_BASE_URL." + Environment.NewLine,
            stderr.ToString());
        Assert.Equal(string.Empty, stdout.ToString());
    }

    [Fact]
    public void Reportは旧名が無ければ何も出さず0()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var env = Env((PleasanterEnvironmentNames.ApiKey, "x"));
        var output = new CliOutput(stdout, stderr, env, false, false);

        Assert.Equal(ExitCodes.Success, LegacyEnvironmentCheck.Report(output, env));
        Assert.Equal(string.Empty, stdout.ToString() + stderr);
    }

    [Theory]
    [InlineData("binary-migrator", "BINARY_MIGRATOR_")]
    [InlineData("feedingest", "FEEDINGEST_")]
    [InlineData("calendar-style-creator", "CALENDAR_STYLE_CREATOR_")]
    public void Prefixはツール_IDから接頭辞を導く(string toolId, string expected)
    {
        Assert.Equal(expected, EnvironmentNames.Prefix(toolId));
    }

    [Fact]
    public void Prefixは空を拒む()
    {
        Assert.Throws<ArgumentException>(() => EnvironmentNames.Prefix(""));
        Assert.Throws<ArgumentNullException>(() => EnvironmentNames.Prefix(null!));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData("1", false)]
    [InlineData("yes", false)]
    [InlineData(" true", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsTrueはtrueだけを真とする(string? value, bool expected)
    {
        Assert.Equal(expected, EnvironmentNames.IsTrue(value));
    }
}
