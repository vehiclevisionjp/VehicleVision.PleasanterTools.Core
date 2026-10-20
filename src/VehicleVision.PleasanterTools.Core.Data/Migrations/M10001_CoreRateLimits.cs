using FluentMigrator;

namespace VehicleVision.PleasanterTools.Core.Data.Migrations;

/// <summary>複数台で共有するレート制限の数え（固定窓）。<see cref="RateLimitStore"/> の表。</summary>
/// <remarks>
/// 1 つの鍵（枠の名前と接続元）につき 1 行。窓（時間を窓の長さで割った整数）が変わったら、数えを 1 に戻す。
/// 古い行は、定期処理が消す。
/// <b>Core の共有マイグレーションは 5 桁（10001〜）、表は <c>Core_</c> 接頭辞、追加だけ（既存の変更・番号の振り直しをしない）。</b>
/// </remarks>
[Migration(10001, "Core: レート制限の数え")]
public sealed class M10001_CoreRateLimits : Migration
{
    public override void Up() =>
        Create.Table("Core_RateLimits")
            .WithColumn("LimitKey").AsString(200).NotNullable().PrimaryKey()
            .WithColumn("Window").AsInt64().NotNullable()
            .WithColumn("HitCount").AsInt32().NotNullable();

    public override void Down() => Delete.Table("Core_RateLimits");
}
