using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Net.Http.Headers;

namespace VehicleVision.PleasanterTools.Core.Web.Tests;

public sealed class StaticCachePolicyTests
{
    [Theory]
    [InlineData("/admin.html", "no-cache")]
    [InlineData("/index.html", "no-cache")]
    [InlineData("/admin", "no-cache")]
    [InlineData("/f/abc", "no-cache")]
    [InlineData("/", "no-cache")]
    [InlineData("/assets/app-1a2b.js", "public, max-age=31536000, immutable")]
    [InlineData("/ASSETS/app-1a2b.css", "public, max-age=31536000, immutable")]
    [InlineData("/assets/x.html", "no-cache")]
    [InlineData("/logo.png", "no-cache")]
    [InlineData("/other/assets/app.js", "no-cache")]
    public void 経路ごとのキャッシュの指示を返す(string path, string expected)
    {
        Assert.Equal(expected, StaticCachePolicy.ValueFor(path));
    }

    [Fact]
    public void 静的ファイルの応答へCacheControlを付ける()
    {
        var http = new DefaultHttpContext();
        http.Request.Path = "/assets/app-1.js";
        var context = new StaticFileResponseContext(http, new NotFoundFileInfo("app-1.js"));

        StaticCachePolicy.Apply(context);

        Assert.Equal(StaticCachePolicy.ImmutableValue, http.Response.Headers[HeaderNames.CacheControl].ToString());
    }
}
