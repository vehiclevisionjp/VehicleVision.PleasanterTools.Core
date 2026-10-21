using System.Data.Common;
using Dapper;

namespace VehicleVision.PleasanterTools.Core.Data;

/// <summary>Core が意味を知っている役割の名前。<b>これ以外の役割は、アプリが決める。</b></summary>
public static class AdminRoleNames
{
    /// <summary>特権管理者。<b>最後の有効な 1 人は、無効化も降格もできない</b>（締め出しを防ぐ）。</summary>
    public const string Administrator = "Administrator";
}

/// <summary>管理者。<b>パスワードのハッシュ・TOTP の共有鍵は含まない。</b></summary>
/// <param name="Id">識別子。</param>
/// <param name="LoginId">ログイン ID。</param>
/// <param name="Role">役割の名前（<see cref="AdminRoleNames"/> のほかは、アプリが決める）。</param>
/// <param name="IsDisabled">無効か。</param>
/// <param name="FailedLoginCount">連続したログイン失敗の回数。</param>
/// <param name="LockedUntil">締め出しの解除の時刻。</param>
/// <param name="LastLoginAt">最終ログイン。一度もログインしていなければ <c>null</c>。</param>
/// <param name="CreatedAt">作った時刻。</param>
/// <param name="TotpEnabled">TOTP（2 要素）を有効にしているか。</param>
/// <param name="Language">表示言語（アプリが正規化して渡す）。</param>
public sealed record AdminUser(
    Guid Id,
    string LoginId,
    string Role,
    bool IsDisabled,
    int FailedLoginCount,
    DateTimeOffset? LockedUntil,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt,
    bool TotpEnabled,
    string Language)
{
    /// <summary>締め出し中か。</summary>
    public bool IsLocked(DateTimeOffset now) => LockedUntil is { } until && until > now;

    /// <summary>
    /// 長い間使われていないか（棚卸し用）。**招待中・無効のアカウントは対象にしない**（使っていないのではなく、使えない）。
    /// 一度もログインしていなければ、作られた日から数える。
    /// </summary>
    public bool IsStale(DateTimeOffset now, TimeSpan threshold, bool invitationPending) =>
        !invitationPending && !IsDisabled && now - (LastLoginAt ?? CreatedAt) > threshold;
}

/// <summary>管理者の変更の結果。</summary>
public enum AdminChangeResult
{
    /// <summary>変えた。</summary>
    Updated,

    /// <summary>その管理者が居ない。</summary>
    NotFound,

    /// <summary><b>有効な Administrator が 0 人になるので断った。</b>締め出しを防ぐ。</summary>
    LastAdministrator,
}

/// <summary>ログイン失敗の記録の結果。</summary>
/// <param name="FailedCount">いまの連続失敗の回数（締め出したら 0 に戻す）。</param>
/// <param name="LockedUntil">締め出したなら、その解除の時刻。</param>
public sealed record AdminFailure(int FailedCount, DateTimeOffset? LockedUntil);

/// <summary>
/// 秘密を暗号化して保管するための窓口。<b>暗号の実装は Core.Data に持たない</b>（鍵の管理はアプリの責務）。
/// アプリは、自分の <c>SecretProtector</c> などをこれに合わせる。
/// </summary>
public interface ISecretCipher
{
    /// <summary>暗号化する。<paramref name="context"/> に束縛する（別の文脈では復号できない）。</summary>
    string Protect(string plaintext, string context);

    /// <summary>復号する。文脈が違えば失敗する。</summary>
    string Unprotect(string protectedValue, string context);
}

/// <summary>管理者の保管先。</summary>
public interface IAdminUserStore
{
    /// <summary>管理者の人数。</summary>
    Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// **最初の管理者を作る。管理者が 1 人も居ないときだけ**成功する（原子的に 1 回だけ）。
    /// すでに居れば <c>null</c>。役割は <see cref="AdminRoleNames.Administrator"/>。
    /// </summary>
    Task<AdminUser?> CreateFirstAsync(
        string loginId, string passwordHash, string language, CancellationToken cancellationToken = default);

    /// <summary>ログイン ID（大文字小文字を区別しない）から引く。パスワードのハッシュも返す。</summary>
    Task<(AdminUser User, string PasswordHash)?> FindForLoginAsync(
        string loginId, CancellationToken cancellationToken = default);

