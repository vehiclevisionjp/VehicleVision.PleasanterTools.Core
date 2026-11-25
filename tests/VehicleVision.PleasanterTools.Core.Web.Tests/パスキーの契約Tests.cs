using VehicleVision.PleasanterTools.Core.Web.Testing;

namespace VehicleVision.PleasanterTools.Core.Web.Tests;

/// <summary>
/// 共有のパスキー契約（P1〜P10）を、参照ホスト（Core.Data のストア＋Kestrel）へ流す。
/// 契約テスト自体が、実際の Fido2NetLib の検証を通ることの確認でもある。
/// </summary>
public sealed class パスキーの契約Tests : AdminPasskeyContractTests
{
    protected override async Task<IAdminPasskeyContractHost> CreateHostAsync() => await PasskeyReferenceHost.StartAsync();
}
