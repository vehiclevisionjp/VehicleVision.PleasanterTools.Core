# VehicleVision.PleasanterTools.Core

VehicleVision.PleasanterTools の各アプリ
（AtomFeed / CalendarFeed / IdentityProvider / Questionnaire ほか）が共通で使う、
コア部分の実装です。**アプリごとにコピーが育ってばらつくのを止める**ために切り出しています。

## パッケージ

| パッケージ | 内容 |
|---|---|
| `VehicleVision.PleasanterTools.Core.Mail` | メール送信（SMTP / Amazon SES / Azure Communication Services）と設定の読み取り |
| `VehicleVision.PleasanterTools.Core.Data` | DB 接続・方言差・暗号化検査・マイグレーションと排他・適用／状態確認のコマンド |
| `VehicleVision.PleasanterTools.Core.Web` | Pleasanter の cookie による本人確認、SSO・SAML の設定の読み取りと検証 |
| `VehicleVision.PleasanterTools.Core.Web.Testing` | 試験用。管理画面の HTTP 契約 C1〜C5 を持つ xUnit 基底クラス（未公開） |

対応 RDBMS は SQL Server / PostgreSQL / MySQL（本番）と SQLite（簡易セットアップ・デバッグ用）です。

試験用パッケージはアプリ本体ではなく試験プロジェクトから参照します。
ホストの実装方法と現在の検証結果は
[契約テストの方針](_documents/管理画面の共通仕様/契約テストの方針.md)を参照してください。

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

何を Core.Web へ移し、何をアプリに残したかは [`_documents/Core.Web-共通化の方針.md`](_documents/Core.Web-共通化の方針.md)。

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

## 配布と参照（nuget.org）

nuget.org へ配布します（`VehicleVision.PleasanterTools.Core.Mail` / `.Data` / `.Web`）。
**認証なしで取得できる**ので、参照するリポジトリは、`PackageReference` を書くだけです
（`nuget.config` の追加も、トークンも要りません）。

```xml
<PackageReference Include="VehicleVision.PleasanterTools.Core.Mail" Version="0.1.0-preview.11" />
```

- 公開は、タグ `vX.Y.Z` の push で `release.yml` が行います（Trusted Publishing。API キーは持ちません）。
  手順は [`_documents/リリース手順書.md`](_documents/リリース手順書.md)
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
| [コア化の方針](_documents/コア化の方針.md) | 何を Core に置くか、アプリ固有の値の渡し方、アプリの採用手順、移行の状況 |
| [Core.Web 共通化の方針](_documents/Core.Web-共通化の方針.md) | Core.Web へ移したもの・移さなかったもの、アプリ固有の値の受け口、各アプリの置き換え手順 |
| [ブランチ運用方針](_documents/ブランチ運用方針.md) | ブランチ・PR・CI・ブランチ保護 |
| [リリース手順書](_documents/リリース手順書.md) | 版の付け方と公開の手順 |
| [CONTRIBUTING.md](CONTRIBUTING.md) | 貢献のしかたと CLA |
| [.github/copilot-instructions.md](.github/copilot-instructions.md) | 規約の全文（`AGENTS.md` はここから生成） |

## ライセンス

デュアルライセンス（AGPL-3.0-or-later OR 商用）。[LICENSING.md](LICENSING.md) を参照してください。
