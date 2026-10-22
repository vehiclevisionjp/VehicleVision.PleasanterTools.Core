using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VehicleVision.PleasanterTools.Core.Web.Testing;
using Xunit.Sdk;

namespace VehicleVision.PleasanterTools.Core.Web.Tests;

public sealed partial class AdminContractTestsTests
{
    [Theory]
    [InlineData(AdminContractOperation.EnrollmentBegin, false)]
    [InlineData(AdminContractOperation.EnrollmentComplete, false)]
    [InlineData(AdminContractOperation.EnrollmentBegin, true)]
    [InlineData(AdminContractOperation.EnrollmentComplete, true)]
    public async Task C1は拒否と通常のログインの両方を検証する(AdminContractOperation operation, bool broken)
    {
        var steps = LoginSteps().Take(1).Concat(
        [
            new Step(operation, broken ? HttpStatusCode.OK : HttpStatusCode.Forbidden),
            LoginSteps()[1],
        ]);
        await RunAsync(steps, run => run.C1_登録済みの人は初回登録を使えず元のTOTPでログインできる(operation), broken);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task C2はパスワードの未指定と誤りを検証する(bool omit, bool broken)
    {
        var steps = LoginSteps().Append(new Step(AdminContractOperation.ReenrollmentBegin,
            broken ? HttpStatusCode.OK : HttpStatusCode.Unauthorized));
        await RunAsync(steps, run => run.C2_登録し直しは誤ったパスワードも未指定も拒む(omit), broken);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(7)]
    public async Task C2は5回とも401で次の正しいパスワードは423であることを検証する(int lockedFromAttempt)
    {
        var steps = LoginSteps().Concat(Enumerable.Range(1, 6).Select(attempt =>
            new Step(AdminContractOperation.ReenrollmentBegin,
                attempt >= lockedFromAttempt ? HttpStatusCode.Locked : HttpStatusCode.Unauthorized)));
        await RunAsync(steps, run => run.C2_パスワードの再確認は5回失敗すると締め出す(), lockedFromAttempt != 6);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task C2は成功時に登録し直しのcookieを要求する(bool broken)
    {
        var steps = LoginSteps().Append(new Step(AdminContractOperation.ReenrollmentBegin,
            HttpStatusCode.OK, Cookie: broken ? null : "repeat=repeat-token"));
        await RunAsync(steps, run => run.C2_正しいパスワードで登録し直しを開始できる(), broken);
    }

    [Theory]
    [InlineData(AdminContractOperation.EnrollmentBegin, false, false)]
    [InlineData(AdminContractOperation.EnrollmentComplete, false, false)]
    [InlineData(AdminContractOperation.TotpLogin, false, false)]
    [InlineData(AdminContractOperation.EnrollmentBegin, true, false)]
    [InlineData(AdminContractOperation.TotpLogin, true, false)]
    [InlineData(AdminContractOperation.TotpLogin, false, true)]
    [InlineData(AdminContractOperation.TotpLogin, true, true)]
    public async Task C3は通常cookieを移さず登録し直しだけを送る(
        AdminContractOperation operation, bool rename, bool broken)
    {
        var steps = LoginSteps().Concat(
        [
            new Step(AdminContractOperation.ReenrollmentBegin, HttpStatusCode.OK, Cookie: "repeat=repeat-token"),
            new Step(operation, broken ? HttpStatusCode.OK : HttpStatusCode.Unauthorized,
                ExpectedCookie: (rename ? "waiting" : "repeat") + "=repeat-token"),
        ]);
        await RunAsync(steps, run => run.C3_登録し直しのcookieをログイン途中として使えない(operation, rename), broken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task C3はPendingによる登録し直しの完了を拒む(bool broken)
    {
        var steps = LoginSteps().Take(1).Append(new Step(AdminContractOperation.ReenrollmentComplete,
            broken ? HttpStatusCode.OK : HttpStatusCode.Unauthorized));
        await RunAsync(steps, run => run.C3_Pendingだけでは登録し直しを完了できない(), broken);
    }

    [Theory]
    [InlineData(AdminContractOperation.Users, false)]
    [InlineData(AdminContractOperation.ChangePassword, false)]
    [InlineData(AdminContractOperation.AuditLogs, false)]
    [InlineData(AdminContractOperation.Users, true)]
    [InlineData(AdminContractOperation.ChangePassword, true)]
    [InlineData(AdminContractOperation.AuditLogs, true)]
    public async Task C4は全ての対象の入口で401を要求する(AdminContractOperation operation, bool broken)
    {
        var steps = LoginSteps().Take(1).Append(new Step(operation,
            broken ? HttpStatusCode.OK : HttpStatusCode.Unauthorized));
        await RunAsync(steps, run => run.C4_Pendingでは管理操作ができない(operation), broken);
    }

    [Theory]
    [InlineData("same")]
    [InlineData("value")]
    [InlineData("shape")]
    [InlineData("status")]
    [InlineData("cookie")]
    public async Task C5は本文の形だけでなく同じ言語で理由を区別しないことを検証する(string mutation)
    {
        var steps = Enumerable.Range(0, 3).Select(index => new Step(
            AdminContractOperation.PasswordLogin,
            index == 2 && mutation == "status" ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized,
            index == 2 && mutation == "shape" ? new { other = "rejected" }
                : (object)new { error = index == 2 && mutation == "value" ? "disabled" : "rejected" },
            Cookie: index == 2 && mutation == "cookie" ? "signed=unexpected" : null));
        await RunAsync(steps, run => run.C5_存在しない人と誤ったパスワードと無効の人を区別しない(), mutation != "same");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 無い入口は準備ヘルパを使わず404を要求する(bool broken)
    {
        var host = new ScriptedHost([new(AdminContractOperation.EnrollmentBegin,
            broken ? HttpStatusCode.OK : HttpStatusCode.NotFound)], absent: true);
        var run = new ContractRun(host);
        await run.InitializeAsync();
        try
        {
            if (broken)
            {
                await Assert.ThrowsAnyAsync<XunitException>(() =>
                    run.C1_登録済みの人は初回登録を使えず元のTOTPでログインできる(AdminContractOperation.EnrollmentBegin));
            }
            else
            {
                await run.C1_登録済みの人は初回登録を使えず元のTOTPでログインできる(AdminContractOperation.EnrollmentBegin);
            }
            Assert.Equal(0, host.SeededCount);
            Assert.Empty(host.Steps);
        }
        finally
        {
            await run.DisposeAsync();
        }
    }

    [Fact]
    public async Task 探査先の設定漏れを実行前に拒む()
    {
        var host = new ScriptedHost([], absent: true);
        host.Endpoints.Remove(AdminContractOperation.EnrollmentBegin);
        var run = new ContractRun(host);
        await Assert.ThrowsAsync<ArgumentException>(run.InitializeAsync);
        await run.DisposeAsync();
    }

    private static Step[] LoginSteps() =>
    [
        new(AdminContractOperation.PasswordLogin, HttpStatusCode.OK,
            new { stage = "otp" }, "waiting=pending-token"),
        new(AdminContractOperation.TotpLogin, HttpStatusCode.OK, Cookie: "signed=session-token"),
    ];

    private static async Task RunAsync(
        IEnumerable<Step> steps, Func<ContractRun, Task> test, bool broken)
    {
        var host = new ScriptedHost(steps);
        var run = new ContractRun(host);
        await run.InitializeAsync();
        try
        {
            if (broken)
            {
                await Assert.ThrowsAnyAsync<XunitException>(() => test(run));
            }
            else
            {
                await test(run);
                Assert.Empty(host.Steps);
            }
        }
        finally
        {
            await run.DisposeAsync();
        }
    }

    private sealed record Step(
        AdminContractOperation Operation,
        HttpStatusCode Status,
        object? Body = null,
        string? Cookie = null,
        string? ExpectedCookie = null);

    private sealed class ContractRun(ScriptedHost host) : AdminAuthenticationContractTests
    {
        protected override Task<IAdminContractHost> CreateHostAsync() => Task.FromResult<IAdminContractHost>(host);
    }

    private sealed class ScriptedHost : IAdminContractHost
    {
        public Queue<Step> Steps { get; }
        public int SeededCount { get; private set; }
        public Dictionary<AdminContractOperation, AdminContractEndpoint> Endpoints { get; } =
            Enum.GetValues<AdminContractOperation>().ToDictionary(operation => operation,
                operation => new AdminContractEndpoint(HttpMethod.Put, "/custom/" + operation,
                    input => new { account = input.LoginId, credential = input.Password, otp = input.Code }));
        public AdminContractSettings Settings { get; }
        public AdminContractCapabilities Capabilities { get; }

        public ScriptedHost(IEnumerable<Step> steps, bool absent = false,
            IReadOnlySet<AdminContractFeature>? features = null)
        {
            Steps = new(steps);
            Capabilities = new(absent ? [] : Enum.GetValues<AdminContractOperation>().ToHashSet())
            {
                Features = features ?? new HashSet<AdminContractFeature>(),
            };
            Settings = new()
            {
                Endpoints = Endpoints,
                Cookies = new("signed", "waiting", "repeat"),
                NextProperty = "stage",
                TotpNextValue = "otp",
                LoginFailureShape = new Dictionary<string, JsonValueKind> { ["error"] = JsonValueKind.String },
                JitNonAdministratorRole = "TestEditor",
            };
        }

        public HttpClient CreateClient() => new(new Handler(this)) { BaseAddress = new Uri("https://example.test") };

        public Task<SeededAdmin> SeedAdminAsync(bool withTotp, bool disabled = false)
        {
            SeededCount++;
            return Task.FromResult(new SeededAdmin("seed-" + SeededCount, "test-password"));
        }

        public async Task<HttpResponseMessage> LoginAsync(HttpClient client, SeededAdmin admin)
        {
            using var request = Endpoints[AdminContractOperation.PasswordLogin]
                .CreateRequest(new(LoginId: admin.LoginId, Password: admin.Password));
            return await client.SendAsync(request);
        }

        public string NextTotpCode(SeededAdmin admin) => "123456";
        public string NextReenrollmentTotpCode(SeededAdmin admin) => "654321";
        public Task<SeededAdmin> SeedAdminWithPermissionsAsync(IReadOnlySet<string> permissions)
        {
            Assert.Contains("users.read", permissions);
            Assert.InRange(permissions.Count, 1, 2);
            return SeedAdminAsync(withTotp: true);
        }

        public Task SetTwoFactorPolicyAsync(AdminContractTwoFactorPolicy policy) => Task.CompletedTask;
        public Task SetLockoutAsync(SeededAdmin admin, bool locked) => Task.CompletedTask;
        public Task EnableAdminAsync(SeededAdmin admin) => Task.CompletedTask;
        private int sessionCount;
        public Task<string> CreateSessionAsync(SeededAdmin admin, AdminContractSessionKind kind) =>
            Task.FromResult($"{kind}=token-{++sessionCount}");

        public async Task<HttpResponseMessage> ProbeSessionAsync(
            HttpClient client, SeededAdmin admin, AdminContractSessionKind kind)
        {
            using var request = Endpoints[AdminContractOperation.Users].CreateRequest(new(LoginId: admin.LoginId));
            return await client.SendAsync(request);
        }

        public Task<SeededAdmin> SeedExternalAdminAsync(
            AdminContractExternalProvider provider, bool withTotp, bool existing = true, bool knownPassword = true) =>
            SeedAdminAsync(withTotp);

        public Task<HttpResponseMessage> ExternalLoginAsync(
            HttpClient client, SeededAdmin admin, AdminContractExternalProvider provider) => LoginAsync(client, admin);

        public Task ConfigureJitAsync(AdminContractExternalProvider provider, string role, bool hasAdministrator) =>
            Task.CompletedTask;

        public Task<HttpRequestMessage> CreateUserLookupRequestAsync(SeededAdmin admin) =>
            Task.FromResult(Endpoints[AdminContractOperation.Users].CreateRequest(new(LoginId: admin.LoginId)));

        public bool ContainsUser(string responseBody, SeededAdmin admin)
        {
            using var body = JsonDocument.Parse(responseBody);
            return body.RootElement.GetProperty("exists").GetBoolean();
        }

        public Task<string> RecoveryCodeAsync(SeededAdmin admin) => Task.FromResult("old-code");

        public Task PrepareExternalReconfirmationAsync(
            HttpClient client, SeededAdmin admin, AdminContractExternalProvider provider, AdminContractReconfirmation proof) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private sealed class Handler(ScriptedHost host) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Assert.NotEmpty(host.Steps);
                var step = host.Steps.Dequeue();
                Assert.Equal(HttpMethod.Put, request.Method);
                Assert.Equal("/custom/" + step.Operation, request.RequestUri!.AbsolutePath);
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                Assert.True(body.RootElement.TryGetProperty("credential", out _));
                if (step.ExpectedCookie is not null)
                {
                    Assert.Equal(step.ExpectedCookie, Assert.Single(request.Headers.GetValues("Cookie")));
                }
                var response = new HttpResponseMessage(step.Status)
                {
                    Content = JsonContent.Create(step.Body ?? new { }),
                };
                if (step.Cookie is not null)
                {
                    response.Headers.Add("Set-Cookie", step.Cookie + "; path=/; httponly");
                }
                return response;
            }
        }
    }
}
