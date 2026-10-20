using System.Security.Cryptography;
using System.Text;
using Dapper;

namespace VehicleVision.PleasanterTools.Core.Data;

/// <summary>サーバ側セッションの段階の名前。<b>DB へ文字列で入る。</b>アプリは、必要ならこれ以外の段階を足せる。</summary>
public static class AdminSessionKinds
{
    /// <summary>パスワードは通ったが、2 要素がまだ。<b>何も操作させない。</b>（短い寿命）</summary>
    public const string Pending = "Pending";

    /// <summary>ログイン済み。</summary>
    public const string Session = "Session";
}

/// <summary>セッションに入った方式（表示用。認可には使わない）。</summary>
public static class SessionVia
{
    public const string Password = "password";
    public const string Saml = "saml";
    public const string Pleasanter = "pleasanter";
}

/// <summary>ログイン中の端末 1 つ分（サーバ側セッション）。</summary>
/// <param name="Handle">端末を指す識別子（セッション ID の SHA-256。cookie の現物ではない）。</param>
/// <param name="CreatedAt">作った時刻。</param>
/// <param name="ExpiresAt">期限。</param>
/// <param name="IpAddress">送信元。</param>
/// <param name="UserAgent">User-Agent。</param>
/// <param name="Via">入った方式（<see cref="SessionVia"/>）。</param>
public sealed record AdminSessionInfo(
    string Handle,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string? IpAddress,
    string? UserAgent,
    string Via = SessionVia.Password);

/// <summary>SAML で入ったセッションが持つ、単一ログアウトに要る値（IdP が付けた NameID とセッション索引）。</summary>
public sealed record SamlSessionKeys(string NameId, string? SessionIndex);

/// <summary>Pleasanter のログインで入ったセッションが持つ、再検証に要る値（Pleasanter のテナント・利用者と、最後に確かめた時刻）。</summary>
public sealed record PleasanterSsoKeys(int TenantId, int UserId, DateTimeOffset VerifiedAt);

/// <summary>外部のログインで入ったセッションが持つ値。2 要素の途中からログイン後へ<b>そのまま引き継ぐ</b>。</summary>
public sealed record SessionSso(SamlSessionKeys? Saml = null, PleasanterSsoKeys? Pleasanter = null);

/// <summary>サーバ側のセッション（DB には ID のハッシュだけを持つ）。</summary>
/// <param name="UserId">持ち主。</param>
/// <param name="Kind">段階（<see cref="AdminSessionKinds"/>）。</param>
/// <param name="ExpiresAt">期限。</param>
/// <param name="Pleasanter">Pleasanter のログインで入ったなら、その値。</param>
public sealed record AdminSession(
    Guid UserId, string Kind, DateTimeOffset ExpiresAt, PleasanterSsoKeys? Pleasanter = null);

/// <summary>サーバ側セッションの保管先。</summary>
public interface IAdminSessionStore
{
    /// <summary>セッションを作り、cookie に載せる ID を返す。**ID は推測できない 256 bit。DB には SHA-256 だけを保管する。**</summary>
    Task<string> CreateAsync(
        Guid userId,
        string kind,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default,
        string? ipAddress = null,
        string? userAgent = null,
        SessionSso? sso = null);

    /// <summary>外部のログイン（SAML・Pleasanter）で入ったセッションが持つ値。どちらでもなければ <c>null</c>。</summary>
    Task<SessionSso?> GetSsoAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Pleasanter に最後に確かめた時刻を更新する。</summary>
    Task TouchPleasanterVerifiedAsync(string sessionId, DateTimeOffset verifiedAt, CancellationToken cancellationToken = default);

    /// <summary>SAML で入ったセッションの単一ログアウトの鍵。SAML で入っていない・無いセッションは <c>null</c>。</summary>
    Task<SamlSessionKeys?> GetSamlKeysAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// IdP からの単一ログアウトで、その NameID（とセッション索引）のセッションをすべて捨てる。捨てた件数を返す。
    /// IdP からの他サイト POST では cookie が届かないので、cookie ではなく鍵で探す。
    /// </summary>
    Task<int> DeleteBySamlKeysAsync(string nameId, string? sessionIndex, CancellationToken cancellationToken = default);

    /// <summary>セッション ID から端末を指す識別子を作る（一覧に出してよい値。cookie の現物は作れない）。</summary>
    string HandleOf(string sessionId);

    /// <summary>ログイン中（<c>Session</c>・期限内）の端末の一覧。新しい順。</summary>
    Task<IReadOnlyList<AdminSessionInfo>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>端末を 1 つ失効させる。<b>持ち主の組で消す</b>（他人の端末の識別子を渡されても消えない）。</summary>
    Task<bool> DeleteByHandleAsync(Guid userId, string handle, CancellationToken cancellationToken = default);

