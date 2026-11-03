using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace VehicleVision.PleasanterTools.Core.Web;

/// <summary>送信元ネットワークを絞る経路の規則。</summary>
/// <param name="SettingName">許可するネットワーク（カンマ区切り CIDR）の設定名。値 <c>inherit</c> で生存確認の設定を引き継ぐ。</param>
/// <param name="PathPrefixes">対象の経路の接頭辞（セグメント単位で照合する。大文字小文字は区別しない）。</param>
public sealed record EndpointNetworkRule(string SettingName, IReadOnlyList<string> PathPrefixes);

/// <summary>生存確認や監視 API などの、送信元ネットワークを制限する。</summary>
/// <remarks>
/// <para>
/// 生存確認（<c>/healthz</c>・<c>/ready</c>）の規則は常に持つ。監視 API や OpenAPI など、アプリ固有の経路は
/// <see cref="EndpointNetworkRule"/> で足す。設定が空の規則は制限しない（既定は全許可）。
/// </para>
/// <para>Questionnaire・IdentityProvider が別々に持っていた実装を 1 つにしたもの（Issue #139）。</para>
/// </remarks>
public sealed class EndpointNetworkRestrictions
{
    /// <summary>生存確認の設定を引き継ぐ値。</summary>
    public const string Inherit = "inherit";

    private static readonly string[] HealthPaths = ["/healthz", "/ready"];

    private readonly List<(IReadOnlyList<string> Paths, IReadOnlyList<IPNetwork> Networks)> rules;

    private EndpointNetworkRestrictions(List<(IReadOnlyList<string>, IReadOnlyList<IPNetwork>)> rules)
    {
        this.rules = rules;
    }

    /// <summary>設定を読み、CIDR でない値があれば起動を止める。</summary>
    /// <param name="configuration">設定。</param>
    /// <param name="healthSettingName">生存確認の許可ネットワークの設定名。</param>
    /// <param name="additionalRules">生存確認のほかに絞る経路の規則。</param>
    public static EndpointNetworkRestrictions FromConfiguration(
        IConfiguration configuration,
        string healthSettingName,
        params EndpointNetworkRule[] additionalRules)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(healthSettingName);
        ArgumentNullException.ThrowIfNull(additionalRules);

        var health = ForwardedProxyNetworks.Parse(configuration[healthSettingName], healthSettingName);
        var rules = new List<(IReadOnlyList<string>, IReadOnlyList<IPNetwork>)> { (HealthPaths, health) };
        foreach (var rule in additionalRules)
        {
            var raw = configuration[rule.SettingName];
            var networks = string.Equals(raw?.Trim(), Inherit, StringComparison.Ordinal)
                ? health
                : ForwardedProxyNetworks.Parse(raw, rule.SettingName);
            rules.Add((rule.PathPrefixes, networks));
        }

        return new EndpointNetworkRestrictions(rules);
    }

    /// <summary>対象経路を今の送信元へ見せてよいか。</summary>
    public bool IsAllowed(PathString path, IPAddress? remoteAddress)
    {
        IReadOnlyList<IPNetwork>? networks = null;
        foreach (var (paths, candidate) in rules)
        {
            if (paths.Any(prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                networks = candidate;
                break;
            }
        }

        if (networks is null || networks.Count == 0)
        {
            return true;
        }

        if (remoteAddress is null)
        {
            return false;
        }

        // リバースプロキシが IPv4 を IPv6 射影で渡しても、IPv4 の CIDR と同じ送信元として扱う。
        var normalized = remoteAddress.IsIPv4MappedToIPv6
            ? remoteAddress.MapToIPv4()
            : remoteAddress;
        return networks.Any(network => network.Contains(normalized));
    }
}

/// <summary>送信元を絞るパイプライン。</summary>
public static class EndpointNetworkRestrictionExtensions
{
    /// <summary>許可したネットワーク以外には対象経路の存在を隠す（404）。</summary>
    public static IApplicationBuilder UseEndpointNetworkRestrictions(
        this IApplicationBuilder app,
        EndpointNetworkRestrictions restrictions)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(restrictions);

        return app.Use(async (context, next) =>
        {
            if (!restrictions.IsAllowed(context.Request.Path, context.Connection.RemoteIpAddress))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await next(context).ConfigureAwait(false);
        });
    }
}
