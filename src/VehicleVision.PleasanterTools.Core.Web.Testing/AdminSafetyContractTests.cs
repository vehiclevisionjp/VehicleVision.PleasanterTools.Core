using System.Net;
using System.Text.Json;
using Xunit;

namespace VehicleVision.PleasanterTools.Core.Web.Testing;

public abstract partial class AdminAuthenticationContractTests
{
    [Theory]
    [InlineData(AdminContractOperation.ResetTwoFactor)]
    [InlineData(AdminContractOperation.DisableUser)]
    public async Task C7_他人の解除と無効化は全段階の全端末を失効させる(AdminContractOperation operation)
    {
        if (!Host.Capabilities.Supports(AdminContractFeature.SessionRevocation))
        {
            return;
        }
        var actor = await Host.SeedAdminAsync(withTotp: true);
        var target = await Host.SeedAdminAsync(withTotp: true);
        var unrelated = await Host.SeedAdminAsync(withTotp: true);
        using var actorClient = Host.CreateClient();
        await SignInAsync(actorClient, actor);

        // 各段階の検査口が本当に有効なセッションを識別できることを、消費前の別セッションで確かめる。
        foreach (var kind in Enum.GetValues<AdminContractSessionKind>())
        {
            var control = await Host.CreateSessionAsync(target, kind);
            await AssertSessionAsync(target, kind, control, HttpStatusCode.OK);
        }
        var sessions = new List<(AdminContractSessionKind Kind, string Cookie)>();
        foreach (var kind in Enum.GetValues<AdminContractSessionKind>())
        {
            for (var device = 0; device < 2; device++)
            {
                sessions.Add((kind, await Host.CreateSessionAsync(target, kind)));
            }
        }
        Assert.Equal(6, sessions.Select(session => session.Cookie).Distinct(StringComparer.Ordinal).Count());
        var otherCookie = await Host.CreateSessionAsync(unrelated, AdminContractSessionKind.Session);
        using var response = await SendAsync(actorClient, operation, new(LoginId: target.LoginId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        if (operation == AdminContractOperation.DisableUser)
        {
            // 無効の利用者を次の要求で拒むだけでは、全セッションの失効を保証できない。
            await Host.EnableAdminAsync(target);
        }
        foreach (var (kind, cookie) in sessions)
        {
            await AssertSessionAsync(target, kind, cookie, HttpStatusCode.Unauthorized);
        }
        await AssertSessionAsync(unrelated, AdminContractSessionKind.Session, otherCookie, HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task C10_他人の2要素解除には独立したreset権限が必要である(bool allowed)
    {
        if (!Host.Capabilities.Supports(AdminContractFeature.ResetTwoFactorPermission))
        {
            return;
        }
        IReadOnlySet<string> permissions = allowed
            ? new HashSet<string> { "users.read", "users.resetTwoFactor" }
            : new HashSet<string> { "users.read" };
        var actor = await Host.SeedAdminWithPermissionsAsync(permissions);
        var target = await Host.SeedAdminAsync(withTotp: true);
        using var client = Host.CreateClient();
        await SignInAsync(client, actor);
        using var response = await SendAsync(client, AdminContractOperation.ResetTwoFactor,
            new(LoginId: target.LoginId));
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
        if (!allowed)
        {
            using var targetClient = Host.CreateClient();
            await SignInAsync(targetClient, target);
        }
    }

    [Fact]
    public async Task C12_再登録の開始後にDisabledになると400で拒み元の鍵を保つ()
    {
        if (!Host.Capabilities.Supports(AdminContractFeature.TwoFactorPolicyChange))
        {
            return;
        }
        await Host.SetTwoFactorPolicyAsync(AdminContractTwoFactorPolicy.Optional);
        var admin = await Host.SeedAdminAsync(withTotp: true);
        using var client = Host.CreateClient();
        await SignInAsync(client, admin);
        using var begin = await BeginReenrollmentAsync(client, admin);
        var code = Host.NextReenrollmentTotpCode(admin);
        await Host.SetTwoFactorPolicyAsync(AdminContractTwoFactorPolicy.Disabled);
        using var complete = await SendAsync(client, AdminContractOperation.ReenrollmentComplete, new(Code: code));
        Assert.Equal(HttpStatusCode.BadRequest, complete.StatusCode);
        AssertNoAuthenticationCookies(complete);
        using var body = JsonDocument.Parse(await complete.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.TryGetProperty(Settings.RecoveryCodesProperty, out _),
            "拒否した登録から復旧コードを返してはいけません。");
        using var original = Host.CreateClient();
        await SignInAsync(original, admin);
    }

    [Theory]
    [InlineData(AdminContractExternalProvider.Saml, false, AdminContractTwoFactorPolicy.Optional)]
    [InlineData(AdminContractExternalProvider.Saml, false, AdminContractTwoFactorPolicy.Required)]
    [InlineData(AdminContractExternalProvider.Saml, false, AdminContractTwoFactorPolicy.Disabled)]
    [InlineData(AdminContractExternalProvider.Saml, true, AdminContractTwoFactorPolicy.Optional)]
    [InlineData(AdminContractExternalProvider.Saml, true, AdminContractTwoFactorPolicy.Required)]
    [InlineData(AdminContractExternalProvider.Saml, true, AdminContractTwoFactorPolicy.Disabled)]
    [InlineData(AdminContractExternalProvider.PleasanterSso, false, AdminContractTwoFactorPolicy.Optional)]
    [InlineData(AdminContractExternalProvider.PleasanterSso, false, AdminContractTwoFactorPolicy.Required)]
    [InlineData(AdminContractExternalProvider.PleasanterSso, false, AdminContractTwoFactorPolicy.Disabled)]
    [InlineData(AdminContractExternalProvider.PleasanterSso, true, AdminContractTwoFactorPolicy.Optional)]
    [InlineData(AdminContractExternalProvider.PleasanterSso, true, AdminContractTwoFactorPolicy.Required)]
    [InlineData(AdminContractExternalProvider.PleasanterSso, true, AdminContractTwoFactorPolicy.Disabled)]
    public async Task C13_外部ログインも締め出しを守り解除後だけ認証を進める(
        AdminContractExternalProvider provider, bool withTotp, AdminContractTwoFactorPolicy policy)
    {
        if (!SupportsExternal(provider))
        {
            return;
        }
        await Host.SetTwoFactorPolicyAsync(policy);
        var admin = await Host.SeedExternalAdminAsync(provider, withTotp);
        var kind = withTotp || policy == AdminContractTwoFactorPolicy.Required
            ? AdminContractSessionKind.Pending : AdminContractSessionKind.Session;
        using (var control = Host.CreateClient())
        {
            using var success = await Host.ExternalLoginAsync(control, admin, provider);
            AssertExternalSuccess(success, kind);
            using var probe = await Host.ProbeSessionAsync(control, admin, kind);
            Assert.Equal(HttpStatusCode.OK, probe.StatusCode);
        }
        await Host.SetLockoutAsync(admin, locked: true);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var denied = Host.CreateClient();
            using var response = await Host.ExternalLoginAsync(denied, admin, provider);
            AssertExternalRejection(response);
            using var probe = await Host.ProbeSessionAsync(denied, admin, kind);
            Assert.Equal(HttpStatusCode.Unauthorized, probe.StatusCode);
        }
        await Host.SetLockoutAsync(admin, locked: false);
        using var unlocked = Host.CreateClient();
        using var accepted = await Host.ExternalLoginAsync(unlocked, admin, provider);
        AssertExternalSuccess(accepted, kind);
        using var unlockedProbe = await Host.ProbeSessionAsync(unlocked, admin, kind);
        Assert.Equal(HttpStatusCode.OK, unlockedProbe.StatusCode);
    }

    [Theory]
    [InlineData(AdminContractExternalProvider.Saml, true)]
    [InlineData(AdminContractExternalProvider.Saml, false)]
    [InlineData(AdminContractExternalProvider.PleasanterSso, true)]
    [InlineData(AdminContractExternalProvider.PleasanterSso, false)]
    public async Task C14_JITはAdministratorと管理者0人を拒み利用者を作らない(
        AdminContractExternalProvider provider, bool hasAdministrator)
    {
        var feature = provider == AdminContractExternalProvider.Saml
            ? AdminContractFeature.SamlJit : AdminContractFeature.PleasanterSsoJit;
        if (!Host.Capabilities.Supports(feature))
        {
            return;
        }
        var observer = await Host.SeedAdminWithPermissionsAsync(new HashSet<string> { "users.read" });
        using var observerClient = Host.CreateClient();
        await SignInAsync(observerClient, observer);
        await Host.SetTwoFactorPolicyAsync(AdminContractTwoFactorPolicy.Optional);
        await Host.ConfigureJitAsync(provider, Settings.JitNonAdministratorRole!, hasAdministrator: true);
        var control = await Host.SeedExternalAdminAsync(provider, withTotp: false, existing: false);
        using (var controlClient = Host.CreateClient())
        {
            using var success = await Host.ExternalLoginAsync(controlClient, control, provider);
            AssertExternalSuccess(success, AdminContractSessionKind.Session);
        }
        await AssertUserExistsAsync(observerClient, control, exists: true);

        await Host.ConfigureJitAsync(provider,
            hasAdministrator ? "Administrator" : Settings.JitNonAdministratorRole!, hasAdministrator);
        var denied = await Host.SeedExternalAdminAsync(provider, withTotp: false, existing: false);
        await AssertUserExistsAsync(observerClient, denied, exists: false);
        using var client = Host.CreateClient();
        using var response = await Host.ExternalLoginAsync(client, denied, provider);
        AssertExternalRejection(response);
        using var deniedProbe = await Host.ProbeSessionAsync(client, denied, AdminContractSessionKind.Session);
        Assert.Equal(HttpStatusCode.Unauthorized, deniedProbe.StatusCode);
        await AssertUserExistsAsync(observerClient, denied, exists: false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task C19_復旧コードは逐次でも競合でも1回しか使えない(bool concurrent)
    {
        if (!Host.Capabilities.Supports(AdminContractFeature.RecoveryCodes))
        {
            return;
        }
        var admin = await Host.SeedAdminAsync(withTotp: true);
        var code = await Host.RecoveryCodeAsync(admin);
        using var first = await PendingClientAsync(admin);
        using var second = await PendingClientAsync(admin);
        var input = new AdminContractInput(Code: code);
        var firstTask = SendAsync(first, AdminContractOperation.RecoveryLogin, input);
        if (!concurrent)
        {
            using var success = await firstTask;
            AssertRecoverySuccess(success);
            using var replay = await SendAsync(second, AdminContractOperation.RecoveryLogin, input);
            Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
            AssertNoAuthenticationCookies(replay);
            return;
        }
        var secondTask = SendAsync(second, AdminContractOperation.RecoveryLogin, input);
        var responses = await Task.WhenAll(firstTask, secondTask);
        try
        {
            var success = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            AssertRecoverySuccess(success);
            var denied = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Unauthorized);
            AssertNoAuthenticationCookies(denied);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task C19_再発行は旧復旧コードを失効させ新コードだけで入れる()
    {
        if (!Host.Capabilities.Supports(AdminContractFeature.RecoveryCodes))
        {
            return;
        }
        var admin = await Host.SeedAdminAsync(withTotp: true);
        var oldCode = await Host.RecoveryCodeAsync(admin);
        using var client = Host.CreateClient();
        await SignInAsync(client, admin);
        using var begin = await BeginReenrollmentAsync(client, admin);
        using var complete = await SendAsync(client, AdminContractOperation.ReenrollmentComplete,
            new(Code: Host.NextReenrollmentTotpCode(admin)));
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        using var body = JsonDocument.Parse(await complete.Content.ReadAsStringAsync());
        var codes = body.RootElement.GetProperty(Settings.RecoveryCodesProperty).EnumerateArray()
            .Select(value => value.GetString()).ToArray();
        Assert.Equal(10, codes.Length);
        Assert.All(codes, code => Assert.False(string.IsNullOrWhiteSpace(code)));
        Assert.Equal(10, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.False(codes.Contains(oldCode, StringComparer.Ordinal), "旧復旧コードを再発行してはいけません。");
        using var oldClient = await PendingClientAsync(admin);
        using var rejected = await SendAsync(oldClient, AdminContractOperation.RecoveryLogin, new(Code: oldCode));
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        AssertNoAuthenticationCookies(rejected);
        using var newClient = await PendingClientAsync(admin);
        using var accepted = await SendAsync(newClient, AdminContractOperation.RecoveryLogin, new(Code: codes[0]));
        AssertRecoverySuccess(accepted);
    }

    public static IEnumerable<object[]> ExternalReconfirmationCases =>
        from provider in Enum.GetValues<AdminContractExternalProvider>()
        from operation in new[] { AdminContractOperation.ReenrollmentBegin, AdminContractOperation.DisableOwnTwoFactor }
        from proof in Enum.GetValues<AdminContractReconfirmation>()
        select new object[] { provider, operation, proof };

    [Theory]
    [MemberData(nameof(ExternalReconfirmationCases))]
    public async Task C20_パスワードを持たない本人も有効な外部再確認だけで再登録と解除ができる(
        AdminContractExternalProvider provider, AdminContractOperation operation, AdminContractReconfirmation proof)
    {
        if (!Host.Capabilities.Supports(AdminContractFeature.ExternalReconfirmation) || !SupportsExternal(provider))
        {
            return;
        }
        await Host.SetTwoFactorPolicyAsync(AdminContractTwoFactorPolicy.Optional);
        var admin = await Host.SeedExternalAdminAsync(provider, withTotp: true, knownPassword: false);
        using var client = Host.CreateClient();
        await Host.PrepareExternalReconfirmationAsync(client, admin, provider, proof);
        using var response = await SendAsync(client, operation, new());
        if (proof == AdminContractReconfirmation.Fresh)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            if (operation == AdminContractOperation.ReenrollmentBegin)
            {
                _ = Cookie(response, Settings.Cookies.Reenroll);
                using var complete = await SendAsync(client, AdminContractOperation.ReenrollmentComplete,
                    new(Code: Host.NextReenrollmentTotpCode(admin)));
                Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
            }
            else
            {
                using var nextLogin = Host.CreateClient();
                using var external = await Host.ExternalLoginAsync(nextLogin, admin, provider);
                AssertExternalSuccess(external, AdminContractSessionKind.Session);
            }
        }
        else
        {
            Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized
                or HttpStatusCode.Forbidden or HttpStatusCode.Conflict or HttpStatusCode.Redirect,
                "本人の有効な再確認が無い操作は拒否する必要があります。");
            AssertNoAuthenticationCookies(response);
            using var nextLogin = Host.CreateClient();
            using var external = await Host.ExternalLoginAsync(nextLogin, admin, provider);
            AssertExternalSuccess(external, AdminContractSessionKind.Pending);
            await CompleteLoginAsync(nextLogin, admin);
        }
    }

    [Fact]
    public async Task C21_再登録の制限は同じSessionを別端末で送っても迂回できない()
    {
        if (!Host.Capabilities.Supports(AdminContractFeature.ReenrollmentRateLimit))
        {
            return;
        }
        var admin = await Host.SeedAdminAsync(withTotp: true);
        using var client = Host.CreateClient();
        await PasswordLoginAsync(client, admin);
        using var login = await SendAsync(client, AdminContractOperation.TotpLogin,
            new(Code: Host.NextTotpCode(admin)));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var cookie = Cookie(login, Settings.Cookies.Session);
        for (var attempt = 0; attempt < 7; attempt++)
        {
            using var isolated = Host.CreateClient();
            using var response = await SendAsync(isolated, AdminContractOperation.ReenrollmentBegin,
                new(Password: attempt < 5 ? admin.Password + "-incorrect" : admin.Password), cookie);
            Assert.Equal(attempt < 5 ? HttpStatusCode.Unauthorized : HttpStatusCode.Locked, response.StatusCode);
            AssertNoAuthenticationCookies(response);
        }
    }

    private bool SupportsExternal(AdminContractExternalProvider provider) =>
        Host.Capabilities.Supports(provider == AdminContractExternalProvider.Saml
            ? AdminContractFeature.SamlLogin : AdminContractFeature.PleasanterSsoLogin);

    private void AssertNoAuthenticationCookies(HttpResponseMessage response)
    {
        AssertNoCookie(response, Settings.Cookies.Session);
        AssertNoCookie(response, Settings.Cookies.Pending);
        AssertNoCookie(response, Settings.Cookies.Reenroll);
    }

    private void AssertExternalRejection(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Unauthorized
            or HttpStatusCode.Forbidden or HttpStatusCode.Conflict or HttpStatusCode.Locked,
            "外部認証は正常な拒否応答で終了する必要があります。");
        AssertNoAuthenticationCookies(response);
    }

    private void AssertExternalSuccess(HttpResponseMessage response, AdminContractSessionKind kind)
    {
        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Redirect);
        _ = Cookie(response, kind == AdminContractSessionKind.Pending ? Settings.Cookies.Pending : Settings.Cookies.Session);
        AssertNoCookie(response, kind == AdminContractSessionKind.Pending ? Settings.Cookies.Session : Settings.Cookies.Pending);
    }

    private void AssertRecoverySuccess(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _ = Cookie(response, Settings.Cookies.Session);
    }

    private async Task AssertSessionAsync(
        SeededAdmin admin, AdminContractSessionKind kind, string cookie, HttpStatusCode status)
    {
        using var client = Host.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        using var response = await Host.ProbeSessionAsync(client, admin, kind);
        Assert.Equal(status, response.StatusCode);
        if (status == HttpStatusCode.Unauthorized)
        {
            AssertNoAuthenticationCookies(response);
        }
    }

    private async Task<HttpClient> PendingClientAsync(SeededAdmin admin)
    {
        var client = Host.CreateClient();
        try
        {
            var cookie = await Host.CreateSessionAsync(admin, AdminContractSessionKind.Pending);
            client.DefaultRequestHeaders.Add("Cookie", cookie);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private async Task AssertUserExistsAsync(HttpClient observer, SeededAdmin admin, bool exists)
    {
        using var request = await Host.CreateUserLookupRequestAsync(admin);
        using var response = await observer.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(exists, Host.ContainsUser(await response.Content.ReadAsStringAsync(), admin));
    }
}