    /// <summary>ID からセッションを引く。無い・期限切れなら <c>null</c>。</summary>
    Task<AdminSession?> FindAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>セッションを消す。</summary>
    Task DeleteAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 利用者のセッションを消す。<paramref name="exceptSessionId"/> があればそれだけ残す
    /// （パスワード変更で、いまの端末以外を失効させる）。
    /// </summary>
    Task DeleteForUserAsync(Guid userId, string? exceptSessionId = null, CancellationToken cancellationToken = default);

    /// <summary>期限切れを消す。</summary>
    Task<int> DeleteExpiredAsync(CancellationToken cancellationToken = default);
}

/// <summary>サーバ側セッションを RDBMS へ保管する。表は <c>Core_AdminSessions</c>（M10007）。</summary>
/// <remarks>
/// cookie に載せるのは**推測できない 256 bit の ID** だけで、DB には <b>SHA-256 だけ</b>を保管する
/// （DB が漏れても、そこから有効な cookie は作れない）。
/// </remarks>
/// <param name="connections">接続のファクトリ。</param>
/// <param name="time">現在時刻。<b>試験で固定できるよう、注入する。</b></param>
public sealed class AdminSessionStore(IDbConnectionFactory connections, TimeProvider time) : IAdminSessionStore
{
    private string Sql(string sql) => SqlDialect.Format(connections.Provider, sql);

    private DateTime Now() => DbTime.ForDb(time.GetUtcNow().UtcDateTime);

