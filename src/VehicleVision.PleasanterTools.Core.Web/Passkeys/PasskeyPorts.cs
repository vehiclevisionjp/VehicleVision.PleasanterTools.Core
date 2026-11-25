namespace VehicleVision.PleasanterTools.Core.Web;

// Core.Web と Core.Data は互いに参照しない（コア化の方針）。パスキーの流れが Core.Data のストアを使うための受け口を、
// ここに置く。アプリは、Core.Data の IAdminPasskeyStore などを包む薄いアダプターを実装して登録する。

/// <summary>保管されたパスキー（資格情報）1 件。<b>公開鍵を監査ログ・ログへ出さない。</b></summary>
public sealed record PasskeyCredential(
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
public sealed record NewPasskeyCredential(
    Guid UserId,
    byte[] CredentialId,
    byte[] PublicKey,
    long SignCount,
    Guid AaGuid,
    IReadOnlyList<string> Transports,
    bool BackupEligible,
    bool BackedUp,
    string Name);

/// <summary>パスキーの保管先。Core.Data の <c>IAdminPasskeyStore</c> と同じ形（持ち主の組で操作する）。</summary>
public interface IPasskeyCredentialStore
{
    /// <summary>持ち主の一覧（登録の古い順）。</summary>
    Task<IReadOnlyList<PasskeyCredential>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>持ち主の件数。</summary>
    Task<int> CountForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>資格情報の ID から引く（ログイン）。</summary>
    Task<PasskeyCredential?> FindByCredentialIdAsync(byte[] credentialId, CancellationToken cancellationToken = default);

    /// <summary>足す。同じ資格情報の ID が既にあれば <c>null</c>。</summary>
    Task<Guid?> AddAsync(NewPasskeyCredential passkey, CancellationToken cancellationToken = default);

    /// <summary>名前を変える（持ち主の組）。</summary>
    Task<bool> RenameAsync(Guid userId, Guid id, string name, CancellationToken cancellationToken = default);

    /// <summary>1 件消す（持ち主の組）。</summary>
    Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken cancellationToken = default);

    /// <summary>持ち主の分をすべて消す。</summary>
    Task<int> DeleteForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>認証の成功を記録する。<paramref name="previousSignCount"/> がいまの値と同じときだけ更新する。</summary>
    Task<bool> RecordSignInAsync(
        Guid id, long previousSignCount, long newSignCount, bool backedUp, CancellationToken cancellationToken = default);
}

/// <summary>チャレンジの保管先。Core.Data の <c>IAdminPasskeyChallengeStore</c> と同じ形（一度限り）。</summary>
public interface IPasskeyChallengeStore
{
    /// <summary>保管する（DB には SHA-256 だけ）。</summary>
    Task StoreAsync(
        byte[] challenge, string purpose, Guid? userId, TimeSpan lifetime, CancellationToken cancellationToken = default);

    /// <summary>消費する。用途・持ち主が合い、期限内で、未使用のときだけ <c>true</c>。</summary>
    Task<bool> ConsumeAsync(byte[] challenge, string purpose, Guid? userId, CancellationToken cancellationToken = default);
}

/// <summary>チャレンジの用途（Core.Data の <c>PasskeyChallengePurposes</c> と同じ値）。</summary>
public static class PasskeyPurposes
{
    /// <summary>登録。</summary>
    public const string Register = "register";

    /// <summary>ログイン。</summary>
    public const string SignIn = "signin";
}

/// <summary>パスキーの流れが見る管理者の状態。</summary>
/// <param name="Id">管理者 ID。</param>
/// <param name="LoginId">ログイン ID。</param>
/// <param name="IsDisabled">無効か。</param>
/// <param name="IsLocked">締め出し中か。</param>
public sealed record PasskeyAccount(Guid Id, string LoginId, bool IsDisabled, bool IsLocked);

/// <summary>パスワードの再確認の結果。</summary>
public enum PasswordConfirmation
{
    /// <summary>確認できた。</summary>
    Confirmed,

    /// <summary>違う（失敗として数える）。</summary>
    Rejected,

    /// <summary>締め出し中。</summary>
    Locked,
}

/// <summary>使えるログイン方法（P5。ほかのパスキーは数えない）。</summary>
/// <param name="Password">パスワード（合言葉のログインが塞がれておらず、招待を受け取り済みで、本人が知る）。</param>
/// <param name="Saml">SAML が有効。</param>
/// <param name="PleasanterSso">Pleasanter のログインが有効。</param>
public sealed record PasskeySignInMethods(bool Password, bool Saml, bool PleasanterSso)
{
    /// <summary>1 つでもあるか。</summary>
    public bool Any => Password || Saml || PleasanterSso;
}

/// <summary>管理者まわりの受け口（アプリが、自分の認証の実装と Core.Data のストアで実装する）。</summary>
public interface IPasskeyAccounts
{
    /// <summary>管理者を引く。無ければ <c>null</c>。</summary>
    Task<PasskeyAccount?> FindAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// パスワードを再確認する。<b>ログインと同じ道（失敗の記録・締め出し）を通す</b>（6.4）。
    /// 成功したら失敗の回数を戻す。
    /// </summary>
    Task<PasswordConfirmation> ConfirmPasswordAsync(Guid userId, string password, CancellationToken cancellationToken = default);

    /// <summary>パスキーでのログインの成功を記録する（失敗の回数を戻し、最終ログインを残す）。</summary>
    Task RecordSignInSucceededAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>その人の全セッションを失効させる（段階を問わない）。</summary>
    Task RevokeSessionsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>使えるログイン方法。</summary>
    Task<PasskeySignInMethods> GetSignInMethodsAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>監査ログへ残す 1 件（Core.Data の <c>AuditEntry</c> へ写す）。</summary>
public sealed record PasskeyAuditEntry(
    DateTimeOffset OccurredAt,
    string Action,
    bool Succeeded,
    int? StatusCode,
    string? ActorKey,
    string? ActorLoginId,
    string? TargetId,
    string? DetailJson,
    string? IpAddress);

/// <summary>監査ログの書き口。書けなくても操作は通す（アプリが例外を握る）。</summary>
public interface IPasskeyAuditSink
{
    /// <summary>書く。対象の種類は常に <c>AdminUser</c>。</summary>
    Task WriteAsync(PasskeyAuditEntry entry, CancellationToken cancellationToken = default);
}
