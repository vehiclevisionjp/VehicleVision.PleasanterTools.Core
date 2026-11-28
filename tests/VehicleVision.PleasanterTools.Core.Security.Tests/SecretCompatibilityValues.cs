namespace VehicleVision.PleasanterTools.Core.Security.Tests;

/// <summary>
/// 2026-10-22 に既存 origin/develop の実装を実行して得た値（Core.Web の <c>AuthenticationCompatibilityValues</c> のうち
/// 暗号に関わるものと同じ）。鍵と入力は試験専用。
/// 既存アプリの保存値（<c>v1:</c>・<c>v1$</c>・<c>v2$</c>）を、置き場を移した後も読めることを固定する。
/// 出典のコミットと生成手順は「Core.Web-共通化の方針」の Issue #92・#99 の節。
/// </summary>
internal static class SecretCompatibilityValues
{
    public const string Key = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";
    public const string Secret = "JBSWY3DPEHPK3PXP";
    public const string Context = "example:totp:00000000-0000-0000-0000-000000000001";
    public const string Packed =
        "v1:iwc6/vk0xShHJs4NdJ419GSzuz5I2mQ4CK0wGFysz0g3pk6Hn4bBWhXL//s=";
    public const string DelimitedV1 =
        "v1$EgQjGQtQ2md5TDL7$sug4ETgv/1uSGhSsJiM0Dw==$Hf/tMNXaA+C9M+Kx11CR5g==";
    public const string DelimitedV2 =
        "v2$KK/G942za8SfPmBv$pZ60/RnuJo4MXMMEQce0mA==$8ZSXDX3a7JdSwjB5SqH7Nw==";
}
