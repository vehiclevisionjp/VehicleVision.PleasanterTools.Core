using System.Data.Common;
using Dapper;

namespace VehicleVision.PleasanterTools.Core.Data;

/// <summary>送信待ちの状態。<b>DB へ整数で入る。値を入れ替えない。</b></summary>
public enum MailStatus
{
    /// <summary>送信待ち（再送待ちを含む）。</summary>
    Pending = 0,

    /// <summary>送信ワーカーが確保して送っている最中。</summary>
    Sending = 1,

    /// <summary>再送しても送れない。<b>人が見る必要がある。</b></summary>
    DeadLetter = 2,
}

/// <summary>確保した送信待ち 1 通。</summary>
/// <param name="MailId">識別子。</param>
/// <param name="Kind">種類。<b>値の意味はアプリが決める</b>（Core は意味を持たない）。</param>
/// <param name="SubjectId">何に紐づくか。アプリが任意に持つ（例: アンケートの ID）。無ければ <c>null</c>。</param>
/// <param name="PayloadProtected">宛先・件名・本文を 1 つにまとめて暗号化したもの。</param>
/// <param name="RetryCount">ここまでの再送の回数。</param>
public sealed record PendingMail(Guid MailId, int Kind, Guid? SubjectId, string PayloadProtected, int RetryCount);

/// <summary>送信待ちの件数。管理画面・監視に出す。</summary>
public sealed record MailOutboxStatus(int PendingCount, DateTimeOffset? OldestPendingAt, int DeadLetterCount);

/// <summary>メールの送信待ち（アウトボックス）。</summary>
/// <remarks>
/// <para>
/// **宛先・件名・本文は 1 つにまとめて暗号化して置く。** 列を分けると、暗号化の掛け忘れが列ごとに起き得る。
/// **送れたら行ごと消す**（残るのは、届いていない間だけ）。
/// </para>
/// <para>
/// 確保は 1 文の UPDATE で原子的に行う（複数の送信ワーカーが同じ 1 通を取らない）。
/// 確保したまま落ちても、期限（<c>LockedUntil</c>）が切れれば解放される。メールは失われない。
/// </para>
/// </remarks>
public interface IMailOutbox
{
    /// <summary>積む。<b>同じ識別子がすでにあれば何もせず <c>false</c></b>（二重に送らない側へ倒す）。</summary>
    Task<bool> EnqueueAsync(
        Guid mailId,
        int kind,
        Guid? subjectId,
        string payloadProtected,
        CancellationToken cancellationToken = default);

    /// <summary>送れる 1 通を確保する。無ければ <c>null</c>。</summary>
    Task<PendingMail?> ClaimAsync(
        string lockedBy,
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default);

    /// <summary>送れたので消す。<b>「念のため」残さない</b>（宛先と本文を持ち続けない）。</summary>
    Task CompleteAsync(Guid mailId, CancellationToken cancellationToken = default);

    /// <summary>再送待ちへ戻す。</summary>
    Task RescheduleAsync(
        Guid mailId,
        DateTimeOffset nextAttemptAt,
        string? error,
        CancellationToken cancellationToken = default);

    /// <summary>再送しても送れないので分離する。</summary>
    Task DeadLetterAsync(Guid mailId, string error, CancellationToken cancellationToken = default);

    /// <summary>確保したまま期限が切れた行を戻す。解放した件数を返す。</summary>
    Task<int> ReleaseExpiredLocksAsync(CancellationToken cancellationToken = default);

    /// <summary>件数を返す。</summary>
    Task<MailOutboxStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}

/// <summary>Dapper を使った実装（SQLite / SQL Server / PostgreSQL / MySQL）。表は <c>Core_MailOutbox</c>（M10004）。</summary>
/// <param name="connections">接続のファクトリ。</param>
/// <param name="time">現在時刻。<b>試験で固定できるよう、注入する。</b></param>
public sealed class MailOutbox(IDbConnectionFactory connections, TimeProvider time) : IMailOutbox
{
    private DatabaseProvider Provider => connections.Provider;

    private string Sql(string sql) => SqlDialect.Format(Provider, sql);

    private DateTime Now() => DbTime.ForDb(time.GetUtcNow().UtcDateTime);

    public async Task<bool> EnqueueAsync(
        Guid mailId,
        int kind,
        Guid? subjectId,
        string payloadProtected,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadProtected);

