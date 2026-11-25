using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using VehicleVision.PleasanterTools.Core.Data;
using VehicleVision.PleasanterTools.Core.Web.Testing;

namespace VehicleVision.PleasanterTools.Core.Web.Tests;

/// <summary>
/// パスキー契約を流すための、参照ホスト。<b>アプリが書くものと同じ形</b>（Core.Data のストアを包む受け口と、
/// <see cref="IAdminPasskeyHost"/> の実装、パスワードのログイン口）を、Kestrel の実サーバで動かす。
/// アプリは、これを手本に受け口を実装する（認証とセッションの仕様書 3.10.12）。
/// </summary>
internal sealed class PasskeyReferenceHost : IAdminPasskeyContractHost
{
    private const string Prefix = "/api/admin";
    private const string RatePolicy = "admin-login";
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);
    private static readonly PasswordHasher Hasher = new();

    private readonly string sqlitePath = Path.Combine(Path.GetTempPath(), $"passkey-ref-{Guid.NewGuid():N}.db");
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 25, 3, 0, 0, TimeSpan.Zero));
    private readonly HashSet<Guid> passwordUnusable = [];
    private readonly Dictionary<Guid, IReadOnlySet<string>> permissions = [];
    private readonly Dictionary<string, Guid> ids = new(StringComparer.OrdinalIgnoreCase);
    private WebApplication? app;
    private AdminUserStore users = null!;
    private AdminSessionStore sessions = null!;
    private AuditLogStore auditLogs = null!;
    private AdminPasskeyOptions options = AdminPasskeyOptions.Disabled;
    private int port;

    private PasskeyReferenceHost(int loginAttempts)
    {
        LoginAttemptsPer5Minutes = loginAttempts;
    }

    public AdminPasskeyContractSettings Settings { get; } = new();

    public string Origin => $"http://localhost:{port}";

    public int LoginAttemptsPer5Minutes { get; }

    /// <summary>試験が直接触るための、現在の設定の差し替え（1 人あたりの上限など）。</summary>
    public void Configure(Func<AdminPasskeyOptions, AdminPasskeyOptions> change) => options = change(options);

    public static async Task<PasskeyReferenceHost> StartAsync(int loginAttempts = 10, Func<AdminPasskeyOptions, AdminPasskeyOptions>? configure = null)
    {
        var host = new PasskeyReferenceHost(loginAttempts);
        await host.InitializeAsync(configure);
        return host;
    }

    private async Task InitializeAsync(Func<AdminPasskeyOptions, AdminPasskeyOptions>? configure)
    {
        var connectionString = $"Data Source={sqlitePath};Pooling=False";
        new DatabaseMigrator(typeof(PasskeyReferenceHost).Assembly, "passkey-reference-host").MigrateUp(DatabaseProvider.Sqlite, connectionString);
        var factory = new DbConnectionFactory(DatabaseProvider.Sqlite, connectionString);
        users = new AdminUserStore(factory, new PlainCipher(), time, "reference");
        sessions = new AdminSessionStore(factory, time);
        auditLogs = new AuditLogStore(factory);
        var passkeys = new AdminPasskeyStore(factory, time);
        var challenges = new AdminPasskeyChallengeStore(factory, time);

        options = new AdminPasskeyOptions { Enabled = true, RpName = "Reference" };
        if (configure is not null)
        {
            options = configure(options);
        }

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<IPasskeyCredentialStore>(new CredentialAdapter(passkeys));
        builder.Services.AddSingleton<IPasskeyChallengeStore>(new ChallengeAdapter(challenges));
        builder.Services.AddSingleton<IPasskeyAccounts>(new AccountAdapter(this));
        builder.Services.AddSingleton<IPasskeyAuditSink>(new AuditAdapter(auditLogs));
        builder.Services.AddSingleton<IAdminPasskeyHost>(new HostAdapter(this));
        // 設定を試験の途中で切り替えるため、要求ごとに現在の設定で作る（アプリは起動時の設定を 1 度だけ登録する）
        builder.Services.AddScoped(sp => new AdminPasskeyService(
            sp.GetRequiredService<IPasskeyCredentialStore>(), sp.GetRequiredService<IPasskeyChallengeStore>(),
            sp.GetRequiredService<IPasskeyAccounts>(), sp.GetRequiredService<IPasskeyAuditSink>(), options, time));
        builder.Services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // パスワードとパスキーのログインが、同じ方針名（同じ IP の同じ枠）を使う。窓は試験の時計で区切る
            limiter.AddPolicy(RatePolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                $"{context.Connection.RemoteIpAddress}|{time.GetUtcNow().Ticks / TimeSpan.FromMinutes(5).Ticks}",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = LoginAttemptsPer5Minutes,
                    Window = TimeSpan.FromMinutes(5),
                    QueueLimit = 0,
                    AutoReplenishment = false,
                }));
        });

        var built = builder.Build();
        built.UseRateLimiter();
        built.MapPost($"{Prefix}/login", PasswordLoginEndpointAsync).RequireRateLimiting(RatePolicy);
        built.MapAdminPasskeyEndpoints(Prefix, RatePolicy);
        await built.StartAsync();
        app = built;
        port = new Uri(built.Urls.First()).Port;
    }

    // ---- パスワードのログイン口（アプリが持つもの。ここでは最小限） ----

    private async Task<IResult> PasswordLoginEndpointAsync(HttpContext http, JsonElement body)
    {
        var loginId = body.TryGetProperty("loginId", out var l) ? l.GetString() ?? "" : "";
        var password = body.TryGetProperty("password", out var p) ? p.GetString() ?? "" : "";
        var found = await users.FindForLoginAsync(loginId);
        if (found is not var (user, hash))
        {
            return Results.Json(new { code = "auth.invalidCredentials" }, statusCode: 401);
        }

        if (user.IsLocked(time.GetUtcNow()))
        {
            return Results.Json(new { code = "auth.loginTemporarilyLocked" }, statusCode: 423);
        }

        if (!Hasher.Verify(password, hash).Verified || user.IsDisabled)
        {
            await users.RecordFailureAsync(user.Id, 5, TimeSpan.FromMinutes(15));
            return Results.Json(new { code = "auth.invalidCredentials" }, statusCode: 401);
        }

        await users.RecordSuccessAsync(user.Id);
        var pending = user.TotpEnabled;
        var id = await sessions.CreateAsync(user.Id, pending ? AdminSessionKinds.Pending : AdminSessionKinds.Session,
            pending ? TimeSpan.FromMinutes(5) : SessionLifetime);
        http.Response.Cookies.Append(pending ? "pending" : "session", id, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict });
        return Results.Json(new { next = pending ? "totp" : "done" });
    }

    // ---- 契約ホストの口 ----

    public HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
        };
        // ブラウザが送る Host（RP ID を導く元）は localhost
        client.DefaultRequestHeaders.Host = $"localhost:{port}";
        return client;
    }

    public async Task<SeededAdmin> SeedAdminAsync(PasskeySeed seed)
    {
        var loginId = $"user-{Guid.NewGuid():N}";
        var password = $"Pw-{Guid.NewGuid():N}";
        var created = await users.CreateAsync(loginId, Hasher.Hash(password, 1000), "Editor", "ja")
            ?? throw new InvalidOperationException("利用者を作れませんでした。");
        ids[loginId] = created.Id;
        if (seed.Disabled)
        {
            await users.SetDisabledAsync(created.Id, true);
        }

        if (seed.Permissions is not null)
        {
            permissions[created.Id] = seed.Permissions;
        }

        if (!seed.PasswordUsable)
        {
            passwordUnusable.Add(created.Id);
        }

        return new SeededAdmin(loginId, password);
    }

    public Task<Guid> UserIdAsync(SeededAdmin admin) => Task.FromResult(ids[admin.LoginId]);

    public Task<HttpResponseMessage> PasswordLoginAsync(HttpClient client, string loginId, string password) =>
        client.PostAsJsonAsync($"{Prefix}/login", new { loginId, password });

    public Task SetPasskeyEnabledAsync(bool enabled)
    {
        options = new AdminPasskeyOptions
        {
            Enabled = enabled, RpName = options.RpName, MaxPasskeysPerUser = options.MaxPasskeysPerUser,
        };
        return Task.CompletedTask;
    }

    public Task AdvanceTimeAsync(TimeSpan span)
    {
        time.Advance(span);
        return Task.CompletedTask;
    }

    public async Task SetLockoutAsync(SeededAdmin admin, bool locked)
    {
        var id = ids[admin.LoginId];
        if (locked)
        {
            await users.RecordFailureAsync(id, 1, TimeSpan.FromMinutes(15));
        }
        else
        {
            await users.RecordSuccessAsync(id);
        }
    }

    public async Task SetDisabledAsync(SeededAdmin admin, bool disabled) =>
        await users.SetDisabledAsync(ids[admin.LoginId], disabled);

    public Task SetPasswordUsableAsync(SeededAdmin admin, bool usable)
    {
        _ = usable ? passwordUnusable.Remove(ids[admin.LoginId]) : passwordUnusable.Add(ids[admin.LoginId]);
        return Task.CompletedTask;
    }

    public async Task EnableTotpAsync(SeededAdmin admin) =>
        await users.EnableTotpAsync(ids[admin.LoginId], "JBSWY3DPEHPK3PXP", 1, []);

    public async Task<IReadOnlyList<PasskeyAuditRecord>> AuditAsync() =>
        [.. (await auditLogs.ListAsync(new AuditLogQuery { Limit = AuditLogQuery.MaxLimit }))
            .Where(e => e.Action.StartsWith("Passkey.", StringComparison.Ordinal))
            .Reverse()
            .Select(e => new PasskeyAuditRecord(e.Action, e.Succeeded, e.TargetId, e.DetailJson))];

    public async Task<bool?> HasSessionViaAsync(SeededAdmin admin, string via) =>
        (await sessions.ListForUserAsync(ids[admin.LoginId])).Any(s => s.Via == via);

    public async ValueTask DisposeAsync()
    {
        if (app is not null)
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(sqlitePath);
    }

    // ---- アプリが書く受け口（Core.Data のストアを包む） ----

    private sealed class PlainCipher : ISecretCipher
    {
        public string Protect(string plaintext, string context) => plaintext;

        public string Unprotect(string protectedValue, string context) => protectedValue;
    }

    private sealed class CredentialAdapter(AdminPasskeyStore store) : IPasskeyCredentialStore
    {
        public async Task<IReadOnlyList<PasskeyCredential>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
            [.. (await store.ListForUserAsync(userId, cancellationToken)).Select(Map)];

        public Task<int> CountForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
            store.CountForUserAsync(userId, cancellationToken);

        public async Task<PasskeyCredential?> FindByCredentialIdAsync(byte[] credentialId, CancellationToken cancellationToken = default) =>
            await store.FindByCredentialIdAsync(credentialId, cancellationToken) is { } found ? Map(found) : null;

        public Task<Guid?> AddAsync(NewPasskeyCredential p, CancellationToken cancellationToken = default) =>
            store.AddAsync(new NewAdminPasskey(p.UserId, p.CredentialId, p.PublicKey, p.SignCount, p.AaGuid, p.Transports,
                p.BackupEligible, p.BackedUp, p.Name), cancellationToken);

        public Task<bool> RenameAsync(Guid userId, Guid id, string name, CancellationToken cancellationToken = default) =>
            store.RenameAsync(userId, id, name, cancellationToken);

        public Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken cancellationToken = default) =>
            store.DeleteAsync(userId, id, cancellationToken);

        public Task<int> DeleteForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
            store.DeleteForUserAsync(userId, cancellationToken);

        public Task<bool> RecordSignInAsync(Guid id, long previous, long next, bool backedUp, CancellationToken cancellationToken = default) =>
            store.RecordSignInAsync(id, previous, next, backedUp, cancellationToken);

        private static PasskeyCredential Map(AdminPasskey p) => new(p.Id, p.UserId, p.CredentialId, p.PublicKey, p.SignCount, p.AaGuid,
            p.Transports, p.BackupEligible, p.BackedUp, p.Name, p.CreatedAt, p.LastUsedAt);
    }

    private sealed class ChallengeAdapter(AdminPasskeyChallengeStore store) : IPasskeyChallengeStore
    {
        public Task StoreAsync(byte[] challenge, string purpose, Guid? userId, TimeSpan lifetime, CancellationToken cancellationToken = default) =>
            store.StoreAsync(challenge, purpose, userId, lifetime, cancellationToken);

        public Task<bool> ConsumeAsync(byte[] challenge, string purpose, Guid? userId, CancellationToken cancellationToken = default) =>
            store.ConsumeAsync(challenge, purpose, userId, cancellationToken);
    }

    private sealed class AccountAdapter(PasskeyReferenceHost owner) : IPasskeyAccounts
    {
        public async Task<PasskeyAccount?> FindAsync(Guid userId, CancellationToken cancellationToken = default) =>
            await owner.users.FindByIdAsync(userId, cancellationToken) is { } u
                ? new PasskeyAccount(u.Id, u.LoginId, u.IsDisabled, u.IsLocked(owner.time.GetUtcNow()))
                : null;

        public async Task<PasswordConfirmation> ConfirmPasswordAsync(Guid userId, string password, CancellationToken cancellationToken = default)
        {
            var user = await owner.users.FindByIdAsync(userId, cancellationToken);
            if (user is null)
            {
                return PasswordConfirmation.Rejected;
            }

            if (user.IsLocked(owner.time.GetUtcNow()))
            {
                return PasswordConfirmation.Locked;
            }

            var found = await owner.users.FindForLoginAsync(user.LoginId, cancellationToken);
            if (found is { } f && Hasher.Verify(password, f.PasswordHash).Verified)
            {
                await owner.users.RecordSuccessAsync(userId, cancellationToken);
                return PasswordConfirmation.Confirmed;
            }

            await owner.users.RecordFailureAsync(userId, 5, TimeSpan.FromMinutes(15), cancellationToken);
            return PasswordConfirmation.Rejected;
        }

        public Task RecordSignInSucceededAsync(Guid userId, CancellationToken cancellationToken = default) =>
            owner.users.RecordSuccessAsync(userId, cancellationToken);

        public Task RevokeSessionsAsync(Guid userId, CancellationToken cancellationToken = default) =>
            owner.sessions.DeleteForUserAsync(userId, null, cancellationToken);

        public Task<PasskeySignInMethods> GetSignInMethodsAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PasskeySignInMethods(!owner.passwordUnusable.Contains(userId), false, false));
    }

    private sealed class AuditAdapter(AuditLogStore store) : IPasskeyAuditSink
    {
        public Task WriteAsync(PasskeyAuditEntry e, CancellationToken cancellationToken = default) =>
            store.WriteAsync(new AuditEntry(e.OccurredAt, e.Action, e.Succeeded, e.StatusCode, e.ActorKey, e.ActorLoginId, "AdminUser",
                e.TargetId, e.DetailJson, e.IpAddress), cancellationToken);
    }

    private sealed class HostAdapter(PasskeyReferenceHost owner) : IAdminPasskeyHost
    {
        public async ValueTask<AdminPasskeyCaller?> GetCallerAsync(HttpContext context)
        {
            if (!context.Request.Cookies.TryGetValue("session", out var sessionId))
            {
                return null;
            }

            var session = await owner.sessions.FindAsync(sessionId);
            if (session is not { Kind: AdminSessionKinds.Session })
            {
                return null;
            }

            var user = await owner.users.FindByIdAsync(session.UserId);
            if (user is null or { IsDisabled: true })
            {
                return null;
            }

            return new AdminPasskeyCaller(user.Id, user.LoginId,
                owner.permissions.TryGetValue(user.Id, out var granted) ? granted : new HashSet<string>());
        }

        public async Task SignInAsync(HttpContext context, PasskeySignedIn user, string via)
        {
            // 張る前に、途中（Pending）の cookie を消す（3.1）
            context.Response.Cookies.Delete("pending");
            var id = await owner.sessions.CreateWithViaAsync(user.UserId, AdminSessionKinds.Session, SessionLifetime, via);
            context.Response.Cookies.Append("session", id, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict });
        }
    }
}
