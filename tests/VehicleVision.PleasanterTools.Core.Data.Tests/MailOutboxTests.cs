namespace VehicleVision.PleasanterTools.Core.Data.Tests;

[Collection(TestDatabases.CollectionName)]
public sealed class MailOutboxTests : DatabaseTestBase
{
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 3, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lock = TimeSpan.FromMinutes(5);

    private (MailOutbox Outbox, FixedTimeProvider Time, DbConnectionFactory Factory) OutboxFor(DatabaseProvider provider)
    {
        var time = new FixedTimeProvider(Start);
        var factory = MigratedFactoryFor(provider);

        // 共有 DB を使う RDBMS では、ほかのテストの行を確保しないよう、先に空にする
        using var connection = factory.Create();
        Dapper.SqlMapper.Execute(connection, SqlDialect.Format(provider, "DELETE FROM [Core_MailOutbox]"));
        return (new MailOutbox(factory, time), time, factory);
    }

    [Theory]
    [EnabledDatabases]
    public async Task 積んで確保し_送れたら消す(DatabaseProvider provider)
    {
        var (outbox, _, _) = OutboxFor(provider);
        var mailId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();

        Assert.True(await outbox.EnqueueAsync(mailId, 7, subjectId, "暗号文"));
        var claimed = await outbox.ClaimAsync("worker", Lock);

        Assert.NotNull(claimed);
        Assert.Equal(mailId, claimed.MailId);
        Assert.Equal(7, claimed.Kind);
        Assert.Equal(subjectId, claimed.SubjectId);
        Assert.Equal("暗号文", claimed.PayloadProtected);
        Assert.Equal(0, claimed.RetryCount);

        await outbox.CompleteAsync(mailId);
        var status = await outbox.GetStatusAsync();
        Assert.Equal(0, status.PendingCount);
        Assert.Null(status.OldestPendingAt);
    }

    [Theory]
    [EnabledDatabases]
    public async Task 紐づけが無いメールも積める(DatabaseProvider provider)
    {
        var (outbox, _, _) = OutboxFor(provider);

        await outbox.EnqueueAsync(Guid.NewGuid(), 1, subjectId: null, "p");
        var claimed = await outbox.ClaimAsync("worker", Lock);

        Assert.Null(claimed!.SubjectId);
    }

    [Theory]
    [EnabledDatabases]
    public async Task 同じ識別子は二重に積まない(DatabaseProvider provider)
    {
        var (outbox, _, _) = OutboxFor(provider);
        var mailId = Guid.NewGuid();

        Assert.True(await outbox.EnqueueAsync(mailId, 1, null, "p"));
        Assert.False(await outbox.EnqueueAsync(mailId, 1, null, "p"));
        Assert.Equal(1, (await outbox.GetStatusAsync()).PendingCount);
    }

    [Theory]
    [EnabledDatabases]
    public async Task 確保した行は_ほかのワーカーが取らない(DatabaseProvider provider)
    {
        var (outbox, _, _) = OutboxFor(provider);
        await outbox.EnqueueAsync(Guid.NewGuid(), 1, null, "p");

        Assert.NotNull(await outbox.ClaimAsync("a", Lock));
        Assert.Null(await outbox.ClaimAsync("b", Lock));
    }

    [Theory]
    [EnabledDatabases]
    public async Task 同時に確保しても_同じ行を取らない(DatabaseProvider provider)
    {
        var (outbox, _, _) = OutboxFor(provider);
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        foreach (var id in ids)
        {
            await outbox.EnqueueAsync(id, 1, null, "p");
        }

        var claims = await Task.WhenAll(Enumerable.Range(0, 5).Select(i => outbox.ClaimAsync($"w{i}", Lock)));

        var claimed = claims.Where(c => c is not null).Select(c => c!.MailId).ToList();
        Assert.Equal(claimed.Count, claimed.Distinct().Count());
        Assert.All(claimed, id => Assert.Contains(id, ids));
    }

    [Theory]
    [EnabledDatabases]
    public async Task 再送待ちは_時刻まで確保されない(DatabaseProvider provider)
    {
        var (outbox, time, _) = OutboxFor(provider);
        var mailId = Guid.NewGuid();
        await outbox.EnqueueAsync(mailId, 1, null, "p");
        await outbox.ClaimAsync("w", Lock);

        await outbox.RescheduleAsync(mailId, Start.AddMinutes(10), "失敗");

        time.Now = Start.AddMinutes(5);
        Assert.Null(await outbox.ClaimAsync("w", Lock));

        time.Now = Start.AddMinutes(11);
        var claimed = await outbox.ClaimAsync("w", Lock);
        Assert.Equal(mailId, claimed!.MailId);
        Assert.Equal(1, claimed.RetryCount);
    }

    [Theory]
    [EnabledDatabases]
    public async Task 送れない行は分離され_確保されない(DatabaseProvider provider)
    {
        var (outbox, _, _) = OutboxFor(provider);
        var mailId = Guid.NewGuid();
        await outbox.EnqueueAsync(mailId, 1, null, "p");

        await outbox.DeadLetterAsync(mailId, new string('x', 2000));

        Assert.Null(await outbox.ClaimAsync("w", Lock));
        var status = await outbox.GetStatusAsync();
        Assert.Equal(0, status.PendingCount);
        Assert.Equal(1, status.DeadLetterCount);
    }

    [Theory]
    [EnabledDatabases]
    public async Task 確保したまま期限が切れた行は_戻る(DatabaseProvider provider)
    {
        var (outbox, time, _) = OutboxFor(provider);
        var mailId = Guid.NewGuid();
        await outbox.EnqueueAsync(mailId, 1, null, "p");
        await outbox.ClaimAsync("w", Lock);

        time.Now = Start.AddMinutes(4);
        Assert.Equal(0, await outbox.ReleaseExpiredLocksAsync());

        time.Now = Start.AddMinutes(6);
        Assert.Equal(1, await outbox.ReleaseExpiredLocksAsync());
        Assert.Equal(mailId, (await outbox.ClaimAsync("w2", Lock))!.MailId);
    }

    [Theory]
    [EnabledDatabases]
    public async Task 件数と一番古い時刻を返す(DatabaseProvider provider)
    {
        var (outbox, time, _) = OutboxFor(provider);
        await outbox.EnqueueAsync(Guid.NewGuid(), 1, null, "p");
        time.Now = Start.AddMinutes(1);
        await outbox.EnqueueAsync(Guid.NewGuid(), 1, null, "p");

        var status = await outbox.GetStatusAsync();

        Assert.Equal(2, status.PendingCount);
        Assert.Equal(Start, status.OldestPendingAt);
    }
}