    /// <summary>ID から引く。</summary>
    Task<AdminUser?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>管理者の一覧（作成の古い順）。</summary>
    Task<IReadOnlyList<AdminUser>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 管理者を作る（招待用）。<paramref name="passwordHash"/> には<b>誰も知らない値</b>のハッシュを渡す
    /// （既定のパスワードを配らないための埋め草。資格情報ではない）。ログイン ID がすでにあれば <c>null</c>。
    /// </summary>
    Task<AdminUser?> CreateAsync(
        string loginId, string passwordHash, string role, string language, CancellationToken cancellationToken = default);

    /// <summary>
    /// 無効化・有効化。**最後の有効な Administrator は無効化できない。**
    /// 「有効な Administrator」は、無効でなく、<b>一度でもログインした</b>人（招待しただけの人は、誰も知らないパスワードしか持たないので数えない）。
    /// </summary>
    Task<AdminChangeResult> SetDisabledAsync(Guid id, bool disabled, CancellationToken cancellationToken = default);

    /// <summary>役割を変える。**最後の有効な Administrator は降格できない。**</summary>
    Task<AdminChangeResult> SetRoleAsync(Guid id, string role, CancellationToken cancellationToken = default);

    /// <summary>
    /// ログイン失敗を記録する。<paramref name="threshold"/> 回目で <paramref name="lockout"/> の間だけ締め出す。
    /// **締め出しは DB に持つ**（プロセス内の記憶だと、インスタンスの数だけ試せる）。
    /// </summary>
    Task<AdminFailure> RecordFailureAsync(
        Guid id, int threshold, TimeSpan lockout, CancellationToken cancellationToken = default);

    /// <summary>ログイン成功を記録する（失敗の回数を戻し、最終ログインを残す）。</summary>
    Task RecordSuccessAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>パスワードのハッシュを差し替える。</summary>
    Task SetPasswordHashAsync(Guid id, string passwordHash, CancellationToken cancellationToken = default);

    /// <summary>TOTP の共有鍵を復号して返す。未登録なら <c>null</c>。</summary>
    Task<string?> GetTotpSecretAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>登録の途中の共有鍵を置く（<see cref="EnableTotpAsync"/> で確定するまで有効にならない）。</summary>
    Task SetTotpPendingAsync(Guid id, string secret, CancellationToken cancellationToken = default);

    /// <summary>登録の途中の共有鍵を復号して返す。無ければ <c>null</c>。</summary>
    Task<string?> GetTotpPendingAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// TOTP を有効にする。共有鍵を確定し、**前の復旧コードを消して新しいもの（ハッシュ）に置き換える。**
    /// </summary>
    Task EnableTotpAsync(
        Guid id,
        string secret,
        long timeStep,
        IReadOnlyList<string> recoveryCodeHashes,
        CancellationToken cancellationToken = default);

    /// <summary>TOTP を解除する（共有鍵と復旧コードを消す）。</summary>
    Task DisableTotpAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// **前回より新しい時間枠のときだけ**使用済みにして <c>true</c>。
    /// 同じ枠の数字は二度通さない（読んでから書くと、同時に来た 2 つが両方通る）。
    /// </summary>
    Task<bool> TryUseTotpStepAsync(Guid id, long timeStep, CancellationToken cancellationToken = default);

    /// <summary>復旧コードを使い切りにして <c>true</c>。**使用済みの印を条件に含めた UPDATE の件数で判断する。**</summary>
    Task<bool> TryUseRecoveryCodeAsync(Guid id, string codeHash, CancellationToken cancellationToken = default);

    /// <summary>未使用の復旧コードの本数。</summary>
    Task<int> CountRecoveryCodesAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 未使用の復旧コードのハッシュの一覧。塩つきのハッシュは入力から同じ値を作れないので、
    /// 呼び出し側が 1 件ずつ照合し、一致したハッシュで <see cref="TryUseRecoveryCodeAsync"/> を呼ぶ。
    /// </summary>
    Task<IReadOnlyList<string>> ListUnusedRecoveryCodeHashesAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>表示言語を変える。<b>空文字は「選んでいない」</b>（ブラウザの言語に従う）。存在しない ID は <see cref="AdminChangeResult.NotFound"/>。</summary>
    /// <exception cref="ArgumentException"><paramref name="language"/> が空白だけ、または 16 文字を超える。</exception>
    Task<AdminChangeResult> SetLanguageAsync(Guid id, string language, CancellationToken cancellationToken = default);
}

