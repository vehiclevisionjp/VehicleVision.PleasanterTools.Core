# 管理画面の API 契約の仕様書

[管理画面の共通仕様書](../管理画面の共通仕様書.md)の 5 章。**基準は Questionnaire（Q）の実装**。
各アプリとの差は 5.5 にまとめる。

## 5.1 共通の約束

| 項目 | 約束 | 根拠 |
|---|---|---|
| 接頭辞 | `/api/admin`（管理画面の入口 `<APP>_ADMIN_PATH` を変えても API の経路は変わらない） | `Q.Web/Endpoints/AdminAuthEndpoints.cs:34`、`Q.Web/AdminPathOptions.cs:8-10` |
| 本文 | JSON。エラーは `{ message }`（文言は `Accept-Language` の言語）か、画面が文言を作る `{ code }` | `Q.Web/Endpoints/AdminUserEndpoints.cs:579-619`、`Q.Web/Endpoints/AdminPleasanterSsoEndpoints.cs:60-160` |
| 言語 | サーバは `Accept-Language` **だけ**を見る。利用者ごとの言語設定は画面の初期値で、応答の言語を決めない | `Q.Web/Localization/RequestLanguage.cs:5-30` |
| キャッシュ | `Cache-Control: no-store, no-cache, must-revalidate`・`Pragma: no-cache` | `Q.Web/Endpoints/AdminAuthEndpoints.cs:674-682` |
| 未認証・権限なし | 401・403 を返す（画面へ転送しない）。Pleasanter に確かめられなかったときは 503 | 同 `:708-723` |
| 認証の外の口 | `/session`・`/captcha/challenge`・`/setup`・`/login*`・`/enroll/*`・`/invitations/accept`・SAML の入口・`/pleasanter-sso/check` | 5.2 の表 |
| レート制限 | 認証の試行は IP ごとに 5 分あたり `<APP>_LOGIN_ATTEMPTS_PER_5MIN`（既定 10）。Pleasanter の確認は別枠（5 分 300 回） | `Q.Web/Program.cs:684-703`、`Q.Web/Services/AppSettingsProvider.cs:410-415` |
| CSRF | cookie は `SameSite=Strict`。Pleasanter の確認は JSON 本文に限る | `Q.Web/Endpoints/AdminAuthEndpoints.cs:688-702`、`Q.Web/Endpoints/AdminPleasanterSsoEndpoints.cs:50-52` |
| 監査 | 更新系（GET・HEAD 以外）を 1 件ずつ残す（7 章） | `Q.Web/Services/AuditLogFilter.cs:61-101` |
| 時刻 | UTC と分かる形（`DateTimeKind.Utc`）で返す。監査ログの `occurredAt` だけは旧の形（時間帯の印なし）を保つ | `Q.Web/Endpoints/AdminUserEndpoints.cs:632-641`、`Q.Web/Endpoints/AdminAuditLogEndpoints.cs:68-69` |
| ID | 管理者 ID は GUID。端末は 16 進 64 文字（`^[0-9a-f]{64}$`） | `Q.Web/Endpoints/AdminSessionEndpoints.cs:18` |

パスワードの失敗は、**上限に達した要求自体も通常と同じ 401（理由を区別しない）**を返し、
**その次の要求から**締め出し中は 423 を返す（再確認も同じ）。締め出しの期間は設定による
（Q は既定 15 分、外部設定は不可。6.4）。
根拠: `Q.Web/Services/AdminAuthenticator.cs:154-165,452-465`、
`Q.Web/Endpoints/AdminAuthEndpoints.cs:319-332`、`Q.Web/Endpoints/AdminUserEndpoints.cs:605-618`。

認可の表記: 「公開」= 認証の外、「Pending」= 途中状態の cookie、「Session」= ログイン済みなら誰でも（`Admin.Session.Any`）、
`users.read` などは権限（6 章）。

## 5.2 認証（`AdminAuthEndpoints`）

