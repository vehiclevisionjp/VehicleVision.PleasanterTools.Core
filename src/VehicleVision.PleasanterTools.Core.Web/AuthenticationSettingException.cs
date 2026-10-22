using System.Collections.Immutable;

namespace VehicleVision.PleasanterTools.Core.Web;

/// <summary>認証設定の誤り。アプリは種類と引数を自分の多言語メッセージへ写し替えられる。</summary>
public sealed class AuthenticationSettingException : InvalidOperationException
{
    internal AuthenticationSettingException(
        AuthenticationSettingError error, string message, object[] arguments, Exception? innerException = null)
        : base(message, innerException)
    {
        Error = error;
        Arguments = [.. arguments];
    }

    /// <summary>誤りの種類。</summary>
    public AuthenticationSettingError Error { get; }

    /// <summary>設定名を含む文言の引数。順序は各種類の宣言を参照。</summary>
    public ImmutableArray<object> Arguments { get; }
}

/// <summary>認証設定の誤りの種類。括弧内は <see cref="AuthenticationSettingException.Arguments"/> の順序。</summary>
public enum AuthenticationSettingError
{
    /// <summary>必須設定なし（有効化の設定名、欠けている設定名）。</summary>
    RequiredWhenEnabled,
    /// <summary>真偽値の誤り（設定名、値）。</summary>
    BooleanValueInvalid,
    /// <summary>整数の範囲の誤り（設定名、最小値、最大値、値）。</summary>
    IntegerRangeInvalid,
    /// <summary>知らない値（設定名、値、使える名前を「 / 」で連結した文字列）。</summary>
    UnknownValue,
    /// <summary>cookie 名の文字の誤り（設定名、値）。</summary>
    CookieNameInvalidChars,
    /// <summary>本アプリの cookie と重なる（設定名、本アプリの接頭辞、値）。</summary>
    CookieNameOverlapsOwn,
    /// <summary>cookie 名なし（設定名）。</summary>
    CookieNamesEmpty,
    /// <summary>内部 URL の誤り（設定名、値）。</summary>
    InternalUrlInvalid,
    /// <summary>ブラウザ URL の誤り（設定名、値）。</summary>
    BrowserUrlInvalid,
    /// <summary>ID リストの長さの誤り（設定名）。</summary>
    IdListTooLong,
    /// <summary>ID リストの値の誤り（設定名）。</summary>
    IdListInvalid,
    /// <summary>絶対 URL の誤り（設定名、値）。</summary>
    AbsoluteUrlInvalid,
    /// <summary>署名証明書なし（設定名）。</summary>
    CertificateRequired,
    /// <summary>属性名なし（取り出し先の設定名、属性名の設定名）。</summary>
    LoginIdClaimRequired,
    /// <summary>PEM が途中で終わっている（設定名）。</summary>
    CertificatePemIncomplete,
    /// <summary>PEM を読めない（設定名）。元の例外は InnerException。</summary>
    CertificatePemUnreadable,
    /// <summary>証明書が見つからない（設定名）。</summary>
    CertificateNotFound,
    /// <summary>base64 の証明書を読めない（設定名）。元の例外は InnerException。</summary>
    CertificateUnreadable,
}
