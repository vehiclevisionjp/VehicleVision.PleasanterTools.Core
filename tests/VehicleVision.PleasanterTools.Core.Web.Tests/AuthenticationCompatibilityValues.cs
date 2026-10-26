namespace VehicleVision.PleasanterTools.Core.Web.Tests;

/// <summary>
/// 2026-10-22 に既存 origin/develop の実装を実行して得た値。鍵と入力は試験専用。
/// A/C の同一実装と Q の保存書式を、方式指定なしで読み取れることを固定する。
/// 出典のコミットと生成手順は「Core.Web-共通化の方針」の Issue #92・#99 の節。
/// </summary>
internal static class AuthenticationCompatibilityValues
{
    public const string Password = "互換性 password 🔑";
    public const string Key = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";
    public const string Secret = "JBSWY3DPEHPK3PXP";
    public const string Context = "example:totp:00000000-0000-0000-0000-000000000001";
    public static readonly DateTime TotpUtcNow = new(2026, 10, 22, 9, 0, 0, DateTimeKind.Utc);
    public const string TotpCode = "221236";
    // 既存の保存値。以後は 64 バイトで書くが、32・64 バイトの固定値は置き換えない。
    public const string Hash32 =
        "v1$210000$/7zF8aCjaXYAs55fxPNzeQ==$y3H6L19StislZOjF8S70N7Zjh5oY48kSNzy4S/UgZwg=";
    public const string Hash64 =
        "v1$1000$Oj663vTAEG3g/DakxIxl4g==$"
        + "BUj6IgBO9rXA2+gtBVxg5kBPpk85I3s7A4SGiAhlDRIU9mKxhxNmLJ918nbTSjbWd0AnQTJ6aRBzSNhzRynbpA==";
    public const string Recovery =
        "v1$210000$wMOLa1fPLd2SxO1s7d/2og==$SAvYcHncByhVYTR+zIFglmd3fL8bGK406e7Ld08ig6M=";
    public const string Packed =
        "v1:iwc6/vk0xShHJs4NdJ419GSzuz5I2mQ4CK0wGFysz0g3pk6Hn4bBWhXL//s=";
    public const string DelimitedV1 =
        "v1$EgQjGQtQ2md5TDL7$sug4ETgv/1uSGhSsJiM0Dw==$Hf/tMNXaA+C9M+Kx11CR5g==";
    public const string DelimitedV2 =
        "v2$KK/G942za8SfPmBv$pZ60/RnuJo4MXMMEQce0mA==$8ZSXDX3a7JdSwjB5SqH7Nw==";
    public const string Token = "0123456789abcdefghijklmnopqrstuv";
    public const string TokenHash = "73337f479fe170d73e53e247f3052e4243cc9c2a0ffa621853d9385c619efb77";
    public const string OldTokenHash = "czN/R5/hcNc+U+JH8wUuQkPMnCoP+mIYU9k4XGGe+3c=";
    public const string OldRecoveryHash = "f57cbf1e3e471b25db4b8ad0844df41b2dae9c7d1944944d128e49294d412f66";
}
