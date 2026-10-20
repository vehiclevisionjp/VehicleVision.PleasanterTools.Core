namespace VehicleVision.PleasanterTools.Core.Data.Tests;

[Collection(TestDatabases.CollectionName)]
public sealed class AuditLogStoreTests : DatabaseTestBase
{
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 3, 0, 0, TimeSpan.Zero);

    /// <summary>共有 DB のほかのテストの行と混ざらないよう、このテスト専用の操作名と実行者を使う。</summary>
    private readonly string actor = $"actor-{Guid.NewGuid():N}";

    private AuditLogStore StoreFor(DatabaseProvider provider) => new(MigratedFactoryFor(provider));

    private AuditEntry Entry(string action, DateTimeOffset at, bool succeeded = true) =>
        new(at, action, succeeded, ActorKey: actor);

    private AuditLogQuery Mine(int limit = 100, int offset = 0, bool failedOnly = false) =>
        new() { ActorKey = actor, Limit = limit, Offset = offset, FailedOnly = failedOnly };

    [Theory]
    [EnabledDatabases]
    public async Task 書いた記録を新しい順に読める(DatabaseProvider provider)
    {
        var store = StoreFor(provider);
        await store.WriteAsync(Entry("a.old", Start));
        await store.WriteAsync(Entry("a.new", Start.AddMinutes(1)));

        var list = await store.ListAsync(Mine());

        Assert.Equal(["a.new", "a.old"], list.Select(e => e.Action));
        Assert.Equal(Start.AddMinutes(1), list[0].OccurredAt);
    }

    [Theory]
    [EnabledDatabases]
    public async Task すべての項目を保てる(DatabaseProvider provider)
    {
        var store = StoreFor(provider);
        var entry = new AuditEntry(
            Start, "survey.deleted", false, 403, actor, "ログイン名", "Survey", "id-1", "{\"reason\":\"理由\"}", "192.0.2.1");

        await store.WriteAsync(entry);
        var read = Assert.Single(await store.ListAsync(Mine()));

        Assert.Equal(entry, read);
    }

    [Theory]
    [EnabledDatabases]
    public async Task 失敗だけに絞れる_結果の状態が無い行も成否で見分ける(DatabaseProvider provider)
    {
        var store = StoreFor(provider);
        await store.WriteAsync(Entry("ok", Start));
        await store.WriteAsync(Entry("ng", Start.AddMinutes(1), succeeded: false));

        var failed = await store.ListAsync(Mine(failedOnly: true));

        Assert.Equal(["ng"], failed.Select(e => e.Action));
    }

    [Theory]
    [EnabledDatabases]
    public async Task 操作名は_完全一致と部分一致で絞れる(DatabaseProvider provider)
    {
        var store = StoreFor(provider);
        await store.WriteAsync(Entry("user.invited", Start));
        await store.WriteAsync(Entry("user.disabled", Start.AddMinutes(1)));
        await store.WriteAsync(Entry("feed.created", Start.AddMinutes(2)));

        Assert.Equal(["user.invited"], (await store.ListAsync(Mine() with { Action = "user.invited" })).Select(e => e.Action));
        Assert.Equal(2, (await store.ListAsync(Mine() with { ActionContains = "user." })).Count);
        // LIKE の記号は文字として扱う（% で全件に当たらない）
        Assert.Empty(await store.ListAsync(Mine() with { ActionContains = "%" }));
    }

    [Theory]
    [EnabledDatabases]
    public async Task 部分一致は_大文字小文字を区別しない(DatabaseProvider provider)
    {
        var store = StoreFor(provider);
        await store.WriteAsync(new AuditEntry(Start, "Auth.Login.Failure", false, ActorKey: actor, ActorLoginId: "Taro.Yamada"));
        await store.WriteAsync(new AuditEntry(Start.AddMinutes(1), "other", true, ActorKey: actor, ActorLoginId: "hanako"));

        Assert.Equal(["Auth.Login.Failure"], (await store.ListAsync(Mine() with { ActionContains = "LOGIN" })).Select(e => e.Action));
        Assert.Equal(["Auth.Login.Failure"], (await store.ListAsync(Mine() with { ActorLoginContains = "yamada" })).Select(e => e.Action));
        Assert.Empty(await store.ListAsync(Mine() with { ActorLoginContains = "%" }));
    }

    [Theory]
    [EnabledDatabases]
    public async Task 期間で絞れる(DatabaseProvider provider)
    {
        var store = StoreFor(provider);
        await store.WriteAsync(Entry("a", Start));
        await store.WriteAsync(Entry("b", Start.AddHours(1)));
        await store.WriteAsync(Entry("c", Start.AddHours(2)));

        var list = await store.ListAsync(Mine() with { From = Start.AddMinutes(30), To = Start.AddHours(2) });

        Assert.Equal(["b"], list.Select(e => e.Action));
    }

    [Theory]
    [EnabledDatabases]
    public async Task 同じ秒の記録も_件数の読み飛ばしで取りこぼさない(DatabaseProvider provider)
    {
        var store = StoreFor(provider);
        foreach (var i in Enumerable.Range(0, 5))
        {
            // 同じ秒に 5 件（秒未満は切り捨てられるので、時刻だけでは並びが定まらない）
            await store.WriteAsync(Entry($"same.{i}", Start.AddMilliseconds(i * 100)));
        }

        var page1 = await store.ListAsync(Mine(limit: 2, offset: 0));
        var page2 = await store.ListAsync(Mine(limit: 2, offset: 2));
        var page3 = await store.ListAsync(Mine(limit: 2, offset: 4));

        var all = page1.Concat(page2).Concat(page3).Select(e => e.Action).ToList();
        Assert.Equal(5, all.Count);
        Assert.Equal(5, all.Distinct().Count());
    }

    [Theory]
    [EnabledDatabases]
    public async Task 古い記録だけを消す(DatabaseProvider provider)
    {
        var store = StoreFor(provider);
        await store.WriteAsync(Entry("old", Start.AddDays(-100)));
        await store.WriteAsync(Entry("new", Start));

        var deleted = await store.DeleteOlderThanAsync(Start.AddDays(-30));

        Assert.True(deleted >= 1);
        Assert.Equal(["new"], (await store.ListAsync(Mine())).Select(e => e.Action));
    }

    [Theory]
    [EnabledDatabases]
    public async Task 制御文字は落とし_桁を超える値は切り詰める(DatabaseProvider provider)
    {
        var store = StoreFor(provider);
        var entry = new AuditEntry(
            Start, new string('a', 300), true, ActorKey: actor, ActorLoginId: "taro\r\nFAKE LOG LINE",
            TargetId: new string('t', 1000), IpAddress: new string('9', 100));

        await store.WriteAsync(entry);
        var read = Assert.Single(await store.ListAsync(Mine()));

        Assert.Equal(128, read.Action.Length);
        Assert.Equal("taroFAKE LOG LINE", read.ActorLoginId);
        Assert.Equal(512, read.TargetId!.Length);
        Assert.Equal(64, read.IpAddress!.Length);
    }

    [Fact]
    public void 文字列を安全にする()
    {
        Assert.Null(AuditText.Safe(null, 10));
        Assert.Null(AuditText.Safe("", 10));
        Assert.Equal("ab", AuditText.Safe("a\nb", 10));
        Assert.Equal("abc", AuditText.Safe("abcdef", 3));
    }
}
