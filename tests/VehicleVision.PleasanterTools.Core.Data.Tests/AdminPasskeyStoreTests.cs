using System.Security.Cryptography;

namespace VehicleVision.PleasanterTools.Core.Data.Tests;

[Collection(TestDatabases.CollectionName)]
public sealed class AdminPasskeyStoreTests : DatabaseTestBase
{
    private static readonly DateTimeOffset Start = new(2026, 11, 25, 3, 0, 0, TimeSpan.Zero);

    private (AdminPasskeyStore Store, AdminPasskeyChallengeStore Challenges, FixedTimeProvider Time, DbConnectionFactory Factory) StoresFor(
        DatabaseProvider provider)
    {
        var time = new FixedTimeProvider(Start);
        var factory = MigratedFactoryFor(provider);
        return (new AdminPasskeyStore(factory, time), new AdminPasskeyChallengeStore(factory, time), time, factory);
    }

    private static NewAdminPasskey NewPasskey(Guid userId, byte[]? credentialId = null, string name = "仕事用 PC") => new(
        userId,
        credentialId ?? RandomNumberGenerator.GetBytes(32),
        RandomNumberGenerator.GetBytes(77),
        0,
        Guid.NewGuid(),
        ["internal", "hybrid"],
        true,
        false,
        name);

    [Theory]
    [EnabledDatabases]
    public async Task 足したパスキーを一覧と_ID_で引ける(DatabaseProvider provider)
    {
        var (store, _, _, _) = StoresFor(provider);
        var userId = Guid.NewGuid();
        var input = NewPasskey(userId, name: "  仕事用 PC  ");

        var id = await store.AddAsync(input);

        Assert.NotNull(id);
        var list = await store.ListForUserAsync(userId);
        var item = Assert.Single(list);
        Assert.Equal(id, item.Id);
        Assert.Equal(userId, item.UserId);
        Assert.Equal(input.CredentialId, item.CredentialId);
        Assert.Equal(input.PublicKey, item.PublicKey);
        Assert.Equal(input.AaGuid, item.AaGuid);
        Assert.Equal(["internal", "hybrid"], item.Transports);
        Assert.True(item.BackupEligible);
        Assert.False(item.BackedUp);
        Assert.Equal("仕事用 PC", item.Name);
        Assert.Equal(Start, item.CreatedAt);
        Assert.Null(item.LastUsedAt);
        Assert.Equal(0, item.SignCount);

        var found = await store.FindByCredentialIdAsync(input.CredentialId);
        Assert.Equal(id, found?.Id);
        Assert.Null(await store.FindByCredentialIdAsync(RandomNumberGenerator.GetBytes(32)));
    }

    [Theory]
    [EnabledDatabases]
    public async Task 最大長の資格情報_ID_でも一意索引に収まり_同じ_ID_は足せない(DatabaseProvider provider)
    {
        var (store, _, _, _) = StoresFor(provider);
        var credentialId = RandomNumberGenerator.GetBytes(AdminPasskeyStore.MaxCredentialIdBytes);

        var first = await store.AddAsync(NewPasskey(Guid.NewGuid(), credentialId));
        var other = Guid.NewGuid();
        var second = await store.AddAsync(NewPasskey(other, credentialId));

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.Equal(0, await store.CountForUserAsync(other));
        Assert.Equal(credentialId, (await store.FindByCredentialIdAsync(credentialId))?.CredentialId);
    }

    [Theory]
    [EnabledDatabases]
    public async Task 最大長の公開鍵と日本語の名前を往復できる(DatabaseProvider provider)
    {
        var (store, _, _, _) = StoresFor(provider);
        var userId = Guid.NewGuid();
        var input = NewPasskey(userId, name: new string('あ', AdminPasskeyStore.MaxNameLength)) with
        {
            PublicKey = RandomNumberGenerator.GetBytes(AdminPasskeyStore.MaxPublicKeyBytes),
        };

        await store.AddAsync(input);

        var item = Assert.Single(await store.ListForUserAsync(userId));
        Assert.Equal(input.PublicKey, item.PublicKey);
        Assert.Equal(input.Name, item.Name);
    }

