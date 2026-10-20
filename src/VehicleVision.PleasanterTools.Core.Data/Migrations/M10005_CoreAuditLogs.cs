using FluentMigrator;

namespace VehicleVision.PleasanterTools.Core.Data.Migrations;

/// <summary>管理操作の記録。<see cref="AuditLogStore"/> の表。</summary>
/// <remarks>
/// <para>
/// <b>追記だけの記録で、ほかの表へ外部キーを張らない。</b> 操作した人が消えても、記録は残る。
/// 実行者は <c>ActorKey</c>（管理者の ID、または <c>{Tenant}:{User}</c> のような外部ユーザの鍵）と、
/// <c>ActorLoginId</c>（その時のログイン ID。<b>居ない ID での試みも残す</b>）で持つ。
/// </para>
/// <para>
/// 結果は <c>Succeeded</c>（必須。失敗も残す）と、HTTP を介する操作だけが持つ <c>StatusCode</c>（NULL 可）。
/// <b>資格情報・回答本文を入れない</b>（<c>DetailJson</c> も同じ）。
/// </para>
/// </remarks>
[Migration(10005, "Core: 管理操作の記録")]
public sealed class M10005_CoreAuditLogs : Migration
{
    public override void Up()
    {
        Create.Table("Core_AuditLogs")
            .WithColumn("AuditLogId").AsGuid().NotNullable().PrimaryKey()
            .WithColumn("OccurredAt").AsDateTime2().NotNullable()
            .WithColumn("ActorKey").AsString(64).Nullable()
            .WithColumn("ActorLoginId").AsString(256).Nullable()
            .WithColumn("Action").AsString(128).NotNullable()
            .WithColumn("Succeeded").AsInt32().NotNullable()
            .WithColumn("StatusCode").AsInt32().Nullable()
            .WithColumn("TargetType").AsString(128).Nullable()
            .WithColumn("TargetId").AsString(512).Nullable()
            .WithColumn("DetailJson").AsString(int.MaxValue).Nullable()
            .WithColumn("IpAddress").AsString(64).Nullable();

        // 新しい順に読む（画面・掃除）
        Create.Index("IX_Core_AuditLogs_OccurredAt").OnTable("Core_AuditLogs")
            .OnColumn("OccurredAt").Descending();

        // MySQL は NVARCHAR が utf8mb3 になる。表ごと utf8mb4 へ直す
        IfDatabase("MySql").Execute.Sql(
            "ALTER TABLE `Core_AuditLogs` CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_as_cs;");
    }

    public override void Down() => Delete.Table("Core_AuditLogs");
}
