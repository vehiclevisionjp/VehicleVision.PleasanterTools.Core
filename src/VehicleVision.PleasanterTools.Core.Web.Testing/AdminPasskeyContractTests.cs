using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace VehicleVision.PleasanterTools.Core.Web.Testing;

/// <summary>
/// パスキーの HTTP 契約（認証とセッションの仕様書 3.10.5 の P1〜P10）。アプリはホストの生成を実装して継承する。
/// パスキーを有効にしたホストで流す。経路・本文の形は <see cref="AdminPasskeyContractSettings"/> と API 契約の仕様書 5.3.1 のとおり。
/// </summary>
public abstract class AdminPasskeyContractTests : IAsyncLifetime
{
    private IAdminPasskeyContractHost? host;
    private IAdminPasskeyContractHost Host => host ?? throw new InvalidOperationException("ホストが初期化されていません。");
    private AdminPasskeyContractSettings Settings => Host.Settings;

    /// <summary>試験ごとに独立したホストを作る（パスキーは有効）。準備に失敗した場合の後片付けもアプリ側で行う。</summary>
    protected abstract Task<IAdminPasskeyContractHost> CreateHostAsync();

    /// <inheritdoc />
    public async Task InitializeAsync() => host = await CreateHostAsync();

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (host is not null)
        {
            await host.DisposeAsync();
        }
    }

    // ---- P1: userVerification=required ----

    [Fact]
    public async Task P1_オプションはresidentKeyとuserVerificationを必須にし_allowCredentialsは空である()
    {
        var admin = await Host.SeedAdminAsync(new());
        using var client = await SessionAsync(admin);
        using var begin = await PostAsync(client, Settings.RegisterBegin, new { password = admin.Password });
        Assert.Equal(HttpStatusCode.OK, begin.StatusCode);
        var registration = JsonNode.Parse(await begin.Content.ReadAsStringAsync())!;
        Assert.Equal("required", registration["authenticatorSelection"]!["residentKey"]!.GetValue<string>());
        Assert.Equal("required", registration["authenticatorSelection"]!["userVerification"]!.GetValue<string>());
        Assert.Equal("none", registration["attestation"]!.GetValue<string>());

        using var anonymous = Host.CreateClient();
        using var login = await PostAsync(anonymous, Settings.LoginBegin, new { });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var assertion = JsonNode.Parse(await login.Content.ReadAsStringAsync())!;
        Assert.Equal("required", assertion["userVerification"]!.GetValue<string>());
        Assert.True(assertion["allowCredentials"] is null || assertion["allowCredentials"]!.AsArray().Count == 0,
            "ディスコバラブルのため allowCredentials は空でなければなりません。");
    }

    [Fact]
    public async Task P1_本人確認なしの登録は400で拒み行を作らない()
    {
        var admin = await Host.SeedAdminAsync(new());
        using var client = await SessionAsync(admin);
        var authenticator = new VirtualPasskeyAuthenticator();
        using var response = await RegisterAsync(client, admin, authenticator, new() { UserVerified = false });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await ListAsync(client));
    }

    [Fact]
    public async Task P1_本人確認なしのログインは401でセッションを作らない()
    {
        var admin = await Host.SeedAdminAsync(new());
        var authenticator = new VirtualPasskeyAuthenticator();
        using (var client = await SessionAsync(admin))
        {
            using var registered = await RegisterAsync(client, admin, authenticator);
            Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        }

        using var anonymous = Host.CreateClient();
        using var response = await PasskeyLoginAsync(anonymous, authenticator, new() { UserVerified = false });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var list = await anonymous.GetAsync(Settings.List);
        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
    }

    // ---- P2: パスキーで入ったセッションだけが 2 要素を満たす ----

    [Fact]
    public async Task P2_TOTPを登録済みの人もパスキーでPendingを経ずにSessionへ入る()
    {
        var admin = await Host.SeedAdminAsync(new());
        var authenticator = new VirtualPasskeyAuthenticator();
        using (var setup = await SessionAsync(admin))
        {
            using var registered = await RegisterAsync(setup, admin, authenticator);
            Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        }

        await Host.EnableTotpAsync(admin);
        using var passwordOnly = Host.CreateClient();
        using var pending = await Host.PasswordLoginAsync(passwordOnly, admin.LoginId, admin.Password);
        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
        using var notYet = await passwordOnly.GetAsync(Settings.List);
        Assert.Equal(HttpStatusCode.Unauthorized, notYet.StatusCode);

        using var client = Host.CreateClient();
        using var response = await PasskeyLoginAsync(client, authenticator);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("done", body.RootElement.GetProperty("next").GetString());
        using var list = await client.GetAsync(Settings.List);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        if (await Host.HasSessionViaAsync(admin, "passkey") is { } hasPasskeySession)
        {
            Assert.True(hasPasskeySession, "パスキーで入ったセッションは via=passkey でなければなりません。");
        }
    }

    // ---- P3: 本人の分だけ ----

    [Fact]
    public async Task P3_他人の分の名前の変更と削除は404で存在を知らせない()
    {
        var owner = await Host.SeedAdminAsync(new());
        var other = await Host.SeedAdminAsync(new());
        using var ownerClient = await SessionAsync(owner);
        using var otherClient = await SessionAsync(other);
        using var registered = await RegisterAsync(ownerClient, owner, new VirtualPasskeyAuthenticator());
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        var id = (await ListAsync(ownerClient)).Single().Id;

        Assert.Empty(await ListAsync(otherClient));
        using var rename = await PutAsync(otherClient, Settings.Rename(id), new { name = "乗っ取り" });
        Assert.Equal(HttpStatusCode.NotFound, rename.StatusCode);
        using var remove = await PostAsync(otherClient, Settings.Remove(id), new { password = other.Password });
        Assert.Equal(HttpStatusCode.NotFound, remove.StatusCode);
        using var nonexistent = await PutAsync(otherClient, Settings.Rename(Guid.NewGuid()), new { name = "x" });
        Assert.Equal(HttpStatusCode.NotFound, nonexistent.StatusCode);
        Assert.Equal("仕事用 PC", (await ListAsync(ownerClient)).Single().Name);
    }

    [Fact]
    public async Task P3_一覧は公開鍵と資格情報のIDを返さず_名前の変更ができる()
    {
        var admin = await Host.SeedAdminAsync(new());
        var authenticator = new VirtualPasskeyAuthenticator();
        using var client = await SessionAsync(admin);
        using var registered = await RegisterAsync(client, admin, authenticator);
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        using var raw = await client.GetAsync(Settings.List);
        var text = await raw.Content.ReadAsStringAsync();
        Assert.DoesNotContain("publicKey", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credentialId", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Convert.ToBase64String(authenticator.CredentialId), text, StringComparison.Ordinal);

        var id = (await ListAsync(client)).Single().Id;
        using var rename = await PutAsync(client, Settings.Rename(id), new { name = "  私用 Mac  " });
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        Assert.Equal("私用 Mac", (await ListAsync(client)).Single().Name);
        using var empty = await PutAsync(client, Settings.Rename(id), new { name = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [Fact]
    public async Task P3_ログインしていない人は一覧も登録も401()
    {
        using var client = Host.CreateClient();
        using var list = await client.GetAsync(Settings.List);
        using var begin = await PostAsync(client, Settings.RegisterBegin, new { password = "x" });
        using var complete = await PostAsync(client, Settings.RegisterComplete, new { credential = new { } });
        Assert.All([list, begin, complete], r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
    }

    // ---- P4: 登録・削除は直近の再認証 ----

    [Fact]
    public async Task P4_登録の開始は再認証が無い_または違うと401_締め出しは423()
    {
        var admin = await Host.SeedAdminAsync(new());
        using var client = await SessionAsync(admin);
        using var none = await PostAsync(client, Settings.RegisterBegin, new { });
        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
        using var wrong = await PostAsync(client, Settings.RegisterBegin, new { password = "wrong-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        using var ok = await PostAsync(client, Settings.RegisterBegin, new { password = admin.Password });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        await Host.SetLockoutAsync(admin, true);
        using var locked = await PostAsync(client, Settings.RegisterBegin, new { password = admin.Password });
        Assert.Equal(HttpStatusCode.Locked, locked.StatusCode);
    }

    [Fact]
    public async Task P4_削除は再認証が無いと401で行は残り_パスワードでもパスキーでも消せる()
    {
        var admin = await Host.SeedAdminAsync(new());
        var first = new VirtualPasskeyAuthenticator();
        var second = new VirtualPasskeyAuthenticator();
        using var client = await SessionAsync(admin);
        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(client, admin, first)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(client, admin, second)).StatusCode);
        var passkeys = await ListAsync(client);
        Assert.Equal(2, passkeys.Count);

        using var none = await PostAsync(client, Settings.Remove(passkeys[0].Id), new { });
        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
        Assert.Equal(2, (await ListAsync(client)).Count);

        using var byPassword = await PostAsync(client, Settings.Remove(passkeys[0].Id), new { password = admin.Password });
        Assert.Equal(HttpStatusCode.OK, byPassword.StatusCode);

        // パスキーによる再認証（ログインのオプションへ応答した、本人の資格情報）
        var proof = await AssertionAsync(client, second);
        using var byPasskey = await PostAsync(client, Settings.Remove(passkeys[1].Id), new { assertion = JsonNode.Parse(proof) });
        Assert.Equal(HttpStatusCode.OK, byPasskey.StatusCode);
        Assert.Empty(await ListAsync(client));
    }

    [Fact]
    public async Task P4_他人のパスキーでは再認証にならない()
    {
        var admin = await Host.SeedAdminAsync(new());
        var stranger = await Host.SeedAdminAsync(new());
        var strangersKey = new VirtualPasskeyAuthenticator();
        using (var strangerClient = await SessionAsync(stranger))
        {
            Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(strangerClient, stranger, strangersKey)).StatusCode);
        }

        using var client = await SessionAsync(admin);
        var proof = await AssertionAsync(client, strangersKey);
        using var response = await PostAsync(client, Settings.RegisterBegin, new { assertion = JsonNode.Parse(proof) });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- P5: パスキーだけの人を作らない ----

    [Fact]
    public async Task P5_使えるログイン方法が無くなる削除は409で断る()
    {
        var admin = await Host.SeedAdminAsync(new());
        var authenticator = new VirtualPasskeyAuthenticator();
        using var client = await SessionAsync(admin);
        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(client, admin, authenticator)).StatusCode);
        var id = (await ListAsync(client)).Single().Id;

        await Host.SetPasswordUsableAsync(admin, false);
        var proof = await AssertionAsync(client, authenticator);
        using var refused = await PostAsync(client, Settings.Remove(id), new { assertion = JsonNode.Parse(proof) });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Single(await ListAsync(client));

        await Host.SetPasswordUsableAsync(admin, true);
        using var allowed = await PostAsync(client, Settings.Remove(id), new { password = admin.Password });
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    // ---- P6: チャレンジは一度限り・短い期限 ----

    [Fact]
    public async Task P6_同じ応答は二度通らない()
    {
        var admin = await Host.SeedAdminAsync(new());
        var authenticator = new VirtualPasskeyAuthenticator();
        using (var client = await SessionAsync(admin))
        {
            Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(client, admin, authenticator)).StatusCode);
        }

        using var anonymous = Host.CreateClient();
        var options = await LoginOptionsAsync(anonymous);
        var assertion = authenticator.Get(options, Host.Origin);
        using var first = await PostAsync(anonymous, Settings.LoginComplete, new { credential = JsonNode.Parse(assertion) });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var replay = await PostAsync(Host.CreateClient(), Settings.LoginComplete, new { credential = JsonNode.Parse(assertion) });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    [Fact]
    public async Task P6_期限切れのチャレンジは通らず_登録のチャレンジも一度限り()
    {
        var admin = await Host.SeedAdminAsync(new());
        var authenticator = new VirtualPasskeyAuthenticator();
        using var client = await SessionAsync(admin);
        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(client, admin, authenticator)).StatusCode);

        using var anonymous = Host.CreateClient();
        var options = await LoginOptionsAsync(anonymous);
        await Host.AdvanceTimeAsync(TimeSpan.FromMinutes(6));
        using var expired = await PostAsync(anonymous, Settings.LoginComplete,
            new { credential = JsonNode.Parse(authenticator.Get(options, Host.Origin)) });
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);

        var second = new VirtualPasskeyAuthenticator();
        using var begin = await PostAsync(client, Settings.RegisterBegin, new { password = admin.Password });
        var registerOptions = await begin.Content.ReadAsStringAsync();
        var attestation = second.Create(registerOptions, Host.Origin);
        using var once = await PostAsync(client, Settings.RegisterComplete, new { credential = JsonNode.Parse(attestation), name = "2 本目" });
        Assert.Equal(HttpStatusCode.OK, once.StatusCode);
        using var twice = await PostAsync(client, Settings.RegisterComplete, new { credential = JsonNode.Parse(attestation), name = "2 本目" });
        Assert.Equal(HttpStatusCode.BadRequest, twice.StatusCode);
    }

    [Fact]
    public async Task P6_失敗の応答は理由を区別しない()
    {
        var admin = await Host.SeedAdminAsync(new());
        var authenticator = new VirtualPasskeyAuthenticator();
        using (var client = await SessionAsync(admin))
        {
            Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(client, admin, authenticator)).StatusCode);
        }

        var stranger = new VirtualPasskeyAuthenticator();
        var attempts = new List<(HttpStatusCode Status, string Body, bool Cookie)>();
        foreach (var (key, tamper) in new (VirtualPasskeyAuthenticator, VirtualPasskeyAuthenticator.Tamper?)[]
        {
            (authenticator, new() { BreakSignature = true }),
            (authenticator, new() { UserVerified = false }),
            (authenticator, new() { Origin = "https://evil.example" }),
            (authenticator, new() { UserHandle = Guid.NewGuid().ToByteArray() }),
        })
        {
            await Host.AdvanceTimeAsync(TimeSpan.FromMinutes(6));
            using var anonymous = Host.CreateClient();
            using var response = await PasskeyLoginAsync(anonymous, key, tamper);
            attempts.Add((response.StatusCode, await response.Content.ReadAsStringAsync(), response.Headers.Contains("Set-Cookie")));
        }

        // 未登録の資格情報（サーバへ登録していない認証器）
        stranger.Create(JsonSerializer.Serialize(new
        {
            challenge = "AAAAAAAAAAAAAAAAAAAAAA",
            rp = new { id = new Uri(Host.Origin).Host },
            user = new { id = "AAAAAAAAAAAAAAAAAAAAAA" },
        }), Host.Origin);
        await Host.AdvanceTimeAsync(TimeSpan.FromMinutes(6));
        using (var anonymous = Host.CreateClient())
        {
            using var response = await PasskeyLoginAsync(anonymous, stranger);
            attempts.Add((response.StatusCode, await response.Content.ReadAsStringAsync(), response.Headers.Contains("Set-Cookie")));
        }

        Assert.All(attempts, a => Assert.Equal(HttpStatusCode.Unauthorized, a.Status));
        Assert.Single(attempts.Select(a => a.Body).Distinct());
        Assert.All(attempts, a => Assert.False(a.Cookie));
    }

    // ---- P7: 署名カウンター ----

    [Fact]
    public async Task P7_カウンターの逆行は拒否し警告の監査を残す_カウンターの無い認証器は通す()
    {
        var admin = await Host.SeedAdminAsync(new());
        var authenticator = new VirtualPasskeyAuthenticator { SignCount = 4 };
        using (var client = await SessionAsync(admin))
        {
            Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(client, admin, authenticator)).StatusCode);
        }

        using (var anonymous = Host.CreateClient())
        {
            using var response = await PasskeyLoginAsync(anonymous, authenticator);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using (var anonymous = Host.CreateClient())
        {
            using var regressed = await PasskeyLoginAsync(anonymous, authenticator, new() { SignCount = 3 });
            Assert.Equal(HttpStatusCode.Unauthorized, regressed.StatusCode);
            using var same = await PasskeyLoginAsync(Host.CreateClient(), authenticator, new() { SignCount = 5 });
            Assert.Equal(HttpStatusCode.Unauthorized, same.StatusCode);
        }

        var audit = await Host.AuditAsync();
        Assert.Contains(audit, a => a is { Action: "Passkey.SignIn.Failed", Succeeded: false }
            && a.DetailJson!.Contains("counter-regression", StringComparison.Ordinal));

        // 拒否したあとも、正しい次の値なら通る（保存値は進んでいない）
        using (var anonymous = Host.CreateClient())
        {
            using var next = await PasskeyLoginAsync(anonymous, authenticator, new() { SignCount = 9 });
            Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        }
    }

    [Fact]
    public async Task P7_カウンターを持たない認証器は0のままでも通る()
    {
        var admin = await Host.SeedAdminAsync(new());
        var authenticator = new VirtualPasskeyAuthenticator { UsesCounter = false, BackupEligible = true, BackedUp = true };
        using (var client = await SessionAsync(admin))
        {
            Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(client, admin, authenticator)).StatusCode);
        }

        for (var i = 0; i < 2; i++)
        {
            using var anonymous = Host.CreateClient();
            using var response = await PasskeyLoginAsync(anonymous, authenticator);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    // ---- P8: CredentialId は全体で一意 ----

    [Fact]
    public async Task P8_登録済みの資格情報は別の人にも結び付けず409()
    {
        var first = await Host.SeedAdminAsync(new());
        var second = await Host.SeedAdminAsync(new());
        var authenticator = new VirtualPasskeyAuthenticator();
        using var firstClient = await SessionAsync(first);
        using var secondClient = await SessionAsync(second);
        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(firstClient, first, authenticator)).StatusCode);

        using var response = await RegisterAsync(secondClient, second, authenticator);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(await ListAsync(secondClient));
    }

    [Fact]
    public async Task P8_1人あたりの上限を超える登録は409()
    {
        var admin = await Host.SeedAdminAsync(new());
        using var client = await SessionAsync(admin);
        // 上限の既定は 10。上限を変えたホストでは、この数を設定に合わせる
        for (var i = 0; i < 10; i++)
        {
            // 登録の開始もレート制限の枠を使う。窓を区切って、枠で止まらないようにする
            await Host.AdvanceTimeAsync(TimeSpan.FromMinutes(6));
            Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(client, admin, new VirtualPasskeyAuthenticator())).StatusCode);
        }

        await Host.AdvanceTimeAsync(TimeSpan.FromMinutes(6));
        using var over = await PostAsync(client, Settings.RegisterBegin, new { password = admin.Password });
        Assert.Equal(HttpStatusCode.Conflict, over.StatusCode);
    }

    // ---- P9: 復旧削除と無効・締め出し ----

    [Fact]
    public async Task P9_復旧削除は対象者の全セッションを失効させ_他人のセッションは残す()
    {
        var actor = await Host.SeedAdminAsync(new(Permissions: new HashSet<string> { Settings.ResetPermission }));
        var target = await Host.SeedAdminAsync(new());
        var bystander = await Host.SeedAdminAsync(new());
        var authenticator = new VirtualPasskeyAuthenticator();
        using var targetClient = await SessionAsync(target);
        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(targetClient, target, authenticator)).StatusCode);
        using var otherDevice = Host.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await PasskeyLoginAsync(otherDevice, authenticator)).StatusCode);
        using var bystanderClient = await SessionAsync(bystander);
        using var actorClient = await SessionAsync(actor);

        using var reset = await PostAsync(actorClient, Settings.Reset(await Host.UserIdAsync(target)), new { });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        using var body = JsonDocument.Parse(await reset.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("reset").GetBoolean());
        Assert.Equal(1, body.RootElement.GetProperty("removed").GetInt32());

        Assert.Equal(HttpStatusCode.Unauthorized, (await targetClient.GetAsync(Settings.List)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await otherDevice.GetAsync(Settings.List)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bystanderClient.GetAsync(Settings.List)).StatusCode);
        using var again = Host.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await PasskeyLoginAsync(again, authenticator)).StatusCode);

        // 本人はパスワードで入り直し、新しいパスキーを登録できる
        using var recovered = await SessionAsync(target);
        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(recovered, target, new VirtualPasskeyAuthenticator())).StatusCode);

        var audit = await Host.AuditAsync();
        Assert.Contains(audit, a => a.Action == "Passkey.Removed" && a.DetailJson!.Contains("\"by\":\"admin\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task P9_復旧削除は権限が無いと403_自分は400()
    {
        var weak = await Host.SeedAdminAsync(new(Permissions: new HashSet<string> { "users.read" }));
        var strong = await Host.SeedAdminAsync(new(Permissions: new HashSet<string> { Settings.ResetPermission }));
        var target = await Host.SeedAdminAsync(new());
        using var weakClient = await SessionAsync(weak);
        using var strongClient = await SessionAsync(strong);

        using var forbidden = await PostAsync(weakClient, Settings.Reset(await Host.UserIdAsync(target)), new { });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var self = await PostAsync(strongClient, Settings.Reset(await Host.UserIdAsync(strong)), new { });
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
        using var unknown = await PostAsync(strongClient, Settings.Reset(Guid.NewGuid()), new { });
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task P9_無効な持ち主と締め出し中の持ち主ではSessionを張らない()
    {
        var admin = await Host.SeedAdminAsync(new());
        var authenticator = new VirtualPasskeyAuthenticator();
        using (var client = await SessionAsync(admin))
        {
            Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(client, admin, authenticator)).StatusCode);
        }

        await Host.SetDisabledAsync(admin, true);
        using (var anonymous = Host.CreateClient())
        {
            using var response = await PasskeyLoginAsync(anonymous, authenticator);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Settings.List)).StatusCode);
        }

        await Host.SetDisabledAsync(admin, false);
        await Host.SetLockoutAsync(admin, true);
        using (var anonymous = Host.CreateClient())
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await PasskeyLoginAsync(anonymous, authenticator)).StatusCode);
        }

        await Host.SetLockoutAsync(admin, false);
        using (var anonymous = Host.CreateClient())
        {
            Assert.Equal(HttpStatusCode.OK, (await PasskeyLoginAsync(anonymous, authenticator)).StatusCode);
        }
    }

    // ---- P10: 無効の間は 404・行は消さない ----

    [Fact]
    public async Task P10_無効の間はすべての口が404で_再び有効にすれば登録済みが使える()
    {
        var admin = await Host.SeedAdminAsync(new());
        var authenticator = new VirtualPasskeyAuthenticator();
        using var client = await SessionAsync(admin);
        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(client, admin, authenticator)).StatusCode);
        var id = (await ListAsync(client)).Single().Id;

        await Host.SetPasskeyEnabledAsync(false);
        using var anonymous = Host.CreateClient();
        var responses = new[]
        {
            await client.GetAsync(Settings.List),
            await PostAsync(client, Settings.RegisterBegin, new { password = admin.Password }),
            await PostAsync(client, Settings.RegisterComplete, new { credential = new { } }),
            await PutAsync(client, Settings.Rename(id), new { name = "x" }),
            await PostAsync(client, Settings.Remove(id), new { password = admin.Password }),
            await PostAsync(anonymous, Settings.LoginBegin, new { }),
            await PostAsync(anonymous, Settings.LoginComplete, new { credential = new { } }),
            await PostAsync(client, Settings.Reset(Guid.NewGuid()), new { }),
        };
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        foreach (var response in responses)
        {
            response.Dispose();
        }

        await Host.SetPasskeyEnabledAsync(true);
        Assert.Single(await ListAsync(client));
        using var again = Host.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await PasskeyLoginAsync(again, authenticator)).StatusCode);
    }

    // ---- レート制限の共有・監査 ----

    [Fact]
    public async Task レート制限はパスワードのログインとパスキーのログインで枠を共有する()
    {
        var limit = Host.LoginAttemptsPer5Minutes;
        // パスワードの失敗でパスキーの口が塞がる
        using (var client = Host.CreateClient())
        {
            for (var i = 0; i < limit; i++)
            {
                using var failed = await Host.PasswordLoginAsync(client, "nobody", "wrong");
                Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
            }

            using var blocked = await PostAsync(client, Settings.LoginBegin, new { });
            Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        }

        // 窓が変わるまで待ち、今度はパスキーの失敗でパスワードの口が塞がる
        await Host.AdvanceTimeAsync(TimeSpan.FromMinutes(6));
        using (var client = Host.CreateClient())
        {
            var used = 0;
            for (; used < limit; used++)
            {
                using var begin = await PostAsync(client, Settings.LoginBegin, new { });
                Assert.Equal(HttpStatusCode.OK, begin.StatusCode);
            }

            using var blocked = await Host.PasswordLoginAsync(client, "nobody", "wrong");
            Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        }
    }

    [Fact]
    public async Task 監査は5つのイベントを残し_公開鍵とチャレンジと署名は残さない()
    {
        var admin = await Host.SeedAdminAsync(new());
        var authenticator = new VirtualPasskeyAuthenticator();
        using var client = await SessionAsync(admin);
        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(client, admin, authenticator)).StatusCode);
        var id = (await ListAsync(client)).Single().Id;
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(client, Settings.Rename(id), new { name = "改名" })).StatusCode);
        using (var anonymous = Host.CreateClient())
        {
            Assert.Equal(HttpStatusCode.OK, (await PasskeyLoginAsync(anonymous, authenticator)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await PasskeyLoginAsync(anonymous, authenticator, new() { BreakSignature = true })).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, Settings.Remove(id), new { password = admin.Password })).StatusCode);

        var audit = await Host.AuditAsync();
        var actions = audit.Select(a => a.Action).ToHashSet();
        foreach (var expected in new[]
        {
            "Passkey.Registered", "Passkey.Removed", "Passkey.Renamed", "Passkey.SignIn.Succeeded", "Passkey.SignIn.Failed",
        })
        {
            Assert.Contains(expected, actions);
        }

        var all = string.Join('\n', audit.Select(a => a.DetailJson));
        Assert.DoesNotContain("publicKey", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"challenge\":", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"signature\":", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(admin.Password, all, StringComparison.Ordinal);
        Assert.Contains(audit, a => a.Action == "Passkey.SignIn.Failed" && !a.Succeeded);
    }

    // ---- 道具 ----

    private sealed record PasskeyRow(Guid Id, string Name);

    private async Task<HttpClient> SessionAsync(SeededAdmin admin)
    {
        var client = Host.CreateClient();
        using var response = await Host.PasswordLoginAsync(client, admin.LoginId, admin.Password);
        Assert.True(response.IsSuccessStatusCode, "準備のパスワードのログインに失敗しました。");
        return client;
    }

    private async Task<List<PasskeyRow>> ListAsync(HttpClient client)
    {
        using var response = await client.GetAsync(Settings.List);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return [.. body.RootElement.GetProperty("passkeys").EnumerateArray()
            .Select(p => new PasskeyRow(p.GetProperty("id").GetGuid(), p.GetProperty("name").GetString()!))];
    }

    private async Task<HttpResponseMessage> RegisterAsync(
        HttpClient client, SeededAdmin admin, VirtualPasskeyAuthenticator authenticator,
        VirtualPasskeyAuthenticator.Tamper? tamper = null)
    {
        using var begin = await PostAsync(client, Settings.RegisterBegin, new { password = admin.Password });
        if (begin.StatusCode != HttpStatusCode.OK)
        {
            return new HttpResponseMessage(begin.StatusCode);
        }

        var attestation = authenticator.Create(await begin.Content.ReadAsStringAsync(), Host.Origin, tamper);
        return await PostAsync(client, Settings.RegisterComplete, new { credential = JsonNode.Parse(attestation), name = "仕事用 PC" });
    }

    private async Task<string> LoginOptionsAsync(HttpClient client)
    {
        using var begin = await PostAsync(client, Settings.LoginBegin, new { });
        Assert.Equal(HttpStatusCode.OK, begin.StatusCode);
        return await begin.Content.ReadAsStringAsync();
    }

    private async Task<string> AssertionAsync(HttpClient client, VirtualPasskeyAuthenticator authenticator) =>
        authenticator.Get(await LoginOptionsAsync(client), Host.Origin);

    private async Task<HttpResponseMessage> PasskeyLoginAsync(
        HttpClient client, VirtualPasskeyAuthenticator authenticator, VirtualPasskeyAuthenticator.Tamper? tamper = null)
    {
        var options = await LoginOptionsAsync(client);
        var assertion = authenticator.Get(options, Host.Origin, tamper);
        return await PostAsync(client, Settings.LoginComplete, new { credential = JsonNode.Parse(assertion) });
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body) =>
        client.PostAsync(path, JsonContent.Create(body));

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, string path, object body) =>
        client.PutAsync(path, JsonContent.Create(body));
}
