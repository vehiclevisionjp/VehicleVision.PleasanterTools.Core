using Dapper;

namespace VehicleVision.PleasanterTools.Core.Data;

/// <summary>管理者の招待 1 件。<b>トークンの現物は持たない</b>（SHA-256 だけ）。</summary>
/// <param name="InvitationId">識別子。</param>
/// <param name="AdminUserId">招かれた管理者。</param>
/// <param name="TokenHash">トークンの SHA-256（16 進 64 文字）。</param>
/// <param name="ExpiresAt">期限。</param>
/// <param name="UsedAt">使った時刻。<c>null</c> なら未使用。</param>
/// <param name="CreatedAt">作った時刻。読むときに入る（置くときは、ストアが時刻を入れる）。</param>
/// <param name="CreatedBy">招いた管理者。</param>
public sealed record AdminInvitation(
    Guid InvitationId,
    Guid AdminUserId,
    string TokenHash,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? UsedAt = null,
    DateTimeOffset? CreatedAt = null,
    Guid? CreatedBy = null);

/// <summary>管理者の招待の保管先。</summary>
public interface IAdminInvitationStore
{
    /// <summary>招待を置く。<b>その人の未使用の招待は先に消す</b>（出し直すと前の招待は死ぬ）。使用済みの行は、使われた事実として残す。</summary>
    Task ReplaceAsync(AdminInvitation invitation, CancellationToken cancellationToken = default);

    /// <summary>トークンのハッシュで引く。</summary>
    Task<AdminInvitation?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// 使い切りにして <c>true</c>。**未使用の行だけを更新し、更新できた件数で判断する**
    /// （読んでから書くと、同時に来た 2 つが両方通る）。
    /// </summary>
    Task<bool> TryConsumeAsync(Guid invitationId, CancellationToken cancellationToken = default);

    /// <summary>招待中（未使用で期限内）の管理者の ID。</summary>
    Task<IReadOnlyList<Guid>> ListPendingAdminUserIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>未使用の招待を取り消す。<b>止めた相手宛ての招待は取り消す</b>（止めたのにパスワードを決められては困る）。</summary>
    Task RevokeUnusedAsync(Guid adminUserId, CancellationToken cancellationToken = default);
}

/// <summary>Dapper を使った実装。表は <c>Core_AdminInvitations</c>（M10002）。</summary>
/// <param name="connections">接続のファクトリ。</param>
/// <param name="time">現在時刻。<b>試験で固定できるよう、注入する。</b></param>
public sealed class AdminInvitationStore(IDbConnectionFactory connections, TimeProvider time) : IAdminInvitationStore
{
    private string Sql(string sql) => SqlDialect.Format(connections.Provider, sql);

    private DateTime Now() => DbTime.ForDb(time.GetUtcNow().UtcDateTime);

    public async Task ReplaceAsync(AdminInvitation invitation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invitation);

        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // 未使用の分だけ消す。使用済みの行は、使われた事実として残す
        await connection.ExecuteAsync(new CommandDefinition(
            Sql("DELETE FROM [Core_AdminInvitations] WHERE [AdminUserId] = @AdminUserId AND [UsedAt] IS NULL"),
            new { invitation.AdminUserId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            Sql("INSERT INTO [Core_AdminInvitations] ([InvitationId], [AdminUserId], [TokenHash], [ExpiresAt], [CreatedAt], [CreatedBy]) "
                + "VALUES (@InvitationId, @AdminUserId, @TokenHash, @ExpiresAt, @Now, @CreatedBy)"),
            new
            {
                invitation.InvitationId,
                invitation.AdminUserId,
                invitation.TokenHash,
                ExpiresAt = DbTime.ForDb(invitation.ExpiresAt.UtcDateTime),
                Now = Now(),
                invitation.CreatedBy,
            },
            transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AdminInvitation?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        var row = await connection.QueryFirstOrDefaultAsync<Row>(new CommandDefinition(
            Sql("SELECT [InvitationId], [AdminUserId], [TokenHash], [ExpiresAt], [UsedAt], [CreatedAt], [CreatedBy] "
                + "FROM [Core_AdminInvitations] WHERE [TokenHash] = @TokenHash"),
            new { TokenHash = tokenHash }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return row is null
            ? null
            : new AdminInvitation(
                row.InvitationId, row.AdminUserId, row.TokenHash, DbTime.AsUtc(row.ExpiresAt),
                DbTime.AsUtc(row.UsedAt), DbTime.AsUtc(row.CreatedAt), row.CreatedBy);
    }

    public async Task<bool> TryConsumeAsync(Guid invitationId, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        return await connection.ExecuteAsync(new CommandDefinition(
            Sql("UPDATE [Core_AdminInvitations] SET [UsedAt] = @Now WHERE [InvitationId] = @InvitationId AND [UsedAt] IS NULL"),
            new { InvitationId = invitationId, Now = Now() },
            cancellationToken: cancellationToken)).ConfigureAwait(false) == 1;
    }

    public async Task<IReadOnlyList<Guid>> ListPendingAdminUserIdsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        return [.. await connection.QueryAsync<Guid>(new CommandDefinition(
            Sql("SELECT [AdminUserId] FROM [Core_AdminInvitations] WHERE [UsedAt] IS NULL AND [ExpiresAt] > @Now"),
            new { Now = Now() }, cancellationToken: cancellationToken)).ConfigureAwait(false)];
    }

    public async Task RevokeUnusedAsync(Guid adminUserId, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        await connection.ExecuteAsync(new CommandDefinition(
            Sql("DELETE FROM [Core_AdminInvitations] WHERE [AdminUserId] = @AdminUserId AND [UsedAt] IS NULL"),
            new { AdminUserId = adminUserId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private sealed class Row
    {
        public Guid InvitationId { get; set; }

        public Guid AdminUserId { get; set; }

        public string TokenHash { get; set; } = "";

        public DateTime ExpiresAt { get; set; }

        public DateTime? UsedAt { get; set; }

        public DateTime? CreatedAt { get; set; }

        public Guid? CreatedBy { get; set; }
    }
}
