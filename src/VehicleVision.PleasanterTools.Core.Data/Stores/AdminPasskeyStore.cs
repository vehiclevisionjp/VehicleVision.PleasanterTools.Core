using System.Data.Common;
using System.Security.Cryptography;
using Dapper;

namespace VehicleVision.PleasanterTools.Core.Data;

/// <summary>登録済みのパスキー（資格情報）1 件。</summary>
/// <param name="Id">行の識別子（画面・API で指す値。資格情報の ID とは別）。</param>
/// <param name="UserId">持ち主（<c>Core_AdminUsers.Id</c>）。</param>
/// <param name="CredentialId">資格情報の ID（生のバイト列）。</param>
/// <param name="PublicKey">公開鍵（COSE）。<b>監査ログ・ログへ出さない。</b></param>
/// <param name="SignCount">署名カウンター。</param>
/// <param name="AaGuid">認証器の種類の識別子（不明なら全 0）。</param>
/// <param name="Transports">登録時に得た転送手段（<c>internal</c>・<c>hybrid</c> など）。</param>
/// <param name="BackupEligible">同期できる資格情報か（BE）。登録時に決まり、変わらない。</param>
/// <param name="BackedUp">いま同期されているか（BS）。</param>
/// <param name="Name">本人が付けた名前。</param>
/// <param name="CreatedAt">登録した時刻。</param>
/// <param name="LastUsedAt">最後に認証に使った時刻。</param>
public sealed record AdminPasskey(
    Guid Id,
    Guid UserId,
    byte[] CredentialId,
    byte[] PublicKey,
    long SignCount,
    Guid AaGuid,
    IReadOnlyList<string> Transports,
    bool BackupEligible,
    bool BackedUp,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt);

/// <summary>パスキーを 1 件足すときの値。</summary>
/// <param name="UserId">持ち主。</param>
/// <param name="CredentialId">資格情報の ID（生のバイト列。1〜1023 バイト）。</param>
/// <param name="PublicKey">公開鍵（COSE。1〜<see cref="AdminPasskeyStore.MaxPublicKeyBytes"/> バイト）。</param>
/// <param name="SignCount">登録時の署名カウンター。</param>
/// <param name="AaGuid">認証器の種類の識別子。</param>
/// <param name="Transports">転送手段。</param>
/// <param name="BackupEligible">BE。</param>
/// <param name="BackedUp">BS。</param>
/// <param name="Name">本人が付ける名前（1〜<see cref="AdminPasskeyStore.MaxNameLength"/> 文字。前後の空白は落とす）。</param>
public sealed record NewAdminPasskey(
    Guid UserId,
    byte[] CredentialId,
    byte[] PublicKey,
    long SignCount,
    Guid AaGuid,
    IReadOnlyList<string> Transports,
    bool BackupEligible,
    bool BackedUp,
    string Name);

/// <summary>パスキー（資格情報）の保管先。</summary>
/// <remarks>
/// 一覧・登録・名前の変更・削除は、<b>持ち主の組で操作する</b>（他人の行の識別子を渡されても動かない）。
/// ログイン時の引き当て（<see cref="FindByCredentialIdAsync"/>）だけが、持ち主を知らずに引く（ディスコバラブル資格情報）。
/// </remarks>
public interface IAdminPasskeyStore
{
    /// <summary>持ち主のパスキーの一覧。登録の古い順。</summary>
    Task<IReadOnlyList<AdminPasskey>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 持ち主のパスキーの件数。<b>最後のログイン方法を消さない確認</b>と、1 人あたりの上限の確認に使う。
    /// </summary>
    Task<int> CountForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>資格情報の ID から引く（ログイン）。無ければ <c>null</c>。</summary>
    Task<AdminPasskey?> FindByCredentialIdAsync(byte[] credentialId, CancellationToken cancellationToken = default);

    /// <summary>
    /// パスキーを足し、行の識別子を返す。<b>同じ資格情報の ID が既にあれば、足さずに <c>null</c></b>
    /// （1 つの資格情報を 2 人に結び付けない）。
    /// </summary>
    Task<Guid?> AddAsync(NewAdminPasskey passkey, CancellationToken cancellationToken = default);

    /// <summary>名前を変える。<b>持ち主の組で探す。</b>変えたら <c>true</c>。</summary>
    Task<bool> RenameAsync(Guid userId, Guid id, string name, CancellationToken cancellationToken = default);

