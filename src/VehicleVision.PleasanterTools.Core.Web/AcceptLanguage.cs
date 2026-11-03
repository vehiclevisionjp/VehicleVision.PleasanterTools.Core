using System.Globalization;

namespace VehicleVision.PleasanterTools.Core.Web;

/// <summary><c>Accept-Language</c> から、対応する言語を選ぶ。</summary>
/// <remarks>
/// <para>
/// 品質値（<c>q</c>）の高い順に見て、最初に対応する言語を返す。<b>地域は見ない</b>（<c>en-US</c> は <c>en</c>）。
/// 対応する言語が無ければ <c>null</c>（文言の fallback は呼び出し側が決める）。
/// </para>
/// <para>
/// 対応言語の集合は引数で受け、<c>HttpContext</c> と文言には依存しない。
/// AtomFeed・CalendarFeed が別々に持っていた同一実装を 1 つにしたもの（Issue #141）。
/// </para>
/// </remarks>
public static class AcceptLanguage
{
    /// <summary>ヘッダの値（複数行可）から、対応する最初の言語を返す。</summary>
    /// <param name="headerValues">ヘッダの値。</param>
    /// <param name="supported">対応する言語コード（小文字）の集合。</param>
    public static string? Resolve(IEnumerable<string?>? headerValues, IReadOnlySet<string> supported)
    {
        ArgumentNullException.ThrowIfNull(supported);
        if (headerValues is null)
        {
            return null;
        }

        var candidates = new List<(string Code, double Quality, int Order)>();
        var order = 0;
        foreach (var value in headerValues)
        {
            foreach (var part in (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var pieces = part.Split(';', StringSplitOptions.TrimEntries);
                var quality = 1.0;
                foreach (var parameter in pieces.Skip(1))
                {
                    if (parameter.StartsWith("q=", StringComparison.OrdinalIgnoreCase)
                        && double.TryParse(parameter[2..], NumberStyles.Float, CultureInfo.InvariantCulture, out var q))
                    {
                        quality = q;
                    }
                }

                candidates.Add((pieces[0].Split('-', '_')[0].ToLowerInvariant(), quality, order++));
            }
        }

        return candidates
            .Where(c => c.Quality > 0 && supported.Contains(c.Code))
            .OrderByDescending(c => c.Quality)
            .ThenBy(c => c.Order)
            .Select(c => c.Code)
            .FirstOrDefault();
    }
}
