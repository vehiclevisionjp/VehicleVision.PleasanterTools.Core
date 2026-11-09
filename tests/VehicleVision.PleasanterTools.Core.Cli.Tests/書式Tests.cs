namespace VehicleVision.PleasanterTools.Core.Cli.Tests;

/// <summary>仕様書 5.7「書式」「既知の名前だけ」「値」の試験。</summary>
public sealed class 書式Tests
{
    [Theory]
    [InlineData("--dry-run")]
    [InlineData("-dry-run")]
    [InlineData("/dry-run")]
    [InlineData("--DRY-RUN")]
    [InlineData("/Dry-Run")]
    [InlineData("-d")]
    [InlineData("/d")]
    [InlineData("-D")]
    public void フラグは3つの接頭辞と短縮形と大文字小文字で同じ(string token)
    {
        var options = TestTool.Run("run", token);
        Assert.True(options.Flag("dry-run"));
    }

    [Theory]
    [InlineData("--out", "x")]
    [InlineData("-out", "x")]
    [InlineData("/out", "x")]
    [InlineData("--out=x", null)]
    [InlineData("-out=x", null)]
    [InlineData("/out=x", null)]
    [InlineData("--out:x", null)]
    [InlineData("/out:x", null)]
    [InlineData("-o", "x")]
    [InlineData("/o:x", null)]
    [InlineData("-o=x", null)]
    [InlineData("--OUT", "x")]
    public void 値は名前_値と名前イコール値と名前コロン値で同じ(string token, string? next)
    {
        var args = next is null ? new[] { "run", token } : ["run", token, next];
        Assert.Equal("x", TestTool.Run(args).Value("out"));
    }

    [Theory]
    [InlineData("--out:C:\\x\\y")]
    [InlineData("/out:C:\\x\\y")]
    [InlineData("--out=C:\\x\\y")]
    public void ドライブ文字を含む値は最初の区切りで分ける(string token)
    {
        Assert.Equal("C:\\x\\y", TestTool.Run("run", token).Value("out"));
        Assert.Equal("C:\\x\\y", TestTool.Run("run", "--out", "C:\\x\\y").Value("out"));
    }

    [Fact]
    public void 値には大文字小文字を区別する()
    {
        Assert.Equal("MiXed", TestTool.Run("run", "--out", "MiXed").Value("out"));
    }

    [Fact]
    public void コマンド名は大文字小文字を区別しない()
    {
        var result = TestTool.Parse("RUN", "--dry-run");
        Assert.Equal(ParseOutcome.Run, result.Outcome);
        Assert.Equal("run", result.CommandName);
    }

    [Fact]
    public void 共通のオプションはコマンドの前にも後ろにも置ける()
    {
        Assert.True(TestTool.Run("--json", "run").Flag("json"));
        Assert.True(TestTool.Run("run", "--json").Flag("json"));
        Assert.True(TestTool.Run("run", "--dry-run", "--json", "--out", "x").Flag("json"));
    }

    [Fact]
    public void 短縮形の結合は解釈しない()
    {
        // -dy は定義した名前ではない。位置引数を受けないコマンドなので、誤りになる
        Assert.Equal("Unknown option or argument: -dy", TestTool.Error("run", "-dy"));
    }

    [Theory]
    [InlineData("/tmp/x")]
    [InlineData("-1")]
    [InlineData("/y")]
    [InlineData("-z")]
    [InlineData("/C:\\x")]
    public void 既知でない単一接頭辞は位置引数になる(string token)
    {
        var result = TestTool.Parse("files", token);
        Assert.Equal(ParseOutcome.Run, result.Outcome);
        Assert.Equal([token], result.Options.Positionals);
    }

    [Theory]
    [InlineData("/tmp/x", "Unknown option or argument: /tmp/x")]
    [InlineData("-1", "Unknown option or argument: -1")]
    [InlineData("/z", "Unknown option or argument: /z")]
    [InlineData("stray", "Unknown option or argument: stray")]
    public void 位置引数を受けないコマンドでは未知の引数が誤りになる(string token, string message)
    {
        Assert.Equal(message, TestTool.Error("run", token));
    }