        var now = Now();
        await using var connection = connections.Create();
        try
        {
            var affected = await connection.ExecuteAsync(new CommandDefinition(
                Sql("INSERT INTO [Core_MailOutbox] ([MailId], [Kind], [SubjectId], [PayloadProtected], [Status], [RetryCount], "
                    + "[NextAttemptAt], [CreatedAt], [UpdatedAt]) "
                    + "VALUES (@MailId, @Kind, @SubjectId, @PayloadProtected, @PendingStatus, 0, @Now, @Now, @Now)"),
                new
                {
                    MailId = mailId,
                    Kind = kind,
                    SubjectId = subjectId,
                    PayloadProtected = payloadProtected,
                    PendingStatus = (int)MailStatus.Pending,
                    Now = now,
                },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            return affected > 0;
        }
        catch (DbException)
        {
            // 主キーの衝突＝すでに積んである。**影響行数では見分けられない**（MySQL は一致行数を返す）ので、衝突そのもので判定する
            return false;
        }
    }

    public async Task<PendingMail?> ClaimAsync(
        string lockedBy,
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockedBy);

        var now = time.GetUtcNow();

        // **確保ごとに一意な値を入れる**（過去の確保と一致させない）
        var owner = lockedBy.Length > 24 ? lockedBy[..24] : lockedBy;
        var parameters = new
        {
            SendingStatus = (int)MailStatus.Sending,
            PendingStatus = (int)MailStatus.Pending,
            LockedBy = $"{owner}:{Guid.NewGuid():N}",
            LockedUntil = DbTime.ForDb((now + lockDuration).UtcDateTime),
            Now = DbTime.ForDb(now.UtcDateTime),
        };

        await using var connection = connections.Create();
        if (SqlDialect.SupportsReturning(Provider))
        {
            return (await connection.QueryFirstOrDefaultAsync<ClaimedRow>(new CommandDefinition(
                MailOutboxSql.ClaimPending(Provider), parameters, cancellationToken: cancellationToken)).ConfigureAwait(false))?.ToPending();
        }

        // MySQL は RETURNING が無いので、確保してから読み直す
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            MailOutboxSql.ClaimPending(Provider), parameters, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return affected == 0
            ? null
            : (await connection.QueryFirstOrDefaultAsync<ClaimedRow>(new CommandDefinition(
                MailOutboxSql.ReadClaimedForMySql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false))?.ToPending();
    }

    public async Task CompleteAsync(Guid mailId, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        await connection.ExecuteAsync(new CommandDefinition(
            Sql("DELETE FROM [Core_MailOutbox] WHERE [MailId] = @MailId"), new { MailId = mailId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task RescheduleAsync(
        Guid mailId,
        DateTimeOffset nextAttemptAt,
        string? error,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        await connection.ExecuteAsync(new CommandDefinition(
            Sql("UPDATE [Core_MailOutbox] SET [Status] = @PendingStatus, [RetryCount] = [RetryCount] + 1, "
                + "[NextAttemptAt] = @NextAttemptAt, [LastError] = @Error, [LockedBy] = NULL, [LockedUntil] = NULL, "
                + "[UpdatedAt] = @Now WHERE [MailId] = @MailId"),
            new
            {
                MailId = mailId,
                PendingStatus = (int)MailStatus.Pending,
                NextAttemptAt = DbTime.ForDb(nextAttemptAt.UtcDateTime),
                Error = Truncate(error),
                Now = Now(),
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task DeadLetterAsync(Guid mailId, string error, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        await connection.ExecuteAsync(new CommandDefinition(
            Sql("UPDATE [Core_MailOutbox] SET [Status] = @DeadLetterStatus, [LastError] = @Error, [LockedBy] = NULL, "
                + "[LockedUntil] = NULL, [UpdatedAt] = @Now WHERE [MailId] = @MailId"),
            new
            {
                MailId = mailId,
                DeadLetterStatus = (int)MailStatus.DeadLetter,
                Error = Truncate(error),
                Now = Now(),
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<int> ReleaseExpiredLocksAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        return await connection.ExecuteAsync(new CommandDefinition(
            Sql("UPDATE [Core_MailOutbox] SET [Status] = @PendingStatus, [LockedBy] = NULL, [LockedUntil] = NULL, [UpdatedAt] = @Now "
                + "WHERE [Status] = @SendingStatus AND [LockedUntil] < @Now"),
            new { PendingStatus = (int)MailStatus.Pending, SendingStatus = (int)MailStatus.Sending, Now = Now() },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<MailOutboxStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        var row = await connection.QueryFirstAsync<StatusRow>(new CommandDefinition(
            Sql("SELECT COUNT(CASE WHEN [Status] <> @DeadLetterStatus THEN 1 END) AS [PendingCount], "
                + "MIN(CASE WHEN [Status] <> @DeadLetterStatus THEN [CreatedAt] END) AS [OldestPendingAt], "
                + "COUNT(CASE WHEN [Status] = @DeadLetterStatus THEN 1 END) AS [DeadLetterCount] FROM [Core_MailOutbox]"),
            new { DeadLetterStatus = (int)MailStatus.DeadLetter },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return new MailOutboxStatus((int)row.PendingCount, DbTime.AsUtc(row.OldestPendingAt), (int)row.DeadLetterCount);
    }

    private static string? Truncate(string? error) =>
        error is null ? null : error.Length <= 1024 ? error : error[..1024];

    /// <summary>確保した行。整数の列は RDBMS によって 64 ビットで返るので、受けてから変換する。</summary>
    private sealed class ClaimedRow
    {
        public Guid MailId { get; set; }

        public long Kind { get; set; }

        public Guid? SubjectId { get; set; }

        public string PayloadProtected { get; set; } = "";

        public long RetryCount { get; set; }

        public PendingMail ToPending() => new(MailId, (int)Kind, SubjectId, PayloadProtected, (int)RetryCount);
    }

    private sealed class StatusRow
    {
        public long PendingCount { get; set; }

        public DateTime? OldestPendingAt { get; set; }

        public long DeadLetterCount { get; set; }
    }
}

/// <summary>メールの確保で、RDBMS ごとに書き方が違う部分。</summary>
internal static class MailOutboxSql
{
    /// <summary>
    /// 送れる 1 通を <b>1 文で原子的に</b>確保する SQL。複数の送信ワーカーが同じ 1 通を取らない。
    /// 識別子は各 RDBMS の引用符で書いてある（<see cref="SqlDialect.Format"/> を通さなくてよい）。
    /// </summary>
    public static string ClaimPending(DatabaseProvider provider) => provider switch
    {
        DatabaseProvider.SqlServer =>
            "UPDATE TOP (1) [Core_MailOutbox] " +
            "SET [Status] = @SendingStatus, [LockedBy] = @LockedBy, [LockedUntil] = @LockedUntil " +
            "OUTPUT inserted.[MailId], inserted.[Kind], inserted.[SubjectId], inserted.[PayloadProtected], inserted.[RetryCount] " +
            "WHERE [Status] = @PendingStatus AND [NextAttemptAt] <= @Now",

        // 行ロックを取り、すでに取られている行は飛ばす（待たずに次の行へ）
        DatabaseProvider.PostgreSql =>
            "UPDATE \"Core_MailOutbox\" AS m " +
            "SET \"Status\" = @SendingStatus, \"LockedBy\" = @LockedBy, \"LockedUntil\" = @LockedUntil " +
            "WHERE m.\"MailId\" = (" +
            "  SELECT c.\"MailId\" FROM \"Core_MailOutbox\" AS c " +
            "  WHERE c.\"Status\" = @PendingStatus AND c.\"NextAttemptAt\" <= @Now " +
            "  ORDER BY c.\"NextAttemptAt\" FOR UPDATE SKIP LOCKED LIMIT 1) " +
            "RETURNING \"MailId\", \"Kind\", \"SubjectId\", \"PayloadProtected\", \"RetryCount\"",

        // SQLite は書き込みが直列なので、副問い合わせで 1 行に絞れば足りる
        DatabaseProvider.Sqlite =>
            "UPDATE \"Core_MailOutbox\" AS m " +
            "SET \"Status\" = @SendingStatus, \"LockedBy\" = @LockedBy, \"LockedUntil\" = @LockedUntil " +
            "WHERE m.rowid = (" +
            "  SELECT c.rowid FROM \"Core_MailOutbox\" AS c " +
            "  WHERE c.\"Status\" = @PendingStatus AND c.\"NextAttemptAt\" <= @Now " +
            "  ORDER BY c.\"NextAttemptAt\" LIMIT 1) " +
            "RETURNING \"MailId\", \"Kind\", \"SubjectId\", \"PayloadProtected\", \"RetryCount\"",

        // MySQL は UPDATE … ORDER BY … LIMIT で 1 行に絞る。RETURNING が無いので ReadClaimedForMySql で読み直す
        DatabaseProvider.MySql =>
            "UPDATE `Core_MailOutbox` " +
            "SET `Status` = @SendingStatus, `LockedBy` = @LockedBy, `LockedUntil` = @LockedUntil " +
            "WHERE `Status` = @PendingStatus AND `NextAttemptAt` <= @Now " +
            "ORDER BY `NextAttemptAt` LIMIT 1",

        _ => throw new NotSupportedException($"対応していない RDBMS: {provider}"),
    };

    /// <summary>MySQL で確保した行を、確保ごとに一意な <c>LockedBy</c> で読み直す。</summary>
    public const string ReadClaimedForMySql =
        "SELECT `MailId`, `Kind`, `SubjectId`, `PayloadProtected`, `RetryCount` FROM `Core_MailOutbox` WHERE `LockedBy` = @LockedBy";
}
