using System.Net.Http.Json;
using System.Text.Json;

namespace VehicleVision.PleasanterTools.Core.Web.Testing;

/// <summary>共有契約が確かめる操作。経路の文字列はアプリが指定する。</summary>
public enum AdminContractOperation
{
    PasswordLogin,
    TotpLogin,
    EnrollmentBegin,
    EnrollmentComplete,
    ReenrollmentBegin,
    ReenrollmentComplete,
    Users,
    ChangePassword,
    AuditLogs,
    ResetTwoFactor,
    DisableUser,
    DisableOwnTwoFactor,
    RecoveryLogin,
}

/// <summary>追加契約を適用する仕組み。未宣言は未対応であり、検証済みを意味しない。</summary>
public enum AdminContractFeature
{
    SessionRevocation,
    ResetTwoFactorPermission,
    TwoFactorPolicyChange,
    SamlLogin,
    PleasanterSsoLogin,
    SamlJit,
    PleasanterSsoJit,
    RecoveryCodes,
    ExternalReconfirmation,
    ReenrollmentRateLimit,
}

/// <summary>入口と仕組みの有無。既存の入口の非存在検査は維持する。</summary>
public sealed record AdminContractCapabilities(IReadOnlySet<AdminContractOperation> Operations)
{
    /// <summary>追加契約の対象。既存ホストは変更せず、未対応として動く。</summary>
    public IReadOnlySet<AdminContractFeature> Features { get; init; } = new HashSet<AdminContractFeature>();

    /// <summary>指定した入口が存在するか。</summary>
    public bool Supports(AdminContractOperation operation) => Operations.Contains(operation);

    /// <summary>指定した仕組みを検証するか。</summary>
    public bool Supports(AdminContractFeature feature) => Features.Contains(feature);
}

/// <summary>アプリの要求本文へ変換する値。null のパスワードは未指定の試験に使う。</summary>
public sealed record AdminContractInput(
    string? LoginId = null,
    string? Password = null,
    string? Code = null,
    string? NewPassword = null)
{
    /// <inheritdoc />
    public override string ToString() => nameof(AdminContractInput);
}

/// <summary>HTTP の方法・経路と、アプリの JSON 本文への変換。</summary>
public sealed record AdminContractEndpoint(
    HttpMethod Method,
    string Path,
    Func<AdminContractInput, object?> Body)
{
    /// <summary>対象者を経路に含む操作の経路変換。未指定なら従来どおり Path を使う。</summary>
    public Func<AdminContractInput, string>? PathForInput { get; init; }

    /// <summary>送信ごとに新しい要求を作る。Body が null を返す場合は本文を付けない。</summary>
    public HttpRequestMessage CreateRequest(AdminContractInput input)
    {
        var request = new HttpRequestMessage(Method, PathForInput?.Invoke(input) ?? Path);
        if (Body(input) is { } body)
        {
            request.Content = JsonContent.Create(body);
        }
        return request;
    }
}

/// <summary>認証の段階ごとの cookie 名。</summary>
public sealed record AdminContractCookies(string Session, string Pending, string Reenroll);

/// <summary>アプリ固有の HTTP の表現。状態コードと不変条件は共有テストが決める。</summary>
public sealed class AdminContractSettings
{
    /// <summary>無い入口も含め、全操作の探査先を指定する。</summary>
    public required IReadOnlyDictionary<AdminContractOperation, AdminContractEndpoint> Endpoints { get; init; }

    /// <summary>認証の段階ごとに異なる cookie 名。</summary>
    public required AdminContractCookies Cookies { get; init; }

    /// <summary>パスワードだけを通した応答で、次の段階を表す JSON のプロパティ名。</summary>
    public required string NextProperty { get; init; }

    /// <summary>登録済みの人に TOTP を要求する、次の段階の値。</summary>
    public required string TotpNextValue { get; init; }

    /// <summary>ログイン失敗時の JSON オブジェクトのプロパティ名と型。余分な項目も拒む。</summary>
    public required IReadOnlyDictionary<string, JsonValueKind> LoginFailureShape { get; init; }

    /// <summary>登録完了時だけ返す復旧コードの配列の項目名。</summary>
    public string RecoveryCodesProperty { get; init; } = "recoveryCodes";

    /// <summary>JIT の正常系で付与する、アプリ固有の非 Administrator の役割。</summary>
    public string? JitNonAdministratorRole { get; init; }

