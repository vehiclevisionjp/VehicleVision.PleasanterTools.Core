namespace VehicleVision.PleasanterTools.Core.Contracts;

/// <summary>
/// 製品群で共通の環境変数の名前と旧名の対応（仕様書 5.6・6.1・6.2、決定 N6）。
/// アプリ固有の値ではないので Core.Contracts が持つ（1.3.1）。ツール固有の旧名（<c>DBCONV_*</c> など）は含めない。
/// </summary>
public static class PleasanterEnvironmentNames
{
    /// <summary>Pleasanter の URL。</summary>
    public const string BaseUrl = "VVPT_PLEASANTER_BASE_URL";

    /// <summary>API キー。</summary>
    public const string ApiKey = "VVPT_PLEASANTER_API_KEY";

    /// <summary>API キーを書いたファイルのパス。</summary>
    public const string ApiKeyFile = "VVPT_PLEASANTER_API_KEY_FILE";

    /// <summary>非 HTTPS を許すか（読み方は <c>Core.Cli</c> の <c>EnvironmentNames.IsTrue</c>）。</summary>
    public const string AllowInsecureHttp = "VVPT_PLEASANTER_ALLOW_INSECURE_HTTP";

    /// <summary>Pleasanter のバージョン。</summary>
    public const string ServerVersion = "VVPT_PLEASANTER_SERVER_VERSION";

    /// <summary>旧名 → 新名（仕様書 6.2 の表）。旧名は受け付けない。検出（<c>Core.Cli</c> の <c>LegacyEnvironmentCheck</c>）だけに使う。</summary>
    public static IReadOnlyDictionary<string, string> Legacy { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["PLEASANTER_BASE_URL"] = BaseUrl,
        ["PLEASANTER_API_KEY"] = ApiKey,
        ["PLEASANTER_API_KEY_FILE"] = ApiKeyFile,
        ["PLEASANTER_ALLOW_INSECURE_HTTP"] = AllowInsecureHttp,
        ["PLEASANTER_SERVER_VERSION"] = ServerVersion,
        ["FEEDINGEST_PLEASANTER_BASE_URL"] = BaseUrl,
        ["FEEDINGEST_PLEASANTER_API_KEY"] = ApiKey,
        ["FEEDINGEST_PLEASANTER_API_KEY_FILE"] = ApiKeyFile,
        ["CALENDARSTYLE_PLEASANTER_API_KEY"] = ApiKey,
    };
}
