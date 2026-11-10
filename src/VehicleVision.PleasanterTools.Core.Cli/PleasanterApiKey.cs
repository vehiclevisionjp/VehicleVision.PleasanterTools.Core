using VehicleVision.PleasanterTools.Core.Contracts;

namespace VehicleVision.PleasanterTools.Core.Cli;

/// <summary>
/// 共通の API キーの読み方（仕様書 6.1、決定 N4）。<see cref="PleasanterEnvironmentNames.ApiKey"/> と
/// <see cref="PleasanterEnvironmentNames.ApiKeyFile"/> のどちらか片方を使う。
/// </summary>
/// <remarks>
/// 失敗は <see cref="UsageException"/>（CLI は <see cref="CliApplication"/> が <c>[ERROR]</c> と終了コード 2 にし、
/// ライブラリは例外のまま使える）。メッセージは ASCII の英語で、キーの値・ファイルの中身は含めない（パスは含める）。
/// </remarks>
public static class PleasanterApiKey
{
    /// <summary>
    /// API キーを解決する。どちらも未設定（空は未設定）なら <see langword="null"/>。
    /// 両方設定されていれば <see cref="UsageException"/>。ファイルは前後の空白・改行を除去して使い、
    /// 読めない・空のときも <see cref="UsageException"/>。
    /// </summary>
    /// <param name="getEnvironmentVariable">環境変数の取得。試験では差し替える。</param>
    /// <param name="readAllText">ファイルの読み込み。省略時は <see cref="File.ReadAllText(string)"/>。</param>
    public static string? Resolve(
        Func<string, string?> getEnvironmentVariable,
        Func<string, string>? readAllText = null)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        var key = getEnvironmentVariable(PleasanterEnvironmentNames.ApiKey);
        var path = getEnvironmentVariable(PleasanterEnvironmentNames.ApiKeyFile);
        var hasKey = !string.IsNullOrEmpty(key);
        var hasPath = !string.IsNullOrEmpty(path);

        if (hasKey && hasPath)
        {
            throw new UsageException(
                $"Both {PleasanterEnvironmentNames.ApiKey} and {PleasanterEnvironmentNames.ApiKeyFile} are set. Set only one.");
        }

        if (hasKey)
        {
            return key;
        }

        if (!hasPath)
        {
            return null;
        }

        string text;
        try
        {
            text = (readAllText ?? File.ReadAllText)(path!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new UsageException($"Cannot read the API key file '{path}'.");
        }

        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            throw new UsageException($"The API key file '{path}' is empty.");
        }

        return trimmed;
    }
}