    internal void Validate(AdminContractCapabilities capabilities)
    {
        var requiredOperations = capabilities.Features.SelectMany(RequiredOperations).ToHashSet();
        // 公開済みホストに、新操作の未使用の探査先を要求しない。
        foreach (var operation in Enum.GetValues<AdminContractOperation>()
            .Where(operation => operation <= AdminContractOperation.AuditLogs
                || requiredOperations.Contains(operation)))
        {
            if (!Endpoints.TryGetValue(operation, out var endpoint)
                || string.IsNullOrWhiteSpace(endpoint.Path))
            {
                throw new ArgumentException($"存在しない入口を含め、{operation} の探査先が必要です。");
            }
        }
        var names = new[] { Cookies.Session, Cookies.Pending, Cookies.Reenroll };
        if (names.Any(string.IsNullOrWhiteSpace) || names.Distinct(StringComparer.Ordinal).Count() != 3)
        {
            throw new ArgumentException("Session・Pending・Reenroll の cookie 名は空でなく、互いに異なる必要があります。");
        }
        if (string.IsNullOrWhiteSpace(NextProperty) || string.IsNullOrWhiteSpace(TotpNextValue)
            || LoginFailureShape.Count == 0)
        {
            throw new ArgumentException("次の段階と、ログイン失敗の本文の形を指定してください。");
        }
        foreach (var operation in capabilities.Operations)
        {
            if (!Enum.IsDefined(operation))
            {
                throw new ArgumentException($"未定義の操作です: {operation}");
            }
            if (operation != AdminContractOperation.PasswordLogin
                && (!capabilities.Supports(AdminContractOperation.PasswordLogin)
                    || !capabilities.Supports(AdminContractOperation.TotpLogin)))
            {
                throw new ArgumentException("C1〜C4 の存在する入口を検証するには、パスワードと TOTP のログインが必要です。");
            }
        }
        if (capabilities.Supports(AdminContractOperation.ReenrollmentComplete)
            && !capabilities.Supports(AdminContractOperation.ReenrollmentBegin))
        {
            throw new ArgumentException("登録し直しの完了を検証するには、開始の入口が必要です。");
        }
        foreach (var feature in capabilities.Features)
        {
            if (!Enum.IsDefined(feature))
            {
                throw new ArgumentException($"未定義の仕組みです: {feature}");
            }
            if (RequiredOperations(feature).Any(operation => !capabilities.Supports(operation)))
            {
                throw new ArgumentException($"{feature} に必要な入口が宣言されていません。");
            }
        }
        if ((capabilities.Supports(AdminContractFeature.SamlJit)
                && !capabilities.Supports(AdminContractFeature.SamlLogin))
            || (capabilities.Supports(AdminContractFeature.PleasanterSsoJit)
                && !capabilities.Supports(AdminContractFeature.PleasanterSsoLogin))
            || (capabilities.Supports(AdminContractFeature.ExternalReconfirmation)
                && !capabilities.Supports(AdminContractFeature.SamlLogin)
                && !capabilities.Supports(AdminContractFeature.PleasanterSsoLogin)))
        {
            throw new ArgumentException("JIT・外部再確認には、対応する外部ログインの宣言が必要です。");
        }
        if (capabilities.Supports(AdminContractFeature.RecoveryCodes)
            && string.IsNullOrWhiteSpace(RecoveryCodesProperty))
        {
            throw new ArgumentException("復旧コードの項目名が必要です。");
        }
        if ((capabilities.Supports(AdminContractFeature.SamlJit)
                || capabilities.Supports(AdminContractFeature.PleasanterSsoJit))
            && (string.IsNullOrWhiteSpace(JitNonAdministratorRole)
                || string.Equals(JitNonAdministratorRole, "Administrator", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("JIT の正常系には非 Administrator の役割が必要です。");
        }
    }

    private static AdminContractOperation[] RequiredOperations(AdminContractFeature feature) => feature switch
    {
        AdminContractFeature.SessionRevocation =>
            [AdminContractOperation.ResetTwoFactor, AdminContractOperation.DisableUser],
        AdminContractFeature.ResetTwoFactorPermission => [AdminContractOperation.ResetTwoFactor],
        AdminContractFeature.TwoFactorPolicyChange =>
            [AdminContractOperation.ReenrollmentBegin, AdminContractOperation.ReenrollmentComplete],
        AdminContractFeature.RecoveryCodes =>
            [AdminContractOperation.RecoveryLogin, AdminContractOperation.ReenrollmentBegin,
                AdminContractOperation.ReenrollmentComplete],
        AdminContractFeature.ExternalReconfirmation =>
            [AdminContractOperation.ReenrollmentBegin, AdminContractOperation.ReenrollmentComplete,
                AdminContractOperation.DisableOwnTwoFactor],
        AdminContractFeature.ReenrollmentRateLimit => [AdminContractOperation.ReenrollmentBegin],
        AdminContractFeature.SamlJit or AdminContractFeature.PleasanterSsoJit =>
            [AdminContractOperation.Users, AdminContractOperation.PasswordLogin, AdminContractOperation.TotpLogin],
        _ => [],
    };
}
