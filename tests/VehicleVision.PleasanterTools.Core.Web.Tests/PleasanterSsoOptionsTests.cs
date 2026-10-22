using Microsoft.Extensions.Configuration;

namespace VehicleVision.PleasanterTools.Core.Web.Tests;

/// <summary>アプリの Options 単体試験を移植。保存・優先順位の試験はアプリに残す。</summary>
public sealed class PleasanterSsoOptionsTests
{
    private static readonly PleasanterSsoSettingKeys Keys = new("APP_");
    private static readonly string[] Roles = ["Editor", "Administrator", "Manager"];

    private static PleasanterSsoOptions Read(Dictionary<string, string?> values, string ownCookiePrefix = "app.") =>
        PleasanterSsoOptions.FromValues(values.GetValueOrDefault, Keys, ownCookiePrefix, Roles, "Editor");

    private static Dictionary<string, string?> Enabled() => new()
    {
        [Keys.EnabledKey] = "true",
        [Keys.InternalBaseUrlKey] = "http://pleasanter:8080",
        [Keys.LoginUrlKey] = "/users/login",
    };

    [Fact]
    public void 未設定は無効と既定値()
    {
        var options = Read([]);
        Assert.False(options.Enabled);
        Assert.Equal([".AspNetCore.Cookies", "Pleasanter_SessionGuid"], options.CookieNamePrefixes.ToArray());
        Assert.Equal(PleasanterSsoUnknownUserPolicy.Reject, options.UnknownUser);
        Assert.Equal("Editor", options.RegisterRole);
        Assert.Equal(TimeSpan.FromMinutes(5), options.RevalidateInterval);
        Assert.Equal(TimeSpan.FromSeconds(5), options.Timeout);
        Assert.False(options.HasMembershipRestriction);
    }

    [Fact]
    public void 設定からも同じ接頭辞で読める()
    {
        var values = Enabled();
        values[Keys.InternalBaseUrlKey] = "http://pleasanter:8080/path";
        var options = PleasanterSsoOptions.FromConfiguration(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build(), "APP_", "app.", Roles, "Manager");
        Assert.True(options.Enabled);
        Assert.Equal("Manager", options.RegisterRole);
        Assert.Equal(new Uri("http://pleasanter:8080/path/"), options.InternalBaseUrl);
    }

