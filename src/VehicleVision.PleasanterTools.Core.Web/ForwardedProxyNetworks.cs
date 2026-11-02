using System.Net;
using Microsoft.AspNetCore.Builder;
using ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders;

namespace VehicleVision.PleasanterTools.Core.Web;

/// <summary>信頼するリバースプロキシのネットワーク（CIDR）の設定を読む。</summary>
/// <remarks>
/// 設定名はアプリごとに違うので引数で受ける。Questionnaire・IdentityProvider が別々に持っていた実装と、
/// AtomFeed・CalendarFeed の転送ヘッダの組み立てを 1 つにしたもの（Issue #139）。
/// </remarks>
public static class ForwardedProxyNetworks
{
    /// <summary>カンマ区切りの CIDR を一覧にする。空なら空の一覧。</summary>
    /// <param name="value">設定値。</param>
    /// <param name="settingName">設定名。CIDR でない値があるときの例外文言に入る。</param>
    public static IReadOnlyList<IPNetwork> Parse(string? value, string settingName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingName);

        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(entry => ParseOne(entry, settingName))
            .ToArray();
    }

    /// <summary>
    /// 指定したネットワークだけを信頼する <see cref="ForwardedHeadersOptions"/> を作る。
    /// </summary>
    /// <remarks>
    /// ⚠️ **既定で信頼されるループバックも外す。** X-Forwarded-For を鵜呑みにすると、IP ごとのレート制限を回避できる。
    /// ネットワークが空のときに <c>UseForwardedHeaders</c> を呼ぶかどうかは、呼び出し側が決める
    /// （呼ばなければ転送ヘッダは無視される）。
    /// </remarks>
    public static ForwardedHeadersOptions CreateOptions(
        IEnumerable<IPNetwork> networks,
        ForwardedHeaders headers = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost)
    {
        ArgumentNullException.ThrowIfNull(networks);

        var options = new ForwardedHeadersOptions { ForwardedHeaders = headers };
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var network in networks)
        {
            options.KnownIPNetworks.Add(network);
        }

        return options;
    }

    private static IPNetwork ParseOne(string value, string settingName) =>
        IPNetwork.TryParse(value, out var network)
            ? network
            : throw new InvalidOperationException(
                $"{settingName} に CIDR でない値がある: {value}");
}
