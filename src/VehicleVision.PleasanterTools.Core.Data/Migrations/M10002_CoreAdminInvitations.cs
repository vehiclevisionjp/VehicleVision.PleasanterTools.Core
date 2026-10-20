using FluentMigrator;

namespace VehicleVision.PleasanterTools.Core.Data.Migrations;

/// <summary>管理者の招待。<see cref="AdminInvitationStore"/> の表。</summary>
/// <remarks>
/// <b>トークンの現物は持たず、SHA-256（16 進 64 文字）だけを持つ。</b> 使用済みの行は、使われた事実として残す。
/// </remarks>
[Migration(10002, "Core: 管理者の招待")]
public sealed class M10002_CoreAdminInvitations : Migration
{
    public override void Up()
    {
        Create.Table("Core_AdminInvitations")
            .WithColumn("InvitationId").AsGuid().NotNullable().PrimaryKey()
            .WithColumn("AdminUserId").AsGuid().NotNullable()
            .WithColumn("TokenHash").AsString(64).NotNullable()
            .WithColumn("ExpiresAt").AsDateTime2().NotNullable()
            .WithColumn("UsedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
            .WithColumn("CreatedBy").AsGuid().Nullable();

        // トークンで引くので一意にする（同じハッシュの招待が 2 つあると、どちらを使ったか決まらない）
        Create.Index("IX_Core_AdminInvitations_TokenHash").OnTable("Core_AdminInvitations")
            .OnColumn("TokenHash").Ascending().WithOptions().Unique();
        Create.Index("IX_Core_AdminInvitations_AdminUserId").OnTable("Core_AdminInvitations")
            .OnColumn("AdminUserId").Ascending();

        // MySQL は文字列が既定で大文字小文字を区別しない。ハッシュの比較は区別する側へ揃える
        IfDatabase("MySql").Execute.Sql(
            "ALTER TABLE `Core_AdminInvitations` CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_as_cs;");
    }

    public override void Down() => Delete.Table("Core_AdminInvitations");
}
