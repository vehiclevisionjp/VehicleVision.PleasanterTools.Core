using Dapper;

namespace VehicleVision.PleasanterTools.Core.Data;

/// <summary>残す操作 1 件。</summary>
/// <remarks>
/// **パスワード・API キー・トークン・復旧コード・TOTP の数字・回答本文を入れない。** 入れないことを設計で担保する:
/// 要求本文は渡さず、呼び出し側が「何を・誰が・どの対象か」だけを渡す。
/// </remarks>
/// <param name="OccurredAt">起きた時刻。</param>
/// <param name="Action">操作の名前（アプリが決める。画面・集計の絞り込みに使うので、名前を変えると過去の記録と食い違う）。</param>
/// <param name="Succeeded">成功したか。<b>失敗も残す</b>（試みられたことが分からなくなる）。</param>
/// <param name="StatusCode">結果の HTTP 状態。HTTP を介さない操作では <c>null</c>。</param>
/// <param name="ActorKey">誰が。管理者の ID、または外部ユーザの鍵。認証を通っていない試みでは <c>null</c>。</param>
/// <param name="ActorLoginId">ログイン ID。居ない ID での試みも残るよう、利用者の行とは別に持つ。</param>
/// <param name="TargetType">対象の種類。</param>
/// <param name="TargetId">対象の識別子。</param>
/// <param name="DetailJson">補足（失敗の理由など）。<b>資格情報を入れない。</b></param>
/// <param name="IpAddress">送信元。</param>
public sealed record AuditEntry(
    DateTimeOffset OccurredAt,
    string Action,
    bool Succeeded,
    int? StatusCode = null,
    string? ActorKey = null,
    string? ActorLoginId = null,
    string? TargetType = null,
    string? TargetId = null,
    string? DetailJson = null,
    string? IpAddress = null);

/// <summary>記録を読むときの絞り込み。既定は「直近を新しい順に」。</summary>
public sealed record AuditLogQuery
{
    /// <summary>読む件数の上限の最大。</summary>
    public const int MaxLimit = 500;

    /// <summary>読む件数の上限（1〜<see cref="MaxLimit"/>）。</summary>
    public int Limit { get; init; } = 100;

    /// <summary>
    /// 読み飛ばす件数（ページ送り。前のページまでに読んだ件数を渡す）。
    /// **時刻では区切らない。** DB へ渡す時刻は秒未満を切り捨てるので、同じ秒の記録が複数あり、時刻で区切ると取りこぼす。
    /// </summary>
    public int Offset { get; init; }

    /// <summary>この時刻以降。</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>この時刻より前。</summary>
    public DateTimeOffset? To { get; init; }

    /// <summary>この実行者の操作だけ。</summary>
    public string? ActorKey { get; init; }

    /// <summary>この操作だけ（完全一致）。</summary>
    public string? Action { get; init; }

    /// <summary>ログイン ID に含まれる文字（部分一致。大文字小文字を区別しない）。</summary>
    public string? ActorLoginContains { get; init; }

    /// <summary>操作の名前に含まれる文字（部分一致。大文字小文字を区別しない）。</summary>
    public string? ActionContains { get; init; }

    /// <summary>失敗だけ。</summary>
    public bool FailedOnly { get; init; }
}

/// <summary>管理操作の記録の保管先。</summary>
public interface IAuditLogStore
{
    /// <summary>記録を書く。<b>列の桁より長い値は、溢れさせずに切り詰める</b>（桁溢れで例外にすると、操作は通ったのに記録だけ落ちる）。</summary>
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default);

    /// <summary>新しい順に読む。</summary>
    Task<IReadOnlyList<AuditEntry>> ListAsync(AuditLogQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定した時刻より古い記録を消す。消した件数を返す。
    /// <b>消さないと増え続ける</b>（1 操作 1 行。画面から読むたびに、大きくなった表を走査することになる）。
    /// </summary>
    Task<int> DeleteOlderThanAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default);
}

/// <summary>記録へ入れる文字列を安全にする（LogSafe 相当）。</summary>
public static class AuditText
{
    /// <summary>制御文字（改行を含む）を落とし、長さを切り詰める。ログの偽装（行の差し込み）を防ぐ。</summary>
    public static string? Safe(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var cleaned = string.Concat(value.Where(c => !char.IsControl(c)));
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
    }
}

/// <summary>Dapper を使った実装。表は <c>Core_AuditLogs</c>（M10005）。</summary>
/// <remarks>
/// 文字列は <see cref="AuditText.Safe"/> を通す（制御文字を落とし、桁へ切り詰める）。
/// <b>補足（<c>DetailJson</c>）だけは、制御文字を落とさず、JSON をそのまま持つ</b>（改行は JSON の中でエスケープされている）。
/// 値はすべてパラメータで渡す。
/// </remarks>
public sealed class AuditLogStore(IDbConnectionFactory connections) : IAuditLogStore
{
    private string Sql(string sql) => SqlDialect.Format(connections.Provider, sql);

