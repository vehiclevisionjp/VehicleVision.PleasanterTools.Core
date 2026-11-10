# 商用ライセンス（LicenseRef-PMC-Commercial）

対象ソフトウェアは **VehicleVision.PleasanterTools.Core**

Copyright (C) PMC Co., Ltd. IT Solution Div.

本書で「当社」とは、株式会社ピー・エム・シー ITソリューション事業部を指します。

本文書は、[`LICENSING.md`](LICENSING.md) の選択肢 **B（商用ライセンス）** の概要です。
**正式な条件は個別に締結する契約書によります。** 本文書は契約書ではありません。

## 概要

商用ライセンスは、**GNU AGPL v3 の義務を負わずに**本ソフトウェアを利用するための
選択肢です。契約できる相手は法人に限りません。次を想定しています。

- 改変版のソースコードを公開せずに利用する（ネットワーク経由で提供する場合を含む）
- proprietary な製品・システムへ組み込む
- 保守・サポート・SLA を契約に含める

## AGPL 版との違い

| | AGPL v3（選択肢 A） | 商用ライセンス（選択肢 B） |
| --- | --- | --- |
| 費用 | 無償 | 個別契約 |
| ソースコード開示義務 | **あり**（改変して配布またはネットワーク提供する場合を含む） | なし |
| proprietary 製品への組み込み | 不可 | 可 |
| 保守・サポート | なし | 契約による |

## 対象範囲

本リポジトリのうち、**当社が著作権を持つ部分**が対象です。次は対象外です。

- サードパーティの OSS と部品（[`NOTICE`](NOTICE) 参照）。各ライセンスに従ってください
  <!-- license:begin sni-scope=library -->
- Windows 向けの `Microsoft.Data.SqlClient.SNI.runtime`（Core.Data が依存する `Microsoft.Data.SqlClient` が推移的に伴うもの）は
  Microsoft 独自の許諾で、本ソフトウェアの一部ではありません。利用するアプリケーションの配置物に入ります。
  頒布の条件は、[`LICENSING.md`](LICENSING.md) を参照してください
  <!-- license:end sni-scope -->
  <!-- license:begin reference-scope=off -->
- Pleasanter 本体（AGPL v3）。**本リポジトリには含めておらず、本ソフトウェアの一部ではありません**
  <!-- license:end reference-scope -->

<!-- license:begin core-terms=library -->
## VehicleVision.PleasanterTools の各ツール・各共通部品に組み込まれた場合

本ソフトウェアは、VehicleVision.PleasanterTools の各ツール・各共通部品に組み込まれて使われます。
**VehicleVision.PleasanterTools の各ツール・各共通部品に組み込まれた本ソフトウェアは、組み込み先と一体として扱い、組み込み先の条件で提供します。**
組み込み先がさらに VehicleVision.PleasanterTools の別のツール・共通部品へ組み込まれる場合は、その組み込み先と一体として扱います。
**組み込み先の商用契約を結んだ方には、組み込まれた本ソフトウェアを、その組み込み先と同じ条件で提供します。**
条件は、組み込み先の `LICENSE-COMMERCIAL.md` と契約書によります。

次の場合は、個別に契約します。

- 本ソフトウェアを単体で利用する場合
- VehicleVision.PleasanterTools 以外へ、本ソフトウェアを直接組み込む場合

外部のアプリケーションが VehicleVision.PleasanterTools の共通部品を利用する場合、その共通部品に組み込まれた本ソフトウェアの部分は、
上のとおり、その共通部品の条件によります。
<!-- license:end core-terms -->

<!-- license:begin dotnetclient-terms=off -->
<!-- license:end dotnetclient-terms -->

<!-- license:begin sni-terms=library -->
<!-- license:end sni-terms -->

## お問い合わせ

株式会社ピー・エム・シー ITソリューション事業部

お問い合わせは、次のページの連絡先からお願いします: [https://www.vehiclevision.jp/division/](https://www.vehiclevision.jp/division/)
