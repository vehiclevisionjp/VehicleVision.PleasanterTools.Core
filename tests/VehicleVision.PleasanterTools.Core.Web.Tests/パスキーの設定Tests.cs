namespace VehicleVision.PleasanterTools.Core.Web.Tests;

public sealed class パスキーの設定Tests
{
    private static AdminPasskeyOptions Read(params (string Key, string Value)[] values)
    {
        var map = values.ToDictionary(v => v.Key, v => v.Value);
        return AdminPasskeyOptions.FromValues(key => map.GetValueOrDefault(key), new AdminPasskeySettingKeys("APP_"), "App");
    }

    [Fact]
    public void 設定名は接頭辞から導く()
    {
        var keys = new AdminPasskeySettingKeys("APP_");
        Assert.Equal("APP_ADMIN_PASSKEY", keys.EnabledKey);
        Assert.Equal("APP_ADMIN_PASSKEY_RPID", keys.RpIdKey);
        Assert.Equal("APP_ADMIN_PASSKEY_ORIGINS", keys.OriginsKey);
    }

    [Fact]
    public void 未指定なら無効でRP_IDもOriginも導出に任せる()
    {
        var options = Read();
        Assert.False(options.Enabled);
        Assert.Null(options.RpId);
        Assert.Empty(options.Origins);
        Assert.Equal(10, options.MaxPasskeysPerUser);
        Assert.Equal(TimeSpan.FromMinutes(5), options.ChallengeLifetime);
    }

    [Fact]
    public void 明示した値を読み_Originはカンマ区切りで複数_重複と大文字を正規化する()
    {
        var options = Read(("APP_ADMIN_PASSKEY", "TRUE"), ("APP_ADMIN_PASSKEY_RPID", "Example.COM"),
            ("APP_ADMIN_PASSKEY_ORIGINS", "https://admin.example.com, https://EXAMPLE.com:8443 ,https://admin.example.com"));
        Assert.True(options.Enabled);
        Assert.Equal("example.com", options.RpId);
        Assert.Equal(["https://admin.example.com", "https://example.com:8443"], options.Origins);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("1")]
    public void 真偽値の書き間違いは起動時に止める(string value)
    {
        var error = Assert.Throws<AuthenticationSettingException>(() => Read(("APP_ADMIN_PASSKEY", value)));
        Assert.Equal(AuthenticationSettingError.BooleanValueInvalid, error.Error);
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("example.com:8443")]
    [InlineData("example.com/path")]
    [InlineData("192.0.2.1")]
    [InlineData("exa mple.com")]
    public void RP_IDはホスト名だけ(string value)
    {
        var error = Assert.Throws<AuthenticationSettingException>(() => Read(("APP_ADMIN_PASSKEY_RPID", value)));
        Assert.Equal(AuthenticationSettingError.PasskeyRpIdInvalid, error.Error);
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://example.com/admin")]
    [InlineData("example.com")]
    [InlineData("https://user@example.com")]
    [InlineData("https://192.0.2.1")]
    public void Originはhttps_のスキーム_ホスト_ポートだけ(string value)
    {
        var error = Assert.Throws<AuthenticationSettingException>(() => Read(("APP_ADMIN_PASSKEY_ORIGINS", value)));
        Assert.Equal(AuthenticationSettingError.PasskeyOriginInvalid, error.Error);
    }

    [Fact]
    public void 開発のlocalhostだけhttpのOriginを許す()
    {
        Assert.Equal(["http://localhost:5000"], Read(("APP_ADMIN_PASSKEY_ORIGINS", "http://localhost:5000")).Origins);
        Assert.Equal(["http://app.localhost"], Read(("APP_ADMIN_PASSKEY_ORIGINS", "http://app.localhost")).Origins);
    }

    [Fact]
    public void OriginのホストがRP_IDと合わなければ止める_サブドメインは許す()
    {
        var error = Assert.Throws<AuthenticationSettingException>(() =>
            Read(("APP_ADMIN_PASSKEY_RPID", "example.com"), ("APP_ADMIN_PASSKEY_ORIGINS", "https://evil.test")));
        Assert.Equal(AuthenticationSettingError.PasskeyOriginRpIdMismatch, error.Error);
        // 末尾が同じだけのホスト（notexample.com）は従属ではない
        Assert.Throws<AuthenticationSettingException>(() =>
            Read(("APP_ADMIN_PASSKEY_RPID", "example.com"), ("APP_ADMIN_PASSKEY_ORIGINS", "https://notexample.com")));
        Assert.Single(Read(("APP_ADMIN_PASSKEY_RPID", "example.com"), ("APP_ADMIN_PASSKEY_ORIGINS", "https://a.b.example.com")).Origins);
    }

    [Fact]
    public void 未指定のRP_IDとOriginはリクエストのホストから導く()
    {
        var rp = PasskeyRelyingParty.Resolve(AdminPasskeyOptions.Disabled, "https", "Admin.Example.com:8443");
        Assert.Equal("admin.example.com", rp!.RpId);
        Assert.Equal(["https://admin.example.com:8443"], rp.Origins);
        Assert.Equal(["https://admin.example.com"], PasskeyRelyingParty.Resolve(AdminPasskeyOptions.Disabled, "https", "admin.example.com")!.Origins);
    }

    [Fact]
    public void 明示を優先し_リクエストのホストは使わない()
    {
        var options = Read(("APP_ADMIN_PASSKEY_RPID", "example.com"), ("APP_ADMIN_PASSKEY_ORIGINS", "https://a.example.com,https://b.example.com"));
        var rp = PasskeyRelyingParty.Resolve(options, "https", "ignored.invalid");
        Assert.Equal("example.com", rp!.RpId);
        Assert.Equal(["https://a.example.com", "https://b.example.com"], rp.Origins);
    }

    [Theory]
    [InlineData("http", "admin.example.com")]
    [InlineData("https", "192.0.2.1")]
    [InlineData("https", "[::1]")]
    public void 導けない要求はnull(string scheme, string host) =>
        Assert.Null(PasskeyRelyingParty.Resolve(AdminPasskeyOptions.Disabled, scheme, host));

    [Fact]
    public void RP_IDだけ明示しOriginを導くとき_ホストが従属しなければnull()
    {
        var options = Read(("APP_ADMIN_PASSKEY_RPID", "example.com"));
        Assert.NotNull(PasskeyRelyingParty.Resolve(options, "https", "admin.example.com"));
        Assert.Null(PasskeyRelyingParty.Resolve(options, "https", "evil.test"));
    }
}