    [Fact]
    public void 単一接頭辞の未知の名前ではイコールの後ろの値を出さない()
    {
        Assert.Equal("Unknown option or argument: -secret", TestTool.Error("run", "-secret=hunter2"));
        Assert.Equal("Unknown option: --secret", TestTool.Error("run", "--secret=hunter2"));
        Assert.Equal("Unknown option: --secret", TestTool.Error("run", "--secret:hunter2"));
    }

    [Fact]
    public void ダッシュ2つで始まる未知の名前は位置引数を受けるコマンドでも誤り()
    {
        Assert.Equal("Unknown option: --foo", TestTool.Error("files", "--foo"));
        Assert.Equal("Unknown option: --dry-rn", TestTool.Error("run", "--dry-rn"));
    }

    [Fact]
    public void 終端のあとはすべて位置引数()
    {
        var options = TestTool.Run("files", "--verbose", "--", "--foo", "--verbose");
        Assert.True(options.Flag("verbose"));
        Assert.Equal(["--foo", "--verbose"], options.Positionals);
    }

    [Fact]
    public void 終端のあとでも位置引数を受けないコマンドでは誤り()
    {
        Assert.Equal("Unknown option or argument: x", TestTool.Error("run", "--", "x"));
    }

    [Fact]
    public void 位置引数の数の上限を超えたら誤り()
    {
        Assert.Equal(["a", "b"], TestTool.Run("files", "a", "b").Positionals);
        Assert.Equal(
            "Too many arguments. At most 2 positional argument(s) allowed.",
            TestTool.Error("files", "a", "b", "c"));
    }

    [Fact]
    public void 宣言していないコマンドのyesは未知のオプションと同じ誤り()
    {
        Assert.True(TestTool.Run("run", "--yes").Flag("yes"));
        Assert.True(TestTool.Run("run", "-y").Flag("yes"));
        Assert.Equal("Unknown option: --yes", TestTool.Error("plain", "--yes"));
        Assert.Equal("Unknown option or argument: -y", TestTool.Error("plain", "-y"));
        var result = TestTool.Parse("plain", "--yes");
        Assert.Equal(ParseOutcome.Error, result.Outcome);
    }

    [Theory]
    [InlineData("--out", "/tmp/x")]
    [InlineData("--out", "-1")]
    [InlineData("--out", "-dry-run")]
    [InlineData("--out", "/dry-run")]
    [InlineData("--out", "--")]
    [InlineData("--offset", "-1")]
    public void 値を取るオプションの次のトークンは常に値として取る(string name, string value)
    {
        Assert.Equal(value, TestTool.Run("run", name, value).Value(name[2..]));
    }

    [Fact]
    public void 次が既知のダッシュ2つのオプションなら値の書き忘れ()
    {
        Assert.Equal("Option --out requires a value.", TestTool.Error("run", "--out", "--dry-run"));
        Assert.Equal("Option --out requires a value.", TestTool.Error("run", "--out", "--json"));
        // 未知の --foo は値として取る
        Assert.Equal("--foo", TestTool.Run("run", "--out", "--foo").Value("out"));
    }

    [Fact]
    public void 値が無ければ誤り()
    {
        Assert.Equal("Option --out requires a value.", TestTool.Error("run", "--out"));
        Assert.Equal("Option --out requires a value.", TestTool.Error("run", "--out="));
        Assert.Equal("Option --out requires a value.", TestTool.Error("run", "-o"));
    }

    [Theory]
    [InlineData("--dry-run=true")]
    [InlineData("/dry-run:1")]
    [InlineData("-d=x")]
    public void フラグに値を付けたら誤り(string token)
    {
        Assert.Equal("Option --dry-run does not take a value.", TestTool.Error("run", token));
    }

