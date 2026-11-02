using Microsoft.Extensions.Configuration;

namespace VehicleVision.PleasanterTools.Core.Web;

/// <summary>ブラウザとアプリ間の通信を HTTP でも許すかを表す。</summary>
/// <remarks>設定名はアプリごとに違うので引数で受ける（Issue #139）。</remarks>
public sealed record TransportSecurityOptions(bool AllowInsecure)
{
    /// <summary>環境変数の明示値を読み取る。未設定は false（HTTP を許さない）。</summary>
    /// <param name="configuration">設定。</param>
    /// <param name="settingName">設定名（例 <c>APP_ALLOW_INSECURE</c>）。</param>
    public static TransportSecurityOptions FromConfiguration(IConfiguration configuration, string settingName)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingName);

        var raw = configuration[settingName];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new TransportSecurityOptions(false);
        }

        return bool.TryParse(raw.Trim(), out var allowInsecure)
            ? new TransportSecurityOptions(allowInsecure)
            : throw new InvalidOperationException($"{settingName} must be true or false.");
    }
}
