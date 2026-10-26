# VehicleVision.PleasanterTools.Core

VehicleVision.PleasanterTools の各アプリ
（AtomFeed / CalendarFeed / IdentityProvider / Questionnaire ほか）が共通で使う、
コア部分の実装です。**アプリごとにコピーが育ってばらつくのを止める**ために切り出しています。

## PleasanterTools ファミリー

PleasanterTools は、ノーコード／ローコード業務基盤 [Pleasanter](https://pleasanter.org/) の
**「孫の手」として当社が作っているツール群**です。
運営: 株式会社ピー・エム・シー ITソリューション事業部 / PMC Co., Ltd. IT Solution Div.
Core は共通部品で、[Core.Data][nuget-data]・[Core.Mail][nuget-mail]・[Core.Web][nuget-web]・
[Core.Web.Testing][nuget-testing] の 4 パッケージを nuget.org で公開しています（プレビュー版）。

### ファミリーの一覧

リポジトリ名は `vehiclevisionjp/VehicleVision.PleasanterTools.<名前>`。社内用 Template は非公開です。
状態は公開済みの最新版、Core の利用は各リポジトリの `develop` の `.csproj` によります（2026-10-22 確認）。
「なし」は Core.Data・Core.Mail・Core.Web の参照なし。「開発中」の説明は目的であり、提供済み機能の保証ではありません。

| リポジトリ | 何をするものか | 種別 | ライセンス | 状態 | Core の利用 |
|---|---|---|---|---|---|
| [Questionnaire][repo-questionnaire] | Pleasanter に回答を蓄積する Web アンケート・フォーム | アプリ | [デュアル][license-questionnaire] | v0.12.0 | Core.Data・Core.Mail・Core.Web |
| [CalendarFeed][repo-calendarfeed] | Pleasanter のレコードを iCalendar の購読フィードとして公開 | アプリ | [デュアル][license-calendarfeed] | 開発中 | Core.Data・Core.Mail・Core.Web |
| [AtomFeed][repo-atomfeed] | Pleasanter のレコードを Atom / RSS フィードとして公開 | アプリ | [デュアル][license-atomfeed] | 開発中 | Core.Data・Core.Mail・Core.Web |
| [IdentityProvider][repo-identityprovider] | Pleasanter のユーザー情報で SAML / OpenID Connect の認証を提供 | アプリ | [デュアル][license-identityprovider] | 開発中 | Core.Data |
| [McpOAuthWrapper][repo-mcpoauthwrapper] | Pleasanter の MCP サーバーへの OAuth 接続を提供 | アプリ | AGPL | v0.3.3 | なし |
| [IndexCreator][repo-indexcreator] | Pleasanter のサイト構成に合わせた DB インデックスを管理 | ツール | AGPL | v0.3.1 | なし |
| [CalendarStyleCreator][repo-calendarstylecreator] | Pleasanter のカレンダーに休日を表示する CSS を生成・配置 | ツール | AGPL | 開発中 | なし |
| [BinaryMigrator][repo-binarymigrator] | 添付ファイルの保管先を Rds・Local・AzureBlob 間で移行 | ツール | [デュアル][license-binarymigrator] | 開発中 | なし |
| [DatabaseConverter][repo-databaseconverter] | Pleasanter の DB を SQL Server・PostgreSQL・MySQL 間で変換 | ツール | [デュアル][license-databaseconverter] | 開発中 | なし |
| [DotNetClient][repo-dotnetclient] | Pleasanter の標準 Web API を呼ぶ .NET クライアント | ライブラリ | LGPL | 開発中 | なし |
| [FeedIngest][repo-feedingest] | RSS / Atom を取り込み、Pleasanter のレコードを作成・更新 | ツール | AGPL | 開発中 | なし |
| [MailIngest][repo-mailingest] | POP3 / IMAP のメールを取り込み、Pleasanter のレコードを作成・更新 | ツール | [デュアル][license-mailingest] | 開発中 | なし |
| [Core][repo-core] | 各ツールの DB・メール・Web・契約テストの共通部品 | ライブラリ | [デュアル][license-core] | v0.1.0-preview.12（プレビュー） | 提供元 |
| Template（社内テンプレート） | 新しいツールのひな形と共通の開発運用を提供 | ツール | AGPL | 開発中（社内用） | なし |

### 名前の付け方

Pleasanter のデータを外へ**出す**側は `…Feed`（AtomFeed・CalendarFeed）、
外のデータを Pleasanter へ**入れる**側は `…Ingest`（FeedIngest・MailIngest）と名付けています。

### ライセンスの使い分け

表の「デュアル」は **AGPL-3.0-or-later と商用の選択制**。業務インパクトが大きいツールで、有償提供の道を残します。
「AGPL」は **AGPL-3.0-or-later 単独**。「LGPL」は **LGPL-3.0-or-later** で、
DotNetClient をアプリへ組み込んで広く使ってもらうための選択です。
公開リポジトリは種類を問わず **CLA（貢献者許諾契約）の対象**とし、未整備のものへは順次導入します。
商用ライセンスの相談先は、表の「デュアル」から辿れる各リポジトリの `LICENSING.md` を参照してください。

### 共通の仕組み

| 仕組み | 紹介・詳細 |
|---|---|
| テンプレート | 社内 Template から新しいツールを作り、名前と資料・設定を実態に合わせます。[作成の流れ][family-operations] |
| CI・ブランチ・リリース | CI を通して `develop` → `master`、タグ `vX.Y.Z` で公開します。[ブランチ運用][branch-policy]・[リリース手順][release-guide] |
| patch 更新 | npm・NuGet の patch 更新を、ロックファイル・NOTICE と合わせて PR にし、必須チェック通過後に自動適用します。[更新の流れ][family-operations] |
| 設定の監査 | 共通の GitHub 設定やファイル・Core 参照版の差を読み取り専用で調べ、自動修復はしません。[監査の範囲][family-operations] |
| 管理画面 | アプリ間で揃える認証・権限・設定などを定めています（各アプリへ反映中）。[共通仕様書][admin-spec] |
| 契約テスト | Core.Web.Testing で管理画面の HTTP 契約を共有し、Questionnaire の試験で採用しています。[契約テストの方針][contract-policy] |

### 技術マニュアル

導入・設定・使い方は、公開サイト [Pleasanter Technical Manual][technical-manual] の
[ツール別マニュアル][tool-manuals]を参照してください。
この README はファミリーの入口、Core の開発・設計・運用の詳細は以下の各節と `_documents/` に置いています。

## パッケージ

| パッケージ | 内容 |
|---|---|
| `VehicleVision.PleasanterTools.Core.Mail` | メール送信（SMTP / Amazon SES / Azure Communication Services）と設定の読み取り |
| `VehicleVision.PleasanterTools.Core.Data` | DB 接続・方言差・暗号化検査・マイグレーションと排他・適用／状態確認のコマンド |
| `VehicleVision.PleasanterTools.Core.Web` | Pleasanter の cookie による本人確認、SSO・SAML の設定の読み取りと検証、パスワード・TOTP・秘密の暗号化・復旧コード・招待・権限表 |
| `VehicleVision.PleasanterTools.Core.Web.Testing` | 試験用。管理画面の HTTP 契約の xUnit 基底クラス。C1〜C5 は preview.11 公開済み、追加の安全性契約は未公開 |

対応 RDBMS は SQL Server / PostgreSQL / MySQL（本番）と SQLite（簡易セットアップ・デバッグ用）です。

試験用パッケージはアプリ本体ではなく試験プロジェクトから参照します。
ホストの実装方法と現在の検証結果は
[契約テストの方針][contract-policy]を参照してください。

## アプリ固有の値は引数で渡す

環境変数の接頭辞・SQLite のファイル名・ロック名・マイグレーションの置き場は、アプリごとに違います。
Core には書かず、呼び出し側が渡します。

```csharp
var options = MailOptions.FromConfiguration(configuration, "QUESTIONNAIRE_MAIL_");

var connectionString = DatabaseConnectionString.Resolve(
    provider, configured, contentRootPath,
    sqliteFileName: "questionnaire.db",
    settingName: "QUESTIONNAIRE_DB_CONNECTIONSTRING");

var migrator = new DatabaseMigrator(
    typeof(M0001_InitialSchema).Assembly,
    lockName: "questionnaire-startup-migrations");

// Core.Web。本アプリの Pleasanter 接続設定（IPleasanterApiConnectionSource）はアプリが登録する
builder.Services.AddPleasanterSessionVerifier(new PleasanterSessionVerifierSettings
{
    SessionProbeKey = "VehicleVision.Questionnaire.SsoProbe",
    BaseUrlSettingName = "QUESTIONNAIRE_PLEASANTER_BASEURL",
    ApiKeySettingName = "QUESTIONNAIRE_PLEASANTER_APIKEY",
});
```

何を Core.Web へ移し、何をアプリに残したかは [`_documents/Core.Web-共通化の方針.md`][web-policy]。

### SSO・SAML 設定とマイグレーションコマンド（preview.12 公開予定）

```csharp
var sso = PleasanterSsoOptions.FromConfiguration(
    configuration, prefix: "APP_", ownCookiePrefix: "app.",
    registerRoles: ["Editor", "Administrator"], defaultRegisterRole: "Editor");
var saml = SamlOptions.FromConfiguration(
    configuration, prefix: "APP_",
    registerRoles: ["Editor", "Administrator"], defaultRegisterRole: "Editor");

// DB と外部設定を合成する場合は FromValues(valueOf, 設定キー型, …) を使う。
var keys = new PleasanterSsoSettingKeys("APP_");
// 設定の誤りは AuthenticationSettingException（InvalidOperationException の派生）。
// Error と Arguments から、アプリの多言語例外へ写し替えられる。

if (MigrationCommand.IsRequested(args))
{
    return await MigrationCommand.RunAsync(migrator, provider, connectionString, args);
}
```

役割はアプリが名前の一覧と既定を渡します。アプリの列挙の数値・複合表記の解釈や
ITfoxtec への設定変換はアプリに残します。既存アプリのアダプターの手順は上記の方針を参照してください。

コマンドは `--migrate`（適用）と `--migrate-status`（未適用ありなら終了コード 1）を受け付けます。
DB の待機は `--wait-for-db=N`、ロックの待機は `RunAsync` の `lockTimeout`
（省略時は `AppDatabase.DefaultMigrationLockTimeout`）で指定します。
**AtomFeed・CalendarFeed は、置き換え後に適用前の一覧と適用件数が出るようになります。**
状態の確認はスキーマを適用しませんが、新規 SQLite DB に空の `VersionInfo` 表を作る場合があります。

**今回は Core のみを実装しています。各アプリの置き換えは preview.12 公開後の別作業です。**
この変更自体はタグを付けず、公開しません。

### 認証の基盤（Issue #92・#99、preview.15 向け）

**読むのは既存の保存値すべて、書くのは新しい 1 方式**です。互換方式の enum は廃止しました。
preview.14 の API を使うアプリはまだなく、この変更は破壊的変更です。

| 部品 | 読むもの | 新しく書くもの |
|---|---|---|
| `new PasswordHasher()` | `v1$反復$塩$ハッシュ` の 32・64 バイト | 同じ書式の PBKDF2-HMAC-SHA512、210000 回・塩 16 バイト・ハッシュ 64 バイト |
| `new TotpService()` | 空白と ASCII ハイフンを除いた 6 桁の ASCII 数字、前後 1 枠 | 20 バイトの Base32 共有鍵、要素別にエスケープした登録 URI |
| `new SecretProtector(base64Key)` | 文脈つき `v1:`・`v2$`、文脈なしの旧 `v1$` | `Protect(plaintext, context)` による文脈必須の `v2$` のみ |

パスワードは空・null を生成時に拒否し、検証時は不一致にします。保存値の反復は符号・空白なしの正数、
塩は非空です。照合成功時に、回数が現行未満かハッシュが 32 バイトなら `NeedsRehash` を返します。
不正な保存値の検証は不一致、`NeedsRehash(stored)` は true です。
TOTP の不正な入力・共有鍵は不一致です。同じ時間枠の再使用防止はアプリが担います。
暗号の復号失敗は旧 `v1:` も含めて null に統一し、呼び出し側で必ず失敗を判定します。
文脈なしの暗号 API は廃止しましたが、旧 `v1$` は `Unprotect(value, context)` で引き続き読めます。

`RecoveryCode` は新規 64 バイト・既存 32 バイトも検証できる塩つき PBKDF2、
`InvitationToken` は小文字 16 進の SHA-256、
`AdminPermissions<TRole>` はアプリから渡された表だけを扱います。
鍵の自動生成・アプリの役割・環境変数名・認証の入口は持ちません。
旧 SHA-256 復旧コードは再発行、旧 Base64 招待は再招待で移行し、旧形式へのフォールバックは残しません。

**今回の変更では各アプリを置換せず、マージ・公開もしません。**
旧書式の詳細と公開後の追随手順は
[Core.Web 共通化の方針](_documents/Core.Web-共通化の方針.md#認証の基盤の共通化issue-92)を参照してください。

## 配布と参照（nuget.org）

nuget.org へ配布します（`VehicleVision.PleasanterTools.Core.Mail` / `.Data` / `.Web`）。
**認証なしで取得できる**ので、参照するリポジトリは、`PackageReference` を書くだけです
（`nuget.config` の追加も、トークンも要りません）。

```xml
<PackageReference Include="VehicleVision.PleasanterTools.Core.Mail" Version="0.1.0-preview.11" />
```

- 公開は、タグ `vX.Y.Z` の push で `release.yml` が行います（Trusted Publishing。API キーは持ちません）。
  手順は [`_documents/リリース手順書.md`][release-guide]
- **nuget.org に出した版は削除できません**（非推奨にできるだけ）。壊したまま出すより、出さない方が安い
- 0.1.0-preview.1 だけは GitHub Packages に公開しました（認証が要る）。
  nuget.org の公開済みの版は Mail・Data が preview.2〜11、Web は preview.10〜11 です
  （2026-10-22 に各パッケージの版一覧を確認。[Mail][mail-versions] / [Data][data-versions] / [Web][web-versions]）

[mail-versions]: https://api.nuget.org/v3-flatcontainer/vehiclevision.pleasantertools.core.mail/index.json
[data-versions]: https://api.nuget.org/v3-flatcontainer/vehiclevision.pleasantertools.core.data/index.json
[web-versions]: https://api.nuget.org/v3-flatcontainer/vehiclevision.pleasantertools.core.web/index.json

## バージョン

セマンティックバージョニング（`vX.Y.Z`）。1.0.0 未満は互換を保証しません。
パッケージはすべて同じ版で出します。`Directory.Build.props` の `VersionPrefix` が単一情報源です。
タグ `vX.Y.Z` を打つと `release.yml` が公開します。

## 開発

```bash
dotnet build
dotnet test
```

- SDK は `global.json`（10.0.100 / `latestFeature`）。**各アプリの `global.json` と同じ値に保つこと。**
- 依存のバージョンは `Directory.Packages.props` が単一情報源（Central Package Management）。
- ブランチは `develop` から切り、PR の base も `develop` にする。

## ドキュメントと規約

| 文書 | 内容 |
|---|---|
| [コア化の方針][core-policy] | 何を Core に置くか、アプリ固有の値の渡し方、アプリの採用手順、移行の状況 |
| [Core.Web 共通化の方針][web-policy] | Core.Web へ移したもの・移さなかったもの、アプリ固有の値の受け口、各アプリの置き換え手順 |
| [ブランチ運用方針][branch-policy] | ブランチ・PR・CI・ブランチ保護 |
| [リリース手順書][release-guide] | 版の付け方と公開の手順 |
| [CONTRIBUTING.md][contributing] | 貢献のしかたと CLA |
| [.github/copilot-instructions.md][instructions] | 規約の全文（`AGENTS.md` はここから生成） |

## ライセンス

デュアルライセンス（AGPL-3.0-or-later OR 商用）。[LICENSING.md][license-core] を参照してください。

[repo-questionnaire]: https://github.com/vehiclevisionjp/VehicleVision.PleasanterTools.Questionnaire
[repo-calendarfeed]: https://github.com/vehiclevisionjp/VehicleVision.PleasanterTools.CalendarFeed
[repo-atomfeed]: https://github.com/vehiclevisionjp/VehicleVision.PleasanterTools.AtomFeed
[repo-identityprovider]: https://github.com/vehiclevisionjp/VehicleVision.PleasanterTools.IdentityProvider
[repo-mcpoauthwrapper]: https://github.com/vehiclevisionjp/VehicleVision.PleasanterTools.McpOAuthWrapper
[repo-indexcreator]: https://github.com/vehiclevisionjp/VehicleVision.PleasanterTools.IndexCreator
[repo-calendarstylecreator]: https://github.com/vehiclevisionjp/VehicleVision.PleasanterTools.CalendarStyleCreator
[repo-binarymigrator]: https://github.com/vehiclevisionjp/VehicleVision.PleasanterTools.BinaryMigrator
[repo-databaseconverter]: https://github.com/vehiclevisionjp/VehicleVision.PleasanterTools.DatabaseConverter
[repo-dotnetclient]: https://github.com/vehiclevisionjp/VehicleVision.PleasanterTools.DotNetClient
[repo-feedingest]: https://github.com/vehiclevisionjp/VehicleVision.PleasanterTools.FeedIngest
[repo-mailingest]: https://github.com/vehiclevisionjp/VehicleVision.PleasanterTools.MailIngest
[repo-core]: https://github.com/vehiclevisionjp/VehicleVision.PleasanterTools.Core
[license-questionnaire]: https://github.com/vehiclevisionjp/VehicleVision.PleasanterTools.Questionnaire/blob/develop/LICENSING.md
[license-calendarfeed]: https://github.com/vehiclevisionjp/VehicleVision.PleasanterTools.CalendarFeed/blob/develop/LICENSING.md
[license-atomfeed]: https://github.com/vehiclevisionjp/VehicleVision.PleasanterTools.AtomFeed/blob/develop/LICENSING.md
[license-identityprovider]: https://github.com/vehiclevisionjp/VehicleVision.PleasanterTools.IdentityProvider/blob/develop/LICENSING.md
[license-binarymigrator]: https://github.com/vehiclevisionjp/VehicleVision.PleasanterTools.BinaryMigrator/blob/develop/LICENSING.md
[license-databaseconverter]: https://github.com/vehiclevisionjp/VehicleVision.PleasanterTools.DatabaseConverter/blob/develop/LICENSING.md
[license-mailingest]: https://github.com/vehiclevisionjp/VehicleVision.PleasanterTools.MailIngest/blob/develop/LICENSING.md
[license-core]: https://github.com/vehiclevisionjp/VehicleVision.PleasanterTools.Core/blob/develop/LICENSING.md
[nuget-data]: https://www.nuget.org/packages/VehicleVision.PleasanterTools.Core.Data
[nuget-mail]: https://www.nuget.org/packages/VehicleVision.PleasanterTools.Core.Mail
[nuget-web]: https://www.nuget.org/packages/VehicleVision.PleasanterTools.Core.Web
[nuget-testing]: https://www.nuget.org/packages/VehicleVision.PleasanterTools.Core.Web.Testing
[family-operations]: https://github.com/vehiclevisionjp/VehicleVision.PleasanterTools.Core/blob/develop/_documents/ファミリーの共通運用の情報.md
[branch-policy]: https://github.com/vehiclevisionjp/VehicleVision.PleasanterTools.Core/blob/develop/_documents/ブランチ運用方針.md
[release-guide]: https://github.com/vehiclevisionjp/VehicleVision.PleasanterTools.Core/blob/develop/_documents/リリース手順書.md
[admin-spec]: https://github.com/vehiclevisionjp/VehicleVision.PleasanterTools.Core/blob/develop/_documents/管理画面の共通仕様書.md
[contract-policy]: https://github.com/vehiclevisionjp/VehicleVision.PleasanterTools.Core/blob/develop/_documents/管理画面の共通仕様/契約テストの方針.md
[core-policy]: https://github.com/vehiclevisionjp/VehicleVision.PleasanterTools.Core/blob/develop/_documents/コア化の方針.md
[web-policy]: https://github.com/vehiclevisionjp/VehicleVision.PleasanterTools.Core/blob/develop/_documents/Core.Web-共通化の方針.md
[contributing]: https://github.com/vehiclevisionjp/VehicleVision.PleasanterTools.Core/blob/develop/CONTRIBUTING.md
[instructions]: https://github.com/vehiclevisionjp/VehicleVision.PleasanterTools.Core/blob/develop/.github/copilot-instructions.md
[technical-manual]: https://pleasanter.docs.vehiclevision.jp/
[tool-manuals]: https://pleasanter.docs.vehiclevision.jp/tools/
