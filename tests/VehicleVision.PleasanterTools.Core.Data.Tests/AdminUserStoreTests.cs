namespace VehicleVision.PleasanterTools.Core.Data.Tests;

/// <summary>試験用の暗号。文脈に束縛される（別の文脈では復号できない）ことだけを再現する。</summary>
internal sealed class FakeCipher : ISecretCipher
{
    public string Protect(string plaintext, string context) => $"enc[{context}]{plaintext}";

    public string Unprotect(string protectedValue, string context)
    {
        var prefix = $"enc[{context}]";
        return protectedValue.StartsWith(prefix, StringComparison.Ordinal)
            ? protectedValue[prefix.Length..]
            : throw new InvalidOperationException("文脈が違う");
    }
}

/// <remarks>
/// 管理者の表は共有 DB の中で 1 つ。<b>最初の管理者の排他（<c>Core_AdminSetup</c>）が、1 つの DB に 1 回だけ</b>効くので、
/// それを確かめるテストは、毎回、表を空にしてから始める。
/// </remarks>
[Collection(TestDatabases.CollectionName)]
public sealed class AdminUserStoreTests : DatabaseTestBase
{
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 3, 0, 0, TimeSpan.Zero);
    private const string AppId = "sample";

    private (AdminUserStore Store, FixedTimeProvider Time) StoreFor(DatabaseProvider provider)
    {
        var time = new FixedTimeProvider(Start);
        var factory = MigratedFactoryFor(provider);

        using (var connection = factory.Create())
        {
            foreach (var table in new[] { "Core_AdminRecoveryCodes", "Core_AdminSetup", "Core_AdminSessions", "Core_AdminUsers" })
            {
                Dapper.SqlMapper.Execute(connection, SqlDialect.Format(provider, $"DELETE FROM [{table}]"));
            }
        }

        return (new AdminUserStore(factory, new FakeCipher(), time, AppId), time);
    }

    private static string NewLoginId() => $"user-{Guid.NewGuid():N}@example.com";

    [Theory]
    [EnabledDatabases]
    public async Task 最初の管理者は_1_回だけ作れる(DatabaseProvider provider)
    {
        var (store, _) = StoreFor(provider);

        var first = await store.CreateFirstAsync("Admin", "hash", "ja");
        var second = await store.CreateFirstAsync("other", "hash", "ja");

        Assert.NotNull(first);
        Assert.Equal(AdminRoleNames.Administrator, first.Role);
        Assert.Equal("ja", first.Language);
        Assert.Null(second);
        Assert.Equal(1, await store.CountAsync());
    }

    [Theory]
    [EnabledDatabases]
    public async Task 同時に最初の管理者を作ろうとしても_通るのは_1_人だけ(DatabaseProvider provider)
    {
        var (store, _) = StoreFor(provider);

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => store.CreateFirstAsync($"admin{i}", "hash", "ja")));

        Assert.Single(results, r => r is not null);
        Assert.Equal(1, await store.CountAsync());
    }

    [Theory]
    [EnabledDatabases]
    public async Task ログイン_ID_は大文字小文字を区別せず引け_重複は断る(DatabaseProvider provider)
    {
        var (store, _) = StoreFor(provider);
        var created = await store.CreateAsync("Taro@Example.com", "hash", "Editor", "en");

        var found = await store.FindForLoginAsync("  taro@EXAMPLE.com ");
        var duplicate = await store.CreateAsync("TARO@example.com", "hash2", "Editor", "ja");

        Assert.NotNull(created);
        Assert.NotNull(found);
        Assert.Equal(created.Id, found.Value.User.Id);
        Assert.Equal("hash", found.Value.PasswordHash);
        Assert.Equal("Taro@Example.com", found.Value.User.LoginId);
        Assert.Null(duplicate);
        Assert.Null(await store.FindForLoginAsync("nobody"));
    }

    [Theory]
    [EnabledDatabases]
    public async Task 一覧は作成の古い順(DatabaseProvider provider)
    {
        var (store, time) = StoreFor(provider);
        var a = await store.CreateAsync(NewLoginId(), "h", "Editor", "ja");
        time.Now = Start.AddMinutes(1);
        var b = await store.CreateAsync(NewLoginId(), "h", "Editor", "ja");

        var list = await store.ListAsync();

        Assert.Equal([a!.Id, b!.Id], list.Select(u => u.Id));
    }

    [Theory]
    [EnabledDatabases]
    public async Task 最後の有効な_Administrator_は_無効化も降格もできない(DatabaseProvider provider)
    {
        var (store, _) = StoreFor(provider);
        var admin = await store.CreateFirstAsync("admin", "hash", "ja");
        await store.RecordSuccessAsync(admin!.Id);

        Assert.Equal(AdminChangeResult.LastAdministrator, await store.SetDisabledAsync(admin.Id, true));
        Assert.Equal(AdminChangeResult.LastAdministrator, await store.SetRoleAsync(admin.Id, "Editor"));
        Assert.Equal(AdminChangeResult.NotFound, await store.SetDisabledAsync(Guid.NewGuid(), true));
    }

    [Theory]
    [EnabledDatabases]
    public async Task 二人目の_Administrator_が居れば_降格できる(DatabaseProvider provider)
    {
        var (store, _) = StoreFor(provider);
        var first = await store.CreateFirstAsync("admin", "hash", "ja");
        var second = await store.CreateAsync(NewLoginId(), "hash", AdminRoleNames.Administrator, "ja");
        await store.RecordSuccessAsync(first!.Id);
        await store.RecordSuccessAsync(second!.Id);

        Assert.Equal(AdminChangeResult.Updated, await store.SetRoleAsync(first.Id, "Editor"));
        Assert.Equal("Editor", (await store.FindByIdAsync(first.Id))!.Role);
        // 残った 1 人は守られる
        Assert.Equal(AdminChangeResult.LastAdministrator, await store.SetRoleAsync(second.Id, "Editor"));
    }

    [Theory]
    [EnabledDatabases]
    public async Task 同時に互いを降格しても_Administrator_は_0_人にならない(DatabaseProvider provider)
    {
        var (store, _) = StoreFor(provider);
        var first = await store.CreateFirstAsync("admin", "hash", "ja");
        var second = await store.CreateAsync(NewLoginId(), "hash", AdminRoleNames.Administrator, "ja");
        await store.RecordSuccessAsync(first!.Id);
        await store.RecordSuccessAsync(second!.Id);

        var results = await Task.WhenAll(
            store.SetRoleAsync(first.Id, "Editor"),
            store.SetRoleAsync(second.Id, "Editor"));

        Assert.Single(results, r => r == AdminChangeResult.Updated);
        var admins = (await store.ListAsync()).Count(u => u.Role == AdminRoleNames.Administrator);
        Assert.Equal(1, admins);
    }

    [Theory]
    [EnabledDatabases]
    public async Task 失敗が閾値に達したら締め出し_成功で戻る(DatabaseProvider provider)
    {
        var (store, time) = StoreFor(provider);
        var user = await store.CreateAsync(NewLoginId(), "hash", "Editor", "ja");

        var first = await store.RecordFailureAsync(user!.Id, 3, TimeSpan.FromMinutes(15));
        var second = await store.RecordFailureAsync(user.Id, 3, TimeSpan.FromMinutes(15));
        var third = await store.RecordFailureAsync(user.Id, 3, TimeSpan.FromMinutes(15));

        Assert.Equal(1, first.FailedCount);
        Assert.Equal(2, second.FailedCount);
        Assert.Equal(0, third.FailedCount);
        Assert.Equal(Start.AddMinutes(15), third.LockedUntil);

        var locked = (await store.FindByIdAsync(user.Id))!;
        Assert.True(locked.IsLocked(Start.AddMinutes(14)));
        Assert.False(locked.IsLocked(Start.AddMinutes(16)));

        time.Now = Start.AddMinutes(20);
        await store.RecordSuccessAsync(user.Id);
        var ok = (await store.FindByIdAsync(user.Id))!;
        Assert.Equal(0, ok.FailedLoginCount);
        Assert.Null(ok.LockedUntil);
        Assert.Equal(Start.AddMinutes(20), ok.LastLoginAt);
    }

    [Theory]
    [EnabledDatabases]
    public async Task パスワードのハッシュを差し替えられる(DatabaseProvider provider)
    {
        var (store, _) = StoreFor(provider);
        var login = NewLoginId();
        var user = await store.CreateAsync(login, "old", "Editor", "ja");

        await store.SetPasswordHashAsync(user!.Id, "new");

        Assert.Equal("new", (await store.FindForLoginAsync(login))!.Value.PasswordHash);
    }

    [Theory]
    [EnabledDatabases]
    public async Task TOTP_の登録は_確定するまで有効にならず_復旧コードは置き換わる(DatabaseProvider provider)
    {
        var (store, _) = StoreFor(provider);
        var user = (await store.CreateAsync(NewLoginId(), "hash", "Editor", "ja"))!;

        await store.SetTotpPendingAsync(user.Id, "PENDING");
        Assert.Equal("PENDING", await store.GetTotpPendingAsync(user.Id));
        Assert.Null(await store.GetTotpSecretAsync(user.Id));
        Assert.False((await store.FindByIdAsync(user.Id))!.TotpEnabled);

        await store.EnableTotpAsync(user.Id, "SECRET", 100, ["h1", "h2", "h3"]);
        Assert.Equal("SECRET", await store.GetTotpSecretAsync(user.Id));
        Assert.Null(await store.GetTotpPendingAsync(user.Id));
        Assert.True((await store.FindByIdAsync(user.Id))!.TotpEnabled);
        Assert.Equal(3, await store.CountRecoveryCodesAsync(user.Id));

        // 登録し直すと、前の復旧コードは消える
        await store.EnableTotpAsync(user.Id, "SECRET2", 200, ["n1"]);
        Assert.Equal(1, await store.CountRecoveryCodesAsync(user.Id));
        Assert.False(await store.TryUseRecoveryCodeAsync(user.Id, "h1"));

        await store.DisableTotpAsync(user.Id);
        Assert.Null(await store.GetTotpSecretAsync(user.Id));
        Assert.Equal(0, await store.CountRecoveryCodesAsync(user.Id));
        Assert.False((await store.FindByIdAsync(user.Id))!.TotpEnabled);
    }

    [Theory]
    [EnabledDatabases]
    public async Task TOTP_の共有鍵は_別の利用者の文脈では復号できない(DatabaseProvider provider)
    {
        var (store, _) = StoreFor(provider);
        var a = (await store.CreateAsync(NewLoginId(), "hash", "Editor", "ja"))!;
        var b = (await store.CreateAsync(NewLoginId(), "hash", "Editor", "ja"))!;
        await store.EnableTotpAsync(a.Id, "SECRET-A", 1, []);

        // 暗号文は利用者 ID とアプリ ID に束縛されている（DB の列を別の行へ写しても読めない）
        var factory = MigratedFactoryFor(provider);
        using var connection = factory.Create();
        var cipherOfA = Dapper.SqlMapper.ExecuteScalar<string>(
            connection, SqlDialect.Format(provider, "SELECT [TotpSecretCipher] FROM [Core_AdminUsers] WHERE [Id] = @Id"), new { Id = a.Id });
        Assert.Contains($"{AppId}:totp:{a.Id:D}", cipherOfA, StringComparison.Ordinal);
        Assert.Null(await store.GetTotpSecretAsync(b.Id));
    }

    [Theory]
    [EnabledDatabases]
    public async Task 同じ時間枠の数字は_2_度通さない(DatabaseProvider provider)
    {
        var (store, _) = StoreFor(provider);
        var user = (await store.CreateAsync(NewLoginId(), "hash", "Editor", "ja"))!;
        await store.EnableTotpAsync(user.Id, "S", 100, []);

        Assert.False(await store.TryUseTotpStepAsync(user.Id, 100));
        Assert.False(await store.TryUseTotpStepAsync(user.Id, 99));
        Assert.True(await store.TryUseTotpStepAsync(user.Id, 101));
        Assert.False(await store.TryUseTotpStepAsync(user.Id, 101));
    }

    [Theory]
    [EnabledDatabases]
    public async Task 復旧コードは使い切り(DatabaseProvider provider)
    {
        var (store, _) = StoreFor(provider);
        var user = (await store.CreateAsync(NewLoginId(), "hash", "Editor", "ja"))!;
        await store.EnableTotpAsync(user.Id, "S", 1, ["code-1", "code-2"]);

        Assert.True(await store.TryUseRecoveryCodeAsync(user.Id, "code-1"));
        Assert.False(await store.TryUseRecoveryCodeAsync(user.Id, "code-1"));
        Assert.False(await store.TryUseRecoveryCodeAsync(user.Id, "unknown"));
        Assert.Equal(1, await store.CountRecoveryCodesAsync(user.Id));
    }

    [Fact]
    public void 棚卸しは_招待中と無効を対象にしない()
    {
        var now = Start;
        var stale = new AdminUser(Guid.NewGuid(), "a", "Editor", false, 0, null, now.AddDays(-100), now.AddDays(-200), false, "ja");
        var neverLoggedIn = stale with { LastLoginAt = null, CreatedAt = now.AddDays(-100) };

        Assert.True(stale.IsStale(now, TimeSpan.FromDays(90), invitationPending: false));
        Assert.True(neverLoggedIn.IsStale(now, TimeSpan.FromDays(90), invitationPending: false));
        Assert.False(stale.IsStale(now, TimeSpan.FromDays(90), invitationPending: true));
        Assert.False((stale with { IsDisabled = true }).IsStale(now, TimeSpan.FromDays(90), invitationPending: false));
        Assert.False(stale.IsStale(now, TimeSpan.FromDays(120), invitationPending: false));
    }
}