    /// <summary>1 件消す。<b>持ち主の組で探す。</b>消したら <c>true</c>。</summary>
    Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken cancellationToken = default);

    /// <summary>持ち主の分をすべて消す（管理者による復旧・利用者の削除）。消した件数を返す。</summary>
    Task<int> DeleteForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 認証に成功したときに、署名カウンター・BS・最後に使った時刻を更新する。
    /// <paramref name="previousSignCount"/> が<b>いまの値と同じときだけ</b>更新する（同じ応答を同時に 2 回通さない）。
    /// 更新できたら <c>true</c>。
    /// </summary>
    Task<bool> RecordSignInAsync(
        Guid id, long previousSignCount, long newSignCount, bool backedUp, CancellationToken cancellationToken = default);
}

/// <summary>パスキーを RDBMS へ保管する。表は <c>Core_AdminPasskeys</c>（M10008）。</summary>
/// <remarks>
/// 資格情報の ID の一意性は、<b>SHA-256 の <c>CredentialIdHash</c> の一意索引</b>で守る
/// （ID が長く、索引のキー長に収まらない恐れがあるため。M10008）。
/// </remarks>
/// <param name="connections">接続のファクトリ。</param>
/// <param name="time">現在時刻。<b>試験で固定できるよう、注入する。</b></param>
public sealed class AdminPasskeyStore(IDbConnectionFactory connections, TimeProvider time) : IAdminPasskeyStore
{
    /// <summary>資格情報の ID の最大バイト数（WebAuthn の上限）。</summary>
    public const int MaxCredentialIdBytes = 1023;

    /// <summary>公開鍵の最大バイト数（列の桁）。</summary>
    public const int MaxPublicKeyBytes = 2048;

    /// <summary>名前の最大文字数（列の桁）。</summary>
    public const int MaxNameLength = 100;

    private const string Columns =
        "[Id], [UserId], [CredentialId], [PublicKey], [SignCount], [AaGuid], [Transports], [BackupEligible], [BackedUp], "
        + "[Name], [CreatedAt], [LastUsedAt]";

    private string Sql(string sql) => SqlDialect.Format(connections.Provider, sql);

    private DateTime Now() => DbTime.ForDb(time.GetUtcNow().UtcDateTime);

