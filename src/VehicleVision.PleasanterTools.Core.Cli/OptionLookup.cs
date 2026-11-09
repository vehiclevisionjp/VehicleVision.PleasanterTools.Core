namespace VehicleVision.PleasanterTools.Core.Cli;

/// <summary>トークンの判定の結果（仕様書 5.2）。</summary>
internal enum TokenKind
{
    /// <summary><c>--</c> だけ。以降はすべて位置引数。</summary>
    Terminator,

    /// <summary><c>--help</c>・<c>-h</c>・<c>-?</c>・<c>/?</c>。</summary>
    Help,

    /// <summary>既知のオプション。</summary>
    Option,

    /// <summary><c>--</c> で始まる未知の名前。</summary>
    UnknownLongOption,

    /// <summary>位置引数（または、位置引数の候補）。</summary>
    Positional,
}

/// <summary>判定した 1 つのトークン。</summary>
/// <param name="Kind">種類。</param>
/// <param name="Option"><see cref="TokenKind.Option"/> のときの定義。</param>
/// <param name="InlineValue"><c>名前=値</c>・<c>名前:値</c> の値。無ければ null。</param>
/// <param name="DisplayName">エラーに出す、値を含まない表記。</param>
internal readonly record struct ArgumentToken(TokenKind Kind, OptionSpec? Option, string? InlineValue, string DisplayName);

/// <summary>既知のオプションの引き当てと、トークンの判定（仕様書 5.1・5.2）。名前は大文字小文字を区別しない。</summary>
internal sealed class OptionLookup
{
    /// <summary>部品が常に受ける <c>--version</c>。</summary>
    public static readonly OptionSpec VersionOption = new()
    {
        Name = "version",
        Description = "Show the version.",
    };

    private readonly Dictionary<string, OptionSpec> longNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OptionSpec> shortNames = new(StringComparer.OrdinalIgnoreCase);

    public OptionLookup(params IEnumerable<OptionSpec>[] groups)
    {
        foreach (var option in groups.SelectMany(group => group))
        {
            longNames[option.Name] = option;
            if (option.ShortName is not null)
            {
                shortNames[option.ShortName] = option;
            }
        }
    }

    public static bool IsHelp(string token) =>
        token.Equals("--help", StringComparison.OrdinalIgnoreCase)
        || token.Equals("-h", StringComparison.OrdinalIgnoreCase)
        || token is "-?" or "/?";

    public ArgumentToken Classify(string token)
    {
        if (token == "--")
        {
            return new(TokenKind.Terminator, null, null, token);
        }
        if (IsHelp(token))
        {
            return new(TokenKind.Help, null, null, token);
        }
        if (token.StartsWith("--", StringComparison.Ordinal))
        {
            var (name, value) = Split(token[2..]);
            return longNames.TryGetValue(name, out var known)
                ? new(TokenKind.Option, known, value, "--" + known.Name)
                : new(TokenKind.UnknownLongOption, null, null, "--" + name);
        }
        if (token.Length >= 2 && token[0] is '-' or '/')
        {
            var (name, value) = Split(token[1..]);
            if (longNames.TryGetValue(name, out var known)
                || (name.Length == 1 && shortNames.TryGetValue(name, out known)))
            {
                return new(TokenKind.Option, known, value, "--" + known.Name);
            }
            return new(TokenKind.Positional, null, null, token[0] + name);
        }
        return new(TokenKind.Positional, null, null, token.Length > 0 ? Split(token).Name : token);
    }

    /// <summary>名前が既知の <c>--</c> 始まりのオプションか（値を取るオプションの次のトークンの判定）。</summary>
    public bool IsKnownLongOption(string token) =>
        token.StartsWith("--", StringComparison.Ordinal) && Classify(token).Kind == TokenKind.Option;

    /// <summary>最初の <c>=</c> または <c>:</c> で、名前と値に分ける。値が無ければ null。</summary>
    private static (string Name, string? Value) Split(string body)
    {
        var index = body.IndexOfAny(['=', ':']);
        return index < 0 ? (body, null) : (body[..index], body[(index + 1)..]);
    }
}
