using System.Buffers.Text;
using System.Formats.Cbor;
using System.Text.Json;
using Fido2NetLib;
using Fido2NetLib.Exceptions;
using Fido2NetLib.Objects;

namespace VehicleVision.PleasanterTools.Core.Web;

/// <summary>パスキーの要求ごとの文脈（HTTP に依存しない）。</summary>
/// <param name="Scheme">要求のスキーム。</param>
/// <param name="Host">要求のホスト（<c>host[:port]</c>。転送ヘッダは、信頼するネットワークだけ適用した後の値）。</param>
/// <param name="IpAddress">送信元（監査用）。</param>
/// <param name="UserAgent">User-Agent（現状は使わない。将来の監査用）。</param>
public sealed record PasskeyRequestContext(string Scheme, string Host, string? IpAddress = null, string? UserAgent = null);

/// <summary>ログイン中の本人。</summary>
public sealed record PasskeyCaller(Guid UserId, string LoginId);

/// <summary>
/// 直近の再認証の証拠（P4）。<b>要求ごとに添える</b>（時間窓は 0）。
/// パスワード、またはパスキーの応答（<c>login/begin</c> のオプションで得たもの）。
/// 外部ログイン（SAML・Pleasanter）の直近性は 11.9 の具体化まで、アプリが確かめて <see cref="ExternallyVerified"/> で伝える。
/// </summary>
public sealed record PasskeyReauthentication(string? Password = null, string? AssertionJson = null, bool ExternallyVerified = false);

/// <summary>結果の種類。</summary>
public enum PasskeyStatus
{
    /// <summary>成功（200）。</summary>
    Ok,

    /// <summary>無い・他人の分（404）。</summary>
    NotFound,

    /// <summary>認証・再認証の失敗（401。理由を区別しない）。</summary>
    Unauthorized,

    /// <summary>締め出し中（423）。</summary>
    Locked,

    /// <summary>要求が不正（400）。</summary>
    InvalidRequest,

    /// <summary>競合・規則違反（409）。</summary>
    Conflict,
}

/// <summary>結果。<see cref="Code"/> は、アプリが多言語の文言へ写す鍵（仕様書 9.2）。</summary>
public sealed record PasskeyResult<T>(PasskeyStatus Status, string? Code = null, T? Value = default)
{
    /// <summary>成功か。</summary>
    public bool IsOk => Status == PasskeyStatus.Ok;
}

/// <summary>一覧・登録の応答に出す 1 件（公開鍵・資格情報の ID は返さない）。</summary>
public sealed record PasskeyView(
    Guid Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, bool BackedUp, IReadOnlyList<string> Transports);

/// <summary>パスキーのログインに成功した管理者。</summary>
public sealed record PasskeySignedIn(Guid UserId, string LoginId);

/// <summary>メッセージ鍵（<c>{ code }</c> に載せる値）。</summary>
public static class PasskeyCodes
{
    /// <summary>ログイン失敗（理由を区別しない）。</summary>
    public const string InvalidCredentials = "auth.invalidCredentials";

    /// <summary>再認証が無い・足りない。</summary>
    public const string ReauthRequired = "passkeys.reauthRequired";

    /// <summary>パスワードの再確認が違う。</summary>
    public const string CurrentPasswordRejected = "users.currentPasswordRejected";

    /// <summary>締め出し中（パスワードの再確認）。</summary>
    public const string TemporarilyLocked = "users.operationTemporarilyLocked";

    /// <summary>登録の応答が不正。</summary>
    public const string InvalidResponse = "passkeys.invalidResponse";

    /// <summary>origin・RP ID を決められない・許されない。</summary>
    public const string OriginNotAllowed = "passkeys.originNotAllowed";

    /// <summary>名前が不正。</summary>
    public const string InvalidName = "passkeys.invalidName";

    /// <summary>上限に達した。</summary>
    public const string LimitReached = "passkeys.limitReached";

    /// <summary>最後のログイン方法は消せない（P5）。</summary>
    public const string LastSignInMethod = "passkeys.lastSignInMethod";

