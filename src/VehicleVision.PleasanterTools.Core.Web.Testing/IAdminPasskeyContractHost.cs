namespace VehicleVision.PleasanterTools.Core.Web.Testing;

/// <summary>パスキー契約の準備に使う利用者の指定。</summary>
/// <param name="Disabled">無効の利用者か。</param>
/// <param name="PasswordUsable">パスワードで入れる人か（P5。偽なら、SAML・Pleasanter も無効な前提）。</param>
/// <param name="Permissions">この権限だけを持つ役割にする（<c>null</c> は権限なし）。</param>
public sealed record PasskeySeed(
    bool Disabled = false, bool PasswordUsable = true, IReadOnlySet<string>? Permissions = null);

/// <summary>試験が読む監査ログ 1 件。</summary>
public sealed record PasskeyAuditRecord(string Action, bool Succeeded, string? TargetId, string? DetailJson);

/// <summary>パスキー契約（P1〜P10）が経路・本文の形を決めるための設定。経路は既定で API 契約の仕様書 5.3.1 のとおり。</summary>
public sealed class AdminPasskeyContractSettings
{
    /// <summary>接頭辞（例 <c>/api/admin</c>）。</summary>
    public string Prefix { get; init; } = "/api/admin";

    /// <summary>一覧（GET）。</summary>
    public string List => $"{Prefix}/me/passkeys";

    /// <summary>登録の開始（POST）。</summary>
    public string RegisterBegin => $"{Prefix}/me/passkeys/begin";

    /// <summary>登録の完了（POST）。</summary>
    public string RegisterComplete => $"{Prefix}/me/passkeys/complete";

    /// <summary>名前の変更（PUT）。</summary>
    public string Rename(Guid id) => $"{Prefix}/me/passkeys/{id}";

    /// <summary>削除（POST）。</summary>
    public string Remove(Guid id) => $"{Prefix}/me/passkeys/{id}/remove";

    /// <summary>ログインの開始（POST・認証の外）。</summary>
    public string LoginBegin => $"{Prefix}/login/passkey/begin";

    /// <summary>ログインの完了（POST・認証の外）。</summary>
    public string LoginComplete => $"{Prefix}/login/passkey/complete";

    /// <summary>復旧: 他人の分の削除（POST）。</summary>
    public string Reset(Guid userId) => $"{Prefix}/users/{userId}/passkeys/reset";

    /// <summary>復旧に要る権限の名前。</summary>
    public string ResetPermission { get; init; } = "users.resetPasskeys";
}

/// <summary>
/// アプリが用意する、試験ごとに独立した HTTP ホストとデータの準備口（パスキー）。
/// <see cref="IAdminContractHost"/> とは別で、同じホストが両方を実装してもよい。
/// </summary>
public interface IAdminPasskeyContractHost : IAsyncDisposable
{
    /// <summary>経路の設定。</summary>
    AdminPasskeyContractSettings Settings { get; }

    /// <summary>ブラウザが送る Origin（RP ID と許可する Origin に合うもの）。</summary>
    string Origin { get; }

    /// <summary>IP ごとのレート制限の上限（パスワードと共有する枠。5 分あたり）。</summary>
    int LoginAttemptsPer5Minutes { get; }

    /// <summary>新しい cookie 容器を持ち、転送を追わないクライアント。呼び出し側が破棄する。</summary>
    HttpClient CreateClient();

    /// <summary>利用者を一意な ID で作る。</summary>
    Task<SeededAdmin> SeedAdminAsync(PasskeySeed seed);

    /// <summary>その利用者の ID（復旧の経路に使う）。</summary>
    Task<Guid> UserIdAsync(SeededAdmin admin);

    /// <summary>実際のパスワードのログイン口を叩く。応答を加工せず返す（レート制限の共有の確認と、Session の用意に使う）。</summary>
    Task<HttpResponseMessage> PasswordLoginAsync(HttpClient client, string loginId, string password);

    /// <summary>機能（パスキー）の有効・無効を切り替える。既存の行は消さない（P10）。</summary>
    Task SetPasskeyEnabledAsync(bool enabled);

    /// <summary>時計を進める（チャレンジの期限・締め出しの期間の確認用）。<b>IP ごとのレート制限の窓も進める</b>（試験が窓を区切るのに使う）。</summary>
    Task AdvanceTimeAsync(TimeSpan span);

    /// <summary>
    /// TOTP を登録済みにする（以後、パスワードだけでは Pending になる。P2。2 要素の方針が <c>Required</c> のホストでもよい）。
    /// </summary>
    Task EnableTotpAsync(SeededAdmin admin);

    /// <summary>締め出し状態だけを変える。</summary>
    Task SetLockoutAsync(SeededAdmin admin, bool locked);

    /// <summary>無効・有効を切り替える（セッションは触らない）。</summary>
    Task SetDisabledAsync(SeededAdmin admin, bool disabled);

    /// <summary>パスワードで入れるかを切り替える（P5。偽は、使えるログイン方法がパスキーだけの状態）。</summary>
    Task SetPasswordUsableAsync(SeededAdmin admin, bool usable);

    /// <summary>監査ログ（古い順）。</summary>
    Task<IReadOnlyList<PasskeyAuditRecord>> AuditAsync();

    /// <summary>その利用者のセッションに、指定した方式（<c>via</c>）で入ったものがあるか。確かめない実装は <c>null</c>。</summary>
    Task<bool?> HasSessionViaAsync(SeededAdmin admin, string via) => Task.FromResult<bool?>(null);
}
