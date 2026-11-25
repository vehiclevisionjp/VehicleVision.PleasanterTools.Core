namespace VehicleVision.PleasanterTools.Core.Contracts;

/// <summary>
/// 製品群で共通の環境変数の名前（仕様書 5.6・6.1、決定 N6）。
/// アプリ固有の値ではないので Core.Contracts が持つ（1.3.1）。旧名は受け付けず、旧名の対応表も持たない。
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
}
