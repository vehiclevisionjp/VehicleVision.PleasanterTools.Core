using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace VehicleVision.PleasanterTools.Core.Web;

/// <summary>ログイン中の本人と、その権限（アプリが、自分のセッションと役割の表から作る）。</summary>
public sealed record AdminPasskeyCaller(Guid UserId, string LoginId, IReadOnlySet<string> Permissions)
{
    /// <summary>流れが使う形へ。</summary>
    public PasskeyCaller ToCaller() => new(UserId, LoginId);
}

/// <summary>パスキーの口が、アプリのセッションとつなぐための受け口。</summary>
public interface IAdminPasskeyHost
{
    /// <summary>
    /// ログイン中（<c>Session</c> の段階だけ。Pending・Reenroll では <c>null</c>）の本人。cookie の現物は Core へ渡さない。
    /// 権限は、要求のたびに DB の役割から引く（cookie へ焼かない。6.1）。
    /// </summary>
    ValueTask<AdminPasskeyCaller?> GetCallerAsync(HttpContext context);

    /// <summary>
    /// パスキーのログインに成功した人のセッションを張る。<b>Pending を消してから Session を張る</b>
    /// （3.1。<c>via</c> は <see cref="AdminPasskeyEndpoints.SessionVia"/>。2 要素は満たす扱いで、Pending を経ない）。
    /// </summary>
    Task SignInAsync(HttpContext context, PasskeySignedIn user, string via);

    /// <summary>
    /// 外部ログイン（SAML・Pleasanter）の直近の再確認があるか（U13・11.9 の具体化まで、既定は <c>false</c>）。
    /// パスワードもパスキーも持たない人が、登録・削除をするための道。cookie や <c>via</c> の表示値だけで <c>true</c> にしない。
    /// </summary>
    ValueTask<bool> IsExternallyReconfirmedAsync(HttpContext context, AdminPasskeyCaller caller) => ValueTask.FromResult(false);
}

/// <summary>パスキーの HTTP の口（API 契約の仕様書 5.3.1）。</summary>
public static class AdminPasskeyEndpoints
{
    /// <summary>セッションの入った方式（Core.Data の <c>SessionVia.Passkey</c> と同じ値）。</summary>
    public const string SessionVia = "passkey";

    /// <summary>他人のパスキーを消す（復旧）権限の名前（権限と監査ログの仕様書 6.2）。</summary>
    public const string ResetPermission = "users.resetPasskeys";

