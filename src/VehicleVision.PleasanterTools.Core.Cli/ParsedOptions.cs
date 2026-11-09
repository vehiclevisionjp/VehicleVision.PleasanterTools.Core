using System.Globalization;

namespace VehicleVision.PleasanterTools.Core.Cli;

/// <summary>
/// 解析したオプションと位置引数。値は「引数 &gt; 環境変数 &gt; 既定値」の順で返す。
/// 名前は接頭辞なし・大文字小文字を区別しない。定義に無い名前を渡したら <see cref="ArgumentException"/>（呼び出し側の誤り）。
/// 数・範囲・一覧の検査に失敗したら <see cref="UsageException"/>（使い方の誤り）。
/// </summary>
public sealed class ParsedOptions
{
    private readonly Dictionary<string, OptionSpec> specs;
    private readonly Dictionary<string, string> given;
    private readonly Func<string, string?> getEnvironmentVariable;

    internal ParsedOptions(
        IEnumerable<OptionSpec> specs,
        Dictionary<string, string> given,
        IReadOnlyList<string> positionals,
        Func<string, string?> getEnvironmentVariable)
    {
        this.specs = specs.ToDictionary(spec => spec.Name, StringComparer.OrdinalIgnoreCase);
        this.given = new Dictionary<string, string>(given, StringComparer.OrdinalIgnoreCase);
        Positionals = positionals;
        this.getEnvironmentVariable = getEnvironmentVariable;
    }

    /// <summary>オプションを持たない空の結果（<see cref="ParseOutcome.Run"/> 以外で使う）。</summary>
    internal static ParsedOptions Empty { get; } = new([], [], [], _ => null);

    /// <summary>位置引数（<c>--</c> 以降を含む）。</summary>
    public IReadOnlyList<string> Positionals { get; }

    /// <summary>値が決まっているか（引数・環境変数、フラグは引数か環境変数の <c>true</c>、値は既定値でも真）。</summary>
    public bool Has(string name)
    {
        var spec = Find(name);
        if (given.ContainsKey(spec.Name))
        {
            return true;
        }
        return spec.Kind == OptionKind.Flag ? EnvironmentFlag(spec) : Value(name) is not null;
    }

    /// <summary>フラグが立っているか。引数にあれば真、無ければ環境変数が <c>true</c>（大文字小文字を問わない）のとき真。</summary>
    public bool Flag(string name)
    {
        var spec = Find(name);
        if (spec.Kind != OptionKind.Flag)
        {
            throw new ArgumentException($"Option '{spec.Name}' is not a flag.", nameof(name));
        }
        return given.ContainsKey(spec.Name) || EnvironmentFlag(spec);
    }

    /// <summary>値を返す。引数 &gt; 環境変数（空は未設定）&gt; <see cref="OptionSpec.DefaultValue"/>。どれも無ければ null。</summary>
    public string? Value(string name)
    {
        var spec = Find(name);
        if (spec.Kind != OptionKind.Value)
        {
            throw new ArgumentException($"Option '{spec.Name}' does not take a value.", nameof(name));
        }
        if (given.TryGetValue(spec.Name, out var value))
        {
            return value;
        }
        return EnvironmentValue(spec) ?? spec.DefaultValue;
    }

    /// <summary>
    /// 整数を返す。引数か環境変数が無ければ <paramref name="defaultValue"/>（範囲の検査はしない）。
    /// 整数でない・範囲外は <see cref="UsageException"/>（<c>Option --name must be an integer from min to max.</c>）。
    /// </summary>
    public int Int32(string name, int min, int max, int defaultValue)
    {
        var spec = Find(name);
        var raw = given.TryGetValue(spec.Name, out var value) ? value : EnvironmentValue(spec);
        if (raw is null)
        {
            return defaultValue;
        }
        if (!int.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
            || number < min
            || number > max)
        {
            throw new UsageException($"Option --{spec.Name} must be an integer from {min} to {max}.");
        }
        return number;
    }

    /// <summary>カンマ区切りの一覧を返す。各要素の前後の空白を除き、空の要素を捨てる。値が無ければ空。</summary>
    public IReadOnlyList<string> List(string name) =>
        (Value(name) ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// 同時に指定できない組を検査する。2 つ以上が決まっていたら
    /// <see cref="UsageException"/>（<c>Use only one of --a and --b.</c>）。
    /// </summary>
    public void EnsureAtMostOne(params string[] names)
    {
        var specsGiven = names.Select(Find).ToList();
        if (specsGiven.Count(spec => Has(spec.Name)) > 1)
        {
            throw new UsageException("Use only one of " + string.Join(" and ", specsGiven.Select(spec => "--" + spec.Name)) + ".");
        }
    }

    /// <summary>引数にも環境変数にも無い必須のオプションがあれば、最初の 1 つの名前を返す（解析の最後の検査）。</summary>
    internal string? FirstMissingRequired(IEnumerable<OptionSpec> required) =>
        required.FirstOrDefault(spec => spec.Required && !given.ContainsKey(spec.Name) && EnvironmentValue(spec) is null)?.Name;

    private OptionSpec Find(string name) =>
        specs.TryGetValue(name, out var spec)
            ? spec
            : throw new ArgumentException($"Option '{name}' is not defined for this command.", nameof(name));

    private string? EnvironmentValue(OptionSpec spec)
    {
        if (spec.EnvironmentVariable is null)
        {
            return null;
        }
        var value = getEnvironmentVariable(spec.EnvironmentVariable);
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private bool EnvironmentFlag(OptionSpec spec) =>
        string.Equals(EnvironmentValue(spec), "true", StringComparison.OrdinalIgnoreCase);
}