    [Theory]
    [EnabledDatabases]
    public async Task 桁を超える値は拒む(DatabaseProvider provider)
    {
        var (store, _, _, _) = StoresFor(provider);
        var userId = Guid.NewGuid();

        await Assert.ThrowsAsync<ArgumentException>(() => store.AddAsync(NewPasskey(userId, name: " ")));
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.AddAsync(NewPasskey(userId, name: new string('a', AdminPasskeyStore.MaxNameLength + 1))));
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.AddAsync(NewPasskey(userId, RandomNumberGenerator.GetBytes(AdminPasskeyStore.MaxCredentialIdBytes + 1))));
        await Assert.ThrowsAsync<ArgumentException>(() => store.AddAsync(NewPasskey(userId, [])));
        Assert.Equal(0, await store.CountForUserAsync(userId));
    }

    [Theory]
    [EnabledDatabases]
    public async Task 名前の変更と削除は持ち主の組で効く(DatabaseProvider provider)
    {
        var (store, _, _, _) = StoresFor(provider);
        var owner = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var id = (await store.AddAsync(NewPasskey(owner)))!.Value;

        Assert.False(await store.RenameAsync(stranger, id, "乗っ取り"));
        Assert.False(await store.DeleteAsync(stranger, id));
        Assert.True(await store.RenameAsync(owner, id, "自宅の Mac"));
        Assert.Equal("自宅の Mac", Assert.Single(await store.ListForUserAsync(owner)).Name);

        Assert.True(await store.DeleteAsync(owner, id));
        Assert.False(await store.DeleteAsync(owner, id));
        Assert.Equal(0, await store.CountForUserAsync(owner));
    }

    [Theory]
    [EnabledDatabases]
    public async Task 件数と持ち主ごとの削除(DatabaseProvider provider)
    {
        var (store, _, _, _) = StoresFor(provider);
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        await store.AddAsync(NewPasskey(owner));
        await store.AddAsync(NewPasskey(owner));
        await store.AddAsync(NewPasskey(other));

        Assert.Equal(2, await store.CountForUserAsync(owner));
        Assert.Equal(2, await store.DeleteForUserAsync(owner));
        Assert.Equal(0, await store.CountForUserAsync(owner));
        Assert.Equal(1, await store.CountForUserAsync(other));
    }

    [Theory]
    [EnabledDatabases]
    public async Task 署名カウンターは前の値が合うときだけ更新し_最終使用を残す(DatabaseProvider provider)
    {
        var (store, _, time, _) = StoresFor(provider);
        var userId = Guid.NewGuid();
        var id = (await store.AddAsync(NewPasskey(userId)))!.Value;
        time.Now = Start.AddMinutes(3);

        Assert.True(await store.RecordSignInAsync(id, 0, 4_000_000_000, true));
        // 同じ前の値でもう一度（同時に通った 2 つ目）は更新されない
        Assert.False(await store.RecordSignInAsync(id, 0, 5, false));

        var item = Assert.Single(await store.ListForUserAsync(userId));
        Assert.Equal(4_000_000_000, item.SignCount);
        Assert.True(item.BackedUp);
        Assert.Equal(Start.AddMinutes(3), item.LastUsedAt);
    }

    [Theory]
    [EnabledDatabases]
    public async Task チャレンジは一度だけ消費できる(DatabaseProvider provider)
    {
        var (_, challenges, _, _) = StoresFor(provider);
        var challenge = RandomNumberGenerator.GetBytes(32);
        await challenges.StoreAsync(challenge, PasskeyChallengePurposes.SignIn, null, IAdminPasskeyChallengeStore.DefaultLifetime);

        Assert.True(await challenges.ConsumeAsync(challenge, PasskeyChallengePurposes.SignIn, null));
        Assert.False(await challenges.ConsumeAsync(challenge, PasskeyChallengePurposes.SignIn, null));
    }

    [Theory]
    [EnabledDatabases]
    public async Task チャレンジは用途と持ち主が違うと消費できず_消費もされない(DatabaseProvider provider)
    {
        var (_, challenges, _, factory) = StoresFor(provider);
        var userId = Guid.NewGuid();
        var challenge = RandomNumberGenerator.GetBytes(32);
        await challenges.StoreAsync(challenge, PasskeyChallengePurposes.Register, userId, IAdminPasskeyChallengeStore.DefaultLifetime);

        Assert.False(await challenges.ConsumeAsync(challenge, PasskeyChallengePurposes.SignIn, userId));
        Assert.False(await challenges.ConsumeAsync(challenge, PasskeyChallengePurposes.Register, Guid.NewGuid()));
        Assert.False(await challenges.ConsumeAsync(challenge, PasskeyChallengePurposes.Register, null));
        Assert.False(await challenges.ConsumeAsync(RandomNumberGenerator.GetBytes(32), PasskeyChallengePurposes.Register, userId));
        Assert.True(await challenges.ConsumeAsync(challenge, PasskeyChallengePurposes.Register, userId));

        // 現物は DB に無い（SHA-256 だけ）
        await challenges.StoreAsync(challenge, PasskeyChallengePurposes.SignIn, null, IAdminPasskeyChallengeStore.DefaultLifetime);
        using var connection = factory.Create();
        var hashes = Dapper.SqlMapper.Query<string>(
            connection, SqlDialect.Format(provider, "SELECT [ChallengeHash] FROM [Core_AdminPasskeyChallenges]")).ToList();
        Assert.DoesNotContain(Convert.ToBase64String(challenge), hashes);
        Assert.Contains(Convert.ToHexStringLower(SHA256.HashData(challenge)), hashes);
    }

    [Theory]
    [EnabledDatabases]
    public async Task 期限切れのチャレンジは消費できず_掃除で消える(DatabaseProvider provider)
    {
        var (_, challenges, time, _) = StoresFor(provider);
        var challenge = RandomNumberGenerator.GetBytes(32);
        await challenges.StoreAsync(challenge, PasskeyChallengePurposes.SignIn, null, TimeSpan.FromMinutes(5));

        time.Now = Start.AddMinutes(6);

        Assert.False(await challenges.ConsumeAsync(challenge, PasskeyChallengePurposes.SignIn, null));
        Assert.True(await challenges.DeleteExpiredAsync() >= 1);
        Assert.Equal(0, await challenges.DeleteExpiredAsync());
    }

    [Theory]
    [EnabledDatabases]
    public async Task 同時に消費しても_1_件だけが通る(DatabaseProvider provider)
    {
        var (_, challenges, _, _) = StoresFor(provider);
        var challenge = RandomNumberGenerator.GetBytes(32);
        await challenges.StoreAsync(challenge, PasskeyChallengePurposes.SignIn, null, IAdminPasskeyChallengeStore.DefaultLifetime);

        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => challenges.ConsumeAsync(challenge, PasskeyChallengePurposes.SignIn, null)));

        Assert.Single(results, true);
    }

    [Theory]
    [EnabledDatabases]
    public void マイグレーションは冪等で_何度当てても壊れない(DatabaseProvider provider)
    {
        var (_, _, _, _) = StoresFor(provider);

        TestDatabases.Migrate(provider, ConnectionStringFor(provider));
        TestDatabases.Migrate(provider, ConnectionStringFor(provider));
    }
}
