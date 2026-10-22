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
}

/// <summary>試験で作った利用者。秘密を診断出力へ載せない。</summary>
public sealed record SeededAdmin(string LoginId, string Password)
{
    /// <inheritdoc />
    public override string ToString() => LoginId;
}
