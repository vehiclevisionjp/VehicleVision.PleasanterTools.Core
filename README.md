# VehicleVision.PleasanterTools.Core

VehicleVision.PleasanterTools の各アプリ
（AtomFeed / CalendarFeed / IdentityProvider / Questionnaire ほか）が共通で使う、
コア部分の実装です。**アプリごとにコピーが育ってばらつくのを止める**ために切り出しています。

## パッケージ

| パッケージ | 内容 |
|---|---|
| `VehicleVision.PleasanterTools.Core.Mail` | メール送信（SMTP / Amazon SES / Azure Communication Services）と設定の読み取り |
| `VehicleVision.PleasanterTools.Core.Data` | DB 接続・RDBMS ごとの方言差・接続の暗号化検査・起動時マイグレーションと排他 |

対応 RDBMS は SQL Server / PostgreSQL / MySQL（本番）と SQLite（簡易セットアップ・デバッグ用）です。

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
```

## 配布と参照（GitHub Packages）

`vehiclevisionjp` の GitHub Packages へ配布します。**Public のリポジトリでも、GitHub Packages の
NuGet は取得に認証が要ります**（`read:packages` 権限のトークン）。

参照するリポジトリの `nuget.config`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="vehiclevisionjp" value="https://nuget.pkg.github.com/vehiclevisionjp/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
    <packageSource key="vehiclevisionjp">
      <package pattern="VehicleVision.PleasanterTools.Core.*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

- **手元:** `dotnet nuget add source` か、ユーザー単位の `nuget.config` に資格情報を置く
  （リポジトリへコミットしない）。
- **GitHub Actions:** ジョブの `permissions` に `packages: read` を付け、`GITHUB_TOKEN` を使う。
  取得する側のリポジトリが、パッケージの設定（Manage Actions access）で許可されている必要がある。

将来 nuget.org へ移す場合は、`release.yml` の公開先を差し替えるだけで済みます
（`id` を変えないこと）。

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

## ライセンス

デュアルライセンス（AGPL-3.0-or-later OR 商用）。[LICENSING.md](LICENSING.md) を参照してください。
