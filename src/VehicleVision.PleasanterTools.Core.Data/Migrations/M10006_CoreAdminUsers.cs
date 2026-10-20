using FluentMigrator;

namespace VehicleVision.PleasanterTools.Core.Data.Migrations;

/// <summary>管理者・初期管理者の排他・TOTP の復旧コード。<see cref="AdminUserStore"/> の表。</summary>
/// <remarks>
/// <para>
/// <b>パスワードは Argon2 などのハッシュだけ、TOTP の共有鍵は暗号文だけを置く</b>（平文を置かない）。
/// ログイン ID の一意性は、小文字にした <c>LoginIdKey</c> の一意索引で守る（大文字小文字を区別しない）。
/// </para>
/// <para>
/// <c>Core_AdminSetup</c> は「最初の管理者を作った」印。主キー（1 行だけ）の一意性で、同時に 2 人が最初の管理者になるのを防ぐ。
/// 役割（<c>Role</c>）は名前で持つ。Core が意味を知っているのは <c>Administrator</c> だけ（最後の 1 人を守る判定）。
/// </para>
/// </remarks>
[Migration(10006, "Core: 管理者ユーザ")]
public sealed class M10006_CoreAdminUsers : Migration
{
    public override void Up()
    {
        Create.Table("Core_AdminUsers")
            .WithColumn("Id").AsGuid().NotNullable().PrimaryKey()
            .WithColumn("LoginId").AsString(256).NotNullable()
            .WithColumn("LoginIdKey").AsString(256).NotNullable()
            .WithColumn("PasswordHash").AsString(512).NotNullable()
            .WithColumn("Role").AsString(32).NotNullable()
            .WithColumn("IsDisabled").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("FailedLoginCount").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("LockedUntil").AsDateTime2().Nullable()
            .WithColumn("LastLoginAt").AsDateTime2().Nullable()
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
            .WithColumn("TotpSecretCipher").AsString(int.MaxValue).Nullable()
            .WithColumn("TotpPendingCipher").AsString(int.MaxValue).Nullable()
            .WithColumn("TotpLastTimeStep").AsInt64().Nullable()
            .WithColumn("Language").AsString(16).NotNullable();

        Create.Index("IX_Core_AdminUsers_LoginIdKey").OnTable("Core_AdminUsers")
            .OnColumn("LoginIdKey").Ascending().WithOptions().Unique();

        Create.Table("Core_AdminSetup")
            .WithColumn("Id").AsInt32().NotNullable().PrimaryKey();

        // 復旧コードは、ハッシュだけを持つ。使うと UsedAt が入る（使い切り）
        Create.Table("Core_AdminRecoveryCodes")
            .WithColumn("UserId").AsGuid().NotNullable().PrimaryKey()
            .WithColumn("CodeHash").AsString(128).NotNullable().PrimaryKey()
            .WithColumn("UsedAt").AsDateTime2().Nullable();

        // MySQL は NVARCHAR が utf8mb3 になる。表ごと utf8mb4 へ直す（ハッシュの比較は大文字小文字を区別する側へ）
        IfDatabase("MySql").Execute.Sql(
            "ALTER TABLE `Core_AdminUsers` CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_as_cs;");
        IfDatabase("MySql").Execute.Sql(
            "ALTER TABLE `Core_AdminRecoveryCodes` CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_as_cs;");
    }

    public override void Down()
    {
        Delete.Table("Core_AdminRecoveryCodes");
        Delete.Table("Core_AdminSetup");
        Delete.Table("Core_AdminUsers");
    }
}
