# ライセンスについて（デュアルライセンス）

<!-- license:begin release=none -->
<!-- license:end release -->

対象ソフトウェアは **VehicleVision.PleasanterTools.Core**

Copyright (C) PMC Co., Ltd. IT Solution Div.

本書で「当社」とは、株式会社ピー・エム・シー ITソリューション事業部を指します。

本ソフトウェアは **デュアルライセンス** で提供されます。利用者は以下のいずれかを選択できます。

| | ライセンス | 全文 | 想定する利用 |
| --- | --- | --- | --- |
| A | **GNU AGPL v3 以降**（AGPL-3.0-or-later） | [`LICENSE`](LICENSE) | オープンソースとして利用・改変・再配布する場合 |
| B | **株式会社ピー・エム・シー ITソリューション事業部の商用ライセンス** | [`LICENSE-COMMERCIAL.md`](LICENSE-COMMERCIAL.md) | AGPL の義務を負わずに利用したい場合（個別契約） |

SPDX 表記： `AGPL-3.0-or-later OR LicenseRef-PMC-Commercial`

## どちらを選ぶべきか

### A. AGPL v3 を選ぶ場合

追加の契約や費用は不要です。ただし AGPL の義務が生じます。

- 本ソフトウェアを改変して**配布する場合、その改変版のソースコードを提供する義務**が生じます
- 本ソフトウェアを改変して**ネットワーク経由で他者に提供する場合（第 13 条）、
  その利用者に対して改変版のソースコードを提供する義務**が生じます
- 本ソフトウェアと結合した著作物を配布する場合、その全体を AGPL で提供する必要があります

<!-- license:begin usage=library -->
**本ソフトウェアは、各アプリケーションへ組み込んで使う共通部品（ライブラリ）です。
組み込んだアプリケーションをネットワーク越しにサービスとして提供する場合は、
その全体が AGPL の対象になり得る**点に注意してください。
<!-- license:end usage -->

組織の内部での利用や受託での提供など、義務の有無が分かりにくい場面は確認してください。

### B. 商用ライセンスを選ぶ場合

AGPL の義務（ソースコード開示・コピーレフト）を負わずに利用できます。
条件は個別契約で定めます。契約できる相手は法人に限りません。次のような場合はこちらを検討してください。

- 改変版のソースコードを公開したくない（ネットワーク経由で提供する場合を含む）
- 自社の proprietary な製品・システムに組み込みたい
- AGPL の義務範囲について社内の法務判断が難しい
- 保守・サポート・SLA を契約に含めたい

**お問い合わせ：** 株式会社ピー・エム・シー ITソリューション事業部
（連絡先は [`LICENSE-COMMERCIAL.md`](LICENSE-COMMERCIAL.md) を参照）

## Pleasanter との関係（デュアルライセンスの前提）

<!-- license:begin independence=library -->
本ソフトウェアは Pleasanter（株式会社インプリム, AGPL v3）の
**独立した別のソフトウェア**です。
<!-- license:end independence -->

- 本体のソースコードを **複製・改変・リンク・プロジェクト参照していません**。Pleasanter のアセンブリも参照しません
- Pleasanter の派生著作物ではありません
  <!-- license:begin connection=api -->
- Pleasanter とは **HTTP 越しに通信**します（標準 Web API など、本体が公開するもの）
  <!-- license:end connection -->
  <!-- license:begin reference=off -->
- Pleasanter 本体のソースは、本リポジトリに含めていません（参照用のサブモジュールも置いていません）
  <!-- license:end reference -->

本ソフトウェアは、Pleasanter 本体の AGPL の**汚染を受けておらず、受け入れるつもりもありません。**
そのため、本ソフトウェアのライセンスは Pleasanter のライセンスに拘束されず、
**著作権者である株式会社ピー・エム・シー ITソリューション事業部が自由に決定できます。
これがデュアルライセンスを成立させている前提です。**

> **AGPL 側だけを見て「どうせ AGPL なのだから本体を取り込んでもよい」と考えないこと。**
> Pleasanter の著作権は当社にないため、**取り込んだ瞬間に B（商用ライセンス）を
> 提供できなくなります。** デュアルの片方が壊れます。

