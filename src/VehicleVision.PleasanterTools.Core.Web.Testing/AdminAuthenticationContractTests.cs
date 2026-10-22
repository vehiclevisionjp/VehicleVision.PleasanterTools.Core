using System.Net;
using System.Text.Json;
using Xunit;

namespace VehicleVision.PleasanterTools.Core.Web.Testing;

/// <summary>管理画面の HTTP 契約。アプリはホストの生成を実装して継承する。</summary>
public abstract partial class AdminAuthenticationContractTests : IAsyncLifetime
{
    private IAdminContractHost? host;
    private IAdminContractHost Host => host ?? throw new InvalidOperationException("ホストが初期化されていません。");
    private AdminContractSettings Settings => Host.Settings;

    /// <summary>試験ごとに独立したホストを作る。準備に失敗した場合の後片付けもアプリ側で行う。</summary>
    protected abstract Task<IAdminContractHost> CreateHostAsync();

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        host = await CreateHostAsync();
        Settings.Validate(Host.Capabilities);
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (host is not null)
        {
            await host.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(AdminContractOperation.EnrollmentBegin)]
    [InlineData(AdminContractOperation.EnrollmentComplete)]
    public async Task C1_登録済みの人は初回登録を使えず元のTOTPでログインできる(AdminContractOperation operation)
    {
        if (await AssertAbsentAsync(operation))
        {
            return;
        }
        var admin = await Host.SeedAdminAsync(withTotp: true);
        using var client = Host.CreateClient();
        await PasswordLoginAsync(client, admin);
        using var response = await SendAsync(client, operation, new(Code: Host.NextTotpCode(admin)));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AssertNoCookie(response, Settings.Cookies.Session);
        await CompleteLoginAsync(client, admin);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task C2_登録し直しは誤ったパスワードも未指定も拒む(bool omitPassword)
    {
        if (await AssertAbsentAsync(AdminContractOperation.ReenrollmentBegin))
        {
            return;
        }
        var admin = await Host.SeedAdminAsync(withTotp: true);
        using var client = Host.CreateClient();
        await SignInAsync(client, admin);
        using var response = await SendAsync(client, AdminContractOperation.ReenrollmentBegin,
            new(Password: omitPassword ? null : admin.Password + "-incorrect"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertNoCookie(response, Settings.Cookies.Reenroll);
    }

    [Fact]
    public async Task C2_正しいパスワードで登録し直しを開始できる()
    {
        if (await AssertAbsentAsync(AdminContractOperation.ReenrollmentBegin))
        {
            return;
        }
        var admin = await Host.SeedAdminAsync(withTotp: true);
        using var client = Host.CreateClient();
        await SignInAsync(client, admin);
        using var response = await BeginReenrollmentAsync(client, admin);
    }

    [Fact]
    public async Task C2_パスワードの再確認は5回失敗すると締め出す()
    {
        if (await AssertAbsentAsync(AdminContractOperation.ReenrollmentBegin))
        {
            return;
        }
        var admin = await Host.SeedAdminAsync(withTotp: true);
        using var client = Host.CreateClient();
        await SignInAsync(client, admin);
        var statuses = new List<HttpStatusCode>();
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var response = await SendAsync(client, AdminContractOperation.ReenrollmentBegin,
                new(Password: admin.Password + "-incorrect"));
            statuses.Add(response.StatusCode);
            AssertNoCookie(response, Settings.Cookies.Reenroll);
        }
        using var locked = await SendAsync(client, AdminContractOperation.ReenrollmentBegin,
            new(Password: admin.Password));
        Assert.Equal(HttpStatusCode.Locked, locked.StatusCode);
        AssertNoCookie(locked, Settings.Cookies.Reenroll);
        Assert.Equal(
            [HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized,
                HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized],
            statuses);
    }

    [Theory]
    [InlineData(AdminContractOperation.EnrollmentBegin, false)]
    [InlineData(AdminContractOperation.EnrollmentComplete, false)]
    [InlineData(AdminContractOperation.TotpLogin, false)]
    [InlineData(AdminContractOperation.EnrollmentBegin, true)]
    [InlineData(AdminContractOperation.TotpLogin, true)]
    public async Task C3_登録し直しのcookieをログイン途中として使えない(
        AdminContractOperation operation, bool renameCookie)
    {
        if (await AssertAbsentAsync(operation))
        {
            return;
        }
        if (await AssertAbsentAsync(AdminContractOperation.ReenrollmentBegin))
        {
            return;
        }
        var admin = await Host.SeedAdminAsync(withTotp: true);
        using var signedIn = Host.CreateClient();
        await SignInAsync(signedIn, admin);
        using var begin = await BeginReenrollmentAsync(signedIn, admin);
        var cookie = Cookie(begin, Settings.Cookies.Reenroll);
        if (renameCookie)
        {
            cookie = Settings.Cookies.Pending + cookie[cookie.IndexOf('=')..];
        }
        using var isolated = Host.CreateClient();
        using var response = await SendAsync(isolated, operation, new(Code: "123456"), cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertNoCookie(response, Settings.Cookies.Session);
    }

    [Fact]
    public async Task C3_Pendingだけでは登録し直しを完了できない()
    {
        if (await AssertAbsentAsync(AdminContractOperation.ReenrollmentComplete))
        {
            return;
        }
        var admin = await Host.SeedAdminAsync(withTotp: true);
        using var client = Host.CreateClient();
        await PasswordLoginAsync(client, admin);
        using var response = await SendAsync(client, AdminContractOperation.ReenrollmentComplete,
            new(Code: Host.NextTotpCode(admin)));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertNoCookie(response, Settings.Cookies.Session);
    }

    [Theory]
    [InlineData(AdminContractOperation.Users)]
    [InlineData(AdminContractOperation.ChangePassword)]
    [InlineData(AdminContractOperation.AuditLogs)]
    public async Task C4_Pendingでは管理操作ができない(AdminContractOperation operation)
    {
        if (await AssertAbsentAsync(operation))
        {
            return;
        }
        var admin = await Host.SeedAdminAsync(withTotp: true);
        using var client = Host.CreateClient();
        await PasswordLoginAsync(client, admin);
        using var response = await SendAsync(client, operation,
            new(Password: admin.Password, NewPassword: admin.Password + "-new"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertNoCookie(response, Settings.Cookies.Session);
    }

    [Fact]
    public async Task C5_存在しない人と誤ったパスワードと無効の人を区別しない()
    {
        if (await AssertAbsentAsync(AdminContractOperation.PasswordLogin))
        {
            return;
        }
        var active = await Host.SeedAdminAsync(withTotp: false);
        var disabled = await Host.SeedAdminAsync(withTotp: false, disabled: true);
        var inputs = new[]
        {
            new AdminContractInput(LoginId: Guid.NewGuid().ToString("N"), Password: active.Password),
            new AdminContractInput(LoginId: active.LoginId, Password: active.Password + "-incorrect"),
            new AdminContractInput(LoginId: disabled.LoginId, Password: disabled.Password),
        };
        JsonElement? previous = null;
        foreach (var input in inputs)
        {
            using var client = Host.CreateClient();
            using var response = await SendAsync(client, AdminContractOperation.PasswordLogin, input);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            AssertNoCookie(response, Settings.Cookies.Session);
            AssertNoCookie(response, Settings.Cookies.Pending);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(JsonValueKind.Object, body.RootElement.ValueKind);
            Assert.Equal(Settings.LoginFailureShape.Count, body.RootElement.EnumerateObject().Count());
            foreach (var (name, kind) in Settings.LoginFailureShape)
            {
                Assert.True(body.RootElement.TryGetProperty(name, out var value), $"エラー本文に {name} がありません。");
                Assert.Equal(kind, value.ValueKind);
            }
            if (previous is { } expected)
            {
                Assert.True(JsonElement.DeepEquals(expected, body.RootElement),
                    "同じ言語のログイン失敗で、理由により本文が変わっています。");
            }
            previous = body.RootElement.Clone();
        }
    }

    private async Task<bool> AssertAbsentAsync(AdminContractOperation operation)
    {
        if (Host.Capabilities.Supports(operation))
        {
            return false;
        }
        using var client = Host.CreateClient();
        using var response = await SendAsync(client, operation,
            new(LoginId: "absent-probe", Password: "absent-probe-password", Code: "123456",
                NewPassword: "absent-probe-new-password"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        return true;
    }

    private async Task PasswordLoginAsync(HttpClient client, SeededAdmin admin)
    {
        using var response = await Host.LoginAsync(client, admin);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(Settings.TotpNextValue, body.RootElement.GetProperty(Settings.NextProperty).GetString());
        _ = Cookie(response, Settings.Cookies.Pending);
        AssertNoCookie(response, Settings.Cookies.Session);
    }

    private async Task CompleteLoginAsync(HttpClient client, SeededAdmin admin)
    {
        using var response = await SendAsync(client, AdminContractOperation.TotpLogin,
            new(Code: Host.NextTotpCode(admin)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _ = Cookie(response, Settings.Cookies.Session);
    }

    private async Task SignInAsync(HttpClient client, SeededAdmin admin)
    {
        await PasswordLoginAsync(client, admin);
        await CompleteLoginAsync(client, admin);
    }

    private async Task<HttpResponseMessage> BeginReenrollmentAsync(HttpClient client, SeededAdmin admin)
    {
        var response = await SendAsync(client, AdminContractOperation.ReenrollmentBegin,
            new(Password: admin.Password));
        try
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            _ = Cookie(response, Settings.Cookies.Reenroll);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpClient client, AdminContractOperation operation, AdminContractInput input, string? cookie = null)
    {
        using var request = Settings.Endpoints[operation].CreateRequest(input);
        request.Headers.AcceptLanguage.ParseAdd("en");
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }
        return await client.SendAsync(request);
    }

    private static string[] Cookies(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Select(value => value.Split(';', 2)[0])
                .Where(value => value.StartsWith(name + "=", StringComparison.Ordinal)
                    && value.Length > name.Length + 1).ToArray()
            : [];

    private static string Cookie(HttpResponseMessage response, string name) =>
        Assert.Single(Cookies(response, name));

    private static void AssertNoCookie(HttpResponseMessage response, string name) =>
        Assert.Empty(Cookies(response, name));
}
