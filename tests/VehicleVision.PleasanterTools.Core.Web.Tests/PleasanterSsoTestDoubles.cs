using System.Collections.Immutable;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;

namespace VehicleVision.PleasanterTools.Core.Web.Tests;

/// <summary>
/// アプリの <c>PleasanterSsoOptions</c> と同じ形の設定。**アプリの型が宣言を足すだけで渡せること**も、これで確かめる。
/// </summary>
/// <remarks>
/// 既定値と <see cref="ShouldForwardCookie"/> は、Questionnaire の <c>PleasanterSsoOptions</c>（2026-10-21 の develop）に合わせた。
/// 本アプリの cookie の接頭辞は <c>q.</c>。
/// </remarks>
internal sealed class TestSsoOptions : IPleasanterSsoVerificationOptions
{
    public const string OwnCookiePrefix = "q.";

    public ImmutableArray<int> AllowedDeptIds { get; init; } = [];

    public ImmutableArray<int> AllowedGroupIds { get; init; } = [];

    public bool Enabled { get; init; }

    public Uri? InternalBaseUrl { get; init; }

    public string LoginUrl { get; init; } = string.Empty;

    public ImmutableArray<string> CookieNamePrefixes { get; init; } = [".AspNetCore.Cookies", "Pleasanter_SessionGuid"];

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    public bool ShouldForwardCookie(string name) =>
        !string.IsNullOrEmpty(name)
        && !name.StartsWith(OwnCookiePrefix, StringComparison.OrdinalIgnoreCase)
        && CookieNamePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));
}

/// <summary>決まった応答を返し、受け取った要求を覚える HTTP ハンドラ。</summary>
internal sealed class FakeHttpMessageHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    public List<string> Bodies { get; } = [];

    public static FakeHttpMessageHandler Json(HttpStatusCode status, string json) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        }));

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        return await respond(request, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>1 つのハンドラを包んだ HttpClient を返すだけの工場。</summary>
internal sealed class SingleHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public List<string> Names { get; } = [];

    public HttpClient CreateClient(string name)
    {
        Names.Add(name);
        return new HttpClient(handler, disposeHandler: false);
    }
}

/// <summary>決まった接続設定を返す。</summary>
internal sealed class StaticConnectionSource(PleasanterApiConnectionSettings settings) : IPleasanterApiConnectionSource
{
    public Task<PleasanterApiConnectionSettings> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(settings);
}

/// <summary>読めない接続設定。</summary>
internal sealed class ThrowingConnectionSource(Exception exception) : IPleasanterApiConnectionSource
{
    public Task<PleasanterApiConnectionSettings> GetAsync(CancellationToken cancellationToken = default) =>
        throw exception;
}

/// <summary>本アプリの Pleasanter 接続設定（API キー）の組み合わせと、アプリごとに違う値。</summary>
internal static class PleasanterSsoTestConnections
{
    public const string ApiKey = "0123456789abcdef-secret-api-key";

    /// <summary>API キーが設定されていない（導入直後）。</summary>
    public static StaticConnectionSource WithoutApiKey { get; } = new(new PleasanterApiConnectionSettings(string.Empty, string.Empty));

    /// <summary>API キーが設定されている。**宛先は内部 URL と別の書き方にして、送り先を見分けられるようにする。**</summary>
    public static StaticConnectionSource WithApiKey { get; } =
        new(new PleasanterApiConnectionSettings("http://pleasanter.api:8080/root", ApiKey));

    /// <summary>アプリごとに違う値。**Core にアプリ名を書かないので、試験でも架空のアプリにする。**</summary>
    public static PleasanterSessionVerifierSettings Settings { get; } = new()
    {
        SessionProbeKey = "VehicleVision.TestApp.SsoProbe",
        BaseUrlSettingName = "TESTAPP_PLEASANTER_BASEURL",
        ApiKeySettingName = "TESTAPP_PLEASANTER_APIKEY",
    };
}

/// <summary>書かれたログを文字列で覚えるロガー。**秘密がログへ出ないことを確かめる。**</summary>
internal sealed class ListLogger : ILogger
{
    public List<string> Messages { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        Messages.Add($"{logLevel}: {formatter(state, exception)} {exception}");
    }
}