| メソッド・パス | 認可 | 要求 | 応答（200） | ほかの状態 | 根拠 |
|---|---|---|---|---|---|
| GET `/session` | 公開 | — | 未認証: `{authenticated:false, setupRequired, pending, pendingLoginId, needsEnrollment, samlEnabled, samlLabel, pleasanterSsoEnabled, pleasanterSsoLabel, pleasanterSsoLoginUrl, passwordSignInEnabled, captchaEnabled}`。認証済み: `{authenticated:true, setupRequired, adminUserId, loginId, role, permissions[], language, twoFactor, hasTotp, …同上の入口の情報…, samlSingleLogout, viaPleasanterSso, pleasanterSsoLogoutUrl, mailEnabled, …アプリ固有…}` | — | `Q.Web/Endpoints/AdminAuthEndpoints.cs:42-192` |
| GET `/captcha/challenge` | 公開 | — | proof-of-work の課題 | 404（無効） | 同 `:194-204` |
| POST `/setup` | 公開（管理者 0 人のときだけ） | `{loginId, password}` | `{next:"done"\|"enroll"}` | 400 入力・条件 / 409 既に居る | 同 `:206-263` |
| POST `/login` | 公開 | `{loginId, password, altcha?}` | `{next:"totp"\|"enroll"\|"done"}` | 401 `auth.invalidCredentials`（理由を区別しない）/ 423 締め出し / 404 合言葉を塞いでいる | 同 `:265-337` |
| POST `/enroll/begin` | Pending | `{}` | `{secret, uri}`（`otpauth://`） | 401 / 400 `auth.twoFactorDisabled` / **403 `auth.totpAlreadyEnrolled`** | 同 `:339-384` |
| POST `/enroll/complete` | Pending | `{code}` | `{recoveryCodes[10]}`。Session になる | 401 / 403 登録済み / 400 `auth.enrollmentRestartRequired`・`auth.twoFactorDisabled` / 401 `auth.totpCodeMismatch` | 同 `:386-449` |
| POST `/login/totp` | Pending | `{code}` | `{authenticated:true}`。Session になる。未登録なら `{next:"enroll"}` | 401 / 423（Pending を消す） | 同 `:451-461, 492-532` |
| POST `/login/recovery` | Pending | `{code}` | 同上 | 同上 | 同 `:463-473` |
| POST `/logout` | 任意 | `{}` | `{signedOut:true}`（Session・Pending・Reenroll を消す） | — | 同 `:475-487` |

## 5.3 自分（`/me`）・管理者・招待・端末（`AdminUserEndpoints`・`AdminSessionEndpoints`）