    [Theory]
    [InlineData("INTERNALBASEURL")]
    [InlineData("LOGINURL")]
    public void 有効なのに必須の設定がなければ引数付き例外(string suffix)
    {
        var key = "APP_PLEASANTERSSO_" + suffix;
        var values = Enabled();
        values.Remove(key);
        var exception = Assert.Throws<AuthenticationSettingException>(() => Read(values));
        Assert.IsAssignableFrom<InvalidOperationException>(exception);
        Assert.Equal(AuthenticationSettingError.RequiredWhenEnabled, exception.Error);
        Assert.Equal(new object[] { Keys.EnabledKey, key }, exception.Arguments.ToArray());
        Assert.Contains(key, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("INTERNALBASEURL", "ftp://pleasanter", AuthenticationSettingError.InternalUrlInvalid)]
    [InlineData("INTERNALBASEURL", "pleasanter:8080", AuthenticationSettingError.InternalUrlInvalid)]
    [InlineData("INTERNALBASEURL", "http://user:pass@pleasanter/", AuthenticationSettingError.InternalUrlInvalid)]
    [InlineData("INTERNALBASEURL", "http://pleasanter/?x=1", AuthenticationSettingError.InternalUrlInvalid)]
    [InlineData("INTERNALBASEURL", "http://pleasanter/#x", AuthenticationSettingError.InternalUrlInvalid)]
    [InlineData("LOGINURL", "javascript:alert(1)", AuthenticationSettingError.BrowserUrlInvalid)]
    [InlineData("LOGINURL", "//evil.example.com/users/login", AuthenticationSettingError.BrowserUrlInvalid)]
    [InlineData("LOGINURL", "/\\evil.example.com", AuthenticationSettingError.BrowserUrlInvalid)]
    [InlineData("LOGINURL", "/login\nbad", AuthenticationSettingError.BrowserUrlInvalid)]
    [InlineData("LOGOUTURL", "javascript:alert(1)", AuthenticationSettingError.BrowserUrlInvalid)]
    [InlineData("ENABLED", "yes", AuthenticationSettingError.BooleanValueInvalid)]
    [InlineData("UNKNOWNUSER", "Allow", AuthenticationSettingError.UnknownValue)]
    [InlineData("UNKNOWNUSER", "1", AuthenticationSettingError.UnknownValue)]
    [InlineData("REGISTERROLE", "Root", AuthenticationSettingError.UnknownValue)]
    [InlineData("REGISTERROLE", "1", AuthenticationSettingError.UnknownValue)]
    [InlineData("REGISTERROLE", "Editor, Administrator", AuthenticationSettingError.UnknownValue)]
    [InlineData("REVALIDATEMINUTES", "0", AuthenticationSettingError.IntegerRangeInvalid)]
    [InlineData("REVALIDATEMINUTES", "61", AuthenticationSettingError.IntegerRangeInvalid)]
    [InlineData("REVALIDATEMINUTES", "five", AuthenticationSettingError.IntegerRangeInvalid)]
    [InlineData("TIMEOUTSECONDS", "0", AuthenticationSettingError.IntegerRangeInvalid)]
    [InlineData("TIMEOUTSECONDS", "31", AuthenticationSettingError.IntegerRangeInvalid)]
    public void 誤記は無効のときも拒否する(string suffix, string value, AuthenticationSettingError error)
    {
        var key = "APP_PLEASANTERSSO_" + suffix;
        foreach (var values in new[] { Enabled(), new Dictionary<string, string?>() })
        {
            values[key] = value;
            var exception = Assert.Throws<AuthenticationSettingException>(() => Read(values));
            Assert.Equal(error, exception.Error);
            Assert.Equal(key, exception.Arguments[0]);
        }
    }

    [Fact]
    public void 整数範囲の例外は翻訳に必要な引数を順番どおり持つ()
    {
        var exception = Assert.Throws<AuthenticationSettingException>(() =>
            Read(new() { [Keys.TimeoutSecondsKey] = " 31 " }));
        Assert.Equal(new object[] { Keys.TimeoutSecondsKey, 1, 30, "31" }, exception.Arguments.ToArray());
    }

    [Theory]
    [InlineData("https://www.example.jp/users/login")]
    [InlineData("/users/login?ReturnUrl=%2F")]
    public void ログイン画面は同じホストのパスか絶対URL(string loginUrl)
    {
        var values = Enabled();
        values[Keys.LoginUrlKey] = loginUrl;
        Assert.Equal(loginUrl, Read(values).LoginUrl);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("app.")]
    [InlineData("app.admin")]
    [InlineData("APP.ADMIN")]
    [InlineData(".AspNetCore.Cookies,app.admin")]
    public void 本アプリのcookieと両方向で重なる指定は拒否(string raw)
    {
        var exception = Assert.Throws<AuthenticationSettingException>(() =>
            PleasanterSsoOptions.ParseCookieNames(raw, Keys, "app."));
        Assert.Equal(AuthenticationSettingError.CookieNameOverlapsOwn, exception.Error);
        Assert.Equal("app.", exception.Arguments[1]);
    }

    [Theory]
    [InlineData("a b", AuthenticationSettingError.CookieNameInvalidChars)]
    [InlineData("a;b", AuthenticationSettingError.CookieNameInvalidChars)]
    [InlineData("a=b", AuthenticationSettingError.CookieNameInvalidChars)]
    [InlineData("a\u007fb", AuthenticationSettingError.CookieNameInvalidChars)]
    [InlineData(",,,", AuthenticationSettingError.CookieNamesEmpty)]
    [InlineData("", AuthenticationSettingError.CookieNamesEmpty)]
    public void cookieの文字と空の指定を検査する(string raw, AuthenticationSettingError error)
    {
        var exception = Assert.Throws<AuthenticationSettingException>(() =>
            PleasanterSsoOptions.ParseCookieNames(raw, Keys, "app."));
        Assert.Equal(error, exception.Error);
    }

    [Fact]
    public void cookieの前方一致と本アプリの除外と重複除去()
    {
        var options = Read(Enabled());
        foreach (var name in new[] { ".AspNetCore.Cookies", ".AspNetCore.CookiesC1", ".AspNetCore.CookiesC12", "Pleasanter_SessionGuid" })
        {
            Assert.True(options.ShouldForwardCookie(name));
        }
        foreach (var name in new[] { "app.admin", "APP.admin.pending", "app.asset", ".AspNetCore.Antiforgery.x", "", ".aspnetcore.Cookies" })
        {
            Assert.False(options.ShouldForwardCookie(name));
        }
        Assert.Equal(["Other", "other"], PleasanterSsoOptions.ParseCookieNames("Other,Other\nother", Keys, "app.").ToArray());
        var direct = new PleasanterSsoOptions("app.", "Editor") { CookieNamePrefixes = ["a"] };
        Assert.False(direct.ShouldForwardCookie("APP.admin"));
        Assert.True(direct.ShouldForwardCookie("another"));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("1,a")]
    [InlineData("1 2")]
    [InlineData(",,,")]
    [InlineData("2147483648")]
    [InlineData("+1")]
    public void 所属の誤記を無制限にしない(string raw)
    {
        foreach (var key in new[] { Keys.AllowedDeptIdsKey, Keys.AllowedGroupIdsKey })
        {
            Assert.Throws<AuthenticationSettingException>(() => Read(new() { [key] = raw }));
        }
    }

    [Fact]
    public void 所属の境界は重複除去の前に件数と文字数を検査する()
    {
        Assert.Equal([10, 20], Read(new() { [Keys.AllowedDeptIdsKey] = " 10,20\n10 " }).AllowedDeptIds.ToArray());
        Assert.True(Read(new() { [Keys.AllowedGroupIdsKey] = "1" }).HasMembershipRestriction);
        Assert.Equal(64, Read(new() { [Keys.AllowedDeptIdsKey] = string.Join(',', Enumerable.Range(1, 64)) }).AllowedDeptIds.Length);
        Assert.Single(Read(new() { [Keys.AllowedDeptIdsKey] = "1" + new string(' ', 1023) }).AllowedDeptIds);
        foreach (var raw in new[] { string.Join(',', Enumerable.Repeat(1, 65)), "1" + new string(' ', 1024) })
        {
            var exception = Assert.Throws<AuthenticationSettingException>(() => Read(new() { [Keys.AllowedDeptIdsKey] = raw }));
            Assert.Equal(AuthenticationSettingError.IdListTooLong, exception.Error);
        }
    }

    [Theory]
    [InlineData("1", "1")]
    [InlineData("60", "30")]
    [InlineData("15", "3")]
    public void 数値の境界と役割名を読む(string minutes, string seconds)
    {
        var values = Enabled();
        values[Keys.UnknownUserKey] = "register";
        values[Keys.RegisterRoleKey] = " manager ";
        values[Keys.RevalidateMinutesKey] = minutes;
        values[Keys.TimeoutSecondsKey] = seconds;
        values[Keys.ButtonLabelKey] = "  ログイン  ";
        var options = Read(values);
        Assert.Equal(PleasanterSsoUnknownUserPolicy.Register, options.UnknownUser);
        Assert.Equal("Manager", options.RegisterRole);
        Assert.Equal(TimeSpan.FromMinutes(int.Parse(minutes)), options.RevalidateInterval);
        Assert.Equal(TimeSpan.FromSeconds(int.Parse(seconds)), options.Timeout);
        Assert.Equal("ログイン", options.ButtonLabel);
    }

    [Fact]
    public void 設定キーとcookie接頭辞と役割はアプリから受ける()
    {
        var keys = new PleasanterSsoSettingKeys("OTHER_");
        var options = PleasanterSsoOptions.FromValues(
            key => key == keys.RegisterRoleKey ? "reader" : null, keys, "other_", ["Reader"], "Reader");
        Assert.Equal("Reader", options.RegisterRole);
        Assert.Equal("other_", options.OwnCookiePrefix);
        Assert.Equal("OTHER_PLEASANTERSSO_BUTTONLABEL", keys.ButtonLabelKey);
        Assert.Throws<ArgumentException>(() => Read([], ""));
        Assert.Throws<ArgumentException>(() => PleasanterSsoOptions.FromValues(_ => null, keys, "app.", [], "Editor"));
        Assert.Throws<ArgumentException>(() => PleasanterSsoOptions.FromValues(_ => null, keys, "app.", Roles, "Missing"));
    }
}
