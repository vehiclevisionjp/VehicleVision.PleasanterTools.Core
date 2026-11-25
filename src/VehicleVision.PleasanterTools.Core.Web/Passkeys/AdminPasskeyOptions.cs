using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace VehicleVision.PleasanterTools.Core.Web;

/// <summary>アプリの接頭辞からパスキーの設定名を導く。</summary>
public sealed class AdminPasskeySettingKeys
{
    /// <param name="prefix">アプリの設定の接頭辞。末尾の下線も含める（例: <c>APP_</c>）。</param>
    public AdminPasskeySettingKeys(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        Prefix = prefix + "ADMIN_PASSKEY";
    }

    private string Prefix { get; }

    /// <summary>パスキーを有効にするか（<c>true</c> / <c>false</c>。既定は無効）。</summary>
    public string EnabledKey => Prefix;

    /// <summary>RP ID（ホスト名だけ）。未指定ならリクエストのホストから導く。</summary>
    public string RpIdKey => Prefix + "_RPID";

    /// <summary>許可する Origin（カンマ区切り）。未指定ならリクエストの Origin から導く。</summary>
    public string OriginsKey => Prefix + "_ORIGINS";
}

/// <summary>管理画面のパスキーの設定。<b>既定は無効。</b>知らない値・書き間違いは起動時に止める。</summary>
/// <remarks>
/// 仕様は 認証とセッションの仕様書 3.10.7。RP ID を変えると、登録済みのパスキーはすべて使えなくなる
/// （資格情報は RP ID に結び付くため）。運用手順書の警告を読むこと。
/// </remarks>
public sealed class AdminPasskeyOptions
{
    /// <summary>1 人あたりの登録の上限の既定（3.10.11 で確定）。</summary>
    public const int DefaultMaxPasskeysPerUser = 10;

    /// <summary>無効の設定（既定）。</summary>
    public static AdminPasskeyOptions Disabled { get; } = new();

    /// <summary>有効か。無効の間は、すべての口が 404（P10）。既存の行は消さない。</summary>
    public bool Enabled { get; init; }

    /// <summary>明示した RP ID（小文字・ホスト名だけ）。<c>null</c> ならリクエストのホストから導く。</summary>
    public string? RpId { get; init; }

    /// <summary>明示した Origin の一覧（スキーム・ホスト・ポートの完全一致）。空ならリクエストの Origin から導く。</summary>
    public IReadOnlyList<string> Origins { get; init; } = [];

    /// <summary>認証器の画面に出す RP の名前（アプリが渡す。設定ではない）。</summary>
    public string RpName { get; init; } = "Admin";

    /// <summary>チャレンジの有効期間（既定 5 分。決定 7/9）。</summary>
    public TimeSpan ChallengeLifetime { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>1 人あたりの登録の上限。</summary>
    public int MaxPasskeysPerUser { get; init; } = DefaultMaxPasskeysPerUser;

    /// <summary>ブラウザへ渡すタイムアウト（ミリ秒。ヒントで、ブラウザが上書きしうる）。</summary>
    public uint TimeoutMilliseconds { get; init; } = 60_000;

    /// <summary>外部設定から読む。接頭辞には末尾の下線も含める。</summary>
    /// <exception cref="AuthenticationSettingException">設定の誤り。</exception>
    public static AdminPasskeyOptions FromConfiguration(IConfiguration configuration, string prefix, string rpName)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return FromValues(key => configuration[key], new(prefix), rpName);
    }

    /// <summary>生値から読む。無効のときも、書き間違い（真偽値・RP ID・Origin）は拒否する。</summary>
    /// <exception cref="AuthenticationSettingException">設定の誤り。</exception>
    public static AdminPasskeyOptions FromValues(Func<string, string?> valueOf, AdminPasskeySettingKeys keys, string rpName)
    {
        ArgumentNullException.ThrowIfNull(valueOf);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentException.ThrowIfNullOrWhiteSpace(rpName);

        var enabled = ParseBoolean(valueOf(keys.EnabledKey), keys.EnabledKey);
        var rpId = ParseRpId(valueOf(keys.RpIdKey), keys.RpIdKey);
        var origins = ParseOrigins(valueOf(keys.OriginsKey), keys.OriginsKey);
        if (rpId is not null)
        {
            foreach (var origin in origins)
            {
                var host = new Uri(origin).IdnHost;
                if (!PasskeyRelyingParty.HostBelongsToRpId(host, rpId))
                {
                    throw new AuthenticationSettingException(AuthenticationSettingError.PasskeyOriginRpIdMismatch,
                        $"{keys.OriginsKey} の {origin} のホストが、{keys.RpIdKey} の {rpId} と一致も従属もしません。",
                        [keys.OriginsKey, origin, keys.RpIdKey, rpId]);
                }
            }
        }

        return new AdminPasskeyOptions { Enabled = enabled, RpId = rpId, Origins = origins, RpName = rpName.Trim() };
    }

    private static bool ParseBoolean(string? value, string key)
    {
        var trimmed = AuthenticationSettingParser.Trim(value);
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (!bool.TryParse(trimmed, out var parsed))
        {
            throw new AuthenticationSettingException(AuthenticationSettingError.BooleanValueInvalid,
                $"{key} は true か false で指定してください: {trimmed}", [key, trimmed]);
        }

        return parsed;
    }

    private static string? ParseRpId(string? value, string key)
    {
        var trimmed = AuthenticationSettingParser.Trim(value);
        if (trimmed.Length == 0)
        {
            return null;
        }

        // ホスト名だけ。スキーム・ポート・パス・空白・IP リテラルは受けない（WebAuthn の RP ID はドメイン）
        if (!PasskeyRelyingParty.IsValidRpId(trimmed))
        {
            throw new AuthenticationSettingException(AuthenticationSettingError.PasskeyRpIdInvalid,
                $"{key} はホスト名だけを書いてください（スキーム・ポート・パスは付けない）: {trimmed}", [key, trimmed]);
        }

        return trimmed.ToLower(CultureInfo.InvariantCulture);
    }

    private static string[] ParseOrigins(string? value, string key)
    {
        var trimmed = AuthenticationSettingParser.Trim(value);
        if (trimmed.Length == 0)
        {
            return [];
        }

        var origins = new List<string>();
        foreach (var entry in trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var origin = PasskeyRelyingParty.NormalizeOrigin(entry)
                ?? throw new AuthenticationSettingException(AuthenticationSettingError.PasskeyOriginInvalid,
                    $"{key} は「https://ホスト[:ポート]」をカンマ区切りで書いてください（開発の localhost だけ http も可）: {entry}",
                    [key, entry]);
            if (!origins.Contains(origin, StringComparer.Ordinal))
            {
                origins.Add(origin);
            }
        }

        return [.. origins];
    }
}