    /// <summary>口を張る。<see cref="AdminPasskeyService"/> と <see cref="IAdminPasskeyHost"/> を DI へ登録しておくこと。</summary>
    /// <param name="endpoints">経路の置き場。</param>
    /// <param name="prefix">接頭辞（例 <c>/api/admin</c>）。</param>
    /// <param name="loginRateLimitPolicy">
    /// パスワードのログインと<b>同じ</b>、IP ごとのレート制限の方針名（枠を共有する。決定 7/9）。
    /// ログインの 2 つの口と、パスワードを受ける再認証の口へ掛ける。
    /// </param>
    public static IEndpointRouteBuilder MapAdminPasskeyEndpoints(
        this IEndpointRouteBuilder endpoints, string prefix, string? loginRateLimitPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(prefix);

        RouteHandlerBuilder Limited(RouteHandlerBuilder builder) =>
            loginRateLimitPolicy is null ? builder : builder.RequireRateLimiting(loginRateLimitPolicy);

        // ---- 認証の外 ----
        Limited(endpoints.MapPost($"{prefix}/login/passkey/begin", async (HttpContext http, AdminPasskeyService service) =>
        {
            if (!service.Enabled)
            {
                return NotFound(http);
            }

            return ToResult(http, await service.BeginSignInAsync(ContextOf(http), http.RequestAborted).ConfigureAwait(false), RawJson);
        }));

        Limited(endpoints.MapPost($"{prefix}/login/passkey/complete", async (
            HttpContext http, AdminPasskeyService service, IAdminPasskeyHost host, JsonElement body) =>
        {
            if (!service.Enabled)
            {
                return NotFound(http);
            }

            var result = await service.CompleteSignInAsync(Raw(body, "credential"), ContextOf(http), http.RequestAborted).ConfigureAwait(false);
            if (!result.IsOk)
            {
                return ToResult(http, result, _ => null);
            }

            await host.SignInAsync(http, result.Value!, SessionVia).ConfigureAwait(false);
            return Json(http, 200, new { next = "done" });
        }));

        // ---- 本人（Session） ----
        endpoints.MapGet($"{prefix}/me/passkeys", async (HttpContext http, AdminPasskeyService service, IAdminPasskeyHost host) =>
        {
            if (!service.Enabled)
            {
                return NotFound(http);
            }

            if (await host.GetCallerAsync(http).ConfigureAwait(false) is not { } caller)
            {
                return Json(http, 401, new { code = PasskeyCodes.InvalidCredentials });
            }

            var passkeys = await service.ListAsync(caller.ToCaller(), http.RequestAborted).ConfigureAwait(false);
            return Json(http, 200, new { passkeys = passkeys.Select(View) });
        });

        Limited(endpoints.MapPost($"{prefix}/me/passkeys/begin", async (
            HttpContext http, AdminPasskeyService service, IAdminPasskeyHost host, JsonElement body) =>
        {
            if (!service.Enabled)
            {
                return NotFound(http);
            }

            if (await host.GetCallerAsync(http).ConfigureAwait(false) is not { } caller)
            {
                return Json(http, 401, new { code = PasskeyCodes.InvalidCredentials });
            }

            var proof = await ProofAsync(http, host, caller, body).ConfigureAwait(false);
            return ToResult(http, await service.BeginRegistrationAsync(caller.ToCaller(), proof, ContextOf(http), http.RequestAborted)
                .ConfigureAwait(false), RawJson);
        }));

        endpoints.MapPost($"{prefix}/me/passkeys/complete", async (
            HttpContext http, AdminPasskeyService service, IAdminPasskeyHost host, JsonElement body) =>
        {
            if (!service.Enabled)
            {
                return NotFound(http);
            }

            if (await host.GetCallerAsync(http).ConfigureAwait(false) is not { } caller)
            {
                return Json(http, 401, new { code = PasskeyCodes.InvalidCredentials });
            }

            var result = await service.CompleteRegistrationAsync(
                caller.ToCaller(), Raw(body, "credential"), Text(body, "name"), ContextOf(http), http.RequestAborted).ConfigureAwait(false);
            return ToResult(http, result, view => new { passkey = View(view) });
        });

        endpoints.MapPut($"{prefix}/me/passkeys/{{id}}", async (
            HttpContext http, string id, AdminPasskeyService service, IAdminPasskeyHost host, JsonElement body) =>
        {
            if (!service.Enabled)
            {
                return NotFound(http);
            }

            if (await host.GetCallerAsync(http).ConfigureAwait(false) is not { } caller)
            {
                return Json(http, 401, new { code = PasskeyCodes.InvalidCredentials });
            }

            // 識別子が GUID でなければ、他人の分と同じ 404
            if (!Guid.TryParse(id, out var passkeyId))
            {
                return Json(http, 404, new { code = PasskeyCodes.NotFound });
            }

            var result = await service.RenameAsync(caller.ToCaller(), passkeyId, Text(body, "name"), ContextOf(http), http.RequestAborted)
                .ConfigureAwait(false);
            return ToResult(http, result, name => new { name });
        });

        Limited(endpoints.MapPost($"{prefix}/me/passkeys/{{id}}/remove", async (
            HttpContext http, string id, AdminPasskeyService service, IAdminPasskeyHost host, JsonElement body) =>
        {
            if (!service.Enabled)
            {
                return NotFound(http);
            }

            if (await host.GetCallerAsync(http).ConfigureAwait(false) is not { } caller)
            {
                return Json(http, 401, new { code = PasskeyCodes.InvalidCredentials });
            }

            if (!Guid.TryParse(id, out var passkeyId))
            {
                return Json(http, 404, new { code = PasskeyCodes.NotFound });
            }

            var proof = await ProofAsync(http, host, caller, body).ConfigureAwait(false);
            var result = await service.RemoveAsync(caller.ToCaller(), passkeyId, proof, ContextOf(http), http.RequestAborted).ConfigureAwait(false);
            return ToResult(http, result, _ => new { removed = true });
        }));

        // ---- 復旧（管理者） ----
        endpoints.MapPost($"{prefix}/users/{{id}}/passkeys/reset", async (
            HttpContext http, string id, AdminPasskeyService service, IAdminPasskeyHost host) =>
        {
            if (!service.Enabled)
            {
                return NotFound(http);
            }

            if (await host.GetCallerAsync(http).ConfigureAwait(false) is not { } caller)
            {
                return Json(http, 401, new { code = PasskeyCodes.InvalidCredentials });
            }

            // 権限はサーバで要求する（画面で隠すだけにしない）
            if (!caller.Permissions.Contains(ResetPermission))
            {
                return Json(http, 403, new { code = "auth.forbidden" });
            }

            if (!Guid.TryParse(id, out var targetId))
            {
                return Json(http, 404, new { code = PasskeyCodes.UserNotFound });
            }

            var result = await service.ResetForUserAsync(caller.ToCaller(), targetId, ContextOf(http), http.RequestAborted).ConfigureAwait(false);
            return ToResult(http, result, removed => new { reset = true, removed });
        });

        return endpoints;
    }

