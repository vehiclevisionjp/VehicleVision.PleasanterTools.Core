using System.Net;
using VehicleVision.PleasanterTools.Core.Web.Testing;
using Xunit.Sdk;

namespace VehicleVision.PleasanterTools.Core.Web.Tests;

public sealed partial class AdminContractTestsTests
{
    public static IEnumerable<object[]> RevocationCases =>
        from operation in new[] { AdminContractOperation.ResetTwoFactor, AdminContractOperation.DisableUser }
        from mutation in new[] { "none", "Session", "Pending", "Reenroll", "unrelated", "always-deny", "operation" }
        select new object[] { operation, mutation };

    [Theory]
    [MemberData(nameof(RevocationCases))]
    public async Task C7は各段階の残存と無関係な人の巻き添えも検出する(
        AdminContractOperation operation, string mutation)
    {
        var steps = LoginSteps().ToList();
        foreach (var kind in Enum.GetValues<AdminContractSessionKind>())
        {
            steps.Add(new(AdminContractOperation.Users,
                mutation == "always-deny" ? HttpStatusCode.Unauthorized : HttpStatusCode.OK,
                ExpectedCookie: $"{kind}=token-{steps.Count - 1}"));
        }
        steps.Add(new(operation, mutation == "operation" ? HttpStatusCode.Forbidden : HttpStatusCode.OK));
        var token = 4;
        foreach (var kind in Enum.GetValues<AdminContractSessionKind>())
        {
            for (var device = 0; device < 2; device++)
            {
                steps.Add(new(AdminContractOperation.Users,
                    mutation == kind.ToString() ? HttpStatusCode.OK : HttpStatusCode.Unauthorized,
                    ExpectedCookie: $"{kind}=token-{token++}"));
            }
        }
        steps.Add(new(AdminContractOperation.Users,
            mutation == "unrelated" ? HttpStatusCode.Unauthorized : HttpStatusCode.OK,
            ExpectedCookie: "Session=token-10"));
        await RunSafetyAsync(steps, run => run.C7_他人の解除と無効化は全段階の全端末を失効させる(operation),
            mutation != "none", AdminContractFeature.SessionRevocation);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task C10はreadだけの許可とreset権限の拒否を検出する(bool allowed, bool broken)
    {
        var steps = LoginSteps().Append(new Step(AdminContractOperation.ResetTwoFactor,
            allowed != broken ? HttpStatusCode.OK : HttpStatusCode.Forbidden));
        if (!allowed)
        {
            steps = steps.Concat(LoginSteps());
        }
        await RunSafetyAsync(steps, run => run.C10_他人の2要素解除には独立したreset権限が必要である(allowed),
            broken, AdminContractFeature.ResetTwoFactorPermission);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("403")]
    [InlineData("500")]
    [InlineData("success")]
    [InlineData("codes")]
    [InlineData("cookie")]
    [InlineData("old-key")]
    public async Task C12は400だけでなく復旧コード非発行と元の鍵の継続を検証する(string mutation)
    {
        var steps = LoginSteps().Concat(
        [
            new Step(AdminContractOperation.ReenrollmentBegin, HttpStatusCode.OK, Cookie: "repeat=new-key"),
            new Step(AdminContractOperation.ReenrollmentComplete, mutation switch
            {
                "403" => HttpStatusCode.Forbidden,
                "500" => HttpStatusCode.InternalServerError,
                "success" => HttpStatusCode.OK,
                _ => HttpStatusCode.BadRequest,
            }, mutation == "codes" ? new { recoveryCodes = new[] { "leaked" } } : new { },
                Cookie: mutation == "cookie" ? "signed=wrong" : null),
            LoginSteps()[0],
            mutation == "old-key"
                ? new Step(AdminContractOperation.TotpLogin, HttpStatusCode.Unauthorized) : LoginSteps()[1],
        ]);
        await RunSafetyAsync(steps, run => run.C12_再登録の開始後にDisabledになると400で拒み元の鍵を保つ(),
            mutation != "none", AdminContractFeature.TwoFactorPolicyChange);
    }

    public static IEnumerable<object[]> ExternalLoginCases =>
        from provider in Enum.GetValues<AdminContractExternalProvider>()
        from withTotp in new[] { false, true }
        from policy in Enum.GetValues<AdminContractTwoFactorPolicy>()
        from mutation in new[] { "none", "ignore-lockout", "clear-lockout", "pending", "always-deny", "never-unlock" }
        select new object[] { provider, withTotp, policy, mutation };

    [Theory]
    [MemberData(nameof(ExternalLoginCases))]
    public async Task C13は締め出し無視と履歴消去と常時拒否を検出する(
        AdminContractExternalProvider provider, bool withTotp, AdminContractTwoFactorPolicy policy, string mutation)
    {
        var pending = withTotp || policy == AdminContractTwoFactorPolicy.Required;
        var success = new Step(AdminContractOperation.PasswordLogin, HttpStatusCode.Redirect,
            Cookie: pending ? "waiting=external" : "signed=external");
        var deny = new Step(AdminContractOperation.PasswordLogin, HttpStatusCode.Forbidden);
        var steps = new[]
        {
            mutation == "always-deny" ? deny : success,
            new Step(AdminContractOperation.Users, HttpStatusCode.OK),
            mutation == "ignore-lockout" ? success
                : mutation == "pending" ? deny with { Cookie = "waiting=bypass" } : deny,
            new Step(AdminContractOperation.Users, HttpStatusCode.Unauthorized),
            mutation == "clear-lockout" ? success : deny,
            new Step(AdminContractOperation.Users, HttpStatusCode.Unauthorized),
            mutation == "never-unlock" ? deny : success,
            new Step(AdminContractOperation.Users, HttpStatusCode.OK),
        };
        await RunSafetyAsync(steps,
            run => run.C13_外部ログインも締め出しを守り解除後だけ認証を進める(provider, withTotp, policy),
            mutation != "none", ExternalFeature(provider));
    }

    public static IEnumerable<object[]> JitCases =>
        from provider in Enum.GetValues<AdminContractExternalProvider>()
        from hasAdministrator in new[] { false, true }
        from mutation in new[] { "none", "allowed", "created", "always-deny", "no-control-user" }
        select new object[] { provider, hasAdministrator, mutation };

    [Theory]
    [MemberData(nameof(JitCases))]
    public async Task C14は拒否時の利用者作成とJITの常時拒否を検出する(
        AdminContractExternalProvider provider, bool hasAdministrator, string mutation)
    {
        var steps = LoginSteps().Concat(
        [
            new Step(AdminContractOperation.PasswordLogin,
                mutation == "always-deny" ? HttpStatusCode.Forbidden : HttpStatusCode.OK,
                Cookie: mutation == "always-deny" ? null : "signed=jit"),
            new Step(AdminContractOperation.Users, HttpStatusCode.OK, new { exists = mutation != "no-control-user" }),
            new Step(AdminContractOperation.Users, HttpStatusCode.OK, new { exists = false }),
            new Step(AdminContractOperation.PasswordLogin,
                mutation == "allowed" ? HttpStatusCode.OK : HttpStatusCode.Conflict,
                Cookie: mutation == "allowed" ? "signed=admin-jit" : null),
            new Step(AdminContractOperation.Users, HttpStatusCode.Unauthorized),
            new Step(AdminContractOperation.Users, HttpStatusCode.OK, new { exists = mutation == "created" }),
        ]);
        await RunSafetyAsync(steps,
            run => run.C14_JITはAdministratorと管理者0人を拒み利用者を作らない(provider, hasAdministrator),
            mutation != "none", ExternalFeature(provider), provider == AdminContractExternalProvider.Saml
                ? AdminContractFeature.SamlJit : AdminContractFeature.PleasanterSsoJit);
    }

    [Theory]
    [InlineData(false, "none")]
    [InlineData(true, "none")]
    [InlineData(false, "replay")]
    [InlineData(true, "replay")]
    [InlineData(false, "always-deny")]
    [InlineData(true, "always-deny")]
    [InlineData(false, "cookie")]
    [InlineData(true, "cookie")]
    public async Task C19は復旧コードの逐次使用と競合使用の二重成功を検出する(bool concurrent, string mutation)
    {
        var steps = new[]
        {
            new Step(AdminContractOperation.RecoveryLogin,
                mutation == "always-deny" ? HttpStatusCode.Unauthorized : HttpStatusCode.OK,
                Cookie: mutation is "always-deny" or "cookie" ? null : "signed=recovered",
                ExpectedCookie: "Pending=token-1"),
            new Step(AdminContractOperation.RecoveryLogin,
                mutation == "replay" ? HttpStatusCode.OK : HttpStatusCode.Unauthorized,
                ExpectedCookie: "Pending=token-2"),
        };
        await RunSafetyAsync(steps, run => run.C19_復旧コードは逐次でも競合でも1回しか使えない(concurrent),
            mutation != "none", AdminContractFeature.RecoveryCodes);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("old-code")]
    [InlineData("new-code")]
    [InlineData("count")]
    [InlineData("duplicate")]
    public async Task C19は再発行時の旧コード失効と新コードの利用を検証する(string mutation)
    {
        var codes = Enumerable.Range(1, mutation == "count" ? 9 : 10)
            .Select(index => mutation == "duplicate" ? "same" : "new-code-" + index).ToArray();
        var steps = LoginSteps().Concat(
        [
            new Step(AdminContractOperation.ReenrollmentBegin, HttpStatusCode.OK, Cookie: "repeat=new-key"),
            new Step(AdminContractOperation.ReenrollmentComplete, HttpStatusCode.OK, new { recoveryCodes = codes }),
            new Step(AdminContractOperation.RecoveryLogin,
                mutation == "old-code" ? HttpStatusCode.OK : HttpStatusCode.Unauthorized),
            new Step(AdminContractOperation.RecoveryLogin,
                mutation == "new-code" ? HttpStatusCode.Unauthorized : HttpStatusCode.OK,
                Cookie: "signed=recovered"),
        ]);
        await RunSafetyAsync(steps, run => run.C19_再発行は旧復旧コードを失効させ新コードだけで入れる(),
            mutation != "none", AdminContractFeature.RecoveryCodes);
    }

    public static IEnumerable<object[]> ReconfirmationCases =>
        from values in AdminAuthenticationContractTests.ExternalReconfirmationCases
        from broken in new[] { false, true }
        select values.Append((object)broken).ToArray();

    [Theory]
    [MemberData(nameof(ReconfirmationCases))]
    public async Task C20は全ての無効な証跡と本人の正常な証跡を検証する(
        AdminContractExternalProvider provider, AdminContractOperation operation,
        AdminContractReconfirmation proof, bool broken)
    {
        var success = (proof == AdminContractReconfirmation.Fresh) != broken;
        var steps = new List<Step>
        {
            new Step(operation, success ? HttpStatusCode.OK : HttpStatusCode.Unauthorized,
                Cookie: success && operation == AdminContractOperation.ReenrollmentBegin ? "repeat=external" : null),
        };
        if (proof == AdminContractReconfirmation.Fresh)
        {
            steps.Add(operation == AdminContractOperation.ReenrollmentBegin
                ? new Step(AdminContractOperation.ReenrollmentComplete, HttpStatusCode.OK)
                : new Step(AdminContractOperation.PasswordLogin, HttpStatusCode.OK, Cookie: "signed=external"));
        }
        else
        {
            steps.Add(new(AdminContractOperation.PasswordLogin, HttpStatusCode.OK, Cookie: "waiting=external"));
            steps.Add(LoginSteps()[1]);
        }
        await RunSafetyAsync(steps,
            run => run.C20_パスワードを持たない本人も有効な外部再確認だけで再登録と解除ができる(provider, operation, proof),
            broken, ExternalFeature(provider), AdminContractFeature.ExternalReconfirmation);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("no-limit")]
    [InlineData("early")]
    [InlineData("cookie")]
    public async Task C21はcookieを別端末へ移した制限迂回と拒否時の発行を検出する(string mutation)
    {
        var steps = LoginSteps().Concat(Enumerable.Range(0, 7).Select(attempt =>
            new Step(AdminContractOperation.ReenrollmentBegin,
                mutation == "no-limit" ? HttpStatusCode.Unauthorized
                    : attempt < (mutation == "early" ? 4 : 5) ? HttpStatusCode.Unauthorized : HttpStatusCode.Locked,
                Cookie: mutation == "cookie" ? "repeat=forbidden" : null,
                ExpectedCookie: "signed=session-token")));
        await RunSafetyAsync(steps, run => run.C21_再登録の制限は同じSessionを別端末で送っても迂回できない(),
            mutation != "none", AdminContractFeature.ReenrollmentRateLimit);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 既存ホストは新操作の探査先も追加ヘルパも実装せず従来の契約を実行できる(bool allEnumValues)
    {
        var host = new ScriptedHost(LoginSteps().Append(
            new Step(AdminContractOperation.ReenrollmentBegin, HttpStatusCode.OK, Cookie: "repeat=repeat-token")));
        foreach (var operation in Enum.GetValues<AdminContractOperation>()
            .Where(operation => operation > AdminContractOperation.AuditLogs))
        {
            host.Endpoints.Remove(operation);
        }
        var legacy = new LegacyHost(host, allEnumValues
            ? new(Enum.GetValues<AdminContractOperation>().ToHashSet()) : null);
        var run = new LegacyRun(legacy);
        await run.InitializeAsync();
        try
        {
            await run.C2_正しいパスワードで登録し直しを開始できる();
            await RunAllAdditionalAsync(run);
            Assert.Empty(host.Steps);
            Assert.Equal(1, host.SeededCount);
            await Assert.ThrowsAsync<NotSupportedException>(async () =>
                await ((IAdminContractHost)legacy).SetLockoutAsync(new("unused", "unused"), true));
        }
        finally
        {
            await run.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(AdminContractFeature.SessionRevocation, AdminContractOperation.DisableUser)]
    [InlineData(AdminContractFeature.ResetTwoFactorPermission, AdminContractOperation.ResetTwoFactor)]
    [InlineData(AdminContractFeature.TwoFactorPolicyChange, AdminContractOperation.ReenrollmentComplete)]
    [InlineData(AdminContractFeature.RecoveryCodes, AdminContractOperation.RecoveryLogin)]
    [InlineData(AdminContractFeature.ExternalReconfirmation, AdminContractOperation.DisableOwnTwoFactor)]
    [InlineData(AdminContractFeature.ReenrollmentRateLimit, AdminContractOperation.ReenrollmentBegin)]
    public async Task 能力と必要な入口の矛盾は初期化で失敗する(
        AdminContractFeature feature, AdminContractOperation missing)
    {
        var host = new ScriptedHost([], features: new HashSet<AdminContractFeature> { feature });
        var operations = host.Capabilities.Operations.Where(operation => operation != missing).ToHashSet();
        var run = new LegacyRun(new LegacyHost(host, new(operations) { Features = host.Capabilities.Features }));
        await Assert.ThrowsAsync<ArgumentException>(run.InitializeAsync);
        await run.DisposeAsync();
    }

    [Fact]
    public void 対象者のIDを経路に含む操作も既存の本文変換と併用できる()
    {
        var endpoint = new AdminContractEndpoint(HttpMethod.Post, "/custom/users/probe/reset", _ => null)
        {
            PathForInput = input => "/custom/users/" + Uri.EscapeDataString(input.LoginId!) + "/reset",
        };
        using var request = endpoint.CreateRequest(new(LoginId: "target-user"));
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/custom/users/target-user/reset", request.RequestUri!.OriginalString);
        Assert.Null(request.Content);
    }

    [Fact]
    public async Task 追加能力を宣言して準備を実装しないホストは未対応の例外で失敗する()
    {
        var host = new ScriptedHost([]);
        var capabilities = new AdminContractCapabilities(host.Capabilities.Operations)
        {
            Features = new HashSet<AdminContractFeature> { AdminContractFeature.TwoFactorPolicyChange },
        };
        var run = new LegacyRun(new LegacyHost(host, capabilities));
        await run.InitializeAsync();
        try
        {
            await Assert.ThrowsAsync<NotSupportedException>(
                run.C12_再登録の開始後にDisabledになると400で拒み元の鍵を保つ);
        }
        finally
        {
            await run.DisposeAsync();
        }
    }

    private static async Task RunAllAdditionalAsync(AdminAuthenticationContractTests run)
    {
        foreach (var operation in new[] { AdminContractOperation.ResetTwoFactor, AdminContractOperation.DisableUser })
        {
            await run.C7_他人の解除と無効化は全段階の全端末を失効させる(operation);
        }
        await run.C10_他人の2要素解除には独立したreset権限が必要である(false);
        await run.C12_再登録の開始後にDisabledになると400で拒み元の鍵を保つ();
        foreach (var provider in Enum.GetValues<AdminContractExternalProvider>())
        {
            await run.C13_外部ログインも締め出しを守り解除後だけ認証を進める(
                provider, true, AdminContractTwoFactorPolicy.Optional);
            await run.C14_JITはAdministratorと管理者0人を拒み利用者を作らない(provider, true);
            await run.C20_パスワードを持たない本人も有効な外部再確認だけで再登録と解除ができる(
                provider, AdminContractOperation.ReenrollmentBegin, AdminContractReconfirmation.Fresh);
        }
        await run.C19_復旧コードは逐次でも競合でも1回しか使えない(false);
        await run.C19_再発行は旧復旧コードを失効させ新コードだけで入れる();
        await run.C21_再登録の制限は同じSessionを別端末で送っても迂回できない();
    }

    private static AdminContractFeature ExternalFeature(AdminContractExternalProvider provider) =>
        provider == AdminContractExternalProvider.Saml
            ? AdminContractFeature.SamlLogin : AdminContractFeature.PleasanterSsoLogin;

    private static async Task RunSafetyAsync(
        IEnumerable<Step> steps, Func<ContractRun, Task> test, bool broken, params AdminContractFeature[] features)
    {
        var host = new ScriptedHost(steps, features: features.ToHashSet());
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

    private sealed class LegacyRun(IAdminContractHost host) : AdminAuthenticationContractTests
    {
        protected override Task<IAdminContractHost> CreateHostAsync() => Task.FromResult(host);
    }

    // 公開済みの必須メンバーだけを実装し、追加メンバーの既定実装と未宣言の適用外を実際に通す。
    private sealed class LegacyHost(ScriptedHost inner, AdminContractCapabilities? capabilities = null) : IAdminContractHost
    {
        public AdminContractSettings Settings => inner.Settings;
        public AdminContractCapabilities Capabilities => capabilities
            ?? new(inner.Endpoints.Keys.Where(operation => operation <= AdminContractOperation.AuditLogs).ToHashSet());
        public HttpClient CreateClient() => inner.CreateClient();
        public Task<SeededAdmin> SeedAdminAsync(bool withTotp, bool disabled = false) => inner.SeedAdminAsync(withTotp, disabled);
        public Task<HttpResponseMessage> LoginAsync(HttpClient client, SeededAdmin admin) => inner.LoginAsync(client, admin);
        public string NextTotpCode(SeededAdmin admin) => inner.NextTotpCode(admin);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
