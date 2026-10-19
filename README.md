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

## 配布と参照（nuget.org）

nuget.org へ配布します（`VehicleVision.PleasanterTools.Core.Mail` / `.Data`）。
**認証なしで取得できる**ので、参照するリポジトリは、`PackageReference` を書くだけです
（`nuget.config` の追加も、トークンも要りません）。

```xml
<PackageReference Include="VehicleVision.PleasanterTools.Core.Mail" Version="0.1.0-preview.2" />
```

- 公開は、タグ `vX.Y.Z` の push で `release.yml` が行います（Trusted Publishing。API キーは持ちません）。
  手順は [`_documents/リリース手順書.md`](_documents/リリース手順書.md)
- **nuget.org に出した版は削除できません**（非推奨にできるだけ）。壊したまま出すより、出さない方が安い
- 0.1.0-preview.1 だけは GitHub Packages にあります（認証が要る）。nuget.org へは preview.2 以降を出します

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
| [ブランチ運用方針](_documents/ブランチ運用方針.md) | ブランチ・PR・CI・ブランチ保護 |
| [リリース手順書](_documents/リリース手順書.md) | 版の付け方と公開の手順 |
| [CONTRIBUTING.md](CONTRIBUTING.md) | 貢献のしかたと CLA |
| [.github/copilot-instructions.md](.github/copilot-instructions.md) | 規約の全文（`AGENTS.md` はここから生成） |

## ライセンス

デュアルライセンス（AGPL-3.0-or-later OR 商用）。[LICENSING.md](LICENSING.md) を参照してください。