    [Theory]
    [InlineData("--out", "a", "--out", "b")]
    [InlineData("--out", "a", "-o", "b")]
    [InlineData("--out=a", "/out:b")]
    public void 重複はエラー(params string[] args)
    {
        Assert.Equal("Option --out was specified more than once.", TestTool.Error(["run", .. args]));
    }

    [Fact]
    public void フラグの重複もエラー()
    {
        Assert.Equal("Option --dry-run was specified more than once.", TestTool.Error("run", "--dry-run", "-d"));
    }

    [Fact]
    public void 必須が無ければ誤りで引数があれば通る()
    {
        Assert.Equal("Option --must is required.", TestTool.Error("strict"));
        Assert.Equal("v", TestTool.Run("strict", "--must", "v").Value("must"));
    }

    [Fact]
    public void コマンドが無いと未知のコマンドは誤り()
    {
        Assert.Equal("No command was given.", TestTool.Error("--json"));
        Assert.Equal("Unknown command: foo", TestTool.Error("foo"));
        Assert.Equal("Unknown command: /tmp/x", TestTool.Error("/tmp/x"));
        Assert.Equal("Unknown option: --foo", TestTool.Error("--foo", "run"));
        Assert.Equal("No command was given.", TestTool.Error("--", "run"));
    }

    [Fact]
    public void コマンドの前に置けるのは共通のオプションだけ()
    {
        Assert.Equal("Unknown option: --dry-run", TestTool.Error("--dry-run", "run"));
        Assert.Equal("Unknown command: -d", TestTool.Error("-d", "run"));
    }

    [Fact]
    public void 数の範囲と形を検査する()
    {
        Assert.Equal(4, TestTool.Run("run", "--parallel", "4").Int32("parallel", 1, 16, 1));
        Assert.Equal(7, TestTool.Run("run").Int32("parallel", 1, 16, 7));
        Assert.Equal(-1, TestTool.Run("run", "--offset", "-1").Int32("offset", -10, 10, 0));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("17")]
    [InlineData("x")]
    [InlineData("1.5")]
    [InlineData("2 ")]
    [InlineData("99999999999")]
    public void 数の誤りは使い方の誤りで値を出さない(string bad)
    {
        var options = TestTool.Run("run", "--parallel", bad);
        var ex = Assert.Throws<UsageException>(() => options.Int32("parallel", 1, 16, 1));
        Assert.Equal("Option --parallel must be an integer from 1 to 16.", ex.Message);
    }

    [Fact]
    public void 一覧は空白を除き空の要素を捨てる()
    {
        Assert.Equal(["a", "b", "c"], TestTool.Run("run", "--tables", " a, b ,,c ,").List("tables"));
        Assert.Empty(TestTool.Run("run").List("tables"));
        Assert.Empty(TestTool.Run("run", "--tables", " , ").List("tables"));
    }

    [Fact]
    public void 排他の指定を検査する()
    {
        var both = TestTool.Run("run", "--tables", "a", "--exclude", "b");
        var ex = Assert.Throws<UsageException>(() => both.EnsureAtMostOne("tables", "exclude"));
        Assert.Equal("Use only one of --tables and --exclude.", ex.Message);
        TestTool.Run("run", "--tables", "a").EnsureAtMostOne("tables", "exclude");
    }

    [Fact]
    public void 定義に無い名前の取得は呼び出し側の誤り()
    {
        var options = TestTool.Run("run");
        Assert.Throws<ArgumentException>(() => options.Flag("nope"));
        Assert.Throws<ArgumentException>(() => options.Value("dry-run"));
        Assert.Throws<ArgumentException>(() => options.Flag("out"));
    }

    [Fact]
    public void エラー文はすべてASCII()
    {
        var texts = new[]
        {
            TestTool.Error("run", "--日本語"),
            TestTool.Error("run", "日本語"),
            TestTool.Error("run", "-日本語=秘密"),
            TestTool.Error("日本語"),
            TestTool.Error("run", "--out"),
        };
        Assert.All(texts, text => Assert.True(TestTool.IsAscii(text), text));
        Assert.Equal("Unknown option: --???", TestTool.Error("run", "--日本語"));
    }
}
