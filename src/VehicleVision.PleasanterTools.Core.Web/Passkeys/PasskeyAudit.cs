namespace VehicleVision.PleasanterTools.Core.Web;

/// <summary>パスキーの監査イベントの <c>Action</c>（決定 7/9。表記は決定の名前のまま）。</summary>
public static class PasskeyAuditActions
{
    /// <summary>登録の完了。</summary>
    public const string Registered = "Passkey.Registered";

    /// <summary>削除（本人・管理者・DB の手作業）。</summary>
    public const string Removed = "Passkey.Removed";

    /// <summary>名前の変更。</summary>
    public const string Renamed = "Passkey.Renamed";

    /// <summary>ログインの成功。</summary>
    public const string SignInSucceeded = "Passkey.SignIn.Succeeded";

    /// <summary>ログインの失敗（理由は <see cref="PasskeyFailureReasons"/>）。</summary>
    public const string SignInFailed = "Passkey.SignIn.Failed";

    /// <summary>全イベント。</summary>
    public static IReadOnlyList<string> All { get; } = [Registered, Removed, Renamed, SignInSucceeded, SignInFailed];
}

/// <summary>失敗の理由の印（監査の <c>DetailJson</c> にだけ残す。応答は区別しない）。</summary>
public static class PasskeyFailureReasons
{
    /// <summary>応答を読めない。</summary>
    public const string Malformed = "malformed";

    /// <summary>チャレンジが無い・期限切れ・使用済み。</summary>
    public const string Challenge = "challenge";

    /// <summary>未登録の資格情報。</summary>
    public const string UnknownCredential = "unknown-credential";

    /// <summary>署名が合わない。</summary>
    public const string BadSignature = "bad-signature";

    /// <summary>本人確認（UV）のフラグが無い。</summary>
    public const string UserVerificationMissing = "uv-missing";

    /// <summary><b>署名カウンターの逆行（複製の疑い）。警告として見分ける。</b></summary>
    public const string CounterRegression = "counter-regression";

    /// <summary>BE（同期できるか）が登録時と違う。</summary>
    public const string BackupEligibilityChanged = "backup-eligibility-changed";

    /// <summary>ユーザーハンドルが持ち主と一致しない。</summary>
    public const string UserHandleMismatch = "user-handle-mismatch";

    /// <summary>origin・RP ID が合わない。</summary>
    public const string Origin = "origin";

    /// <summary>持ち主が無効。</summary>
    public const string Disabled = "disabled";

    /// <summary>持ち主が締め出し中。</summary>
    public const string Locked = "locked";

    /// <summary>同じ応答が同時に通ろうとした。</summary>
    public const string Concurrent = "concurrent";

    /// <summary>そのほか検証に失敗。</summary>
    public const string Invalid = "invalid";

    /// <summary>再認証に使ったパスキーが、本人のものでない。</summary>
    public const string OtherUser = "other-user";
}
