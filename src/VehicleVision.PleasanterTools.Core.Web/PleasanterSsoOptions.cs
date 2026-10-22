using System.Collections.Immutable;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using static VehicleVision.PleasanterTools.Core.Web.AuthenticationSettingParser;

namespace VehicleVision.PleasanterTools.Core.Web;

/// <summary>本アプリに居ない利用者が Pleasanter から来たときの扱い。</summary>
public enum PleasanterSsoUnknownUserPolicy
{
    Reject = 0,
    Register = 1,
}

/// <summary>Pleasanter の cookie で本人を確かめる設定。</summary>
public sealed class PleasanterSsoOptions : IPleasanterSsoVerificationOptions
{
    public const string DefaultCookieNames = ".AspNetCore.Cookies,Pleasanter_SessionGuid";
    public const int DefaultRevalidateMinutes = 5;
    public const int MinimumRevalidateMinutes = 1;
    public const int MaximumRevalidateMinutes = 60;
    public const int DefaultTimeoutSeconds = 5;
    public const int MinimumTimeoutSeconds = 1;
    public const int MaximumTimeoutSeconds = 30;

    /// <summary>本アプリの cookie の接頭辞と既定の役割を渡す。直接作る場合も必須。</summary>
    public PleasanterSsoOptions(string ownCookiePrefix, string defaultRegisterRole)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownCookiePrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultRegisterRole);
        OwnCookiePrefix = ownCookiePrefix;
        RegisterRole = defaultRegisterRole;
    }

    /// <summary>設定に何を書いても転送しない、本アプリの cookie の接頭辞。</summary>
    public string OwnCookiePrefix { get; }
    public ImmutableArray<int> AllowedDeptIds { get; init; } = [];
    public ImmutableArray<int> AllowedGroupIds { get; init; } = [];
    public bool HasMembershipRestriction => !AllowedDeptIds.IsDefaultOrEmpty || !AllowedGroupIds.IsDefaultOrEmpty;
    public bool Enabled { get; init; }
    public Uri? InternalBaseUrl { get; init; }
    public string LoginUrl { get; init; } = string.Empty;
    public string LogoutUrl { get; init; } = string.Empty;
    public ImmutableArray<string> CookieNamePrefixes { get; init; } = [".AspNetCore.Cookies", "Pleasanter_SessionGuid"];
    public PleasanterSsoUnknownUserPolicy UnknownUser { get; init; }
    public string RegisterRole { get; init; }
    public TimeSpan RevalidateInterval { get; init; } = TimeSpan.FromMinutes(DefaultRevalidateMinutes);
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(DefaultTimeoutSeconds);
    public string ButtonLabel { get; init; } = string.Empty;

    /// <summary>外部設定から読む。接頭辞には末尾の下線も含める。</summary>
    public static PleasanterSsoOptions FromConfiguration(
        IConfiguration configuration, string prefix, string ownCookiePrefix,
        IReadOnlyList<string> registerRoles, string defaultRegisterRole)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return FromValues(key => configuration[key], new(prefix), ownCookiePrefix, registerRoles, defaultRegisterRole);
    }

    /// <summary>生値から読む。無効のときも、数値・列挙・URL・cookie・所属の誤記は拒否する。</summary>
    /// <exception cref="AuthenticationSettingException">設定の誤り。</exception>
    public static PleasanterSsoOptions FromValues(
        Func<string, string?> valueOf, PleasanterSsoSettingKeys keys, string ownCookiePrefix,
        IReadOnlyList<string> registerRoles, string defaultRegisterRole)
    {
        ArgumentNullException.ThrowIfNull(valueOf);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownCookiePrefix);
        var enabled = ParseBoolean(valueOf(keys.EnabledKey), keys.EnabledKey);
        var internalBaseUrl = Trim(valueOf(keys.InternalBaseUrlKey));
        var loginUrl = Trim(valueOf(keys.LoginUrlKey));
        var logoutUrl = Trim(valueOf(keys.LogoutUrlKey));
        var cookieNames = Trim(valueOf(keys.CookieNamesKey));
        var unknownUser = ParseEnum<PleasanterSsoUnknownUserPolicy>(
            valueOf(keys.UnknownUserKey), keys.UnknownUserKey, rejectNumeric: true) ?? PleasanterSsoUnknownUserPolicy.Reject;
        var registerRole = ParseRole(valueOf(keys.RegisterRoleKey), keys.RegisterRoleKey, registerRoles, defaultRegisterRole);
        var revalidateMinutes = ParseInteger(valueOf(keys.RevalidateMinutesKey), keys.RevalidateMinutesKey,
            DefaultRevalidateMinutes, MinimumRevalidateMinutes, MaximumRevalidateMinutes);
        var timeoutSeconds = ParseInteger(valueOf(keys.TimeoutSecondsKey), keys.TimeoutSecondsKey,
            DefaultTimeoutSeconds, MinimumTimeoutSeconds, MaximumTimeoutSeconds);
        var prefixes = ParseCookieNames(
            cookieNames.Length == 0 ? DefaultCookieNames : cookieNames, keys, ownCookiePrefix);

        Uri? internalUri = null;
        if (internalBaseUrl.Length > 0)
        {
            internalUri = ParseBaseUrl(internalBaseUrl, keys.InternalBaseUrlKey);
        }
        if (loginUrl.Length > 0)
        {
            ValidateBrowserUrl(loginUrl, keys.LoginUrlKey);
        }
        if (logoutUrl.Length > 0)
        {
            ValidateBrowserUrl(logoutUrl, keys.LogoutUrlKey);
        }
        if (enabled)
        {
            if (internalUri is null)
            {
                throw Required(keys.EnabledKey, keys.InternalBaseUrlKey);
            }
            if (loginUrl.Length == 0)
            {
                throw Required(keys.EnabledKey, keys.LoginUrlKey);
            }
        }

        return new(ownCookiePrefix, defaultRegisterRole)
        {
            Enabled = enabled,
            AllowedDeptIds = ParseAllowedIds(valueOf(keys.AllowedDeptIdsKey), keys.AllowedDeptIdsKey),
            AllowedGroupIds = ParseAllowedIds(valueOf(keys.AllowedGroupIdsKey), keys.AllowedGroupIdsKey),
            InternalBaseUrl = internalUri,
            LoginUrl = loginUrl,
            LogoutUrl = logoutUrl,
            CookieNamePrefixes = prefixes,
            UnknownUser = unknownUser,
            RegisterRole = registerRole,
            RevalidateInterval = TimeSpan.FromMinutes(revalidateMinutes),
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
            ButtonLabel = Trim(valueOf(keys.ButtonLabelKey)),
        };
    }

    /// <summary>転送する cookie の前方一致指定を読む。本アプリの接頭辞と両方向で重なる指定は拒否。</summary>
    public static ImmutableArray<string> ParseCookieNames(
        string raw, PleasanterSsoSettingKeys keys, string ownCookiePrefix)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownCookiePrefix);
        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var part in (raw ?? string.Empty).Split(
            [',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!part.All(IsCookieNameCharacter))
            {
                throw new AuthenticationSettingException(AuthenticationSettingError.CookieNameInvalidChars,
                    $"{keys.CookieNamesKey} に cookie の名前として使えない文字があります: {part}", [keys.CookieNamesKey, part]);
            }
            if (part.StartsWith(ownCookiePrefix, StringComparison.OrdinalIgnoreCase)
                || ownCookiePrefix.StartsWith(part, StringComparison.OrdinalIgnoreCase))
            {
                throw new AuthenticationSettingException(AuthenticationSettingError.CookieNameOverlapsOwn,
                    $"{keys.CookieNamesKey} に本アプリの cookie（{ownCookiePrefix} で始まる名前）と重なる指定があります: {part}",
                    [keys.CookieNamesKey, ownCookiePrefix, part]);
            }
            if (!builder.Contains(part, StringComparer.Ordinal))
            {
                builder.Add(part);
            }
        }
        if (builder.Count == 0)
        {
            throw new AuthenticationSettingException(AuthenticationSettingError.CookieNamesEmpty,
                $"{keys.CookieNamesKey} に転送する cookie の名前がありません。", [keys.CookieNamesKey]);
        }
        return builder.ToImmutable();
    }

    public bool ShouldForwardCookie(string name) =>
        !string.IsNullOrEmpty(name)
        && !name.StartsWith(OwnCookiePrefix, StringComparison.OrdinalIgnoreCase)
        && CookieNamePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));

    private static bool IsCookieNameCharacter(char character) =>
        character is > ' ' and < (char)0x7F
        && "()<>@,;:\\\"/[]?={}".IndexOf(character, StringComparison.Ordinal) < 0;

    private static Uri ParseBaseUrl(string value, string key)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new AuthenticationSettingException(AuthenticationSettingError.InternalUrlInvalid,
                $"{key} は http(s) の絶対 URL で書いてください（問い合わせ文字列・資格情報は不可）: {value}", [key, value]);
        }
        return uri.AbsolutePath.EndsWith('/') ? uri : new Uri(uri.GetLeftPart(UriPartial.Path) + "/");
    }

    private static void ValidateBrowserUrl(string value, string key)
    {
        if (value.StartsWith('/'))
        {
            if (value.StartsWith("//", StringComparison.Ordinal)
                || value.StartsWith("/\\", StringComparison.Ordinal)
                || value.Any(char.IsControl))
            {
                throw BrowserUrlInvalid(key, value);
            }
            return;
        }
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw BrowserUrlInvalid(key, value);
        }
    }

    private static AuthenticationSettingException BrowserUrlInvalid(string key, string value) =>
        new(AuthenticationSettingError.BrowserUrlInvalid,
            $"{key} は / で始まる同じホストのパスか、http(s) の絶対 URL で書いてください: {value}", [key, value]);

    private static ImmutableArray<int> ParseAllowedIds(string? raw, string key)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }
        var parts = raw.Split([',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (raw.Length > 1024 || parts.Length is 0 or > 64)
        {
            throw new AuthenticationSettingException(AuthenticationSettingError.IdListTooLong,
                $"{key} は正の ID を 64 件以内、1024 文字以内で指定してください。", [key]);
        }
        var ids = ImmutableArray.CreateBuilder<int>();
        foreach (var part in parts)
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            {
                throw new AuthenticationSettingException(AuthenticationSettingError.IdListInvalid,
                    $"{key} は正の ID をカンマか改行で区切って指定してください。", [key]);
            }
            if (!ids.Contains(id))
            {
                ids.Add(id);
            }
        }
        return ids.ToImmutable();
    }

    private static bool ParseBoolean(string? value, string key)
    {
        var trimmed = Trim(value);
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

    private static int ParseInteger(string? value, string key, int defaultValue, int minimum, int maximum)
    {
        var trimmed = Trim(value);
        if (trimmed.Length == 0)
        {
            return defaultValue;
        }
        if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            || parsed < minimum || parsed > maximum)
        {
            throw new AuthenticationSettingException(AuthenticationSettingError.IntegerRangeInvalid,
                $"{key} は {minimum} 以上 {maximum} 以下の整数で指定してください: {trimmed}",
                [key, minimum, maximum, trimmed]);
        }
        return parsed;
    }
}
