using FluentMigrator;

namespace VehicleVision.PleasanterTools.Core.Data.Migrations;

/// <summary>
/// セッションに「入った方式（via）」を持たせる（#216）。<see cref="AdminSessionStore"/> の表へ列を足す。
/// </summary>
/// <remarks>
/// SAML・Pleasanter は鍵の列から分かるが、パスキー（<see cref="SessionVia.Passkey"/>）は鍵を持たないので、列に残す。
/// <b>NULL は従来どおり「列から推し量る」</b>（既存の行は書き換えない）。表示用で、認可・再認証の証拠には使わない。
/// </remarks>
[Migration(10009, "Core: セッションの入った方式（via）")]
public sealed class M10009_CoreAdminSessionVia : Migration
{
    public override void Up() =>
        Alter.Table("Core_AdminSessions").AddColumn("Via").AsString(32).Nullable();

    public override void Down() =>
        Delete.Column("Via").FromTable("Core_AdminSessions");
}
