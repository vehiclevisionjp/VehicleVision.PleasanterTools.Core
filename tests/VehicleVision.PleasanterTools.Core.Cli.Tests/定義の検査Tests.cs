namespace VehicleVision.PleasanterTools.Core.Cli.Tests;

/// <summary>仕様書 5.7「定義の検査」の試験。誤りは定義の時点で例外にする。</summary>
public sealed class 定義の検査Tests
{
    private static OptionSpec Option(string name, string? shortName = null) =>
        new() { Name = name, ShortName = shortName, Description = "An option." };

    private static ToolSpec Tool(params OptionSpec[] options) =>
        new("t", "A tool.", "1", [new CommandSpec("c", "A command.", options)]);

    [Fact]
    public void 正しい定義は通る()
    {
        _ = Tool(Option("dry-run", "d"), Option("out"));
    }

    [Fact]
    public void オプション名の重複は例外()
    {
        Assert.Throws<ArgumentException>(() => Tool(Option("out"), Option("out")));
    }

    [Theory]
    [InlineData("Out")]
    [InlineData("DRY-RUN")]
    public void 大文字小文字だけが違う名前は定義の時点で例外(string upper)
    {
        // 名前は小文字だけなので、大文字を含む定義そのものが例外になる
        Assert.Throws<ArgumentException>(() => Tool(Option(upper.ToLowerInvariant()), Option(upper)));
    }

    [Fact]
    public void 大文字小文字だけが違うコマンド名は例外()
    {
        Assert.Throws<ArgumentException>(() => new ToolSpec("t", "d", "1", [
            new CommandSpec("run", "s", []),
            new CommandSpec("RUN", "s", []),
        ]));
    }

    [Theory]
    [InlineData("help")]
    [InlineData("version")]
    [InlineData("HELP")]
    public void 予約語はオプション名にもコマンド名にも使えない(string name)
    {
        Assert.Throws<ArgumentException>(() => Tool(Option(name)));
        Assert.Throws<ArgumentException>(() => new ToolSpec("t", "d", "1", [new CommandSpec(name, "s", [])]));
        Assert.Throws<ArgumentException>(() => new ToolSpec("t", "d", "1", [new CommandSpec("c", "s", [])], [Option(name)]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("x")]
    [InlineData("1abc")]
    [InlineData("a_b")]
    [InlineData("a b")]
    [InlineData("-abc")]
    [InlineData("日本語")]
    [InlineData("a=b")]
    [InlineData("a:b")]
    public void オプション名の形の違反は例外(string name)
    {
        Assert.Throws<ArgumentException>(() => Tool(Option(name)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("Y")]
    [InlineData("yy")]
    [InlineData("h")]
    [InlineData("-")]
    public void 短縮形の形の違反と予約は例外(string shortName)
    {
        Assert.Throws<ArgumentException>(() => Tool(Option("out", shortName)));
    }

    [Fact]
    public void 短縮形の重複は例外()
    {
        Assert.Throws<ArgumentException>(() => Tool(Option("out", "o"), Option("only", "o")));
    }

    [Fact]
    public void 共通のオプションとコマンドのオプションの名前の重複は例外()
    {
        Assert.Throws<ArgumentException>(() => new ToolSpec(
            "t", "d", "1", [new CommandSpec("c", "s", [Option("json")])], [StandardOptions.Json]));
        Assert.Throws<ArgumentException>(() => new ToolSpec(
            "t", "d", "1", [new CommandSpec("c", "s", [Option("other", "y")])], [StandardOptions.Yes]));
    }

    [Fact]
    public void 別のコマンドで同じ名前を使うのは通る()
    {
        _ = new ToolSpec("t", "d", "1", [
            new CommandSpec("a", "s", [StandardOptions.Yes]),
            new CommandSpec("b", "s", [StandardOptions.Yes]),
        ]);
    }

    [Fact]
    public void ヘルプに出す文字列がASCIIでなければ例外()
    {
        Assert.Throws<ArgumentException>(() => Tool(new OptionSpec { Name = "out", Description = "出力先" }));
        Assert.Throws<ArgumentException>(() => Tool(new OptionSpec { Name = "out", Description = "line1\nline2" }));
        Assert.Throws<ArgumentException>(() => Tool(new OptionSpec { Name = "out", Description = "" }));
        Assert.Throws<ArgumentException>(() => new ToolSpec("t", "日本語", "1", [new CommandSpec("c", "s", [])]));
        Assert.Throws<ArgumentException>(() => new ToolSpec("t", "d", "1", [new CommandSpec("c", "概要", [])]));
        Assert.Throws<ArgumentException>(() => new ToolSpec("ツール", "d", "1", [new CommandSpec("c", "s", [])]));
        Assert.Throws<ArgumentException>(() => new ToolSpec("t", "d", "１", [new CommandSpec("c", "s", [])]));
        Assert.Throws<ArgumentException>(() => Tool(new OptionSpec
        {
            Name = "out", Description = "d", Kind = OptionKind.Value, DefaultValue = "既定",
        }));
    }

    [Fact]
    public void フラグに必須と値の名前と既定値は付けられない()
    {
        Assert.Throws<ArgumentException>(() => Tool(new OptionSpec { Name = "out", Description = "d", Required = true }));
        Assert.Throws<ArgumentException>(() => Tool(new OptionSpec { Name = "out", Description = "d", ValueName = "X" }));
        Assert.Throws<ArgumentException>(() => Tool(new OptionSpec { Name = "out", Description = "d", DefaultValue = "x" }));
    }

    [Fact]
    public void 環境変数の名前の形の違反は例外()
    {
        Assert.Throws<ArgumentException>(() => Tool(new OptionSpec
        {
            Name = "out", Description = "d", Kind = OptionKind.Value, EnvironmentVariable = "A=B",
        }));
        Assert.Throws<ArgumentException>(() => Tool(new OptionSpec
        {
            Name = "out", Description = "d", Kind = OptionKind.Value, EnvironmentVariable = "",
        }));
    }

    [Fact]
    public void コマンドが無いツールと位置引数の上限の誤りは例外()
    {
        Assert.Throws<ArgumentException>(() => new ToolSpec("t", "d", "1", []));
        Assert.Throws<ArgumentException>(() => new ToolSpec("t", "d", "1", [new CommandSpec("c", "s", [], true, 0)]));
    }

    [Fact]
    public void withで書き換えた定義も解析の前に検査する()
    {
        var broken = Tool(Option("out")) with { Description = "日本語" };
        Assert.Throws<ArgumentException>(() => CommandLineParser.Parse(broken, ["c"]));
    }
}
