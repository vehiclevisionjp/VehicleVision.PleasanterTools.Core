namespace VehicleVision.PleasanterTools.Core.Data.Tests;

[Collection(TestDatabases.CollectionName)]
public sealed class AppSettingStoreTests : DatabaseTestBase
{
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 3, 0, 0, TimeSpan.Zero);

    private (AppSettingStore Store, FixedTimeProvider Time) StoreFor(DatabaseProvider provider)
    {
        var time = new FixedTimeProvider(Start);
        return (new AppSettingStore(MigratedFactoryFor(provider), time), time);
    }

    private static string NewKey() => $"TEST_{Guid.NewGuid():N}";

    [Theory]
    [EnabledDatabases]
    public async Task 置いた設定を読める(DatabaseProvider provider)
    {
        var (store, _) = StoreFor(provider);
        var key = NewKey();
        var admin = Guid.NewGuid();

        await store.SaveAsync(key, "値", isSecret: false, admin);

        var record = Assert.Single(await store.ListAsync(), r => r.SettingKey == key);
        Assert.Equal("値", record.Value);
        Assert.False(record.IsSecret);
        Assert.Equal(Start, record.UpdatedAt);
        Assert.Equal(admin, record.UpdatedByAdminUserId);
    }

    [Theory]
    [EnabledDatabases]
    public async Task 同じ名前は置き換わる(DatabaseProvider provider)
    {
        var (store, time) = StoreFor(provider);
        var key = NewKey();
        await store.SaveAsync(key, "1", isSecret: false, Guid.NewGuid());

        time.Now = Start.AddMinutes(1);
        await store.SaveAsync(key, "2", isSecret: true, updatedByAdminUserId: null);

        var record = Assert.Single(await store.ListAsync(), r => r.SettingKey == key);
        Assert.Equal("2", record.Value);
        Assert.True(record.IsSecret);
        Assert.Equal(Start.AddMinutes(1), record.UpdatedAt);
        Assert.Null(record.UpdatedByAdminUserId);
    }

    [Theory]
    [EnabledDatabases]
    public async Task 消すと既定へ戻る(DatabaseProvider provider)
    {
        var (store, _) = StoreFor(provider);
        var key = NewKey();
        await store.SaveAsync(key, "1", isSecret: false, updatedByAdminUserId: null);

        await store.DeleteAsync(key);

        Assert.DoesNotContain(await store.ListAsync(), r => r.SettingKey == key);
    }

    [Theory]
    [EnabledDatabases]
    public async Task 日本語と長い値を保てる(DatabaseProvider provider)
    {
        var (store, _) = StoreFor(provider);
        var key = NewKey();
        var value = string.Concat(Enumerable.Repeat("設定の値あいうえお", 2000));

        await store.SaveAsync(key, value, isSecret: false, updatedByAdminUserId: null);

        Assert.Equal(value, Assert.Single(await store.ListAsync(), r => r.SettingKey == key).Value);
    }

    [Theory]
    [EnabledDatabases]
    public async Task 設定名が空なら断る(DatabaseProvider provider)
    {
        var (store, _) = StoreFor(provider);

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.SaveAsync(" ", "1", isSecret: false, updatedByAdminUserId: null));
    }
}