/// <summary>管理者を RDBMS へ保管する。表は <c>Core_AdminUsers</c> ほか（M10006）。</summary>
/// <remarks>
/// <para>
/// **TOTP の共有鍵は <see cref="ISecretCipher"/> で暗号化して保管し、アプリ ID と利用者 ID に束縛する。** 平文は置かない。
/// </para>
/// <para>
/// **競合する書き込みは、条件を含めた 1 文の UPDATE / INSERT の件数で判断する**
/// （読んでから書くと、同時に来た 2 つが両方通る）。
/// </para>
/// </remarks>
/// <param name="connections">接続のファクトリ。</param>
/// <param name="cipher">TOTP の共有鍵の暗号化。</param>
/// <param name="time">現在時刻。<b>試験で固定できるよう、注入する。</b></param>
/// <param name="appId">アプリ ID（<see cref="AppDatabase.AppId"/>）。暗号化の文脈に入れ、別のアプリの暗号文を復号させない。</param>
public sealed class AdminUserStore(
    IDbConnectionFactory connections, ISecretCipher cipher, TimeProvider time, string appId) : IAdminUserStore
{
    private const string SelectColumns =
        "SELECT [Id], [LoginId], [Role], [IsDisabled], [FailedLoginCount], [LockedUntil], [LastLoginAt], [CreatedAt], "
        + "[TotpSecretCipher], [Language], [PasswordHash] FROM [Core_AdminUsers]";

    private string Sql(string sql) => SqlDialect.Format(connections.Provider, sql);

    private DateTime Now() => DbTime.ForDb(time.GetUtcNow().UtcDateTime);

    private static string KeyOf(string loginId) => loginId.Trim().ToLowerInvariant();

    private string TotpContext(Guid id) => $"{appId}:totp:{id:D}";

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            Sql("SELECT COUNT(*) FROM [Core_AdminUsers]"), cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<AdminUser?> CreateFirstAsync(
        string loginId, string passwordHash, string language, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();

        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // **印の主キー（1 行だけ）で排他する。** 先に入れた 1 つだけが通り、他は一意制約で落ちる
            await connection.ExecuteAsync(new CommandDefinition(
                Sql("INSERT INTO [Core_AdminSetup] ([Id]) VALUES (1)"), transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
        catch (DbException)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);

            // 一意制約で落ちた（すでに最初の管理者が居る）なら null。それ以外の失敗は握りつぶさない
            if (await IsSetUpAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            throw;
        }

        await InsertUserAsync(connection, transaction, id, loginId, passwordHash, AdminRoleNames.Administrator, language, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return await FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
    }

    private Task<int> InsertUserAsync(
        DbConnection connection, DbTransaction? transaction, Guid id, string loginId, string passwordHash, string role,
        string language, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            Sql("INSERT INTO [Core_AdminUsers] ([Id], [LoginId], [LoginIdKey], [PasswordHash], [Role], [IsDisabled], "
                + "[FailedLoginCount], [CreatedAt], [Language]) "
                + "VALUES (@Id, @LoginId, @LoginIdKey, @PasswordHash, @Role, 0, 0, @CreatedAt, @Language)"),
            new
            {
                Id = id,
                LoginId = loginId.Trim(),
                LoginIdKey = KeyOf(loginId),
                PasswordHash = passwordHash,
                Role = role,
                CreatedAt = Now(),
                Language = language,
            },
            transaction, cancellationToken: cancellationToken));

    /// <summary>印が立っているか（別の接続で読む。落ちた接続・トランザクションの外から見る）。</summary>
    private async Task<bool> IsSetUpAsync(CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            Sql("SELECT COUNT(*) FROM [Core_AdminSetup]"), cancellationToken: cancellationToken)).ConfigureAwait(false) > 0;
    }

    public async Task<(AdminUser User, string PasswordHash)?> FindForLoginAsync(
        string loginId, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        var row = await connection.QuerySingleOrDefaultAsync<UserRow>(new CommandDefinition(
            Sql($"{SelectColumns} WHERE [LoginIdKey] = @Key"), new { Key = KeyOf(loginId) },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return row is null ? null : (row.ToUser(), row.PasswordHash);
    }

    public async Task<AdminUser?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        var row = await connection.QuerySingleOrDefaultAsync<UserRow>(new CommandDefinition(
            Sql($"{SelectColumns} WHERE [Id] = @Id"), new { Id = id },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return row?.ToUser();
    }

    public async Task<IReadOnlyList<AdminUser>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        var rows = await connection.QueryAsync<UserRow>(new CommandDefinition(
            Sql($"{SelectColumns} ORDER BY [CreatedAt], [Id]"), cancellationToken: cancellationToken)).ConfigureAwait(false);
        return [.. rows.Select(r => r.ToUser())];
    }

    public async Task<AdminUser?> CreateAsync(
        string loginId, string passwordHash, string role, string language, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        await using var connection = connections.Create();
        try
        {
            // ログイン ID の一意性は LoginIdKey の一意索引が守る
            await InsertUserAsync(connection, null, id, loginId, passwordHash, role, language, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DbException)
        {
            // 一意制約で落ちた（同じログイン ID がある）なら null。それ以外の失敗は握りつぶさない
            if (await FindForLoginAsync(loginId, cancellationToken).ConfigureAwait(false) is not null)
            {
                return null;
            }

            throw;
        }

        return await FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public Task<AdminChangeResult> SetDisabledAsync(
        Guid id, bool disabled, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, "[IsDisabled] = @Value", disabled ? 1 : 0, leavesAdministrator: disabled, cancellationToken);

    public Task<AdminChangeResult> SetRoleAsync(Guid id, string role, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, "[Role] = @Value", role, leavesAdministrator: role != AdminRoleNames.Administrator, cancellationToken);

    /// <summary>
    /// 1 文で変える。<paramref name="leavesAdministrator"/> のときは、**ほかに有効な Administrator が居る場合だけ**通す
    /// （同時に 2 人が互いを降格しても、最後の 1 人は残る）。
    /// </summary>
    /// <remarks>
    /// 件数を数える副問い合わせは派生表で包む（MySQL は、更新する表をそのまま副問い合わせに使えない）。
    /// 「有効な Administrator」は、無効でなく、**一度でもログインした**人（招待しただけの人は、誰も知らないパスワードしか持たないので数えない。
    /// この条件は厳しい側に外れる＝まだ入っていない人が居る間は断るが、断るのは安全側）。
    /// </remarks>
    private async Task<AdminChangeResult> ChangeAsync(
        Guid id, string setClause, object value, bool leavesAdministrator, CancellationToken cancellationToken)
    {
        var guard = leavesAdministrator
            ? " AND (NOT ([Role] = @Admin AND [IsDisabled] = 0 AND [LastLoginAt] IS NOT NULL) OR (SELECT COUNT(*) FROM "
                + "(SELECT [Id] FROM [Core_AdminUsers] WHERE [Role] = @Admin AND [IsDisabled] = 0 AND [LastLoginAt] IS NOT NULL "
                + "AND [Id] <> @Id) AS [Others]) > 0)"
            : string.Empty;

        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // ⚠️ **Administrator の行を先にロックして、同時の変更を 1 つずつにする。**
        // 1 文の UPDATE の副問い合わせは、PostgreSQL（READ COMMITTED）では文の開始時点の状態を見るので、
        // 2 人が同時に互いを外すと「相手が残る」と見て両方通り、有効な Administrator が 0 人になる（実機で再現）。
        // ロックの順序は主キー順に揃える（揃えないと MySQL でデッドロックする）。SQLite は書き込みが直列なので不要
        if (leavesAdministrator)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                AdminUserSql.LockAdministrators(connections.Provider),
                new { Admin = AdminRoleNames.Administrator }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        var updated = await connection.ExecuteAsync(new CommandDefinition(
            Sql($"UPDATE [Core_AdminUsers] SET {setClause} WHERE [Id] = @Id{guard}"),
            new { Id = id, Value = value, Admin = AdminRoleNames.Administrator },
            transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (updated > 0)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return AdminChangeResult.Updated;
        }

        var exists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            Sql("SELECT COUNT(*) FROM [Core_AdminUsers] WHERE [Id] = @Id"), new { Id = id },
            transaction, cancellationToken: cancellationToken)).ConfigureAwait(false) > 0;
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return exists ? AdminChangeResult.LastAdministrator : AdminChangeResult.NotFound;
    }

    public async Task<AdminFailure> RecordFailureAsync(
        Guid id, int threshold, TimeSpan lockout, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        // 加算は 1 文で行う（読んでから書くと、並行した失敗が数えられない）
        await connection.ExecuteAsync(new CommandDefinition(
            Sql("UPDATE [Core_AdminUsers] SET [FailedLoginCount] = [FailedLoginCount] + 1 WHERE [Id] = @Id"),
            new { Id = id }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        var count = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            Sql("SELECT [FailedLoginCount] FROM [Core_AdminUsers] WHERE [Id] = @Id"),
            new { Id = id }, cancellationToken: cancellationToken)).ConfigureAwait(false) ?? 0;

        if (count < threshold)
        {
            return new AdminFailure(count, null);
        }

        // 締め出す。**恒久的には締め出さない**（正規の利用者を狙って締め出す嫌がらせが成立する）。回数は戻す
        var until = time.GetUtcNow() + lockout;
        await connection.ExecuteAsync(new CommandDefinition(
            Sql("UPDATE [Core_AdminUsers] SET [FailedLoginCount] = 0, [LockedUntil] = @Until WHERE [Id] = @Id"),
            new { Id = id, Until = DbTime.ForDb(until.UtcDateTime) },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return new AdminFailure(0, until);
    }

    public async Task RecordSuccessAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        await connection.ExecuteAsync(new CommandDefinition(
            Sql("UPDATE [Core_AdminUsers] SET [FailedLoginCount] = 0, [LockedUntil] = NULL, [LastLoginAt] = @Now WHERE [Id] = @Id"),
            new { Id = id, Now = Now() },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task SetPasswordHashAsync(Guid id, string passwordHash, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        await connection.ExecuteAsync(new CommandDefinition(
            Sql("UPDATE [Core_AdminUsers] SET [PasswordHash] = @PasswordHash WHERE [Id] = @Id"),
            new { Id = id, PasswordHash = passwordHash },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<string?> GetTotpSecretAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        var protectedValue = await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            Sql("SELECT [TotpSecretCipher] FROM [Core_AdminUsers] WHERE [Id] = @Id"), new { Id = id },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return string.IsNullOrEmpty(protectedValue) ? null : cipher.Unprotect(protectedValue, TotpContext(id));
    }

    public async Task SetTotpPendingAsync(Guid id, string secret, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        await connection.ExecuteAsync(new CommandDefinition(
            Sql("UPDATE [Core_AdminUsers] SET [TotpPendingCipher] = @Cipher WHERE [Id] = @Id"),
            new { Id = id, Cipher = cipher.Protect(secret, TotpContext(id)) },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<string?> GetTotpPendingAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        var protectedValue = await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            Sql("SELECT [TotpPendingCipher] FROM [Core_AdminUsers] WHERE [Id] = @Id"), new { Id = id },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return string.IsNullOrEmpty(protectedValue) ? null : cipher.Unprotect(protectedValue, TotpContext(id));
    }

    public async Task EnableTotpAsync(
        Guid id,
        string secret,
        long timeStep,
        IReadOnlyList<string> recoveryCodeHashes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recoveryCodeHashes);

        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            Sql("UPDATE [Core_AdminUsers] SET [TotpSecretCipher] = @Cipher, [TotpPendingCipher] = NULL, "
                + "[TotpLastTimeStep] = @TimeStep WHERE [Id] = @Id"),
            new { Id = id, Cipher = cipher.Protect(secret, TotpContext(id)), TimeStep = timeStep },
            transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);

        // **登録し直すと前の復旧コードは消える。** 残すと配り直した意味が無い
        await connection.ExecuteAsync(new CommandDefinition(
            Sql("DELETE FROM [Core_AdminRecoveryCodes] WHERE [UserId] = @Id"), new { Id = id },
            transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        foreach (var hash in recoveryCodeHashes)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                Sql("INSERT INTO [Core_AdminRecoveryCodes] ([UserId], [CodeHash]) VALUES (@Id, @Hash)"),
                new { Id = id, Hash = hash }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DisableTotpAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            Sql("UPDATE [Core_AdminUsers] SET [TotpSecretCipher] = NULL, [TotpPendingCipher] = NULL, [TotpLastTimeStep] = NULL "
                + "WHERE [Id] = @Id"),
            new { Id = id }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            Sql("DELETE FROM [Core_AdminRecoveryCodes] WHERE [UserId] = @Id"), new { Id = id },
            transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> TryUseTotpStepAsync(Guid id, long timeStep, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        return await connection.ExecuteAsync(new CommandDefinition(
            Sql("UPDATE [Core_AdminUsers] SET [TotpLastTimeStep] = @TimeStep "
                + "WHERE [Id] = @Id AND ([TotpLastTimeStep] IS NULL OR [TotpLastTimeStep] < @TimeStep)"),
            new { Id = id, TimeStep = timeStep },
            cancellationToken: cancellationToken)).ConfigureAwait(false) > 0;
    }

    public async Task<bool> TryUseRecoveryCodeAsync(Guid id, string codeHash, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        return await connection.ExecuteAsync(new CommandDefinition(
            Sql("UPDATE [Core_AdminRecoveryCodes] SET [UsedAt] = @Now "
                + "WHERE [UserId] = @Id AND [CodeHash] = @Hash AND [UsedAt] IS NULL"),
            new { Id = id, Hash = codeHash, Now = Now() },
            cancellationToken: cancellationToken)).ConfigureAwait(false) > 0;
    }

    public async Task<int> CountRecoveryCodesAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            Sql("SELECT COUNT(*) FROM [Core_AdminRecoveryCodes] WHERE [UserId] = @Id AND [UsedAt] IS NULL"),
            new { Id = id }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> ListUnusedRecoveryCodeHashesAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        var hashes = await connection.QueryAsync<string>(new CommandDefinition(
            Sql("SELECT [CodeHash] FROM [Core_AdminRecoveryCodes] WHERE [UserId] = @Id AND [UsedAt] IS NULL"),
            new { Id = id }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return hashes.ToList();
    }

    public async Task<AdminChangeResult> SetLanguageAsync(
        Guid id, string language, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(language);
        if (language.Length > 0 && string.IsNullOrWhiteSpace(language))
        {
            throw new ArgumentException("言語は空にするか、値を入れる（空白だけは不可）。", nameof(language));
        }

        if (language.Length > 16)
        {
            throw new ArgumentException("言語は 16 文字以内にする。", nameof(language));
        }

        await using var connection = connections.Create();
        var updated = await connection.ExecuteAsync(new CommandDefinition(
            Sql("UPDATE [Core_AdminUsers] SET [Language] = @Language WHERE [Id] = @Id"),
            new { Id = id, Language = language }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (updated > 0)
        {
            return AdminChangeResult.Updated;
        }

        // 同じ値への更新は、MySQL では 0 件と数えられる。存在するかで分ける
        var exists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            Sql("SELECT COUNT(*) FROM [Core_AdminUsers] WHERE [Id] = @Id"), new { Id = id },
            cancellationToken: cancellationToken)).ConfigureAwait(false) > 0;
        return exists ? AdminChangeResult.Updated : AdminChangeResult.NotFound;
    }

    private sealed class UserRow
    {
        public Guid Id { get; set; }

        public string LoginId { get; set; } = "";

        public string Role { get; set; } = "";

        public int IsDisabled { get; set; }

        public int FailedLoginCount { get; set; }

        public DateTime? LockedUntil { get; set; }

        public DateTime? LastLoginAt { get; set; }

        public DateTime CreatedAt { get; set; }

        public string? TotpSecretCipher { get; set; }

        public string Language { get; set; } = "";

        public string PasswordHash { get; set; } = "";

        public AdminUser ToUser() => new(
            Id,
            LoginId,
            Role,
            IsDisabled != 0,
            FailedLoginCount,
            DbTime.AsUtc(LockedUntil),
            DbTime.AsUtc(LastLoginAt),
            DbTime.AsUtc(CreatedAt),
            !string.IsNullOrEmpty(TotpSecretCipher),
            Language);
    }
}

/// <summary>管理者の変更で、RDBMS ごとに書き方が違う部分。</summary>
internal static class AdminUserSql
{
    /// <summary>
    /// Administrator の行を、主キー順にロックする SELECT（<b>トランザクションの中で使う</b>）。
    /// 最後の Administrator を守る判定を、同時の変更の間で 1 つずつにするため。SQLite は書き込みが直列なのでロック句が要らない。
    /// </summary>
    public static string LockAdministrators(DatabaseProvider provider) => provider switch
    {
        DatabaseProvider.SqlServer =>
            "SELECT [Id] FROM [Core_AdminUsers] WITH (UPDLOCK, HOLDLOCK) WHERE [Role] = @Admin ORDER BY [Id]",
        DatabaseProvider.PostgreSql =>
            "SELECT \"Id\" FROM \"Core_AdminUsers\" WHERE \"Role\" = @Admin ORDER BY \"Id\" FOR UPDATE",
        DatabaseProvider.MySql =>
            "SELECT `Id` FROM `Core_AdminUsers` WHERE `Role` = @Admin ORDER BY `Id` FOR UPDATE",
        DatabaseProvider.Sqlite =>
            "SELECT \"Id\" FROM \"Core_AdminUsers\" WHERE \"Role\" = @Admin ORDER BY \"Id\"",
        _ => throw new NotSupportedException($"対応していない RDBMS: {provider}"),
    };
}
