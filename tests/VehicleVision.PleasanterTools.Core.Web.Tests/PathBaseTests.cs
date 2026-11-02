using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace VehicleVision.PleasanterTools.Core.Web.Tests;

public sealed class PathBaseTests
{
    private const string Setting = "TEST_PATH_BASE";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("/")]
    public void 未設定と根はサブパスを使わない(string? value)
    {
        var options = PathBaseOptions.Parse(value, Setting);

        Assert.False(options.IsConfigured);
        Assert.Equal(PathBaseOptions.Root, options);
    }

    [Theory]
    [InlineData("/app")]
    [InlineData("/app/sub")]
    [InlineData("/a-b_c.d~e")]
    public void 正しいサブパスを受ける(string value)
    {
        var options = PathBaseOptions.Parse(value, Setting);

        Assert.True(options.IsConfigured);
        Assert.Equal(value, options.Value.Value);
    }

    [Theory]
    [InlineData("app")]
    [InlineData("/app/")]
    [InlineData("/app//sub")]
    [InlineData("/app?x=1")]
    [InlineData("/app#x")]
    [InlineData("/a%20b")]
    [InlineData("/./app")]
    [InlineData("/app/..")]
    [InlineData("/ap p")]
    public void 書き間違いは設定名つきの英語の例外で止める(string value)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => PathBaseOptions.Parse(value, Setting));

        Assert.Contains(Setting, ex.Message);
        Assert.Contains(value, ex.Message);
    }

    [Fact]
    public void 長すぎる値は拒否する()
    {
        var value = "/" + new string('a', PathBaseOptions.MaximumLength);

        Assert.Throws<InvalidOperationException>(() => PathBaseOptions.Parse(value, Setting));
    }

    [Fact]
    public void 設定から読む()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [Setting] = "/app" })
            .Build();

        Assert.Equal("/app", PathBaseOptions.FromConfiguration(configuration, Setting).Value.Value);
        Assert.False(PathBaseOptions.FromConfiguration(configuration, "OTHER").IsConfigured);
    }

    [Theory]
    [InlineData("https://host", "https://host/app")]
    [InlineData("https://host/", "https://host/app")]
    [InlineData("https://host/app", "https://host/app")]
    [InlineData("https://host/app/", "https://host/app")]
    [InlineData("https://host/APP", "https://host/APP")]
    [InlineData("not a url", "not a url")]
    public void 起点URLへサブパスを二重にならず足す(string baseUrl, string expected)
    {
        var options = PathBaseOptions.Parse("/app", Setting);

        Assert.Equal(expected, options.ComposePublicUrl(baseUrl));
    }

    [Fact]
    public void サブパス未設定なら起点URLを変えない()
    {
        Assert.Equal("https://host/", PathBaseOptions.Root.ComposePublicUrl("https://host/"));
        Assert.Null(PathBaseOptions.Parse("/app", Setting).ComposePublicUrl(null));
    }

    [Fact]
    public async Task サブパス配下はPathBaseを移して通し後で戻す()
    {
        var (status, seen) = await RunAsync("/app", path: "/APP/api/x");

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.Equal(("/app", "/api/x"), seen);
    }

    [Fact]
    public async Task サブパスちょうどは根として扱う()
    {
        var (status, seen) = await RunAsync("/app", path: "/app");

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.Equal(("/app", "/"), seen);
    }

    [Theory]
    [InlineData("/api/x")]
    [InlineData("/application")]
    [InlineData("/")]
    public async Task サブパスの外は404で応答しない(string path)
    {
        var (status, seen) = await RunAsync("/app", path);

        Assert.Equal(StatusCodes.Status404NotFound, status);
        Assert.Null(seen);
    }

    [Theory]
    [InlineData("/healthz")]
    [InlineData("/READY")]
    public async Task 生存確認はサブパスの外でも通る(string path)
    {
        var (status, seen) = await RunAsync("/app", path);

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.Equal(("", path), seen);
    }

    [Fact]
    public async Task ANCMが同じPathBaseを渡したら二重に剥がさない()
    {
        var (status, seen) = await RunAsync("/app", path: "/api/x", pathBase: "/app");

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.Equal(("/app", "/api/x"), seen);
    }

    [Fact]
    public async Task 未設定なら何も変えない()
    {
        var (status, seen) = await RunAsync(null, path: "/api/x");

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.Equal(("", "/api/x"), seen);
    }

    private static async Task<(int Status, (string PathBase, string Path)? Seen)> RunAsync(
        string? subPath,
        string path,
        string pathBase = "")
    {
        (string, string)? seen = null;
        var builder = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());
        builder.UseStrictPathBase(PathBaseOptions.Parse(subPath, Setting));
        builder.Run(context =>
        {
            seen = (context.Request.PathBase.Value ?? string.Empty, context.Request.Path.Value ?? string.Empty);
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.PathBase = pathBase;
        await builder.Build()(context);

        return (context.Response.StatusCode, seen);
    }
}