    public async Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await using var connection = connections.Create();
        await connection.ExecuteAsync(new CommandDefinition(
            Sql("INSERT INTO [Core_AuditLogs] ([AuditLogId], [OccurredAt], [ActorKey], [ActorLoginId], [Action], [Succeeded], "
                + "[StatusCode], [TargetType], [TargetId], [DetailJson], [IpAddress]) "
                + "VALUES (@AuditLogId, @OccurredAt, @ActorKey, @ActorLoginId, @Action, @Succeeded, "
                + "@StatusCode, @TargetType, @TargetId, @DetailJson, @IpAddress)"),
            new
            {
                AuditLogId = Guid.NewGuid(),
                OccurredAt = DbTime.ForDb(entry.OccurredAt.UtcDateTime),
                ActorKey = AuditText.Safe(entry.ActorKey, 64),
                ActorLoginId = AuditText.Safe(entry.ActorLoginId, 256),
                Action = AuditText.Safe(entry.Action, 128) ?? string.Empty,
                Succeeded = entry.Succeeded ? 1 : 0,
                entry.StatusCode,
                TargetType = AuditText.Safe(entry.TargetType, 128),
                TargetId = AuditText.Safe(entry.TargetId, 512),
                entry.DetailJson,
                IpAddress = AuditText.Safe(entry.IpAddress, 64),
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AuditEntry>> ListAsync(
        AuditLogQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegative(query.Offset);

        // **組み立てるのは条件の「形」だけで、値は必ず引数で渡す。** 形は下に並んでいる定数しか入らない
        var conditions = new List<string>();
        var parameters = new DynamicParameters();
        parameters.Add("Limit", Math.Clamp(query.Limit, 1, AuditLogQuery.MaxLimit));
        parameters.Add("Offset", query.Offset);

        if (query.From is { } from)
        {
            conditions.Add("[OccurredAt] >= @From");
            parameters.Add("From", DbTime.ForDb(from.UtcDateTime));
        }

        if (query.To is { } to)
        {
            conditions.Add("[OccurredAt] < @To");
            parameters.Add("To", DbTime.ForDb(to.UtcDateTime));
        }

        if (!string.IsNullOrEmpty(query.ActorKey))
        {
            conditions.Add("[ActorKey] = @ActorKey");
            parameters.Add("ActorKey", query.ActorKey);
        }

        if (!string.IsNullOrEmpty(query.Action))
        {
            conditions.Add("[Action] = @Action");
            parameters.Add("Action", query.Action);
        }

        // **大文字小文字を区別しない**（PostgreSQL の LIKE は区別するので、両辺を小文字にして揃える）
        if (!string.IsNullOrWhiteSpace(query.ActorLoginContains))
        {
            conditions.Add($"LOWER([ActorLoginId]) LIKE @ActorLoginLike ESCAPE '{SqlDialect.LikeEscape}'");
            parameters.Add("ActorLoginLike", $"%{SqlDialect.EscapeLike(query.ActorLoginContains.Trim().ToLowerInvariant())}%");
        }

        if (!string.IsNullOrWhiteSpace(query.ActionContains))
        {
            conditions.Add($"LOWER([Action]) LIKE @ActionLike ESCAPE '{SqlDialect.LikeEscape}'");
            parameters.Add("ActionLike", $"%{SqlDialect.EscapeLike(query.ActionContains.Trim().ToLowerInvariant())}%");
        }

        if (query.FailedOnly)
        {
            conditions.Add("[Succeeded] = 0");
        }

        var sql = "SELECT [OccurredAt], [ActorKey], [ActorLoginId], [Action], [Succeeded], [StatusCode], "
            + "[TargetType], [TargetId], [DetailJson], [IpAddress] FROM [Core_AuditLogs]"
            + (conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : string.Empty)
            // **同じ秒の記録の並びを定める**（秒未満を切り捨てて持つので、時刻だけでは並びが定まらない）
            + " ORDER BY [OccurredAt] DESC, [AuditLogId] DESC "
            + SqlDialect.Page(connections.Provider);

        await using var connection = connections.Create();
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            Sql(sql), parameters, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return [.. rows.Select(r => new AuditEntry(
            DbTime.AsUtc(r.OccurredAt), r.Action, r.Succeeded != 0, r.StatusCode,
            r.ActorKey, r.ActorLoginId, r.TargetType, r.TargetId, r.DetailJson, r.IpAddress))];
    }

    public async Task<int> DeleteOlderThanAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        return await connection.ExecuteAsync(new CommandDefinition(
            Sql("DELETE FROM [Core_AuditLogs] WHERE [OccurredAt] < @Threshold"),
            new { Threshold = DbTime.ForDb(threshold.UtcDateTime) },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private sealed class Row
    {
        public DateTime OccurredAt { get; set; }

        public string? ActorKey { get; set; }

        public string? ActorLoginId { get; set; }

        public string Action { get; set; } = string.Empty;

        public int Succeeded { get; set; }

        public int? StatusCode { get; set; }

        public string? TargetType { get; set; }

        public string? TargetId { get; set; }

        public string? DetailJson { get; set; }

        public string? IpAddress { get; set; }
    }
}