| メソッド・パス | 認可 | 要求 | 応答（200） | ほかの状態 | 根拠 |
|---|---|---|---|---|---|
| GET `/users` | `users.read` | — | `{users:[{adminUserId, loginId, role, isDisabled, hasTotp, invitationPending, lastLoginAt, createdAt}]}` | — | `Q.Web/Endpoints/AdminUserEndpoints.cs:53-73` |
| POST `/users` | `users.write` | `{loginId, role}`（パスワードは受け取らない） | `{adminUserId, invitationToken, expiresAt, mailSent}` | 400 `users.roleNotSupported`・`users.invalidInput` / 409 `users.duplicateLoginId` | 同 `:75-117, 621-630` |
| POST `/users/{id}/invitation` | `users.write` | — | 同上（前の招待は死ぬ） | 404 / 400（無効の人） | 同 `:119-152` |
| POST `/users/{id}/disable` | `users.write` | — | `{isDisabled:true}` | 404 / 409 `users.selfNotAllowed`・`users.lastAdministrator` | 同 `:154-163, 557-572` |
| POST `/users/{id}/enable` | `users.write` | — | `{isDisabled:false}` | 同上 | 同 `:165-173` |
| POST `/users/{id}/totp/reset` | **`users.read`（群の既定のまま）**。画面は `users.resetTwoFactor` で出し分ける（未決事項 U1） | — | `{reset:true}` | 400 `users.selfNotAllowed`・`auth.twoFactorNotEnrolled` / 404 | 同 `:175-222`、`Q.Frontend/src/admin/AdminApp.svelte:631` |
| POST `/users/{id}/role` | `users.write` | `{role}`（定義済みの名前だけ。数値は不可） | `{role}`。相手の全セッションを失効 | 400 / 404 / 409 自分・最後の Administrator | 同 `:224-255, 643-652` |
| GET `/users/{id}/sessions` | `users.read` | — | `{sessions:[端末]}` | — | `Q.Web/Endpoints/AdminSessionEndpoints.cs:79-92` |
| POST `/users/{id}/sessions/{handle}/revoke` | `users.write` | — | `{revoked:true}` | 400 自分 / 404 `sessions.notFound`（他人の handle も 404） | 同 `:94-118, 123-143` |
| POST `/me/password` | Session | `{currentPassword, newPassword}` | `{changed:true, revokedSessions}`（いまの端末以外を失効） | 400 条件・同じ値 / 401 `users.currentPasswordRejected` / 423 | `Q.Web/Endpoints/AdminUserEndpoints.cs:263-292` |
| PUT `/me/language` | Session | `{language}`（空なら未選択へ戻す） | `{language}` | 400 `users.unsupportedLanguage` | 同 `:294-321` |
| POST `/me/totp/disable` | Session | `{password}` | `{removed:true}` | 400 `auth.twoFactorRequired`・`auth.twoFactorNotEnrolled` / 401 / 423 | 同 `:337-389` |
| POST `/me/totp/begin` | Session ＋ **パスワードの再確認** | `{password}` | `{secret, uri}`。Reenroll の cookie を張る | 401 / 423 / 400 `auth.twoFactorDisabled` | 同 `:391-434` |
| POST `/me/totp/complete` | Session ＋ 同じ人の Reenroll | `{code}` | `{recoveryCodes[10]}`（前の復旧コードは無効） | 400 `auth.enrollmentRestartRequired` / 401 `auth.totpCodeMismatch` | 同 `:436-485` |
| GET `/me/sessions` | Session | — | `{sessions:[{adminSessionId, current, createdAt, expiresAt, ipAddress, userAgent, loginMethod:"Password"\|"Saml"\|"PleasanterSso"}]}` | — | `Q.Web/Endpoints/AdminSessionEndpoints.cs:29-39, 145-177` |
| POST `/me/sessions/revoke-others` | Session | — | `{revoked:<件数>}` | — | 同 `:41-52` |
| POST `/me/sessions/{handle}/revoke` | Session | — | `{revoked:true}` | 400 `sessions.currentCannotBeRevoked` / 404 | 同 `:54-77` |
| POST `/invitations/accept` | 公開 | `{token, password, altcha?}` | `{next:"totp"\|"enroll"\|"done"}` | 401 `users.invitationInvalid`（理由を区別しない）/ 400 パスワードの条件（理由つき） | `Q.Web/Endpoints/AdminUserEndpoints.cs:488-555` |

`/me/totp/complete` では方針の `Disabled` を入口で確かめていない。begin の後に設定が `Disabled` へ変わると、
`EnsureEnrollmentAllowed` の例外になる（`Q.Web/Services/AdminAuthenticator.cs:399-410`）。**応答の状態は未確認**（未決事項 U4）。

## 5.4 外部ログイン・監査・設定（共通の候補）

