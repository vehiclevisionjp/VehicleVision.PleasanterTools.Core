namespace VehicleVision.PleasanterTools.Core.Web;

/// <summary>アプリの接頭辞から Pleasanter SSO の設定名を導く。</summary>
public sealed class PleasanterSsoSettingKeys
{
    /// <param name="prefix">アプリの設定の接頭辞。末尾の下線も含める（例: <c>APP_</c>）。</param>
    public PleasanterSsoSettingKeys(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        Prefix = prefix + "PLEASANTERSSO_";
    }

    private string Prefix { get; }
    public string EnabledKey => Prefix + "ENABLED";
    public string AllowedDeptIdsKey => Prefix + "ALLOWEDDEPTIDS";
    public string AllowedGroupIdsKey => Prefix + "ALLOWEDGROUPIDS";
    public string InternalBaseUrlKey => Prefix + "INTERNALBASEURL";
    public string LoginUrlKey => Prefix + "LOGINURL";
    public string LogoutUrlKey => Prefix + "LOGOUTURL";
    public string CookieNamesKey => Prefix + "COOKIENAMES";
    public string UnknownUserKey => Prefix + "UNKNOWNUSER";
    public string RegisterRoleKey => Prefix + "REGISTERROLE";
    public string RevalidateMinutesKey => Prefix + "REVALIDATEMINUTES";
    public string TimeoutSecondsKey => Prefix + "TIMEOUTSECONDS";
    public string ButtonLabelKey => Prefix + "BUTTONLABEL";
}

/// <summary>アプリの接頭辞から SAML の設定名を導く。</summary>
public sealed class SamlSettingKeys
{
    /// <param name="prefix">アプリの設定の接頭辞。末尾の下線も含める（例: <c>APP_</c>）。</param>
    public SamlSettingKeys(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        Prefix = prefix + "SAML_";
    }

    private string Prefix { get; }
    public string EnabledKey => Prefix + "ENABLED";
    public string EntityIdKey => Prefix + "ENTITYID";
    public string IdpEntityIdKey => Prefix + "IDPENTITYID";
    public string SingleSignOnUrlKey => Prefix + "SINGLESIGNONURL";
    public string SingleLogoutUrlKey => Prefix + "SINGLELOGOUTURL";
    public string IdpCertificateKey => Prefix + "IDPCERTIFICATE";
    public string UnknownUserKey => Prefix + "UNKNOWNUSER";
    public string RegisterRoleKey => Prefix + "REGISTERROLE";
    public string LoginIdSourceKey => Prefix + "LOGINIDSOURCE";
    public string LoginIdClaimKey => Prefix + "LOGINIDCLAIM";
    public string ButtonLabelKey => Prefix + "BUTTONLABEL";
}
