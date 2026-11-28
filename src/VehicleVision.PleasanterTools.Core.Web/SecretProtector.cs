using System.ComponentModel;

namespace VehicleVision.PleasanterTools.Core.Web;

/// <summary>
/// 互換のために残している、<see cref="Security.SecretProtector"/> への薄い委譲。
/// 実装・書式（<c>v2$</c>）・AAD・鍵の扱いは <c>VehicleVision.PleasanterTools.Core.Security</c> が持つ。
/// ASP.NET Core に依存しない場所（CLI など）では、そちらを直接参照すること。
/// 新しいコードは <see cref="Security.SecretProtector"/> を使う（この型は 1.0.0 までに外す）。
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class SecretProtector
{
    private readonly Security.SecretProtector inner;

    public SecretProtector(string base64Key)
    {
        inner = new Security.SecretProtector(base64Key);
    }

    public static string GenerateKey() => Security.SecretProtector.GenerateKey();

    public static byte[] DecodeKey(string base64Key) => Security.SecretProtector.DecodeKey(base64Key);

    public string Protect(string plaintext, string context) => inner.Protect(plaintext, context);

    public string? Unprotect(string? protectedValue, string? context) => inner.Unprotect(protectedValue, context);
}
