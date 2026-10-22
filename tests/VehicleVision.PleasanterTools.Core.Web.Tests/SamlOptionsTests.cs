using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;

namespace VehicleVision.PleasanterTools.Core.Web.Tests;

/// <summary>既存の SAML 設定テスト。ITfoxtec の照合設定の試験はアプリに残す。</summary>
public sealed class SamlOptionsTests
{
    private static readonly SamlSettingKeys Keys = new("APP_");
    private static readonly string[] Roles = ["Editor", "Administrator", "Manager"];

    private static X509Certificate2 Certificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=test-idp", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    private static Dictionary<string, string?> Minimum()
    {
        using var certificate = Certificate();
        return new()
        {
            [Keys.EnabledKey] = "true",
            [Keys.EntityIdKey] = "https://app.example.jp",
            [Keys.IdpEntityIdKey] = "https://idp.example.com/entity",
            [Keys.SingleSignOnUrlKey] = "https://idp.example.com/sso",
            [Keys.IdpCertificateKey] = certificate.ExportCertificatePem(),
        };
    }

    private static SamlOptions Read(Dictionary<string, string?> values) =>
        SamlOptions.FromValues(values.GetValueOrDefault, Keys, Roles, "Editor");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("false")]
    [InlineData("yes")]
    [InlineData("1")]
    public void true以外は無効で他の値を読まない(string? enabled)
    {
        var options = SamlOptions.FromValues(key =>
        {
            Assert.Equal(Keys.EnabledKey, key);
            return enabled;
        }, Keys, Roles, "Editor");
        Assert.False(options.Enabled);
        Assert.False(options.SingleLogoutEnabled);
        Assert.Empty(options.IdpCertificates);
        Assert.Equal(SamlUnknownUserPolicy.Reject, options.UnknownUser);
        Assert.Equal("Editor", options.RegisterRole);
    }

    [Fact]
    public void 設定から接頭辞を付けて読み取る()
    {
        var options = SamlOptions.FromConfiguration(
            new ConfigurationBuilder().AddInMemoryCollection(Minimum()).Build(), "APP_", Roles, "Editor");
        Assert.True(options.Enabled);
        Assert.Equal("https://app.example.jp", options.EntityId);
        Assert.Equal("https://idp.example.com/entity", options.IdpEntityId);
        Assert.Equal(new Uri("https://idp.example.com/sso"), options.SingleSignOnUrl);
        Assert.Equal("CN=test-idp", Assert.Single(options.IdpCertificates).Subject);
        Assert.Equal(SamlLoginIdSource.NameId, options.LoginIdSource);
    }

    [Theory]
    [InlineData("ENTITYID", AuthenticationSettingError.RequiredWhenEnabled)]
    [InlineData("IDPENTITYID", AuthenticationSettingError.RequiredWhenEnabled)]
    [InlineData("SINGLESIGNONURL", AuthenticationSettingError.RequiredWhenEnabled)]
    [InlineData("IDPCERTIFICATE", AuthenticationSettingError.CertificateRequired)]
    public void 足りない値を種類と設定名で伝える(string suffix, AuthenticationSettingError error)
    {
        var key = "APP_SAML_" + suffix;
        var values = Minimum();
        values.Remove(key);
        var exception = Assert.Throws<AuthenticationSettingException>(() => Read(values));
        Assert.Equal(error, exception.Error);
        Assert.Contains(key, exception.Arguments);
        Assert.Contains(key, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ftp://idp.example.com/sso")]
    [InlineData("/sso")]
    [InlineData("idp.example.com")]
    public void SSOとSLOはhttpの絶対URLだけ(string value)
    {
        foreach (var key in new[] { Keys.SingleSignOnUrlKey, Keys.SingleLogoutUrlKey })
        {
            var values = Minimum();
            values[key] = value;
            var exception = Assert.Throws<AuthenticationSettingException>(() => Read(values));
            Assert.Equal(AuthenticationSettingError.AbsoluteUrlInvalid, exception.Error);
            Assert.Equal(new object[] { key, value }, exception.Arguments.ToArray());
        }
    }

    [Fact]
    public void SLOは任意で設定したときだけ有効()
    {
        var values = Minimum();
        Assert.False(Read(values).SingleLogoutEnabled);
        values[Keys.SingleLogoutUrlKey] = "http://idp.example.com/slo";
        var options = Read(values);
        Assert.True(options.SingleLogoutEnabled);
        Assert.Equal(new Uri("http://idp.example.com/slo"), options.SingleLogoutUrl);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PEMもbase64も複数枚読める(bool pem)
    {
        using var certificate = Certificate();
        var raw = pem ? certificate.ExportCertificatePem() : Convert.ToBase64String(certificate.RawData);
        var values = Minimum();
        values[Keys.IdpCertificateKey] = raw + (pem ? "\n" : ",\n") + raw;
        var options = Read(values);
        Assert.Equal(2, options.IdpCertificates.Length);
        Assert.All(options.IdpCertificates, actual => Assert.Equal(certificate.Thumbprint, actual.Thumbprint));
    }

    [Theory]
    [InlineData("証明書ではない", AuthenticationSettingError.CertificateUnreadable)]
    [InlineData("AQID", AuthenticationSettingError.CertificateUnreadable)]
    [InlineData("-----BEGIN CERTIFICATE-----", AuthenticationSettingError.CertificatePemIncomplete)]
    [InlineData("-----BEGIN CERTIFICATE-----\nabc\n-----END CERTIFICATE-----", AuthenticationSettingError.CertificatePemUnreadable)]
    [InlineData("-----BEGIN", AuthenticationSettingError.CertificatePemIncomplete)]
    [InlineData(",,,", AuthenticationSettingError.CertificateRequired)]
    public void 証明書の誤りを引数と元の例外で伝える(string value, AuthenticationSettingError error)
    {
        var values = Minimum();
        values[Keys.IdpCertificateKey] = value;
        var exception = Assert.Throws<AuthenticationSettingException>(() => Read(values));
        Assert.Equal(error, exception.Error);
        Assert.Equal(new object[] { Keys.IdpCertificateKey }, exception.Arguments.ToArray());
        if (error is AuthenticationSettingError.CertificateUnreadable or AuthenticationSettingError.CertificatePemUnreadable)
        {
            Assert.NotNull(exception.InnerException);
        }
    }

    [Theory]
    [InlineData("Register", SamlUnknownUserPolicy.Register)]
    [InlineData("reject", SamlUnknownUserPolicy.Reject)]
    [InlineData("REGISTER", SamlUnknownUserPolicy.Register)]
    [InlineData("1", SamlUnknownUserPolicy.Register)]
    [InlineData("Reject, Register", SamlUnknownUserPolicy.Register)]
    public void 未登録の扱いは既存の列挙の読み方を保つ(string raw, SamlUnknownUserPolicy expected)
    {
        var values = Minimum();
        values[Keys.UnknownUserKey] = raw;
        Assert.Equal(expected, Read(values).UnknownUser);
    }

    [Theory]
    [InlineData("UNKNOWNUSER", "JitRegister")]
    [InlineData("UNKNOWNUSER", "2")]
    [InlineData("LOGINIDSOURCE", "Other")]
    [InlineData("REGISTERROLE", "Root")]
    [InlineData("REGISTERROLE", "1")]
    public void 知らない値は既定へ落とさない(string suffix, string raw)
    {
        var key = "APP_SAML_" + suffix;
        var values = Minimum();
        values[key] = raw;
        var exception = Assert.Throws<AuthenticationSettingException>(() => Read(values));
        Assert.Equal(AuthenticationSettingError.UnknownValue, exception.Error);
        Assert.Equal(key, exception.Arguments[0]);
        Assert.Equal(raw, exception.Arguments[1]);
    }

    [Theory]
    [InlineData("Claim")]
    [InlineData("1")]
    [InlineData("NameId, Claim")]
    public void 属性を使うときだけ属性名が要る(string raw)
    {
        var values = Minimum();
        values[Keys.LoginIdSourceKey] = raw;
        var exception = Assert.Throws<AuthenticationSettingException>(() => Read(values));
        Assert.Equal(AuthenticationSettingError.LoginIdClaimRequired, exception.Error);
        Assert.Equal(new object[] { Keys.LoginIdSourceKey, Keys.LoginIdClaimKey }, exception.Arguments.ToArray());
        values[Keys.LoginIdClaimKey] = " login_id ";
        values[Keys.RegisterRoleKey] = " manager ";
        var options = Read(values);
        Assert.Equal(SamlLoginIdSource.Claim, options.LoginIdSource);
        Assert.Equal("login_id", options.LoginIdClaim);
        Assert.Equal("Manager", options.RegisterRole);
    }

    [Fact]
    public void 既定の役割と設定名もアプリが決める()
    {
        Assert.Equal("OTHER_SAML_IDPCERTIFICATE", new SamlSettingKeys("OTHER_").IdpCertificateKey);
        var values = Minimum();
        var options = SamlOptions.FromValues(values.GetValueOrDefault, Keys, ["Reader"], "Reader");
        Assert.Equal("Reader", options.RegisterRole);
    }
}
