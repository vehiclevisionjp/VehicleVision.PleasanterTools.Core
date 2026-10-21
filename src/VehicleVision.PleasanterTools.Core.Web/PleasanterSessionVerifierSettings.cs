namespace VehicleVision.PleasanterTools.Core.Web;

/// <summary><see cref="PleasanterSessionVerifier"/> へ渡す、アプリごとに違う値。</summary>
/// <remarks>**Core にアプリ名を書かない**（<c>_documents/コア化の方針.md</c>）。値は呼び出し側が渡す。</remarks>
public sealed record PleasanterSessionVerifierSettings
{
    /// <summary>代わりの経路で Pleasanter の <c>/api/sessions/set</c> に書く鍵。</summary>
    /// <remarks>
    /// **アプリごとに別の名前にする**（例: <c>VehicleVision.Questionnaire.SsoProbe</c>）。
    /// Pleasanter のセッションごとに保存され、セッションと一緒に消える。値に意味は無い。
    /// </remarks>
    public required string SessionProbeKey { get; init; }

    /// <summary>本アプリの Pleasanter 接続設定の URL の設定名。**ログで運用者へ案内するためだけに使う。**</summary>
    public required string BaseUrlSettingName { get; init; }

    /// <summary>本アプリの Pleasanter 接続設定の API キーの設定名。**ログで運用者へ案内するためだけに使う。**</summary>
    public required string ApiKeySettingName { get; init; }

    /// <summary>ログのカテゴリ。**空なら <see cref="PleasanterSessionVerifier"/> の完全名。**</summary>
    /// <remarks>
    /// アプリの設定がログの量をカテゴリ（名前空間）で絞っているなら、移す前のカテゴリ
    /// （例: <c>VehicleVision.PleasanterTools.Questionnaire.Web.Services.PleasanterSessionVerifier</c>）を渡すと、
    /// 設定を変えずに済む。
    /// </remarks>
    public string? LoggerCategory { get; init; }

    /// <summary>値が揃っているかを確かめる。</summary>
    /// <exception cref="ArgumentException">空の値がある。</exception>
    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(SessionProbeKey, nameof(SessionProbeKey));
        ArgumentException.ThrowIfNullOrWhiteSpace(BaseUrlSettingName, nameof(BaseUrlSettingName));
        ArgumentException.ThrowIfNullOrWhiteSpace(ApiKeySettingName, nameof(ApiKeySettingName));
    }
}
