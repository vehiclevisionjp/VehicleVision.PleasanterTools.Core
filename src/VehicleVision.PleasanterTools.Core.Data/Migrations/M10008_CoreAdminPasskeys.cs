using FluentMigrator;

namespace VehicleVision.PleasanterTools.Core.Data.Migrations;

/// <summary>パスキー（WebAuthn の資格情報）とそのチャレンジ。<see cref="AdminPasskeyStore"/>・<see cref="AdminPasskeyChallengeStore"/> の表。</summary>
/// <remarks>
/// <para>
/// <b>既存の表は変えない</b>（#213 の決定 2/9）。<c>UserId</c> は <c>Core_AdminUsers.Id</c> を指すが、外部キーは張らない
/// （セッションの表と同じ。利用者を消す側が <c>DeleteForUserAsync</c> で消す）。
/// </para>
/// <para>
/// <b>資格情報の ID は一意にしたいが、ID そのものは索引に載せない。</b>
/// WebAuthn の資格情報 ID は最大 1023 バイト（base64url で 1364 文字）で、SQL Server の非クラスタ化索引のキー（1700 バイト）にも
/// MySQL の索引（3072 バイト）にも、文字列のまま載せると収まらない恐れがある。そこで <b>SHA-256（小文字 16 進 64 文字）の
/// <c>CredentialIdHash</c> 列を一意索引にする</b>。<c>CredentialId</c>（base64url）は、表示・調査用に原文も持つ。
/// 引くのは常にハッシュで行い、<c>CredentialId</c> の大文字小文字の照合順序には頼らない。
/// </para>
/// <para>
/// チャレンジは、<b>DB には SHA-256 だけ</b>置く（セッション ID と同じ。DB が漏れても、有効なチャレンジは作れない）。
/// 一度限り（消費は 1 文の DELETE の件数で決める）・短い期限で、<c>ExpiresAt</c> の索引で掃除する。
/// </para>
/// </remarks>
[Migration(10008, "Core: パスキーの資格情報とチャレンジ")]
public sealed class M10008_CoreAdminPasskeys : Migration
{
    public override void Up()
    {
        Create.Table("Core_AdminPasskeys")
            .WithColumn("Id").AsGuid().NotNullable().PrimaryKey()
            .WithColumn("UserId").AsGuid().NotNullable()
            .WithColumn("CredentialId").AsString(1400).NotNullable()
            .WithColumn("CredentialIdHash").AsString(64).NotNullable()
            .WithColumn("PublicKey").AsBinary(2048).NotNullable()
            .WithColumn("SignCount").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("AaGuid").AsGuid().NotNullable()
            .WithColumn("Transports").AsString(256).Nullable()
            .WithColumn("BackupEligible").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("BackedUp").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("Name").AsString(100).NotNullable()
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
            .WithColumn("LastUsedAt").AsDateTime2().Nullable();

        Create.Index("UX_Core_AdminPasskeys_CredentialIdHash").OnTable("Core_AdminPasskeys")
            .OnColumn("CredentialIdHash").Ascending().WithOptions().Unique();
        Create.Index("IX_Core_AdminPasskeys_UserId").OnTable("Core_AdminPasskeys").OnColumn("UserId").Ascending();

        Create.Table("Core_AdminPasskeyChallenges")
            .WithColumn("ChallengeHash").AsString(64).NotNullable().PrimaryKey()
            .WithColumn("Purpose").AsString(16).NotNullable()
            .WithColumn("UserId").AsGuid().Nullable()
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
            .WithColumn("ExpiresAt").AsDateTime2().NotNullable();

        Create.Index("IX_Core_AdminPasskeyChallenges_ExpiresAt").OnTable("Core_AdminPasskeyChallenges")
            .OnColumn("ExpiresAt").Ascending();

        // MySQL は NVARCHAR が utf8mb3 になる。表ごと utf8mb4 へ直す（ハッシュ・ID の比較は大文字小文字を区別する側へ）
        IfDatabase("MySql").Execute.Sql(
            "ALTER TABLE `Core_AdminPasskeys` CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_as_cs;");
        IfDatabase("MySql").Execute.Sql(
            "ALTER TABLE `Core_AdminPasskeyChallenges` CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_as_cs;");
    }

    public override void Down()
    {
        Delete.Table("Core_AdminPasskeyChallenges");
        Delete.Table("Core_AdminPasskeys");
    }
}
