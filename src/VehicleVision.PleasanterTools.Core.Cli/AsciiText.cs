using System.Text;

namespace VehicleVision.PleasanterTools.Core.Cli;

/// <summary>コンソールへ出す文字列を ASCII の 1 行にする（仕様書 2.1）。</summary>
/// <remarks>出力の Issue（#171）の <c>CliOutput.ToAscii</c> と同じ規則。そちらが入ったら寄せる。</remarks>
internal static class AsciiText
{
    /// <summary>ASCII の印字可能文字（0x20〜0x7E）だけか。</summary>
    public static bool IsPrintable(string value) => value.All(c => c is >= ' ' and <= '~');

    /// <summary>印字可能な ASCII 以外（日本語・制御文字・改行を含む）を、1 文字（サロゲートペアも 1 つ）につき <c>?</c> にする。</summary>
    public static string Sanitize(string value)
    {
        if (IsPrintable(value))
        {
            return value;
        }
        var builder = new StringBuilder(value.Length);
        foreach (var rune in value.EnumerateRunes())
        {
            builder.Append(rune.Value is >= 0x20 and <= 0x7E ? (char)rune.Value : '?');
        }
        return builder.ToString();
    }
}
