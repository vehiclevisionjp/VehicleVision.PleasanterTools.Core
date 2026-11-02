using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;

namespace VehicleVision.PleasanterTools.Core.Web;

/// <summary>画面の配信にキャッシュの指示を付ける。</summary>
/// <remarks>
/// <para>
/// ⚠️ **付けないと、ブラウザが独自の判断で使い回す。**
/// <c>Cache-Control</c> が無いと <c>Last-Modified</c> からの推測で保持してよいことになっており、
/// **再検証すらしない場合がある。** 実際に作り直した画面が出ず、古いものを 1 週間見続けた事例がある。
/// </para>
/// <para>**入口の HTML と、中身を指す資産で扱いを分ける。**</para>
/// <list type="bullet">
/// <item>
/// **入口（<c>admin.html</c> / <c>index.html</c>）は名前が変わらない。** ここが古いと古い資産を指し続けるので、毎回確かめさせる。
/// ⚠️ <c>no-store</c> にはしない。再検証は許し、変わっていなければ 304 で済ませる
/// </item>
/// <item>
/// **資産（<c>/assets/</c>）は内容ハッシュ付きの名前。** 中身が変われば名前が変わるので、長く持たせても古いものを掴み続けない
/// </item>
/// </list>
/// <para>Questionnaire・IdentityProvider が別々に持っていた同一実装を 1 つにしたもの（Issue #140）。</para>
/// </remarks>
public static class StaticCachePolicy
{
    /// <summary>入口の HTML。**毎回確かめさせる。**</summary>
    public const string RevalidateValue = "no-cache";

    /// <summary>内容ハッシュ付きの資産。**1 年持たせる。**</summary>
    public const string ImmutableValue = "public, max-age=31536000, immutable";

    /// <summary>静的ファイルの応答へキャッシュの指示を付ける。</summary>
    public static void Apply(StaticFileResponseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Context.Response.Headers.CacheControl = ValueFor(context.Context.Request.Path);
    }

    /// <summary>経路に対する指示を返す。</summary>
    /// <remarks>
    /// ⚠️ **HTML かどうかで決める。** 拡張子の無い経路（<c>/admin</c> など）も入口の HTML を返すため、
    /// そこも確かめさせる側に倒す。<c>/assets/</c> の外に置いた画像や書体は、名前が変わらないので対象にしない。
    /// </remarks>
    public static string ValueFor(PathString path)
    {
        var value = path.Value ?? string.Empty;

        return value.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase)
            && !value.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            ? ImmutableValue
            : RevalidateValue;
    }
}