    private static string IdHash(string sessionId) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sessionId)));

    public async Task<string> CreateAsync(
        Guid userId,
        string kind,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default,
        string? ipAddress = null,
        string? userAgent = null,
        SessionSso? sso = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);

        var now = time.GetUtcNow();
        var samlKeys = sso?.Saml;
        var pleasanter = sso?.Pleasanter;
        var sessionId = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        await using var connection = connections.Create();
        await connection.ExecuteAsync(new CommandDefinition(
            Sql("INSERT INTO [Core_AdminSessions] ([Id], [UserId], [Kind], [CreatedAt], [ExpiresAt], [IpAddress], [UserAgent], "
                + "[SamlNameId], [SamlSessionIndex], [PleasanterTenantId], [PleasanterUserId], [PleasanterVerifiedAt]) "
                + "VALUES (@Id, @UserId, @Kind, @CreatedAt, @ExpiresAt, @IpAddress, @UserAgent, @SamlNameId, @SamlSessionIndex, "
                + "@PleasanterTenantId, @PleasanterUserId, @PleasanterVerifiedAt)"),
            new
            {
                Id = IdHash(sessionId),
                UserId = userId,
                Kind = kind,
                CreatedAt = DbTime.ForDb(now.UtcDateTime),
                ExpiresAt = DbTime.ForDb((now + lifetime).UtcDateTime),
                IpAddress = AuditText.Safe(ipAddress, 64),
                UserAgent = AuditText.Safe(userAgent, 512),
                SamlNameId = AuditText.Safe(samlKeys?.NameId, 256),
                SamlSessionIndex = AuditText.Safe(samlKeys?.SessionIndex, 256),
                PleasanterTenantId = pleasanter?.TenantId,
                PleasanterUserId = pleasanter?.UserId,
                PleasanterVerifiedAt = pleasanter is null ? null : (DateTime?)DbTime.ForDb(pleasanter.VerifiedAt.UtcDateTime),
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return sessionId;
    }

    public string HandleOf(string sessionId) => IdHash(sessionId);

    public async Task<int> DeleteBySamlKeysAsync(string nameId, string? sessionIndex, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        return await connection.ExecuteAsync(new CommandDefinition(
            string.IsNullOrEmpty(sessionIndex)
                ? Sql("DELETE FROM [Core_AdminSessions] WHERE [SamlNameId] = @NameId")
                : Sql("DELETE FROM [Core_AdminSessions] WHERE [SamlNameId] = @NameId AND [SamlSessionIndex] = @SessionIndex"),
            new { NameId = nameId, SessionIndex = sessionIndex },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<SessionSso?> GetSsoAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        var row = await connection.QuerySingleOrDefaultAsync<SsoRow>(
            new CommandDefinition(
                Sql("SELECT [SamlNameId] AS [NameId], [SamlSessionIndex] AS [SessionIndex], [PleasanterTenantId] AS [TenantId], "
                    + "[PleasanterUserId] AS [UserId], [PleasanterVerifiedAt] AS [VerifiedAt] "
                    + "FROM [Core_AdminSessions] WHERE [Id] = @Id"),
                new { Id = IdHash(sessionId) }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        var saml = row.NameId is { Length: > 0 } nameId ? new SamlSessionKeys(nameId, row.SessionIndex) : null;
        var pleasanter = ToPleasanterKeys(row.TenantId, row.UserId, row.VerifiedAt);
        return saml is null && pleasanter is null ? null : new SessionSso(saml, pleasanter);
    }

    private static PleasanterSsoKeys? ToPleasanterKeys(int? tenantId, int? userId, DateTime? verifiedAt) =>
        tenantId is { } t && userId is { } u && DbTime.AsUtc(verifiedAt) is { } v ? new PleasanterSsoKeys(t, u, v) : null;

    public async Task TouchPleasanterVerifiedAsync(string sessionId, DateTimeOffset verifiedAt, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        await connection.ExecuteAsync(new CommandDefinition(
            Sql("UPDATE [Core_AdminSessions] SET [PleasanterVerifiedAt] = @VerifiedAt WHERE [Id] = @Id"),
            new { Id = IdHash(sessionId), VerifiedAt = DbTime.ForDb(verifiedAt.UtcDateTime) },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<SamlSessionKeys?> GetSamlKeysAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        var row = await connection.QuerySingleOrDefaultAsync<SsoRow>(new CommandDefinition(
            Sql("SELECT [SamlNameId] AS [NameId], [SamlSessionIndex] AS [SessionIndex] FROM [Core_AdminSessions] WHERE [Id] = @Id"),
            new { Id = IdHash(sessionId) }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return row is { NameId: { Length: > 0 } nameId } ? new SamlSessionKeys(nameId, row.SessionIndex) : null;
    }

    public async Task<IReadOnlyList<AdminSessionInfo>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        var rows = await connection.QueryAsync<InfoRow>(
            new CommandDefinition(
                Sql("SELECT [Id], [CreatedAt], [ExpiresAt], [IpAddress], [UserAgent], [SamlNameId], [PleasanterUserId] FROM [Core_AdminSessions] "
                    + "WHERE [UserId] = @UserId AND [Kind] = @Kind AND [ExpiresAt] > @Now ORDER BY [CreatedAt] DESC, [Id] DESC"),
                new { UserId = userId, Kind = AdminSessionKinds.Session, Now = Now() },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        return [.. rows.Select(r => new AdminSessionInfo(
            r.Id, DbTime.AsUtc(r.CreatedAt), DbTime.AsUtc(r.ExpiresAt), r.IpAddress, r.UserAgent,
            !string.IsNullOrEmpty(r.SamlNameId) ? SessionVia.Saml : r.PleasanterUserId is not null ? SessionVia.Pleasanter : SessionVia.Password))];
    }

    public async Task<bool> DeleteByHandleAsync(Guid userId, string handle, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        return await connection.ExecuteAsync(new CommandDefinition(
            Sql("DELETE FROM [Core_AdminSessions] WHERE [Id] = @Id AND [UserId] = @UserId"),
            new { Id = handle, UserId = userId }, cancellationToken: cancellationToken)).ConfigureAwait(false) > 0;
    }

    public async Task<AdminSession?> FindAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        var row = await connection.QuerySingleOrDefaultAsync<FindRow>(
            new CommandDefinition(
                Sql("SELECT [UserId], [Kind], [ExpiresAt], [PleasanterTenantId] AS [TenantId], [PleasanterUserId], "
                    + "[PleasanterVerifiedAt] AS [VerifiedAt] FROM [Core_AdminSessions] WHERE [Id] = @Id AND [ExpiresAt] > @Now"),
                new { Id = IdHash(sessionId), Now = Now() },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return row is null
            ? null
            : new AdminSession(row.UserId, row.Kind, DbTime.AsUtc(row.ExpiresAt), ToPleasanterKeys(row.TenantId, row.PleasanterUserId, row.VerifiedAt));
    }

    public async Task DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        await connection.ExecuteAsync(new CommandDefinition(
            Sql("DELETE FROM [Core_AdminSessions] WHERE [Id] = @Id"), new { Id = IdHash(sessionId) },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task DeleteForUserAsync(Guid userId, string? exceptSessionId = null, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        await connection.ExecuteAsync(new CommandDefinition(
            exceptSessionId is null
                ? Sql("DELETE FROM [Core_AdminSessions] WHERE [UserId] = @UserId")
                : Sql("DELETE FROM [Core_AdminSessions] WHERE [UserId] = @UserId AND [Id] <> @Except"),
            new { UserId = userId, Except = exceptSessionId is null ? null : IdHash(exceptSessionId) },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<int> DeleteExpiredAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        return await connection.ExecuteAsync(new CommandDefinition(
            Sql("DELETE FROM [Core_AdminSessions] WHERE [ExpiresAt] <= @Now"), new { Now = Now() },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private sealed class SsoRow
    {
        public string? NameId { get; set; }

        public string? SessionIndex { get; set; }

        public int? TenantId { get; set; }

        public int? UserId { get; set; }

        public DateTime? VerifiedAt { get; set; }
    }

    private sealed class InfoRow
    {
        public string Id { get; set; } = "";

        public DateTime CreatedAt { get; set; }

        public DateTime ExpiresAt { get; set; }

        public string? IpAddress { get; set; }

        public string? UserAgent { get; set; }

        public string? SamlNameId { get; set; }

        public int? PleasanterUserId { get; set; }
    }

    private sealed class FindRow
    {
        public Guid UserId { get; set; }

        public string Kind { get; set; } = "";

        public DateTime ExpiresAt { get; set; }

        public int? TenantId { get; set; }

        public int? PleasanterUserId { get; set; }

        public DateTime? VerifiedAt { get; set; }
    }
}