    /// <summary>資格情報の ID から、一意索引に載せるハッシュ（小文字 16 進 64 文字）を作る。</summary>
    internal static string CredentialIdHash(byte[] credentialId) =>
        Convert.ToHexStringLower(SHA256.HashData(credentialId));

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
        return Convert.FromBase64String(padded);
    }

    private static string CleanName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var trimmed = name.Trim();
        if (trimmed.Length is 0 or > MaxNameLength)
        {
            throw new ArgumentException($"The name must be 1 to {MaxNameLength} characters.", nameof(name));
        }

        return trimmed;
    }

    public async Task<IReadOnlyList<AdminPasskey>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            Sql($"SELECT {Columns} FROM [Core_AdminPasskeys] WHERE [UserId] = @UserId ORDER BY [CreatedAt], [Id]"),
            new { UserId = userId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return [.. rows.Select(ToModel)];
    }

    public async Task<int> CountForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        return await connection.QuerySingleAsync<int>(new CommandDefinition(
            Sql("SELECT COUNT(*) FROM [Core_AdminPasskeys] WHERE [UserId] = @UserId"),
            new { UserId = userId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<AdminPasskey?> FindByCredentialIdAsync(byte[] credentialId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentialId);

        await using var connection = connections.Create();
        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            Sql($"SELECT {Columns} FROM [Core_AdminPasskeys] WHERE [CredentialIdHash] = @Hash"),
            new { Hash = CredentialIdHash(credentialId) }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return row is null ? null : ToModel(row);
    }

    public async Task<Guid?> AddAsync(NewAdminPasskey passkey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(passkey);
        if (passkey.CredentialId.Length is 0 or > MaxCredentialIdBytes)
        {
            throw new ArgumentException($"The credential ID must be 1 to {MaxCredentialIdBytes} bytes.", nameof(passkey));
        }

        if (passkey.PublicKey.Length is 0 or > MaxPublicKeyBytes)
        {
            throw new ArgumentException($"The public key must be 1 to {MaxPublicKeyBytes} bytes.", nameof(passkey));
        }

        var name = CleanName(passkey.Name);
        var id = Guid.NewGuid();
        var hash = CredentialIdHash(passkey.CredentialId);

        await using var connection = connections.Create();
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                Sql("INSERT INTO [Core_AdminPasskeys] ([Id], [UserId], [CredentialId], [CredentialIdHash], [PublicKey], [SignCount], "
                    + "[AaGuid], [Transports], [BackupEligible], [BackedUp], [Name], [CreatedAt]) "
                    + "VALUES (@Id, @UserId, @CredentialId, @CredentialIdHash, @PublicKey, @SignCount, "
                    + "@AaGuid, @Transports, @BackupEligible, @BackedUp, @Name, @CreatedAt)"),
                new
                {
                    Id = id,
                    passkey.UserId,
                    CredentialId = ToBase64Url(passkey.CredentialId),
                    CredentialIdHash = hash,
                    passkey.PublicKey,
                    passkey.SignCount,
                    passkey.AaGuid,
                    Transports = passkey.Transports.Count == 0 ? null : string.Join(',', passkey.Transports),
                    BackupEligible = passkey.BackupEligible ? 1 : 0,
                    BackedUp = passkey.BackedUp ? 1 : 0,
                    Name = name,
                    CreatedAt = Now(),
                },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            return id;
        }
        catch (DbException)
        {
            // 一意索引の違反（同じ資格情報の ID が既にある）なら null。ほかの失敗は、そのまま投げる
            var exists = await connection.QuerySingleAsync<int>(new CommandDefinition(
                Sql("SELECT COUNT(*) FROM [Core_AdminPasskeys] WHERE [CredentialIdHash] = @Hash"),
                new { Hash = hash }, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (exists > 0)
            {
                return null;
            }

            throw;
        }
    }

    public async Task<bool> RenameAsync(Guid userId, Guid id, string name, CancellationToken cancellationToken = default)
    {
        var clean = CleanName(name);

        await using var connection = connections.Create();
        return await connection.ExecuteAsync(new CommandDefinition(
            Sql("UPDATE [Core_AdminPasskeys] SET [Name] = @Name WHERE [Id] = @Id AND [UserId] = @UserId"),
            new { Id = id, UserId = userId, Name = clean }, cancellationToken: cancellationToken)).ConfigureAwait(false) > 0;
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        return await connection.ExecuteAsync(new CommandDefinition(
            Sql("DELETE FROM [Core_AdminPasskeys] WHERE [Id] = @Id AND [UserId] = @UserId"),
            new { Id = id, UserId = userId }, cancellationToken: cancellationToken)).ConfigureAwait(false) > 0;
    }

    public async Task<int> DeleteForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        return await connection.ExecuteAsync(new CommandDefinition(
            Sql("DELETE FROM [Core_AdminPasskeys] WHERE [UserId] = @UserId"),
            new { UserId = userId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<bool> RecordSignInAsync(
        Guid id, long previousSignCount, long newSignCount, bool backedUp, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        return await connection.ExecuteAsync(new CommandDefinition(
            Sql("UPDATE [Core_AdminPasskeys] SET [SignCount] = @NewSignCount, [BackedUp] = @BackedUp, [LastUsedAt] = @Now "
                + "WHERE [Id] = @Id AND [SignCount] = @PreviousSignCount"),
            new
            {
                Id = id,
                PreviousSignCount = previousSignCount,
                NewSignCount = newSignCount,
                BackedUp = backedUp ? 1 : 0,
                Now = Now(),
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false) > 0;
    }

    private static AdminPasskey ToModel(Row row) => new(
        row.Id,
        row.UserId,
        FromBase64Url(row.CredentialId),
        row.PublicKey,
        row.SignCount,
        row.AaGuid,
        string.IsNullOrEmpty(row.Transports) ? [] : row.Transports.Split(',', StringSplitOptions.RemoveEmptyEntries),
        row.BackupEligible != 0,
        row.BackedUp != 0,
        row.Name,
        DbTime.AsUtc(row.CreatedAt),
        DbTime.AsUtc(row.LastUsedAt));

    private sealed class Row
    {
        public Guid Id { get; set; }

        public Guid UserId { get; set; }

        public string CredentialId { get; set; } = "";

        public byte[] PublicKey { get; set; } = [];

        public long SignCount { get; set; }

        public Guid AaGuid { get; set; }

        public string? Transports { get; set; }

        public int BackupEligible { get; set; }

        public int BackedUp { get; set; }

        public string Name { get; set; } = "";

        public DateTime CreatedAt { get; set; }

        public DateTime? LastUsedAt { get; set; }
    }
}
