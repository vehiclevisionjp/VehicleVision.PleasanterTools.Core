using System.Globalization;
using System.Text.Json;

namespace VehicleVision.PleasanterTools.Core.Cli.Tests;

/// <summary>仕様書 5.7「JSON」の試験。プロセスは起動しない。</summary>
public sealed class JsonTests
{
    private static readonly IReadOnlyList<CliIssue> None = [];

    [Fact]
    public void 枠の項目と順序()
    {
        var json = JsonResult.Serialize("check", true, 0, None, None);
        Assert.Equal("{\"command\":\"check\",\"ok\":true,\"exitCode\":0,\"errors\":[],\"warnings\":[]}", json);
    }

    [Fact]
    public void 失敗の例は仕様書3_3のとおり()
    {
        var json = JsonResult.Serialize(
            "validate", false, 1, [new CliIssue("ConfigInvalid", "sourceId is duplicated.", "sources[1].id")], None,
            writer =>
            {
                writer.WriteStartObject("checked");
                writer.WriteNumber("sources", 2);
                writer.WriteEndObject();
            });
        Assert.Equal(
            "{\"command\":\"validate\",\"ok\":false,\"exitCode\":1,"
            + "\"errors\":[{\"code\":\"ConfigInvalid\",\"message\":\"sourceId is duplicated.\",\"path\":\"sources[1].id\"}],"
            + "\"warnings\":[],\"checked\":{\"sources\":2}}",
            json);
    }

    [Fact]
    public void 一部だけ完了は_okがfalseでerrorsが空()
    {
        var json = JsonResult.Serialize("run", false, 3, None, [new CliIssue("MoreMessagesRemain", "The message limit was reached.")]);
        Assert.Equal(
            "{\"command\":\"run\",\"ok\":false,\"exitCode\":3,\"errors\":[],"
            + "\"warnings\":[{\"code\":\"MoreMessagesRemain\",\"message\":\"The message limit was reached.\"}]}",
            json);
    }

    [Fact]
    public void 固有の項目は枠の後ろの同じ階層に付く()
    {
        var json = JsonResult.Serialize("plan", true, 0, None, None, writer => writer.WriteString("planId", "3f9a"));
        using var document = JsonDocument.Parse(json);
        var names = document.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(["command", "ok", "exitCode", "errors", "warnings", "planId"], names);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void okと終了コードが食い違えば例外(bool ok, int exitCode)
    {
        Assert.Throws<ArgumentException>(() => JsonResult.Serialize("x", ok, exitCode, None, None));
    }

    [Fact]
    public void 固有の項目の値と名前も_uXXXXで_バージョンの枠も同じ()
    {
        // 既定のエンコーダーに頼らず JavaScriptEncoder を明示していることを、固有の項目とバージョンでも固定する（仕様書 3.2）
        var json = JsonResult.Serialize(
            "x", true, 0, None, None, writer => writer.WriteString("名前", "値😀"));
        Assert.Contains("\"\\u540D\\u524D\":\"\\u5024\\uD83D\\uDE00\"", json, StringComparison.Ordinal);
        Assert.True(TestTool.IsAscii(json));
        var version = JsonResult.SerializeVersion("1.0.0-é");
        Assert.Contains("\"version\":\"1.0.0-\\u00E9\"", version, StringComparison.Ordinal);
        Assert.True(TestTool.IsAscii(version));
    }

    [Fact]
    public void 非ASCIIは_uXXXXで_サロゲートペアは2つ()
    {
        var json = JsonResult.Serialize("x", false, 1, [new CliIssue("Bad", "日本 \U0001F600 <a&b> 'q'", "pé")], None);
        Assert.Contains("\"message\":\"\\u65E5\\u672C \\uD83D\\uDE00 ", json, StringComparison.Ordinal);
        Assert.Contains("\"path\":\"p\\u00E9\"", json, StringComparison.Ordinal);
        Assert.True(TestTool.IsAscii(json));
        using var document = JsonDocument.Parse(json);
        Assert.Equal(
            "日本 \U0001F600 <a&b> 'q'",
            document.RootElement.GetProperty("errors")[0].GetProperty("message").GetString());
    }

    [Fact]
    public void 制御文字と引用符は_エスケープされ_1行のまま()
    {
        var json = JsonResult.Serialize("x", false, 1, [new CliIssue("Bad", "a\nb\r\tc\u007F\"d\\e")], None);
        Assert.DoesNotContain('\n', json);
        Assert.DoesNotContain('\r', json);
        Assert.DoesNotContain('\t', json);
        Assert.DoesNotContain('\u007F', json);
        Assert.True(TestTool.IsAscii(json));
        using var document = JsonDocument.Parse(json);
        Assert.Equal("a\nb\r\tc\u007F\"d\\e", document.RootElement.GetProperty("errors")[0].GetProperty("message").GetString());
    }

    [Fact]
    public void 日時はUTCのISO8601で書ける()
    {
        var at = new DateTimeOffset(2026, 11, 9, 1, 2, 3, TimeSpan.FromHours(9));
        var json = JsonResult.Serialize("x", true, 0, None, None, writer => writer.WriteString("at", at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)));
        Assert.Contains("\"at\":\"2026-11-08T16:02:03Z\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void 版の枠()
    {
        Assert.Equal("{\"command\":\"version\",\"ok\":true,\"version\":\"0.4.0\"}", JsonResult.SerializeVersion("0.4.0"));
    }

    [Fact]
    public void redactはmessageとpathに効く()
    {
        var json = JsonResult.Serialize(
            "x", false, 1, [new CliIssue("Bad", "token abc123", "p/abc123")], None, redact: text => text.Replace("abc123", "***", StringComparison.Ordinal));
        Assert.DoesNotContain("abc123", json, StringComparison.Ordinal);
        Assert.Contains("token ***", json, StringComparison.Ordinal);
    }

    [Fact]
    public void CliOutputのWriteJsonはstdoutへ1行だけ書く()
    {
        using var stdout = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        using var stderr = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        var output = new CliOutput(stdout, stderr, TestTool.Env(), true, true);
        output.WriteJson("check", true, 0, None, None);
        output.Warn("careful");
        Assert.Equal("{\"command\":\"check\",\"ok\":true,\"exitCode\":0,\"errors\":[],\"warnings\":[]}\n", stdout.ToString());
        Assert.DoesNotContain("\u001b", stdout.ToString(), StringComparison.Ordinal);
        Assert.StartsWith("\u001b[33m[WARN]", stderr.ToString(), StringComparison.Ordinal);
    }
}
