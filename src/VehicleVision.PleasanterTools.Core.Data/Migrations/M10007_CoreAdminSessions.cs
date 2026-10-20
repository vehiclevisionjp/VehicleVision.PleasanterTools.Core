using FluentMigrator;

namespace VehicleVision.PleasanterTools.Core.Data.Migrations;

/// <summary>管理者のサーバ側セッション。<see cref="AdminSessionStore"/> の表。</summary>
/// <remarks>
/// <para>
/// cookie に載せるのは<b>推測できない 256 bit の ID</b> だけで、DB には <b>SHA-256 だけ</b>（<c>Id</c>）を置く。
/// DB が漏れても、そこから有効な cookie は作れない。
/// </para>
/// <para>
/// SAML・Pleasanter のログインで入ったセッションの鍵（単一ログアウト・再検証に要る値）は、列で持つ（検索するため）。
/// 外部キーは張らない（利用者を消す側が、<c>DeleteForUserAsync</c> で消す）。
/// </para>
/// </remarks>
[Migration(10007, "Core: 管理者のセッション")]
public sealed class M10007_CoreAdminSessions : Migration
{
    public override void Up()
    {
        Create.Table("Core_AdminSessions")
            .WithColumn("Id").AsString(64).NotNullable().PrimaryKey()
            .WithColumn("UserId").AsGuid().NotNullable()
            .WithColumn("Kind").AsString(16).NotNullable()
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
            .WithColumn("ExpiresAt").AsDateTime2().NotNullable()
            .WithColumn("IpAddress").AsString(64).Nullable()
            .WithColumn("UserAgent").AsString(512).Nullable()
            .WithColumn("SamlNameId").AsString(256).Nullable()
            .WithColumn("SamlSessionIndex").AsString(256).Nullable()
            .WithColumn("PleasanterTenantId").AsInt32().Nullable()
            .WithColumn("PleasanterUserId").AsInt32().Nullable()
            .WithColumn("PleasanterVerifiedAt").AsDateTime2().Nullable();

        Create.Index("IX_Core_AdminSessions_UserId").OnTable("Core_AdminSessions").OnColumn("UserId").Ascending();
        Create.Index("IX_Core_AdminSessions_ExpiresAt").OnTable("Core_AdminSessions").OnColumn("ExpiresAt").Ascending();
        Create.Index("IX_Core_AdminSessions_SamlNameId").OnTable("Core_AdminSessions").OnColumn("SamlNameId").Ascending();

        // MySQL は NVARCHAR が utf8mb3 になる。表ごと utf8mb4 へ直す（ID のハッシュは大文字小文字を区別する側へ）
        IfDatabase("MySql").Execute.Sql(
            "ALTER TABLE `Core_AdminSessions` CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_as_cs;");
    }

    public override void Down() => Delete.Table("Core_AdminSessions");
}