    private static string? RawJson(string json) => json;

    private static object View(PasskeyView p) =>
        new { id = p.Id, name = p.Name, createdAt = p.CreatedAt, lastUsedAt = p.LastUsedAt, backedUp = p.BackedUp, transports = p.Transports };

    private static PasskeyRequestContext ContextOf(HttpContext http) =>
        new(http.Request.Scheme, http.Request.Host.ToString(), http.Connection.RemoteIpAddress?.ToString(),
            http.Request.Headers.UserAgent.ToString());

    private static async Task<PasskeyReauthentication> ProofAsync(
        HttpContext http, IAdminPasskeyHost host, AdminPasskeyCaller caller, JsonElement body) =>
        new(Text(body, "password"), Raw(body, "assertion"), await host.IsExternallyReconfirmedAsync(http, caller).ConfigureAwait(false));

    private static string? Text(JsonElement body, string name) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? Raw(JsonElement body, string name) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value.GetRawText()
            : null;

    private static IResult NotFound(HttpContext http)
    {
        NoStore(http);
        return Results.NotFound();
    }

    private static IResult Json(HttpContext http, int status, object body)
    {
        NoStore(http);
        return Results.Json(body, statusCode: status);
    }

    private static void NoStore(HttpContext http)
    {
        http.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        http.Response.Headers.Pragma = "no-cache";
    }

    private static IResult ToResult<T>(HttpContext http, PasskeyResult<T> result, Func<T, object?> success)
    {
        if (result.IsOk)
        {
            NoStore(http);
            // オプションは生の JSON（Fido2NetLib が書いたもの）。それ以外は匿名型
            return success(result.Value!) is string raw
                ? Results.Content(raw, "application/json")
                : Results.Json(success(result.Value!));
        }

        return Json(http, result.Status switch
        {
            PasskeyStatus.NotFound => 404,
            PasskeyStatus.Unauthorized => 401,
            PasskeyStatus.Locked => 423,
            PasskeyStatus.InvalidRequest => 400,
            _ => 409,
        }, new { code = result.Code });
    }
}

/// <summary>DI への登録。</summary>
public static class AdminPasskeyServiceCollectionExtensions
{
    /// <summary>
    /// パスキーの流れを登録する。アプリは、<see cref="IPasskeyCredentialStore"/>・<see cref="IPasskeyChallengeStore"/>・
    /// <see cref="IPasskeyAccounts"/>・<see cref="IPasskeyAuditSink"/>・<see cref="IAdminPasskeyHost"/> も登録する。
    /// </summary>
    public static IServiceCollection AddAdminPasskeys(this IServiceCollection services, AdminPasskeyOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        services.AddSingleton(options);
        services.AddSingleton<AdminPasskeyService>(provider => new AdminPasskeyService(
            provider.GetRequiredService<IPasskeyCredentialStore>(), provider.GetRequiredService<IPasskeyChallengeStore>(),
            provider.GetRequiredService<IPasskeyAccounts>(), provider.GetRequiredService<IPasskeyAuditSink>(), options,
            provider.GetService<TimeProvider>() ?? TimeProvider.System));
        return services;
    }
}
