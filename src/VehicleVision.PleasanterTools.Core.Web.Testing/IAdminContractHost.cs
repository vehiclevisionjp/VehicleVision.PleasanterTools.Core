namespace VehicleVision.PleasanterTools.Core.Web.Testing;

/// <summary>アプリが用意する、試験ごとに独立した HTTP ホストとデータの準備口。</summary>
public interface IAdminContractHost : IAsyncDisposable
{
    /// <summary>経路・要求本文・応答の形・cookie の名前。</summary>
    AdminContractSettings Settings { get; }

    /// <summary>存在する入口。存在しない入口も設定し、404 を検証する。</summary>
    AdminContractCapabilities Capabilities { get; }

    /// <summary>新しい cookie 容器を持ち、転送を追わないクライアント。呼び出し側が破棄する。</summary>
    HttpClient CreateClient();

    /// <summary>
    /// 管理操作の権限を持つ利用者を一意な ID で作る。TOTP は未使用の時間枠で登録する。
    /// 無効化した利用者も、ほかの条件（パスワード・役割）は有効な利用者と同じにする。
    /// </summary>
    Task<SeededAdmin> SeedAdminAsync(bool withTotp, bool disabled = false);

    /// <summary>パスワードだけを HTTP で照合する。応答を加工せず返し、呼び出し側が破棄する。</summary>
    Task<HttpResponseMessage> LoginAsync(HttpClient client, SeededAdmin admin);

    /// <summary>その利用者の、サーバが受け付ける未使用の時間枠の TOTP を返す。</summary>
    string NextTotpCode(SeededAdmin admin);

    /// <summary>指定した権限だけを持つ役割の利用者を作る。画面の権限制御を代用しない。</summary>
    Task<SeededAdmin> SeedAdminWithPermissionsAsync(IReadOnlySet<string> permissions) =>
        throw new NotSupportedException("権限を分離した利用者の準備は未対応です。");

    /// <summary>外部設定の方針を切り替える。対象のセッション・登録途中の鍵は維持する。</summary>
    Task SetTwoFactorPolicyAsync(AdminContractTwoFactorPolicy policy) =>
        throw new NotSupportedException("2 要素方針の切り替えは未対応です。");

    /// <summary>締め出し状態だけを変更する。セッションや失敗履歴をほかの目的で消さない。</summary>
    Task SetLockoutAsync(SeededAdmin admin, bool locked) =>
        throw new NotSupportedException("締め出し状態の準備は未対応です。");

    /// <summary>無効化した利用者を再度有効にする。消えたセッションは再作成しない。</summary>
    Task EnableAdminAsync(SeededAdmin admin) =>
        throw new NotSupportedException("利用者の再有効化は未対応です。");

    /// <summary>対象者の段階別セッションを独立に準備し、その cookie ヘッダ値を返す。</summary>
    Task<string> CreateSessionAsync(SeededAdmin admin, AdminContractSessionKind kind) =>
        throw new NotSupportedException("段階別セッションの準備は未対応です。");

    /// <summary>
    /// 与えられた cookie だけを実際の HTTP の口で検証する。有効なら 200、失効なら 401。
    /// Pending・Reenroll は対応する段階の操作を使い、ストアの有無を HTTP 応答に偽装しない。
    /// 応答は加工せず返す。成功で消費する操作でもよい。
    /// </summary>
    Task<HttpResponseMessage> ProbeSessionAsync(
        HttpClient client, SeededAdmin admin, AdminContractSessionKind kind) =>
        throw new NotSupportedException("段階別セッションの HTTP 検証は未対応です。");

    /// <summary>
    /// 外部認証の本人を準備する。existing の場合だけローカル利用者を作る。
    /// knownPassword=false の場合は本人が使えるパスワードを持たせない。
    /// </summary>
    Task<SeededAdmin> SeedExternalAdminAsync(
        AdminContractExternalProvider provider, bool withTotp, bool existing = true, bool knownPassword = true) =>
        throw new NotSupportedException("外部認証の本人の準備は未対応です。");

    /// <summary>
    /// 有効な署名・相関を持つ SAML または隔離した Pleasanter 検証先を通して HTTP でログインする。
    /// アプリの締め出し・JIT・認可を迂回しない。転送を追わず、加工しない応答を返す。
    /// </summary>
    Task<HttpResponseMessage> ExternalLoginAsync(
        HttpClient client, SeededAdmin admin, AdminContractExternalProvider provider) =>
        throw new NotSupportedException("外部ログインの HTTP 実行は未対応です。");

    /// <summary>
    /// JIT の実行時設定を準備する。禁止値も注入し、設定入力時の拒否だけで実行時の検査を代用しない。
    /// hasAdministrator=false は有効な Administrator が 0 人の状態。
    /// </summary>
    Task ConfigureJitAsync(AdminContractExternalProvider provider, string role, bool hasAdministrator) =>
        throw new NotSupportedException("JIT の実行時設定の準備は未対応です。");

    /// <summary>対象の外部 ID に対応する利用者を、認証済み管理者の一覧 API で検索する要求を作る。</summary>
    Task<HttpRequestMessage> CreateUserLookupRequestAsync(SeededAdmin admin) =>
        throw new NotSupportedException("利用者一覧の HTTP 要求は未対応です。");

    /// <summary>一覧 API の加工していない本文に、対象のログイン ID が存在するかを読む。</summary>
    bool ContainsUser(string responseBody, SeededAdmin admin) =>
        throw new NotSupportedException("利用者一覧の本文の読み取りは未対応です。");

    /// <summary>準備時に登録した、まだ使っていない復旧コードを 1 本返す。診断へ出さない。</summary>
    Task<string> RecoveryCodeAsync(SeededAdmin admin) =>
        throw new NotSupportedException("復旧コードの準備は未対応です。");

    /// <summary>HTTP で開始した再登録の途中鍵に対応する数字。元の鍵の数字とは区別する。</summary>
    string NextReenrollmentTotpCode(SeededAdmin admin) =>
        throw new NotSupportedException("再登録の途中鍵の TOTP は未対応です。");

    /// <summary>
    /// パスワードを持たない本人の Session と、指定した外部再確認の証跡を準備する。
    /// Fresh は外部ログインを完了する。Expired はアプリの有効期限外とし、時間幅を Core で決めない。
    /// DisplayOnly は via だけを持つ。cookie 自体を本人の再確認の証拠にしない。
    /// </summary>
    Task PrepareExternalReconfirmationAsync(
        HttpClient client, SeededAdmin admin, AdminContractExternalProvider provider,
        AdminContractReconfirmation proof) =>
        throw new NotSupportedException("外部再確認の証跡の準備は未対応です。");
}

/// <summary>外部ログインの入口。</summary>
public enum AdminContractExternalProvider { Saml, PleasanterSso }

/// <summary>2 要素の外部設定。</summary>
public enum AdminContractTwoFactorPolicy { Required, Optional, Disabled }

/// <summary>失効を検査するセッションの段階。</summary>
public enum AdminContractSessionKind { Session, Pending, Reenroll }

/// <summary>外部再確認の証跡。時間幅や再認証 API の形はアプリ側で具体化する。</summary>
public enum AdminContractReconfirmation { Missing, Expired, DifferentUser, Incomplete, DisplayOnly, Fresh }

/// <summary>試験で作った利用者。秘密を診断出力へ載せない。</summary>
public sealed record SeededAdmin(string LoginId, string Password)
{
    /// <inheritdoc />
    public override string ToString() => LoginId;
}