    /// <summary>資格情報が既に登録されている（P8）。</summary>
    public const string DuplicateCredential = "passkeys.duplicateCredential";

    /// <summary>パスキーが無い。</summary>
    public const string NotFound = "passkeys.notFound";

    /// <summary>対象の管理者が居ない。</summary>
    public const string UserNotFound = "users.notFound";

    /// <summary>自分は対象にできない（T9 と同じ）。</summary>
    public const string SelfNotAllowed = "users.selfNotAllowed";
}

/// <summary>
/// 管理画面のパスキー（WebAuthn）の流れ（登録・ログイン・一覧・名前の変更・削除・復旧削除）。仕様は 認証とセッションの仕様書 3.10。
/// </summary>
/// <remarks>
/// HTTP と cookie には依存しない（<see cref="AdminPasskeyEndpoints"/> が薄く包む）。検証は Fido2NetLib（4.3.3）に任せ、
/// <b>UV フラグ・署名カウンター・BE の確認は、ライブラリとは別に自分でも行う</b>（多重に守る）。
/// Pleasanter 本体の実装は取り込んでいない。
/// </remarks>
public sealed class AdminPasskeyService(
    IPasskeyCredentialStore credentials,
    IPasskeyChallengeStore challenges,
    IPasskeyAccounts accounts,
    IPasskeyAuditSink audit,
    AdminPasskeyOptions options,
    TimeProvider time)
{
    private const string DefaultName = "Passkey";
    private const int MaxNameLength = 100;

    /// <summary>有効か（P10）。無効の間は、すべての口が 404。</summary>
    public bool Enabled => options.Enabled;

    /// <summary>設定。</summary>
    public AdminPasskeyOptions Options => options;

    // ---- 一覧・名前の変更 ----

    /// <summary>本人の一覧。</summary>
    public async Task<IReadOnlyList<PasskeyView>> ListAsync(PasskeyCaller caller, CancellationToken cancellationToken = default) =>
        [.. (await credentials.ListForUserAsync(caller.UserId, cancellationToken).ConfigureAwait(false)).Select(ToView)];

    /// <summary>名前を変える。他人の分・無い分は 404（存在を知らせない。P3）。</summary>
    public async Task<PasskeyResult<string>> RenameAsync(
        PasskeyCaller caller, Guid id, string? name, PasskeyRequestContext context, CancellationToken cancellationToken = default)
    {
        var clean = CleanName(name, required: true);
        if (clean is null)
        {
            return new(PasskeyStatus.InvalidRequest, PasskeyCodes.InvalidName);
        }

        var target = (await credentials.ListForUserAsync(caller.UserId, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(p => p.Id == id);
        if (target is null || !await credentials.RenameAsync(caller.UserId, id, clean, cancellationToken).ConfigureAwait(false))
        {
            return new(PasskeyStatus.NotFound, PasskeyCodes.NotFound);
        }

        await WriteAuditAsync(PasskeyAuditActions.Renamed, true, 200, caller, caller.UserId, context,
            new() { ["credentialId"] = EncodeId(target.CredentialId) }, cancellationToken).ConfigureAwait(false);
        return new(PasskeyStatus.Ok, Value: clean);
    }

    // ---- 登録 ----

    /// <summary>登録の開始。直近の再認証を要する（P4）。オプションの JSON を返す。</summary>
    public async Task<PasskeyResult<string>> BeginRegistrationAsync(
        PasskeyCaller caller, PasskeyReauthentication reauthentication, PasskeyRequestContext context,
        CancellationToken cancellationToken = default)
    {
        var rp = PasskeyRelyingParty.Resolve(options, context.Scheme, context.Host);
        if (rp is null)
        {
            return new(PasskeyStatus.InvalidRequest, PasskeyCodes.OriginNotAllowed);
        }

        var reauth = await ReauthenticateAsync(caller, reauthentication, context, cancellationToken).ConfigureAwait(false);
        if (reauth is not null)
        {
            return new(reauth.Status, reauth.Code);
        }

        var existing = await credentials.ListForUserAsync(caller.UserId, cancellationToken).ConfigureAwait(false);
        if (existing.Count >= options.MaxPasskeysPerUser)
        {
            return new(PasskeyStatus.Conflict, PasskeyCodes.LimitReached);
        }

        CredentialCreateOptions created;
        try
        {
            created = CreateFido2(rp).RequestNewCredential(new RequestNewCredentialParams
            {
                User = ToFido2User(caller),
                ExcludeCredentials = [.. existing.Select(p => new PublicKeyCredentialDescriptor(p.CredentialId))],
                AuthenticatorSelection = Selection(),
                AttestationPreference = AttestationConveyancePreference.None,
            });
        }
        catch (Fido2ConfigurationException)
        {
            return new(PasskeyStatus.InvalidRequest, PasskeyCodes.OriginNotAllowed);
        }

        await challenges.StoreAsync(created.Challenge, PasskeyPurposes.Register, caller.UserId, options.ChallengeLifetime, cancellationToken)
            .ConfigureAwait(false);
        return new(PasskeyStatus.Ok, Value: created.ToJson());
    }

    /// <summary>登録の完了。UV フラグの無い応答・使用済みのチャレンジ・登録済みの資格情報を拒む。</summary>
    public async Task<PasskeyResult<PasskeyView>> CompleteRegistrationAsync(
        PasskeyCaller caller, string? attestationJson, string? name, PasskeyRequestContext context,
        CancellationToken cancellationToken = default)
    {
        var clean = CleanName(name, required: false);
        if (clean is null)
        {
            return new(PasskeyStatus.InvalidRequest, PasskeyCodes.InvalidName);
        }

        var rp = PasskeyRelyingParty.Resolve(options, context.Scheme, context.Host);
        AuthenticatorAttestationRawResponse? response = Parse<AuthenticatorAttestationRawResponse>(attestationJson);
        var challenge = response is null ? null : ChallengeOf(response.Response?.ClientDataJson);
        if (rp is null || response?.Response is null || challenge is null)
        {
            return new(PasskeyStatus.InvalidRequest, PasskeyCodes.InvalidResponse);
        }

        // 一度限り。検証に失敗しても、使ったチャレンジは戻らない
        if (!await challenges.ConsumeAsync(challenge, PasskeyPurposes.Register, caller.UserId, cancellationToken).ConfigureAwait(false))
        {
            return new(PasskeyStatus.InvalidRequest, PasskeyCodes.InvalidResponse);
        }

        var authData = AuthDataOf(response.Response.AttestationObject);
        if (authData is null || (authData[32] & AuthenticatorFlags.UserVerified) == 0)
        {
            return new(PasskeyStatus.InvalidRequest, PasskeyCodes.InvalidResponse);
        }

        var existing = await credentials.ListForUserAsync(caller.UserId, cancellationToken).ConfigureAwait(false);
        if (existing.Count >= options.MaxPasskeysPerUser)
        {
            return new(PasskeyStatus.Conflict, PasskeyCodes.LimitReached);
        }

        RegisteredPublicKeyCredential registered;
        try
        {
            var original = CredentialCreateOptions.Create(
                ConfigurationOf(rp), challenge, ToFido2User(caller), Selection(), AttestationConveyancePreference.None,
                [.. existing.Select(p => new PublicKeyCredentialDescriptor(p.CredentialId))], null, PubKeyCredParam.Defaults);
            registered = await CreateFido2(rp).MakeNewCredentialAsync(new MakeNewCredentialParams
            {
                AttestationResponse = response,
                OriginalOptions = original,
                IsCredentialIdUniqueToUserCallback = async (args, token) =>
                    await credentials.FindByCredentialIdAsync(args.CredentialId, token).ConfigureAwait(false) is null,
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Fido2VerificationException exception)
        {
            return exception.Code == Fido2ErrorCode.NonUniqueCredentialId
                ? new(PasskeyStatus.Conflict, PasskeyCodes.DuplicateCredential)
                : new(PasskeyStatus.InvalidRequest, PasskeyCodes.InvalidResponse);
        }
        catch (Fido2ConfigurationException)
        {
            return new(PasskeyStatus.InvalidRequest, PasskeyCodes.OriginNotAllowed);
        }

        var id = await credentials.AddAsync(new NewPasskeyCredential(
            caller.UserId, registered.Id, registered.PublicKey, registered.SignCount, registered.AaGuid,
            [.. (registered.Transports ?? []).Select(TransportName)], registered.IsBackupEligible, registered.IsBackedUp,
            clean), cancellationToken).ConfigureAwait(false);
        if (id is null)
        {
            // 同時に別の登録が先に入った（P8）
            return new(PasskeyStatus.Conflict, PasskeyCodes.DuplicateCredential);
        }

        await WriteAuditAsync(PasskeyAuditActions.Registered, true, 200, caller, caller.UserId, context, new()
        {
            ["credentialId"] = EncodeId(registered.Id),
            ["name"] = clean,
            ["aaGuid"] = registered.AaGuid.ToString(),
            ["backupEligible"] = registered.IsBackupEligible,
        }, cancellationToken).ConfigureAwait(false);

        var saved = (await credentials.ListForUserAsync(caller.UserId, cancellationToken).ConfigureAwait(false)).First(p => p.Id == id);
        return new(PasskeyStatus.Ok, Value: ToView(saved));
    }

    // ---- ログイン ----

    /// <summary>ログインの開始（認証の外）。<c>allowCredentials</c> は空（ディスコバラブル）で、UV を必須にする。</summary>
    public async Task<PasskeyResult<string>> BeginSignInAsync(PasskeyRequestContext context, CancellationToken cancellationToken = default)
    {
        var rp = PasskeyRelyingParty.Resolve(options, context.Scheme, context.Host);
        if (rp is null)
        {
            return new(PasskeyStatus.InvalidRequest, PasskeyCodes.OriginNotAllowed);
        }

        AssertionOptions assertion;
        try
        {
            assertion = CreateFido2(rp).GetAssertionOptions(new GetAssertionOptionsParams
            {
                AllowedCredentials = [],
                UserVerification = UserVerificationRequirement.Required,
            });
        }
        catch (Fido2ConfigurationException)
        {
            return new(PasskeyStatus.InvalidRequest, PasskeyCodes.OriginNotAllowed);
        }

        await challenges.StoreAsync(assertion.Challenge, PasskeyPurposes.SignIn, null, options.ChallengeLifetime, cancellationToken)
            .ConfigureAwait(false);
        return new(PasskeyStatus.Ok, Value: assertion.ToJson());
    }

    /// <summary>
    /// ログインの完了。成功なら管理者を返す（<b>セッションを張るのは呼び出し側。<c>via=passkey</c>、2 要素は満たす扱い</b>）。
    /// 失敗は、理由を区別しない 401。理由は監査にだけ残す。
    /// </summary>
    public async Task<PasskeyResult<PasskeySignedIn>> CompleteSignInAsync(
        string? assertionJson, PasskeyRequestContext context, CancellationToken cancellationToken = default)
    {
        var verified = await VerifyAssertionAsync(assertionJson, context, cancellationToken).ConfigureAwait(false);
        if (verified.Credential is not { } credential)
        {
            await WriteFailureAsync(verified, null, context, cancellationToken).ConfigureAwait(false);
            return new(PasskeyStatus.Unauthorized, PasskeyCodes.InvalidCredentials);
        }

        // 持ち主が無効・締め出し中なら、セッションを張らない（パスキーを迂回の道にしない）
        var account = await accounts.FindAsync(credential.UserId, cancellationToken).ConfigureAwait(false);
        var blocked = account is null || account.IsDisabled ? PasskeyFailureReasons.Disabled : account.IsLocked ? PasskeyFailureReasons.Locked : null;
        if (blocked is not null)
        {
            await WriteFailureAsync(verified with { Reason = blocked }, credential, context, cancellationToken).ConfigureAwait(false);
            return new(PasskeyStatus.Unauthorized, PasskeyCodes.InvalidCredentials);
        }

        // 資格情報の更新は、ここで確定する（同じ応答の同時 2 回を通さない）
        if (!await credentials.RecordSignInAsync(credential.Id, credential.SignCount, verified.NewSignCount, verified.BackedUp, cancellationToken)
                .ConfigureAwait(false))
        {
            await WriteFailureAsync(verified with { Reason = PasskeyFailureReasons.Concurrent }, credential, context, cancellationToken)
                .ConfigureAwait(false);
            return new(PasskeyStatus.Unauthorized, PasskeyCodes.InvalidCredentials);
        }

        await accounts.RecordSignInSucceededAsync(credential.UserId, cancellationToken).ConfigureAwait(false);
        var caller = new PasskeyCaller(account!.Id, account.LoginId);
        await WriteAuditAsync(PasskeyAuditActions.SignInSucceeded, true, 200, caller, caller.UserId, context,
            new() { ["credentialId"] = EncodeId(credential.CredentialId) }, cancellationToken).ConfigureAwait(false);
        return new(PasskeyStatus.Ok, Value: new PasskeySignedIn(account.Id, account.LoginId));
    }

    // ---- 削除・復旧 ----

    /// <summary>
    /// 本人が削除する。直近の再認証を要する（P4）。他人の分は 404（P3）。
    /// 削除の結果、使えるログイン方法が無くなるなら断る（P5）。ほかのセッションは失効させない。
    /// </summary>
    public async Task<PasskeyResult<bool>> RemoveAsync(
        PasskeyCaller caller, Guid id, PasskeyReauthentication reauthentication, PasskeyRequestContext context,
        CancellationToken cancellationToken = default)
    {
        var target = (await credentials.ListForUserAsync(caller.UserId, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(p => p.Id == id);
        if (target is null)
        {
            return new(PasskeyStatus.NotFound, PasskeyCodes.NotFound);
        }

        var reauth = await ReauthenticateAsync(caller, reauthentication, context, cancellationToken).ConfigureAwait(false);
        if (reauth is not null)
        {
            return new(reauth.Status, reauth.Code);
        }

        // ほかのパスキーは数えない（パスキーだけの人を作らない）
        if (!(await accounts.GetSignInMethodsAsync(caller.UserId, cancellationToken).ConfigureAwait(false)).Any)
        {
            return new(PasskeyStatus.Conflict, PasskeyCodes.LastSignInMethod);
        }

        if (!await credentials.DeleteAsync(caller.UserId, id, cancellationToken).ConfigureAwait(false))
        {
            return new(PasskeyStatus.NotFound, PasskeyCodes.NotFound);
        }

        await WriteAuditAsync(PasskeyAuditActions.Removed, true, 200, caller, caller.UserId, context,
            new() { ["credentialId"] = EncodeId(target.CredentialId), ["by"] = "self" }, cancellationToken).ConfigureAwait(false);
        return new(PasskeyStatus.Ok, Value: true);
    }

    /// <summary>
    /// 復旧: 管理者が他人の全パスキーを消す。対象者の<b>全セッションを失効</b>させる（P9）。自分は対象にできない。
    /// 権限（<c>users.resetPasskeys</c>）の確認は呼び出し側（<see cref="AdminPasskeyEndpoints"/>）が行う。
    /// 最後のログイン方法の規則（P5）は適用しない（復旧は、本人が別の方法で入って登録し直すための操作）。
    /// </summary>
    public async Task<PasskeyResult<int>> ResetForUserAsync(
        PasskeyCaller actor, Guid targetUserId, PasskeyRequestContext context, CancellationToken cancellationToken = default)
    {
        if (actor.UserId == targetUserId)
        {
            return new(PasskeyStatus.InvalidRequest, PasskeyCodes.SelfNotAllowed);
        }

        var account = await accounts.FindAsync(targetUserId, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return new(PasskeyStatus.NotFound, PasskeyCodes.UserNotFound);
        }

        var existing = await credentials.ListForUserAsync(targetUserId, cancellationToken).ConfigureAwait(false);
        var removed = await credentials.DeleteForUserAsync(targetUserId, cancellationToken).ConfigureAwait(false);
        await accounts.RevokeSessionsAsync(targetUserId, cancellationToken).ConfigureAwait(false);

        // 1 資格情報 1 行。消した行が 0 でも、操作の事実を 1 行残す
        if (existing.Count == 0)
        {
            await WriteAuditAsync(PasskeyAuditActions.Removed, true, 200, actor, targetUserId, context,
                new() { ["by"] = "admin", ["removed"] = 0 }, cancellationToken).ConfigureAwait(false);
        }

        foreach (var passkey in existing)
        {
            await WriteAuditAsync(PasskeyAuditActions.Removed, true, 200, actor, targetUserId, context,
                new() { ["credentialId"] = EncodeId(passkey.CredentialId), ["by"] = "admin" }, cancellationToken).ConfigureAwait(false);
        }

        return new(PasskeyStatus.Ok, Value: removed);
    }

    // ---- 再認証 ----

    private async Task<PasskeyResult<bool>?> ReauthenticateAsync(
        PasskeyCaller caller, PasskeyReauthentication proof, PasskeyRequestContext context, CancellationToken cancellationToken)
    {
        if (proof.ExternallyVerified)
        {
            return null;
        }

        if (proof.AssertionJson is not null)
        {
            var verified = await VerifyAssertionAsync(proof.AssertionJson, context, cancellationToken).ConfigureAwait(false);
            if (verified.Credential is null)
            {
                await WriteFailureAsync(verified, null, context, cancellationToken, reauth: true).ConfigureAwait(false);
                return new(PasskeyStatus.Unauthorized, PasskeyCodes.ReauthRequired);
            }

            if (verified.Credential.UserId != caller.UserId)
            {
                await WriteFailureAsync(verified with { Reason = PasskeyFailureReasons.OtherUser, Credential = null }, null, context,
                    cancellationToken, reauth: true).ConfigureAwait(false);
                return new(PasskeyStatus.Unauthorized, PasskeyCodes.ReauthRequired);
            }

            // 再認証のパスキーも、ログインと同じく署名カウンターなどを更新する（同じ応答の再利用を許さない）
            if (!await credentials.RecordSignInAsync(verified.Credential.Id, verified.Credential.SignCount, verified.NewSignCount,
                    verified.BackedUp, cancellationToken).ConfigureAwait(false))
            {
                return new(PasskeyStatus.Unauthorized, PasskeyCodes.ReauthRequired);
            }

            return null;
        }

        if (!string.IsNullOrEmpty(proof.Password))
        {
            return await accounts.ConfirmPasswordAsync(caller.UserId, proof.Password, cancellationToken).ConfigureAwait(false) switch
            {
                PasswordConfirmation.Confirmed => null,
                PasswordConfirmation.Locked => new(PasskeyStatus.Locked, PasskeyCodes.TemporarilyLocked),
                _ => new(PasskeyStatus.Unauthorized, PasskeyCodes.CurrentPasswordRejected),
            };
        }

        return new(PasskeyStatus.Unauthorized, PasskeyCodes.ReauthRequired);
    }

    // ---- 認証の応答の検証 ----

    private sealed record Verification(
        PasskeyCredential? Credential, string? Reason, long NewSignCount, bool BackedUp, PasskeyCredential? Known = null);

    private async Task<Verification> VerifyAssertionAsync(string? assertionJson, PasskeyRequestContext context, CancellationToken cancellationToken)
    {
        var response = Parse<AuthenticatorAssertionRawResponse>(assertionJson);
        var challenge = response?.Response is null ? null : ChallengeOf(response.Response.ClientDataJson);
        if (response?.Response is null || challenge is null)
        {
            return Fail(PasskeyFailureReasons.Malformed);
        }

        // 一度限り（検証に失敗しても戻らない）
        if (!await challenges.ConsumeAsync(challenge, PasskeyPurposes.SignIn, null, cancellationToken).ConfigureAwait(false))
        {
            return Fail(PasskeyFailureReasons.Challenge);
        }

        var credential = response.RawId is { Length: > 0 }
            ? await credentials.FindByCredentialIdAsync(response.RawId, cancellationToken).ConfigureAwait(false)
            : null;
        if (credential is null)
        {
            return Fail(PasskeyFailureReasons.UnknownCredential);
        }

        var rp = PasskeyRelyingParty.Resolve(options, context.Scheme, context.Host);
        var data = response.Response.AuthenticatorData;
        if (rp is null || data is null || data.Length < 37)
        {
            return Fail(PasskeyFailureReasons.Malformed, credential);
        }

        // ユーザーハンドルは、持ち主の UserId と一致しなければならない（ディスコバラブル）
        if (response.Response.UserHandle is null || !response.Response.UserHandle.AsSpan().SequenceEqual(UserHandleOf(credential.UserId)))
        {
            return Fail(PasskeyFailureReasons.UserHandleMismatch, credential);
        }

        VerifyAssertionResult result;
        try
        {
            result = await CreateFido2(rp).MakeAssertionAsync(new MakeAssertionParams
            {
                AssertionResponse = response,
                OriginalOptions = new AssertionOptions
                {
                    Challenge = challenge,
                    RpId = rp.RpId,
                    AllowCredentials = [],
                    UserVerification = UserVerificationRequirement.Required,
                    Timeout = options.TimeoutMilliseconds,
                },
                StoredPublicKey = credential.PublicKey,
                StoredSignatureCounter = (uint)Math.Min(credential.SignCount, uint.MaxValue),
                IsUserHandleOwnerOfCredentialIdCallback = (args, _) =>
                    Task.FromResult(args.UserHandle.AsSpan().SequenceEqual(UserHandleOf(credential.UserId))),
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Fido2VerificationException exception)
        {
            return Fail(exception.Code switch
            {
                Fido2ErrorCode.InvalidSignature => PasskeyFailureReasons.BadSignature,
                Fido2ErrorCode.InvalidSignCount => PasskeyFailureReasons.CounterRegression,
                Fido2ErrorCode.UserVerificationRequirementNotMet => PasskeyFailureReasons.UserVerificationMissing,
                Fido2ErrorCode.InvalidRpidHash or Fido2ErrorCode.InvalidAuthenticatorResponseOrigin
                    or Fido2ErrorCode.CrossOriginRequestNotAllowed or Fido2ErrorCode.InvalidAuthenticatorResponseTopOrigin
                    => PasskeyFailureReasons.Origin,
                _ => PasskeyFailureReasons.Invalid,
            }, credential);
        }
        catch (Fido2ConfigurationException)
        {
            return Fail(PasskeyFailureReasons.Origin, credential);
        }

        // ライブラリとは別に、フラグとカウンターを確かめる（署名が通った後だけ。偽の応答で警告を出させない）
        var flags = data[32];
        var newCount = (long)((uint)(data[33] << 24 | data[34] << 16 | data[35] << 8 | data[36]));
        if ((flags & AuthenticatorFlags.UserVerified) == 0)
        {
            return Fail(PasskeyFailureReasons.UserVerificationMissing, credential);
        }

        if (((flags & AuthenticatorFlags.BackupEligible) != 0) != credential.BackupEligible)
        {
            return Fail(PasskeyFailureReasons.BackupEligibilityChanged, credential);
        }

        // 保存値が 0 でなく、受け取った値が保存値より大きくないときは、複製の疑い。両方 0（カウンターを持たない認証器）は通す
        if (credential.SignCount != 0 && newCount <= credential.SignCount)
        {
            return Fail(PasskeyFailureReasons.CounterRegression, credential);
        }

        _ = result;
        return new Verification(credential, null, newCount, (flags & AuthenticatorFlags.BackedUp) != 0, credential);
    }

    private static Verification Fail(string reason, PasskeyCredential? known = null) => new(null, reason, 0, false, known);

    private async Task WriteFailureAsync(
        Verification verification, PasskeyCredential? credential, PasskeyRequestContext context, CancellationToken cancellationToken,
        bool reauth = false)
    {
        var known = credential ?? verification.Known;
        var detail = new Dictionary<string, object?> { ["reason"] = verification.Reason };
        if (known is not null)
        {
            detail["credentialId"] = EncodeId(known.CredentialId);
        }

        if (reauth)
        {
            detail["purpose"] = "reauth";
        }

        await TryWriteAsync(new PasskeyAuditEntry(time.GetUtcNow(), PasskeyAuditActions.SignInFailed, false, 401, null, null,
            known?.UserId.ToString(), JsonSerializer.Serialize(detail), context.IpAddress), cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteAuditAsync(
        string action, bool succeeded, int status, PasskeyCaller actor, Guid target, PasskeyRequestContext context,
        Dictionary<string, object?> detail, CancellationToken cancellationToken) =>
        await TryWriteAsync(new PasskeyAuditEntry(time.GetUtcNow(), action, succeeded, status, actor.UserId.ToString(), actor.LoginId,
            target.ToString(), JsonSerializer.Serialize(detail), context.IpAddress), cancellationToken).ConfigureAwait(false);

    private async Task TryWriteAsync(PasskeyAuditEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            await audit.WriteAsync(entry, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // 書けなくても操作は通す（7.1）。例外の詳細（資格情報を含みうる）は握る
        }
    }

    // ---- Fido2 まわり ----

    private Fido2 CreateFido2(PasskeyRelyingParty rp) => new(ConfigurationOf(rp), null!);

    private Fido2Configuration ConfigurationOf(PasskeyRelyingParty rp) => new()
    {
        RPID = rp.RpId,
        RPName = options.RpName,
        Origins = new HashSet<string>(rp.Origins, StringComparer.Ordinal),
        Timeout = options.TimeoutMilliseconds,
    };

    private static AuthenticatorSelection Selection() => new()
    {
        ResidentKey = ResidentKeyRequirement.Required,
        UserVerification = UserVerificationRequirement.Required,
    };

    /// <summary>ユーザーハンドル＝UserId の 16 バイト（RFC 4122 の並び）。個人を特定する情報を含めない。</summary>
    public static byte[] UserHandleOf(Guid userId) => userId.ToByteArray(bigEndian: true);

    private static Fido2User ToFido2User(PasskeyCaller caller) => new()
    {
        Id = UserHandleOf(caller.UserId),
        Name = caller.LoginId,
        DisplayName = caller.LoginId,
    };

    private static T? Parse<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or FormatException)
        {
            return null;
        }
    }

    /// <summary>clientDataJSON の <c>challenge</c>（base64url）を取り出す。</summary>
    private static byte[]? ChallengeOf(byte[]? clientDataJson)
    {
        if (clientDataJson is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(clientDataJson);
            return document.RootElement.TryGetProperty("challenge", out var value) && value.ValueKind == JsonValueKind.String
                ? Base64Url.DecodeFromChars(value.GetString()!)
                : null;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>attestationObject（CBOR）から authData を取り出す（フラグの自前確認用）。</summary>
    private static byte[]? AuthDataOf(byte[]? attestationObject)
    {
        if (attestationObject is null)
        {
            return null;
        }

        try
        {
            var reader = new CborReader(attestationObject, CborConformanceMode.Lax);
            var count = reader.ReadStartMap() ?? 0;
            byte[]? authData = null;
            for (var i = 0; i < count; i++)
            {
                if (reader.PeekState() == CborReaderState.TextString && reader.ReadTextString() == "authData")
                {
                    authData = reader.ReadByteString();
                }
                else
                {
                    reader.SkipValue();
                }
            }

            return authData is { Length: >= 37 } ? authData : null;
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string TransportName(AuthenticatorTransport transport) => transport switch
    {
        AuthenticatorTransport.SmartCard => "smart-card",
        _ => transport.ToString().ToLowerInvariant(),
    };

    private static string EncodeId(byte[] credentialId) => Base64Url.EncodeToString(credentialId);

    private static string? CleanName(string? name, bool required)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return required ? null : DefaultName;
        }

        return trimmed.Length > MaxNameLength || trimmed.Any(char.IsControl) ? null : trimmed;
    }

    private static PasskeyView ToView(PasskeyCredential p) => new(p.Id, p.Name, p.CreatedAt, p.LastUsedAt, p.BackedUp, p.Transports);

    private static class AuthenticatorFlags
    {
        public const byte UserVerified = 0x04;
        public const byte BackupEligible = 0x08;
        public const byte BackedUp = 0x10;
    }
}