| メソッド・パス | 認可 | 要求 | 応答（200） | ほかの状態 | 根拠 |
|---|---|---|---|---|---|
| GET `/saml/metadata` | 公開 | — | SP のメタデータ（`application/samlmetadata+xml`） | 404（無効） | `Q.Web/Endpoints/AdminSamlEndpoints.cs:72-108` |
| GET `/saml/login` | 公開 | `returnUrl` | 302（IdP へ） | 404 | 同 `:110-143` |
| POST `/saml/acs` | 公開 | SAMLResponse | 302（管理画面へ。失敗は `?samlError=`） | 404 | 同 `:146-263` |
| GET `/saml/logout` | 任意 | — | 302 | — | 同 `:289-327` |
| GET・POST `/saml/slo` | 公開 | SAMLRequest / SAMLResponse | 302 | 404（SLO 未設定） | 同 `:329-410` |
| GET・PUT `/saml/settings`、POST `/saml/settings/test` | `settings.saml` | 設定 / `{metadataUrl}` | 設定 / `{reachable, entityId}` | 400 `{message}`・`{code:"invalid-url"\|"not-metadata"\|"fetch-failed"}` | 同 `:412-504` |
| POST `/pleasanter-sso/check` | 公開 | `{}` | `{status:"unauthenticated"}` / `{status:"signedIn", next}` | 403 / 409 `setup-required` / 503 / 404 | `Q.Web/Endpoints/AdminPleasanterSsoEndpoints.cs:50-152` |
| GET・PUT `/pleasanter-sso/settings`、POST `/pleasanter-sso/settings/test` | `settings.pleasanterSso` | 設定 | 設定 / `{status, …}` | 400 | 同 `:154-253` |
| GET `/audit-logs` | `audit.read` | `limit`（既定 50、1〜500）, `offset`, `from`, `to`, `adminUserId`, `failedOnly`, `action`（部分一致） | `{entries:[{occurredAt, adminUserId, adminLoginId, action, statusCode, targetType, targetId, detail, ipAddress}], hasMore}` | — | `Q.Web/Endpoints/AdminAuditLogEndpoints.cs:25-85` |
| GET・PUT `/settings`、POST `/settings/test`・`/settings/test-send` | `settings.manage` | 設定値の一覧（**項目はアプリ固有**） | 設定（秘密は返さない） | 400 `settings.*` | `Q.Web/Endpoints/AdminSettingsEndpoints.cs:17-179` |
| GET `/application/version` | Session | — | 版・構成の情報 | — | `Q.Web/Endpoints/AdminVersionEndpoints.cs:28-34` |

## 5.5 アプリ固有（共通化しない）

Q だけが持つもの: `/surveys/**`（約 30）・`/templates/**`・`/outbox/**`・`/notifications/**`・`/maintenance`・
`/auto-reply/*`・`/note/preview`・`/me/response-notification`。経路と権限は `Q.Web/Endpoints/Admin*Endpoints.cs` を見る。

AtomFeed・CalendarFeed の業務の入口（`/admin/api/feeds/**`、CalendarFeed は `…/caldav` を足す）と、
IdP の業務の入口（`/api/admin/oidc-clients/**`・`/saml-providers/**`・`/signing-keys/**`・`/pleasanter/**`・`/status`）も共通化しない。

## 5.6 各アプリとの対応

経路ごとの対応（同じ口があるか・形がどう違うか）は [各アプリの差異の一覧](各アプリの差異の一覧.md)の A.3 にまとめた。
大きな差は次の 5 つ。

- **接頭辞**: AtomFeed・CalendarFeed は `/admin/api`。Q と IdP は `/api/admin`（`AF.Web/Endpoints/AdminAuthEndpoints.cs:40`）
- **2 要素の登録**: AtomFeed・CalendarFeed は `/enroll/*` を持たず、`/me/totp/*` をログイン途中にも使う
  （`AF.Web/Endpoints/AdminAuthEndpoints.cs:563-589`）
- **更新の方法**: AtomFeed・CalendarFeed は役割の変更が PUT、端末の失効が DELETE、成功の多くが 204
- **IdP**: 管理者の管理・招待・2 要素の口は無い（Pleasanter で行う）
- **締め出し**: Q は上限到達の失敗も 401、次の要求から 423。AF・CF のログインは締め出し中も 401、
  IdP は上限到達の要求から 429 の画面（根拠と期間の差は 6.4・A.2）。統一は U3 の未決事項
