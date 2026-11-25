using System.Globalization;
using System.Net;

namespace VehicleVision.PleasanterTools.Core.Web;

/// <summary>この要求で使う RP ID と許可する Origin。</summary>
/// <param name="RpId">RP ID（小文字のホスト名）。</param>
/// <param name="Origins">許可する Origin（スキーム・ホスト・ポートの完全一致）。</param>
public sealed record PasskeyRelyingParty(string RpId, IReadOnlyList<string> Origins)
{
    /// <summary>
    /// 設定とリクエストのホストから、RP ID と Origin を決める（決定 6/9）。
    /// <b>明示した設定を優先</b>し、無ければリクエストのホストから導く。
    /// プロキシ経由のホストは、呼び出し側が転送ヘッダ（信頼するネットワークだけ）を適用した後の値を渡す
    /// （未設定のまま <c>X-Forwarded-Host</c> を信用しない）。サブパス配置でも、RP ID はホスト名だけ。
    /// </summary>
    /// <param name="options">設定。</param>
    /// <param name="scheme">要求のスキーム（<c>https</c> など）。</param>
    /// <param name="host">要求のホスト（ポートがあれば <c>host:port</c>）。</param>
    /// <returns>決まらない（IP リテラル・許されないスキーム・RP ID と合わない）ときは <c>null</c>。</returns>
    public static PasskeyRelyingParty? Resolve(AdminPasskeyOptions options, string scheme, string host)
    {
        ArgumentNullException.ThrowIfNull(options);

        var requestOrigin = NormalizeOrigin($"{scheme}://{host}");
        var rpId = options.RpId;
        if (rpId is null)
        {
            if (requestOrigin is null)
            {
                return null;
            }

            rpId = new Uri(requestOrigin).IdnHost.ToLower(CultureInfo.InvariantCulture);
            if (!IsValidRpId(rpId))
            {
                return null;
            }
        }

        IReadOnlyList<string> origins = options.Origins.Count > 0 ? options.Origins : requestOrigin is null ? [] : [requestOrigin];
        if (origins.Count == 0 || origins.Any(origin => !HostBelongsToRpId(new Uri(origin).IdnHost, rpId)))
        {
            return null;
        }

        return new PasskeyRelyingParty(rpId, origins);
    }

    /// <summary>RP ID として使える（ホスト名だけ。IP リテラルは不可）か。</summary>
    public static bool IsValidRpId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 253
            || value.Any(c => char.IsWhiteSpace(c) || c is ':' or '/' or '?' or '#' or '@' or '[' or ']'))
        {
            return false;
        }

        return !IPAddress.TryParse(value, out _) && Uri.CheckHostName(value) == UriHostNameType.Dns;
    }

    /// <summary>
    /// Origin を「スキーム://ホスト[:既定でないポート]」へ正規化する。<b>https だけ</b>。
    /// 開発のため、<c>localhost</c>（と <c>*.localhost</c>）だけ http も許す（ブラウザが安全なコンテキストとして扱う）。
    /// パス・問い合わせ・資格情報つきは不正（<c>null</c>）。
    /// </summary>
    public static string? NormalizeOrigin(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.AbsolutePath != "/"
            || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0
            || value.EndsWith("/?", StringComparison.Ordinal) || value.EndsWith('#'))
        {
            return null;
        }

        var host = uri.IdnHost.ToLower(CultureInfo.InvariantCulture);
        if (!IsValidRpId(host))
        {
            return null;
        }

        var isLocalhost = host == "localhost" || host.EndsWith(".localhost", StringComparison.Ordinal);
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && isLocalhost))
        {
            return null;
        }

        return uri.IsDefaultPort ? $"{uri.Scheme}://{host}" : $"{uri.Scheme}://{host}:{uri.Port.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>ホストが RP ID と一致、またはそのサブドメインか（WebAuthn の登録可能なドメインの関係）。</summary>
    public static bool HostBelongsToRpId(string host, string rpId) =>
        string.Equals(host, rpId, StringComparison.OrdinalIgnoreCase)
        || host.EndsWith("." + rpId, StringComparison.OrdinalIgnoreCase);
}
