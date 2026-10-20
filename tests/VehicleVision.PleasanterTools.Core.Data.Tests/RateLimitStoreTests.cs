namespace VehicleVision.PleasanterTools.Core.Data.Tests;

[Collection(TestDatabases.CollectionName)]
public sealed class RateLimitStoreTests : DatabaseTestBase
{
    private RateLimitStore StoreFor(DatabaseProvider provider) => new(MigratedFactoryFor(provider));

    private static string NewKey() => $"test:{Guid.NewGuid():N}";

    [Theory]
    [EnabledDatabases]
    public async Task 同じ窓では数えが増える(DatabaseProvider provider)
    {
        var store = StoreFor(provider);
        var key = NewKey();

        Assert.Equal(1, await store.HitAsync(key, 100));
        Assert.Equal(2, await store.HitAsync(key, 100));
        Assert.Equal(3, await store.HitAsync(key, 100));
    }

    [Theory]
    [EnabledDatabases]
    public async Task 窓が変わったら_数えを_1_に戻す(DatabaseProvider provider)
    {
        var store = StoreFor(provider);
        var key = NewKey();

        await store.HitAsync(key, 100);
        await store.HitAsync(key, 100);

        Assert.Equal(1, await store.HitAsync(key, 101));
        Assert.Equal(2, await store.HitAsync(key, 101));
    }

    [Theory]
    [EnabledDatabases]
    public async Task 鍵が違えば数えも別(DatabaseProvider provider)
    {
        var store = StoreFor(provider);

        Assert.Equal(1, await store.HitAsync(NewKey(), 100));
        Assert.Equal(1, await store.HitAsync(NewKey(), 100));
    }

    [Theory]
    [EnabledDatabases]
    public async Task 同時に数えても_数えこぼさない(DatabaseProvider provider)
    {
        var store = StoreFor(provider);
        var key = NewKey();

        var counts = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => store.HitAsync(key, 100)));

        // 全員が別の値を受け取り、最後は 10 になる
        Assert.Equal(Enumerable.Range(1, 10), counts.Order());
    }

    [Theory]
    [EnabledDatabases]
    public async Task 古い窓の行だけを消す(DatabaseProvider provider)
    {
        var store = StoreFor(provider);
        var oldKey = NewKey();
        var currentKey = NewKey();
        await store.HitAsync(oldKey, 1);
        await store.HitAsync(currentKey, 1_000_000_000);

        var purged = await store.PurgeAsync(1_000_000_000);

        Assert.True(purged >= 1);
        // 消された鍵は、数え直し（1）になる。残した鍵は続きの数（2）になる
        Assert.Equal(1, await store.HitAsync(oldKey, 1_000_000_000));
        Assert.Equal(2, await store.HitAsync(currentKey, 1_000_000_000));
    }
}
