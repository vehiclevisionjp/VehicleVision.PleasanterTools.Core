using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using static VehicleVision.PleasanterTools.Core.Web.AuthenticationSettingParser;

namespace VehicleVision.PleasanterTools.Core.Web;

/// <summary>本アプリに居ない利用者が IdP から来たときの扱い。</summary>
public enum SamlUnknownUserPolicy
{
    Reject = 0,
    Register = 1,
}

/// <summary>ログイン ID の取り出し先。</summary>
public enum SamlLoginIdSource
{
    NameId = 0,
    Claim = 1,
}

/// <summary>SAML の設定。信頼の起点は設定で渡された署名証明書だけとし、外部取得しない。</summary>
public sealed class SamlOptions
{
    /// <summary>直接作る場合も、アプリの既定の役割を渡す。</summary>
    public SamlOptions(string defaultRegisterRole)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultRegisterRole);
        RegisterRole = defaultRegisterRole;
    }

    public bool Enabled { get; init; }
    public string EntityId { get; init; } = string.Empty;
    public string IdpEntityId { get; init; } = string.Empty;
    public Uri? SingleSignOnUrl { get; init; }
    /// <summary>IdP の署名証明書。入れ替え中は複数枚を渡せる。</summary>
    public ImmutableArray<X509Certificate2> IdpCertificates { get; init; } = [];
    public Uri? SingleLogoutUrl { get; init; }
    public bool SingleLogoutEnabled => Enabled && SingleLogoutUrl is not null;
    public SamlUnknownUserPolicy UnknownUser { get; init; }
    public string RegisterRole { get; init; }
    public SamlLoginIdSource LoginIdSource { get; init; }
    public string LoginIdClaim { get; init; } = string.Empty;
    public string ButtonLabel { get; init; } = string.Empty;

    /// <summary>外部設定から読む。接頭辞には末尾の下線も含める。</summary>
    public static SamlOptions FromConfiguration(
        IConfiguration configuration, string prefix, IReadOnlyList<string> registerRoles, string defaultRegisterRole)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return FromValues(key => configuration[key], new(prefix), registerRoles, defaultRegisterRole);
    }

    /// <summary>生値から読む。true 以外は無効とし、他の設定を読まない（既存アプリと同じ）。</summary>
    /// <exception cref="AuthenticationSettingException">有効な設定の誤り。</exception>
    public static SamlOptions FromValues(
        Func<string, string?> valueOf, SamlSettingKeys keys,
        IReadOnlyList<string> registerRoles, string defaultRegisterRole)
    {
        ArgumentNullException.ThrowIfNull(valueOf);
        ArgumentNullException.ThrowIfNull(keys);
        if (!(bool.TryParse(valueOf(keys.EnabledKey)?.Trim(), out var enabled) && enabled))
        {
            return new(defaultRegisterRole);
        }

        var entityId = Trim(valueOf(keys.EntityIdKey));
        var idpEntityId = Trim(valueOf(keys.IdpEntityIdKey));
        var singleSignOnUrl = Trim(valueOf(keys.SingleSignOnUrlKey));
        var certificates = ReadCertificates(valueOf(keys.IdpCertificateKey), keys.IdpCertificateKey);
        Require(entityId, keys, keys.EntityIdKey);
        Require(idpEntityId, keys, keys.IdpEntityIdKey);
        Require(singleSignOnUrl, keys, keys.SingleSignOnUrlKey);
        if (certificates.IsEmpty)
        {
            throw new AuthenticationSettingException(AuthenticationSettingError.CertificateRequired,
                $"{keys.IdpCertificateKey} が要ります。IdP の署名証明書（PEM か base64）を入れてください。",
                [keys.IdpCertificateKey]);
        }
        if (!Uri.TryCreate(singleSignOnUrl, UriKind.Absolute, out var ssoUri)
            || (ssoUri.Scheme != Uri.UriSchemeHttps && ssoUri.Scheme != Uri.UriSchemeHttp))
        {
            throw AbsoluteUrlInvalid(keys.SingleSignOnUrlKey, singleSignOnUrl);
        }
        var singleLogoutUrl = Trim(valueOf(keys.SingleLogoutUrlKey));
        Uri? sloUri = null;
        if (singleLogoutUrl.Length > 0
            && (!Uri.TryCreate(singleLogoutUrl, UriKind.Absolute, out sloUri)
                || (sloUri.Scheme != Uri.UriSchemeHttps && sloUri.Scheme != Uri.UriSchemeHttp)))
        {
            throw AbsoluteUrlInvalid(keys.SingleLogoutUrlKey, singleLogoutUrl);
        }
        var loginIdSource = ParseEnum<SamlLoginIdSource>(valueOf(keys.LoginIdSourceKey), keys.LoginIdSourceKey)
            ?? SamlLoginIdSource.NameId;
        var loginIdClaim = Trim(valueOf(keys.LoginIdClaimKey));
        if (loginIdSource == SamlLoginIdSource.Claim && loginIdClaim.Length == 0)
        {
            throw new AuthenticationSettingException(AuthenticationSettingError.LoginIdClaimRequired,
                $"{keys.LoginIdSourceKey}=Claim のときは {keys.LoginIdClaimKey} が要ります。",
                [keys.LoginIdSourceKey, keys.LoginIdClaimKey]);
        }
        return new(defaultRegisterRole)
        {
            Enabled = true,
            EntityId = entityId,
            IdpEntityId = idpEntityId,
            SingleSignOnUrl = ssoUri,
            SingleLogoutUrl = sloUri,
            IdpCertificates = certificates,
            UnknownUser = ParseEnum<SamlUnknownUserPolicy>(valueOf(keys.UnknownUserKey), keys.UnknownUserKey)
                ?? SamlUnknownUserPolicy.Reject,
            RegisterRole = ParseRole(valueOf(keys.RegisterRoleKey), keys.RegisterRoleKey, registerRoles, defaultRegisterRole),
            LoginIdSource = loginIdSource,
            LoginIdClaim = loginIdClaim,
            ButtonLabel = Trim(valueOf(keys.ButtonLabelKey)),
        };
    }

    private static void Require(string value, SamlSettingKeys keys, string key)
    {
        if (value.Length == 0)
        {
            throw Required(keys.EnabledKey, key);
        }
    }

    private static AuthenticationSettingException AbsoluteUrlInvalid(string key, string value) =>
        new(AuthenticationSettingError.AbsoluteUrlInvalid,
            $"{key} は http(s) の絶対 URL で書いてください: {value}", [key, value]);

    private static ImmutableArray<X509Certificate2> ReadCertificates(string? raw, string key)
    {
        var trimmed = Trim(raw);
        if (trimmed.Length == 0)
        {
            return [];
        }
        if (trimmed.Contains("-----BEGIN", StringComparison.Ordinal))
        {
            return ReadPem(trimmed, key);
        }
        var builder = ImmutableArray.CreateBuilder<X509Certificate2>();
        foreach (var part in trimmed.Split(
            [',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                builder.Add(X509CertificateLoader.LoadCertificate(Convert.FromBase64String(part)));
            }
            catch (Exception exception) when (exception is FormatException or CryptographicException)
            {
                throw new AuthenticationSettingException(AuthenticationSettingError.CertificateUnreadable,
                    $"{key} を証明書として読めませんでした。PEM か、DER を base64 にしたものを入れてください。",
                    [key], exception);
            }
        }
        return builder.ToImmutable();
    }

    private static ImmutableArray<X509Certificate2> ReadPem(string pem, string key)
    {
        var builder = ImmutableArray.CreateBuilder<X509Certificate2>();
        var index = 0;
        while (true)
        {
            var begin = pem.IndexOf("-----BEGIN", index, StringComparison.Ordinal);
            if (begin < 0)
            {
                break;
            }
            var endMarker = pem.IndexOf("-----END", begin, StringComparison.Ordinal);
            if (endMarker < 0)
            {
                throw new AuthenticationSettingException(AuthenticationSettingError.CertificatePemIncomplete,
                    $"{key} の PEM が途中で終わっています。", [key]);
            }
            var end = pem.IndexOf("-----", endMarker + "-----END".Length, StringComparison.Ordinal);
            var length = (end < 0 ? pem.Length : end + "-----".Length) - begin;
            try
            {
                builder.Add(X509Certificate2.CreateFromPem(pem.AsSpan(begin, length)));
            }
            catch (Exception exception) when (exception is ArgumentException or CryptographicException)
            {
                throw new AuthenticationSettingException(AuthenticationSettingError.CertificatePemUnreadable,
                    $"{key} の PEM を読めませんでした。", [key], exception);
            }
            index = begin + length;
        }
        if (builder.Count == 0)
        {
            throw new AuthenticationSettingException(AuthenticationSettingError.CertificateNotFound,
                $"{key} に証明書が見つかりませんでした。", [key]);
        }
        return builder.ToImmutable();
    }
}
