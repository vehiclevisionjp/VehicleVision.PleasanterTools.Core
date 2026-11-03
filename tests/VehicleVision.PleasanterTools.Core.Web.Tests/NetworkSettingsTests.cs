using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace VehicleVision.PleasanterTools.Core.Web.Tests;

public sealed class NetworkSettingsTests
{
    private const string Health = "T_HEALTH";
    private const string Monitoring = "T_MON";

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();

    [Fact]
    public void CIDRの一覧を読み空は空にする()
    {
        Assert.Empty(ForwardedProxyNetworks.Parse(null, "S"));
        Assert.Empty(ForwardedProxyNetworks.Parse("  ", "S"));
        Assert.Equal(2, ForwardedProxyNetworks.Parse("10.0.0.0/8, 192.168.1.0/24,", "S").Count);
    }

    [Fact]
    public void CIDRでない値は設定名つきで止める()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ForwardedProxyNetworks.Parse("10.0.0.0/8,abc", "T_NET"));

        Assert.Contains("T_NET", ex.Message);
        Assert.Contains("abc", ex.Message);
    }

    [Fact]
    public void 転送ヘッダの設定は指定したネットワークだけを信頼する()
    {
        var networks = ForwardedProxyNetworks.Parse("10.0.0.0/8", "S");

        var options = ForwardedProxyNetworks.CreateOptions(networks);

        Assert.Empty(options.KnownProxies);
        Assert.Single(options.KnownIPNetworks);
        Assert.Equal(
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
            options.ForwardedHeaders);
    }

    [Fact]
    public void 設定が空なら制限しない()
    {
        var restrictions = EndpointNetworkRestrictions.FromConfiguration(Config(), Health);

        Assert.True(restrictions.IsAllowed("/healthz", IPAddress.Parse("8.8.8.8")));
        Assert.True(restrictions.IsAllowed("/healthz", null));
    }

    [Fact]
    public void 生存確認は許可したネットワークだけに見せる()
    {
        var restrictions = EndpointNetworkRestrictions.FromConfiguration(Config((Health, "10.0.0.0/8")), Health);

        Assert.True(restrictions.IsAllowed("/healthz", IPAddress.Parse("10.1.2.3")));
        Assert.True(restrictions.IsAllowed("/READY/x", IPAddress.Parse("10.1.2.3")));
        Assert.False(restrictions.IsAllowed("/healthz", IPAddress.Parse("8.8.8.8")));
        Assert.False(restrictions.IsAllowed("/healthz", null));
        Assert.True(restrictions.IsAllowed("/api/x", IPAddress.Parse("8.8.8.8")));
        Assert.True(restrictions.IsAllowed("/healthzz", IPAddress.Parse("8.8.8.8")));
    }

    [Fact]
    public void IPv4射影のIPv6はIPv4として照合する()
    {
        var restrictions = EndpointNetworkRestrictions.FromConfiguration(Config((Health, "10.0.0.0/8")), Health);

        Assert.True(restrictions.IsAllowed("/healthz", IPAddress.Parse("::ffff:10.1.2.3")));
        Assert.False(restrictions.IsAllowed("/healthz", IPAddress.Parse("::ffff:8.8.8.8")));
    }

    [Fact]
    public void 追加の規則は独立し_inherit_で生存確認を引き継ぐ()
    {
        var rule = new EndpointNetworkRule(Monitoring, ["/api/monitoring"]);

        var own = EndpointNetworkRestrictions.FromConfiguration(
            Config((Health, "10.0.0.0/8"), (Monitoring, "192.168.0.0/16")), Health, rule);
        Assert.True(own.IsAllowed("/api/monitoring/status", IPAddress.Parse("192.168.1.1")));
        Assert.False(own.IsAllowed("/api/monitoring/status", IPAddress.Parse("10.1.1.1")));

        var inherit = EndpointNetworkRestrictions.FromConfiguration(
            Config((Health, "10.0.0.0/8"), (Monitoring, " inherit ")), Health, rule);
        Assert.True(inherit.IsAllowed("/api/monitoring/status", IPAddress.Parse("10.1.1.1")));
        Assert.False(inherit.IsAllowed("/api/monitoring/status", IPAddress.Parse("192.168.1.1")));

        var open = EndpointNetworkRestrictions.FromConfiguration(Config((Health, "10.0.0.0/8")), Health, rule);
        Assert.True(open.IsAllowed("/api/monitoring/status", IPAddress.Parse("8.8.8.8")));
    }

    [Fact]
    public void 追加の規則のCIDR誤りは設定名で止める()
    {
        var rule = new EndpointNetworkRule(Monitoring, ["/m"]);

        var ex = Assert.Throws<InvalidOperationException>(
            () => EndpointNetworkRestrictions.FromConfiguration(Config((Monitoring, "x")), Health, rule));

        Assert.Contains(Monitoring, ex.Message);
    }

    [Theory]
    [InlineData("10.1.1.1", 200)]
    [InlineData("8.8.8.8", 404)]
    public async Task パイプラインは許可外に404を返す(string remote, int expected)
    {
        var restrictions = EndpointNetworkRestrictions.FromConfiguration(Config((Health, "10.0.0.0/8")), Health);
        var builder = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());
        builder.UseEndpointNetworkRestrictions(restrictions);
        builder.Run(_ => Task.CompletedTask);

        var context = new DefaultHttpContext();
        context.Request.Path = "/healthz";
        context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
        await builder.Build()(context);

        Assert.Equal(expected, context.Response.StatusCode);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" true ", true)]
    [InlineData("False", false)]
    public void HTTP許可の設定を読む(string? raw, bool expected)
    {
        var configuration = raw is null ? Config() : Config(("T_INSECURE", raw));

        Assert.Equal(expected, TransportSecurityOptions.FromConfiguration(configuration, "T_INSECURE").AllowInsecure);
    }

    [Fact]
    public void HTTP許可の誤記は設定名つきで止める()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => TransportSecurityOptions.FromConfiguration(Config(("T_INSECURE", "yes")), "T_INSECURE"));

        Assert.Contains("T_INSECURE", ex.Message);
    }
}
