using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;

namespace VehicleVision.PleasanterTools.Core.Web.Tests;

/// <summary>DI への登録と、アプリごとに違う値の受け口を確かめる（Issue #74）。</summary>
/// <remarks>
/// **HttpClient の設定（cookie を覚えない・転送を追わない）は、各アプリの <c>Program.cs</c> に同じものが書き写されていた。**
/// Core で登録するので、その設定が崩れていないことを機械で押さえる。
/// </remarks>
public class PleasanterSessionVerifierRegistrationTests
{
    private static ServiceProvider Build(
        PleasanterSessionVerifierSettings? settings = null,
        CategoryRecorder? recorder = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            if (recorder is not null)
            {
                builder.AddProvider(recorder);
            }
        });
        services.AddSingleton<IPleasanterApiConnectionSource>(PleasanterSsoTestConnections.WithoutApiKey);
        services.AddPleasanterSessionVerifier(settings ?? PleasanterSsoTestConnections.Settings);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void 問い合わせのHttpClientはcookieを覚えず転送を追わない()
    {
        using var provider = Build();
        var factory = provider.GetRequiredService<IHttpMessageHandlerFactory>();

        HttpMessageHandler? handler = factory.CreateHandler(PleasanterSessionVerifier.HttpClientName);
        while (handler is DelegatingHandler delegating)
        {
            handler = delegating.InnerHandler;
        }

        var sockets = Assert.IsType<SocketsHttpHandler>(handler);
        Assert.False(sockets.UseCookies);
        Assert.False(sockets.AllowAutoRedirect);
    }

    [Fact]
    public void HttpClientの時間切れは無限で要求ごとに設定の値で掛ける()
    {
        using var provider = Build();

        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(PleasanterSessionVerifier.HttpClientName);

        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
    }

    [Fact]
    public void 検証器は単一のインスタンスで登録される()
    {
        using var provider = Build();

        var first = provider.GetRequiredService<IPleasanterSessionVerifier>();
        var second = provider.GetRequiredService<IPleasanterSessionVerifier>();

        Assert.IsType<PleasanterSessionVerifier>(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void ログのカテゴリは既定で型の完全名()
    {
        var recorder = new CategoryRecorder();
        using var provider = Build(recorder: recorder);

        provider.GetRequiredService<IPleasanterSessionVerifier>();

        Assert.Contains(typeof(PleasanterSessionVerifier).FullName!, recorder.Categories);
    }

    [Fact]
    public void ログのカテゴリはアプリが渡せる()
    {
        // **移す前のカテゴリを渡せば、アプリのログの設定（カテゴリで量を絞るもの）を変えずに済む**
        const string category = "VehicleVision.PleasanterTools.TestApp.Web.Services.PleasanterSessionVerifier";
        var recorder = new CategoryRecorder();
        using var provider = Build(PleasanterSsoTestConnections.Settings with { LoggerCategory = category }, recorder);

        provider.GetRequiredService<IPleasanterSessionVerifier>();

        Assert.Contains(category, recorder.Categories);
        Assert.DoesNotContain(typeof(PleasanterSessionVerifier).FullName!, recorder.Categories);
    }

    public static TheoryData<PleasanterSessionVerifierSettings> IncompleteSettings => new()
    {
        PleasanterSsoTestConnections.Settings with { SessionProbeKey = " " },
        PleasanterSsoTestConnections.Settings with { BaseUrlSettingName = string.Empty },
        PleasanterSsoTestConnections.Settings with { ApiKeySettingName = " " },
    };

    [Theory]
    [MemberData(nameof(IncompleteSettings))]
    public void アプリごとの値が空なら登録の時点で止める(PleasanterSessionVerifierSettings settings)
    {
        var services = new ServiceCollection();

        Assert.ThrowsAny<ArgumentException>(() => services.AddPleasanterSessionVerifier(settings));
    }

    [Theory]
    [MemberData(nameof(IncompleteSettings))]
    public void アプリごとの値が空なら作る時点で止める(PleasanterSessionVerifierSettings settings)
    {
        Assert.ThrowsAny<ArgumentException>(() => new PleasanterSessionVerifier(
            new SingleHttpClientFactory(FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}")),
            PleasanterSsoTestConnections.WithoutApiKey,
            settings,
            NullLogger.Instance));
    }

    [Fact]
    public void 接続設定の文字列にAPIキーを出さない()
    {
        var text = new PleasanterApiConnectionSettings("http://pleasanter.api/", PleasanterSsoTestConnections.ApiKey).ToString();

        Assert.DoesNotContain(PleasanterSsoTestConnections.ApiKey, text, StringComparison.Ordinal);
        Assert.Contains("http://pleasanter.api/", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ftp://pleasanter.api/", PleasanterSsoTestConnections.ApiKey)]
    [InlineData("pleasanter.api", PleasanterSsoTestConnections.ApiKey)]
    [InlineData(null, PleasanterSsoTestConnections.ApiKey)]
    [InlineData("http://pleasanter.api/", " ")]
    [InlineData("http://pleasanter.api/", null)]
    public async Task 接続設定が揃っていなければAPIキーを送らない(string? baseUrl, string? apiKey)
    {
        var handler = FakeHttpMessageHandler.Json(HttpStatusCode.Forbidden, """{"Id":0,"StatusCode":403,"Message":"Forbidden"}""");
        var verifier = new PleasanterSessionVerifier(
            new SingleHttpClientFactory(handler),
            new StaticConnectionSource(new PleasanterApiConnectionSettings(baseUrl, apiKey)),
            PleasanterSsoTestConnections.Settings,
            NullLogger.Instance);

        var result = await verifier.VerifyAsync(
            new StringValues(".AspNetCore.Cookies=AUTH"),
            new TestSsoOptions { Enabled = true, InternalBaseUrl = new Uri("http://pleasanter.internal/") });

        Assert.Equal(PleasanterSessionStatus.UpstreamError, result.Status);
        Assert.Equal("http-403", result.Reason);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task 問い合わせは登録した名前のHttpClientで行う()
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"StatusCode":200,"Response":{"TotalCount":1,"Data":[{"TenantId":1,"UserId":2,"LoginId":"sso-user1"}]}}""",
                Encoding.UTF8,
                "application/json"),
        }));
        var factory = new SingleHttpClientFactory(handler);
        var verifier = new PleasanterSessionVerifier(
            factory,
            PleasanterSsoTestConnections.WithoutApiKey,
            PleasanterSsoTestConnections.Settings,
            NullLogger.Instance);

        await verifier.VerifyAsync(
            new StringValues(".AspNetCore.Cookies=AUTH"),
            new TestSsoOptions { Enabled = true, InternalBaseUrl = new Uri("http://pleasanter.internal/") });

        Assert.Equal([PleasanterSessionVerifier.HttpClientName], factory.Names);
    }

    /// <summary>作られたロガーのカテゴリを覚える。</summary>
    internal sealed class CategoryRecorder : ILoggerProvider
    {
        public List<string> Categories { get; } = [];

        public ILogger CreateLogger(string categoryName)
        {
            lock (Categories)
            {
                Categories.Add(categoryName);
            }

            return NullLogger.Instance;
        }

        public void Dispose()
        {
        }
    }
}
