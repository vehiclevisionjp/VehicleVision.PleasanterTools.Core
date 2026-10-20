namespace VehicleVision.PleasanterTools.Core.Data.Tests;

/// <summary>試験で時刻を固定する。</summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

[Collection(TestDatabases.CollectionName)]
public sealed class AdminInvitationStoreTests : DatabaseTestBase
{
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 3, 0, 0, TimeSpan.Zero);

    private (AdminInvitationStore Store, FixedTimeProvider Time) StoreFor(DatabaseProvider provider)
    {
        var time = new FixedTimeProvider(Start);
        return (new AdminInvitationStore(MigratedFactoryFor(provider), time), time);
    }

    private static string NewHash() => Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

    private static AdminInvitation NewInvitation(Guid adminUserId, DateTimeOffset expiresAt, string? hash = null) =>
        new(Guid.NewGuid(), adminUserId, hash ?? NewHash(), expiresAt, CreatedBy: Guid.NewGuid());

    [Theory]
    [EnabledDatabases]
    public async Task 置いた招待をハッシュで引ける(DatabaseProvider provider)
    {
        var (store, _) = StoreFor(provider);
        var invitation = NewInvitation(Guid.NewGuid(), Start.AddHours(1));

        await store.ReplaceAsync(invitation);
        var found = await store.FindByTokenHashAsync(invitation.TokenHash);

        Assert.NotNull(found);
        Assert.Equal(invitation.InvitationId, found.InvitationId);
        Assert.Equal(invitation.AdminUserId, found.AdminUserId);
        Assert.Equal(invitation.CreatedBy, found.CreatedBy);
        Assert.Equal(Start.AddHours(1), found.ExpiresAt);
        Assert.Equal(Start, found.CreatedAt);
        Assert.Null(found.UsedAt);
        Assert.Null(await store.FindByTokenHashAsync(NewHash()));
    }

    [Theory]
    [EnabledDatabases]
    public async Task 出し直すと_前の未使用の招待は死ぬ(DatabaseProvider provider)
    {
        var (store, _) = StoreFor(provider);
        var adminUserId = Guid.NewGuid();
        var first = NewInvitation(adminUserId, Start.AddHours(1));
        var second = NewInvitation(adminUserId, Start.AddHours(1));

        await store.ReplaceAsync(first);
        await store.ReplaceAsync(second);

        Assert.Null(await store.FindByTokenHashAsync(first.TokenHash));
        Assert.NotNull(await store.FindByTokenHashAsync(second.TokenHash));
    }

    [Theory]
    [EnabledDatabases]
    public async Task 使い切りは_1_回だけ通る(DatabaseProvider provider)
    {
        var (store, time) = StoreFor(provider);
        var invitation = NewInvitation(Guid.NewGuid(), Start.AddHours(1));
        await store.ReplaceAsync(invitation);

        time.Now = Start.AddMinutes(5);
        Assert.True(await store.TryConsumeAsync(invitation.InvitationId));
        Assert.False(await store.TryConsumeAsync(invitation.InvitationId));

        var found = await store.FindByTokenHashAsync(invitation.TokenHash);
        Assert.Equal(Start.AddMinutes(5), found!.UsedAt);
    }

    [Theory]
    [EnabledDatabases]
    public async Task 同時に使い切ろうとしても_通るのは_1_つだけ(DatabaseProvider provider)
    {
        var (store, _) = StoreFor(provider);
        var invitation = NewInvitation(Guid.NewGuid(), Start.AddHours(1));
        await store.ReplaceAsync(invitation);

        var results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => store.TryConsumeAsync(invitation.InvitationId)));

        Assert.Single(results, r => r);
    }

    [Theory]
    [EnabledDatabases]
    public async Task 使用済みの行は_出し直しても残る(DatabaseProvider provider)
    {
        var (store, _) = StoreFor(provider);
        var adminUserId = Guid.NewGuid();
        var used = NewInvitation(adminUserId, Start.AddHours(1));
        await store.ReplaceAsync(used);
        await store.TryConsumeAsync(used.InvitationId);

        await store.ReplaceAsync(NewInvitation(adminUserId, Start.AddHours(1)));

        Assert.NotNull(await store.FindByTokenHashAsync(used.TokenHash));
    }

    [Theory]
    [EnabledDatabases]
    public async Task 招待中は_未使用で期限内だけ(DatabaseProvider provider)
    {
        var (store, time) = StoreFor(provider);
        var pending = Guid.NewGuid();
        var expired = Guid.NewGuid();
        var used = Guid.NewGuid();
        await store.ReplaceAsync(NewInvitation(pending, Start.AddHours(1)));
        await store.ReplaceAsync(NewInvitation(expired, Start.AddMinutes(1)));
        var usedInvitation = NewInvitation(used, Start.AddHours(1));
        await store.ReplaceAsync(usedInvitation);
        await store.TryConsumeAsync(usedInvitation.InvitationId);

        time.Now = Start.AddMinutes(10);
        var ids = await store.ListPendingAdminUserIdsAsync();

        Assert.Contains(pending, ids);
        Assert.DoesNotContain(expired, ids);
        Assert.DoesNotContain(used, ids);
    }

    [Theory]
    [EnabledDatabases]
    public async Task 取り消すと_未使用の招待だけ消える(DatabaseProvider provider)
    {
        var (store, _) = StoreFor(provider);
        var adminUserId = Guid.NewGuid();
        var invitation = NewInvitation(adminUserId, Start.AddHours(1));
        await store.ReplaceAsync(invitation);

        await store.RevokeUnusedAsync(adminUserId);

        Assert.Null(await store.FindByTokenHashAsync(invitation.TokenHash));
    }
}
