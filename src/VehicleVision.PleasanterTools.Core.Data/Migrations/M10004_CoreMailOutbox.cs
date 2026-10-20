using FluentMigrator;

namespace VehicleVision.PleasanterTools.Core.Data.Migrations;

/// <summary>メールの送信待ち（アウトボックス）。<see cref="MailOutbox"/> の表。</summary>
/// <remarks>
/// <b>宛先・件名・本文は 1 つにまとめて暗号化して <c>PayloadProtected</c> に置く</b>（列を分けると、暗号化の掛け忘れが列ごとに起き得る）。
/// 送れたら行ごと消す。<c>SubjectId</c> は、そのメールが何に紐づくかをアプリが任意に持つ（例: アンケートの ID）。Core は意味を持たない。
/// </remarks>
[Migration(10004, "Core: メールの送信待ち")]
public sealed class M10004_CoreMailOutbox : Migration
{
    public override void Up()
    {
        Create.Table("Core_MailOutbox")
            .WithColumn("MailId").AsGuid().NotNullable().PrimaryKey()
            .WithColumn("Kind").AsInt32().NotNullable()
            .WithColumn("SubjectId").AsGuid().Nullable()
            .WithColumn("PayloadProtected").AsString(int.MaxValue).NotNullable()
            .WithColumn("Status").AsInt32().NotNullable()
            .WithColumn("RetryCount").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("NextAttemptAt").AsDateTime2().NotNullable()
            .WithColumn("LastError").AsString(1024).Nullable()
            .WithColumn("LockedBy").AsString(64).Nullable()
            .WithColumn("LockedUntil").AsDateTime2().Nullable()
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
            .WithColumn("UpdatedAt").AsDateTime2().NotNullable();

        // 送れる行を、次の試行の時刻の順に確保する
        Create.Index("IX_Core_MailOutbox_Status_NextAttemptAt").OnTable("Core_MailOutbox")
            .OnColumn("Status").Ascending()
            .OnColumn("NextAttemptAt").Ascending();

        // MySQL は NVARCHAR が utf8mb3 になる。表ごと utf8mb4 へ直す
        IfDatabase("MySql").Execute.Sql(
            "ALTER TABLE `Core_MailOutbox` CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_as_cs;");
    }

    public override void Down() => Delete.Table("Core_MailOutbox");
}
