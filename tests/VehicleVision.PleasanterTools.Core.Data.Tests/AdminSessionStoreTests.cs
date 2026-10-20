namespace VehicleVision.PleasanterTools.Core.Data.Tests;

[Collection(TestDatabases.CollectionName)]
public sealed class AdminSessionStoreTests : DatabaseTestBase
{
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 3, 0, 0, TimeSpan.Zero);

    private (AdminSessionStore Store, FixedTimeProvider Time, DbConnectionFactory Factory) StoreFor(DatabaseProvider provider)
    {
        var time = new FixedTimeProvider(Start);
        var factory = MigratedFactoryFor(provider);
        return (new AdminSessionStore(factory, time), time, factory);
    }

    [Theory]
    [EnabledDatabases]
    public async Task 作ったセッションを引ける_DB_にはハッシュだけを置く(DatabaseProvider provider)
    {
        var (store, _, factory) = StoreFor(provider);
        var userId = Guid.NewGuid();

        var sessionId = await store.CreateAsync(userId, AdminSessionKinds.Session, TimeSpan.FromHours(8), ipAddress: "192.0.2.1", userAgent: "UA");
        var session = await store.FindAsync(sessionId);

        Assert.NotNull(session);
        Assert.Equal(userId, session.UserId);
        Assert.Equal(AdminSessionKinds.Session, session.Kind);
        Assert.Equal(Start.AddHours(8), session.ExpiresAt);
        Assert.Null(session.Pleasanter);

        // cookie の現物（sessionId）は DB にない。あるのは SHA-256（HandleOf）だけ
        using var connection = factory.Create();
        var stored = Dapper.SqlMapper.Query<string>(
            connection, SqlDialect.Format(provider, "SELECT [Id] FROM [Core_AdminSessions] WHERE [UserId] = @UserId"), new { UserId = userId }).ToList();
        Assert.Equal([store.HandleOf(sessionId)], stored);
        Assert.NotEqual(sessionId, stored[0]);
        Assert.True(sessionId.Length >= 43);
    }

    [Theory]
    [EnabledDatabases]
    public async Task 期限切れと未知の_ID_は引けない(DatabaseProvider provider)
    {
        var (store, time, _) = StoreFor(provider);
        var sessionId = await store.CreateAsync(Guid.NewGuid(), AdminSessionKinds.Session, TimeSpan.FromMinutes(5));

        time.Now = Start.AddMinutes(6);

        Assert.Null(await store.FindAsync(sessionId));
        Assert.Null(await store.FindAsync("unknown"));
    }

    [Theory]
    [EnabledDatabases]
    public async Task 期限切れは掃除で消える(DatabaseProvider provider)
    {
        var (store, time, _) = StoreFor(provider);
        await store.CreateAsync(Guid.NewGuid(), AdminSessionKinds.Session, TimeSpan.FromMinutes(5));

        time.Now = Start.AddMinutes(10);

        Assert.True(await store.DeleteExpiredAsync() >= 1);
    }

    [Theory]
    [EnabledDatabases]
    public async Task 一覧は_ログイン済みで期限内の端末だけ_新しい順(DatabaseProvider provider)
    {
        var (store, time, _) = StoreFor(provider);
        var userId = Guid.NewGuid();
        var older = await store.CreateAsync(userId, AdminSessionKinds.Session, TimeSpan.FromHours(8));
        time.Now = Start.AddMinutes(1);
        var newer = await store.CreateAsync(userId, AdminSessionKinds.Session, TimeSpan.FromHours(8));
        await store.CreateAsync(userId, AdminSessionKinds.Pending, TimeSpan.FromMinutes(5));
        await store.CreateAsync(Guid.NewGuid(), AdminSessionKinds.Session, TimeSpan.FromHours(8));

        var list = await store.ListForUserAsync(userId);

        Assert.Equal([store.HandleOf(newer), store.HandleOf(older)], list.Select(i => i.Handle));
        Assert.All(list, i => Assert.Equal(SessionVia.Password, i.Via));
    }

    [Theory]
    [EnabledDatabases]
    public async Task 端末の失効は_持ち主の組で消す(DatabaseProvider provider)
    {
        var (store, _, _) = StoreFor(provider);
        var owner = Guid.NewGuid();
        var sessionId = await store.CreateAsync(owner, AdminSessionKinds.Session, TimeSpan.FromHours(1));
        var handle = store.HandleOf(sessionId);

        Assert.False(await store.DeleteByHandleAsync(Guid.NewGuid(), handle));
        Assert.NotNull(await store.FindAsync(sessionId));

        Assert.True(await store.DeleteByHandleAsync(owner, handle));
        Assert.Null(await store.FindAsync(sessionId));
    }

    [Theory]
    [EnabledDatabases]
    public async Task 利用者のセッションを消す_指定した端末だけ残せる(DatabaseProvider provider)
    {
        var (store, _, _) = StoreFor(provider);
        var userId = Guid.NewGuid();
        var keep = await store.CreateAsync(userId, AdminSessionKinds.Session, TimeSpan.FromHours(1));
        var drop = await store.CreateAsync(userId, AdminSessionKinds.Session, TimeSpan.FromHours(1));
        var other = await store.CreateAsync(Guid.NewGuid(), AdminSessionKinds.Session, TimeSpan.FromHours(1));

        await store.DeleteForUserAsync(userId, exceptSessionId: keep);

        Assert.NotNull(await store.FindAsync(keep));
        Assert.Null(await store.FindAsync(drop));
        Assert.NotNull(await store.FindAsync(other));

        await store.DeleteForUserAsync(userId);
        Assert.Null(await store.FindAsync(keep));
    }

    [Theory]
    [EnabledDatabases]
    public async Task SAML_の鍵で単一ログアウトできる(DatabaseProvider provider)
    {
        var (store, _, _) = StoreFor(provider);
        var name = $"name-{Guid.NewGuid():N}";
        var a = await store.CreateAsync(Guid.NewGuid(), AdminSessionKinds.Session, TimeSpan.FromHours(1), sso: new SessionSso(new SamlSessionKeys(name, "idx-1")));
        var b = await store.CreateAsync(Guid.NewGuid(), AdminSessionKinds.Session, TimeSpan.FromHours(1), sso: new SessionSso(new SamlSessionKeys(name, "idx-2")));

        Assert.Equal(new SamlSessionKeys(name, "idx-1"), await store.GetSamlKeysAsync(a));
        Assert.Equal(1, await store.DeleteBySamlKeysAsync(name, "idx-1"));
        Assert.Null(await store.FindAsync(a));
        Assert.NotNull(await store.FindAsync(b));

        // セッション索引が無ければ、その NameID のすべてを捨てる
        Assert.Equal(1, await store.DeleteBySamlKeysAsync(name, null));
        Assert.Null(await store.FindAsync(b));
    }

    [Theory]
    [EnabledDatabases]
    public async Task Pleasanter_のログインの値は_引き継ぎと再検証の更新ができる(DatabaseProvider provider)
    {
        var (store, _, _) = StoreFor(provider);
        var verified = Start.AddMinutes(-1);
        var sessionId = await store.CreateAsync(
            Guid.NewGuid(), AdminSessionKinds.Session, TimeSpan.FromHours(1),
            sso: new SessionSso(Pleasanter: new PleasanterSsoKeys(1, 42, verified)));

        var sso = await store.GetSsoAsync(sessionId);
        Assert.Equal(new PleasanterSsoKeys(1, 42, verified), sso!.Pleasanter);
        Assert.Null(sso.Saml);
        Assert.Equal(new PleasanterSsoKeys(1, 42, verified), (await store.FindAsync(sessionId))!.Pleasanter);

        await store.TouchPleasanterVerifiedAsync(sessionId, Start);
        Assert.Equal(Start, (await store.GetSsoAsync(sessionId))!.Pleasanter!.VerifiedAt);

        var list = await store.ListForUserAsync((await store.FindAsync(sessionId))!.UserId);
        Assert.Equal(SessionVia.Pleasanter, Assert.Single(list).Via);
    }

    [Theory]
    [EnabledDatabases]
    public async Task 外部のログインでなければ_SSO_の値は無い(DatabaseProvider provider)
    {
        var (store, _, _) = StoreFor(provider);
        var sessionId = await store.CreateAsync(Guid.NewGuid(), AdminSessionKinds.Session, TimeSpan.FromHours(1));

        Assert.Null(await store.GetSsoAsync(sessionId));
        Assert.Null(await store.GetSamlKeysAsync(sessionId));
        Assert.Null(await store.GetSsoAsync("unknown"));
    }
}