<!-- license:begin pleasanter-note=free -->
本ソフトウェアは、VehicleVision.PleasanterTools の各アプリケーションが共通で組み込んで使う部品（ライブラリ）です。
Core.Web の本人確認（`PleasanterSessionVerifier`）は、Pleasanter の標準 Web API（`/api/users/get`・`/api/sessions/set`・`/api/users/{id}/get`）を
HTTP で呼びます。Core.Data は、Pleasanter のデータベースのスキーマを参照しません。
<!-- license:end pleasanter-note -->

## 依存ソフトウェアについて

本ソフトウェアが利用する OSS は **寛容ライセンス（MIT / BSD 系 / Apache-2.0）** に
限定します。一覧は [`NOTICE`](NOTICE) を参照してください。依存の宣言（パッケージ参照）は各 csproj にあり、
`packages.lock.json` を置いているリポジトリでは、推移的依存と実際の版もそこで確認できます。

<!-- license:begin deps-note=free -->
Core.Data が依存する `Microsoft.Data.SqlClient` は、Windows 向けのネイティブ SNI（`Microsoft.Data.SqlClient.SNI.runtime`。
Microsoft 独自の許諾）を推移的に伴います。条件は、下の「Microsoft.Data.SqlClient.SNI.runtime」の節と [`NOTICE`](NOTICE) に記しています。
<!-- license:end deps-note -->

これは意図的な制約です。**コピーレフト系（GPL / AGPL / SSPL 等）の依存が 1 つでも入ると、
商用ライセンスでの提供ができなくなります。** 依存を追加する際は必ずライセンスを確認してください。

<!-- license:begin sni=library -->
### Microsoft.Data.SqlClient.SNI.runtime（利用するアプリケーションの配置物に入る）

本ソフトウェアの Core.Data は `Microsoft.Data.SqlClient` に依存し、これが
`Microsoft.Data.SqlClient.SNI.runtime`（ネイティブのライブラリ。Microsoft Software License Terms
「MICROSOFT.DATA.SQLCLIENT.SNI LIBRARY」）を推移的に伴います。MIT・BSD・Apache-2.0 ではない
Microsoft 独自の許諾です。

- 本ソフトウェアは、この依存を宣言するだけで、この部品を配置物として持ちません
- 本ソフトウェアを組み込んだアプリケーションの、SQL Server に接続する Windows 向けの配置物には、この部品が含まれます
- 頒布するときの条件は、そのアプリケーションの `LICENSING.md`
  （「例外: Microsoft.Data.SqlClient.SNI.runtime」の節）と `LICENSE-COMMERCIAL.md` に書かれています。
  あわせて [`NOTICE`](NOTICE) を参照してください

条件の正本は、パッケージ内の `LICENSE.txt` です。上の整理は法的な助言ではありません。
<!-- license:end sni -->

<!-- license:begin core=library -->
### 各アプリケーションに組み込まれた場合の商用ライセンス

本ソフトウェアは、当社のアプリケーション（VehicleVision.PleasanterTools の各ツール）へ組み込まれて使われます。
当社のアプリケーションの商用ライセンス（B）の契約を結んだ方には、そのアプリケーションに組み込まれた本ソフトウェアを、
そのアプリケーションと同じ条件で提供します。
本ソフトウェアを単体で利用する場合と、当社のアプリケーション以外へ組み込む場合は、個別に契約します。
<!-- license:end core -->

<!-- license:begin dotnetclient=off -->
<!-- license:end dotnetclient -->

## 当社の共通部品との組み合わせ

VehicleVision.PleasanterTools の各ツールと、当社の共通部品（Core・DotNetClient）を組み合わせて使う場合は、
**呼ぶ側のツールと、呼ばれる側の共通部品を、一体として扱います。**
共通部品は、呼ぶ側のツールの一部として、そのツールの利用条件（AGPL または商用ライセンス）で提供します。
当社のツール以外（外部のアプリケーション）が共通部品を使う場合と、共通部品を単体で使う場合は、
各共通部品の `LICENSING.md` の条件によります。

## 貢献を受け取るとき

**デュアルライセンスを保つには、貢献者からの許諾が要る。**
他人の著作物を商用ライセンスで再頒布する権利は、その人から貰わない限り無い。

**署名のない貢献を 1 つでも取り込むと、その部分だけ商用ライセンスで提供できなくなる。**
後から気付いても、当人全員から同意を集め直すか、該当コードを書き直すしかない。

手続きは [`CLA.md`](CLA.md) と [`CONTRIBUTING.md`](CONTRIBUTING.md) を参照してください。
