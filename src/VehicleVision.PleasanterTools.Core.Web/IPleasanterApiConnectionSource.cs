namespace VehicleVision.PleasanterTools.Core.Web;

/// <summary>本アプリの Pleasanter 接続設定（API キーを送っている先）。</summary>
/// <param name="BaseUrl">Pleasanter の URL。末尾の <c>/</c> は有っても無くてもよい。</param>
/// <param name="ApiKey">本アプリの API キー。**ログへ出さない。**</param>
public sealed record PleasanterApiConnectionSettings(string? BaseUrl, string? ApiKey)
{
    /// <summary>API キーを隠した文字列。**レコードの既定の ToString はキーをそのまま出すため、置き換える。**</summary>
    public override string ToString() =>
        $"{nameof(PleasanterApiConnectionSettings)} {{ BaseUrl = {BaseUrl}, ApiKey = {(string.IsNullOrEmpty(ApiKey) ? "(なし)" : "***")} }}";
}

/// <summary>本アプリの Pleasanter 接続設定を返す。</summary>
/// <remarks>
/// <para>
/// <see cref="PleasanterSessionVerifier"/> は、利用者の API 利用が禁止されているときと、グループの所属を確かめるときにだけ使う。
/// **API キーはこの URL へだけ送る**（内部 URL を書き換えられてもキーが漏れない）。
/// </para>
/// <para>
/// 設定の置き場はアプリごとに違う（DB に保存した値、起動時に読んだ値など）。アプリが自分の置き場から読んで返す。
/// 読めないときは <see cref="FormatException"/>・<see cref="OverflowException"/>・<see cref="InvalidOperationException"/>
/// を投げてよい。**検証側は警告を残し、API キーが無いものとして扱う。**
/// </para>
/// </remarks>
public interface IPleasanterApiConnectionSource
{
    /// <summary>いまの接続設定を返す。</summary>
    Task<PleasanterApiConnectionSettings> GetAsync(CancellationToken cancellationToken = default);
}
