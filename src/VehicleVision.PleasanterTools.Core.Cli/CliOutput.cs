using System.Text;
using System.Text.Json;

namespace VehicleVision.PleasanterTools.Core.Cli;

/// <summary>
/// stdout と stderr の書き分け、接頭辞、色、ASCII 化、秘密の伏せ（仕様書 2 章）。
/// 複数の処理から呼ばれても、1 行が途中で混ざらない。
/// </summary>
/// <remarks>
/// 接頭辞付きの行（<see cref="Ok"/>・<see cref="Warn"/>・<see cref="Error"/>・<see cref="Info"/>）と、
/// 接頭辞なしの行（<see cref="Result"/>・<see cref="Progress"/>）は、すべて <c>redact</c> → <see cref="ToAscii"/> の順で
/// 1 行にしてから書く。秘密を伏せる部品そのものは持たず、呼び出し側が <c>redact</c> で渡す（仕様書 5.6）。
/// 色は、出力先が端末で、<c>NO_COLOR</c> が未設定または空で、<c>TERM</c> が <c>dumb</c> でないときだけ（2.4）。判定は出力先ごと。
/// </remarks>
public sealed class CliOutput
{
    private const string Reset = "\u001b[0m";
    private const string Green = "\u001b[32m";
    private const string Yellow = "\u001b[33m";
    private const string Red = "\u001b[31m";

    private readonly TextWriter _stdout;
    private readonly TextWriter _stderr;
    private readonly bool _stdoutColor;
    private readonly bool _stderrColor;
    private readonly Func<string, string>? _redact;

    // stdout と stderr で共有する。行が混ざらず、書いた順も保たれる
    private readonly object _gate = new();

    private bool _stdoutWritten;

    /// <summary>
    /// stdout へ 1 行でも書いたか。<c>--json</c> のとき、想定外の例外で枠を出すか
    /// （出力済みなら二重にしない。仕様書 3.2）の判定に使う。
    /// </summary>
    public bool HasWrittenToStdout
    {
        get
        {
            lock (_gate)
            {
                return _stdoutWritten;
            }
        }
    }

    /// <summary>出力を作る。</summary>
    /// <param name="stdout">結果の出力先。</param>
    /// <param name="stderr">診断の出力先。</param>
    /// <param name="getEnvironmentVariable">環境変数の取得（<c>NO_COLOR</c> の判定に使う）。</param>
    /// <param name="stdoutIsTerminal"><paramref name="stdout"/> が端末につながっているか。</param>
    /// <param name="stderrIsTerminal"><paramref name="stderr"/> が端末につながっているか。</param>
    /// <param name="redact">秘密を伏せる関数。省略時は何もしない。</param>
    public CliOutput(
        TextWriter stdout,
        TextWriter stderr,
        Func<string, string?> getEnvironmentVariable,
        bool stdoutIsTerminal,
        bool stderrIsTerminal,
        Func<string, string>? redact = null)
    {
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        _stdout = stdout;
        _stderr = stderr;
        _stdoutColor = UseColor(stdoutIsTerminal, getEnvironmentVariable);
        _stderrColor = UseColor(stderrIsTerminal, getEnvironmentVariable);
        _redact = redact;
    }

    /// <summary>
    /// コンソール（<see cref="Console.Out"/>・<see cref="Console.Error"/>）へ出す。端末かどうかは、リダイレクトされていないかで判定する。
    /// Windows では、端末の出力先の仮想端末の処理を有効にし、有効にできなければ、その出力先は色なしにする（仕様書 2.4）。
    /// </summary>
    /// <param name="getEnvironmentVariable">環境変数の取得。省略時は環境から読む。</param>
    /// <param name="redact">秘密を伏せる関数。</param>
    public static CliOutput ForConsole(Func<string, string?>? getEnvironmentVariable = null, Func<string, string>? redact = null) =>
        ForConsole(
            Console.Out,
            Console.Error,
            getEnvironmentVariable ?? Environment.GetEnvironmentVariable,
            !Console.IsOutputRedirected,
            !Console.IsErrorRedirected,
            ConsoleVirtualTerminal.TryEnable,
            redact);

    /// <summary>
    /// <see cref="ForConsole(Func{string, string?}?, Func{string, string}?)"/> の、実機に依存する部分（端末か・仮想端末の処理を有効にできるか）を
    /// 差し替えられる形。端末の出力先についてだけ <paramref name="enableVirtualTerminal"/>（引数は stderr か）を呼び、偽なら色なしにする。
    /// </summary>
    internal static CliOutput ForConsole(
        TextWriter stdout,
        TextWriter stderr,
        Func<string, string?> getEnvironmentVariable,
        bool stdoutIsTerminal,
        bool stderrIsTerminal,
        Func<bool, bool> enableVirtualTerminal,
        Func<string, string>? redact = null) =>
        new(
            stdout,
            stderr,
            getEnvironmentVariable,
            stdoutIsTerminal && enableVirtualTerminal(false),
            stderrIsTerminal && enableVirtualTerminal(true),
            redact);

    /// <summary>成功。stdout へ <c>[OK] message</c>（緑）。</summary>
    public void Ok(string message) => WritePrefixed(_stdout, _stdoutColor, "[OK]", Green, message);

    /// <summary>警告。stderr へ <c>[WARN] message</c>（黄）。</summary>
    public void Warn(string message) => WritePrefixed(_stderr, _stderrColor, "[WARN]", Yellow, message);

    /// <summary>失敗。stderr へ <c>[ERROR] message</c>（赤）。</summary>
    public void Error(string message) => WritePrefixed(_stderr, _stderrColor, "[ERROR]", Red, message);

    /// <summary>補足。stdout へ <c>[INFO] message</c>（色なし）。</summary>
    public void Info(string message) => WritePrefixed(_stdout, _stdoutColor, "[INFO]", null, message);

    /// <summary>結果の 1 行。stdout へ接頭辞なしで書く（一覧・表・要約）。</summary>
    public void Result(string line) => WriteLine(_stdout, Clean(line));

    /// <summary>進行表示の 1 行。stderr へ接頭辞なしで書く。</summary>
    public void Progress(string line) => WriteLine(_stderr, Clean(line));

    /// <summary>
    /// <c>--json</c> の結果を、枠つきの 1 行の JSON として stdout へ書く（<see cref="JsonResult.Serialize"/>）。
    /// 文言は <c>redact</c> を通す。JSON は ASCII なので <see cref="ToAscii"/> は通さない。
    /// </summary>
    public void WriteJson(
        string command,
        bool ok,
        int exitCode,
        IReadOnlyList<CliIssue> errors,
        IReadOnlyList<CliIssue> warnings,
        Action<Utf8JsonWriter>? writeFields = null) =>
        WriteLine(_stdout, JsonResult.Serialize(command, ok, exitCode, errors, warnings, writeFields, _redact));

    /// <summary><c>--version --json</c> の枠を、1 行の JSON として stdout へ書く（<see cref="JsonResult.SerializeVersion"/>）。</summary>
    public void WriteVersionJson(string version) => WriteLine(_stdout, JsonResult.SerializeVersion(version));

    /// <summary>
    /// 色を付けるか（仕様書 2.4）。出力先が端末で、<c>NO_COLOR</c> が未設定または空で、<c>TERM</c> が <c>dumb</c> でないときだけ真。
    /// </summary>
    /// <param name="isTerminal">出力先が端末につながっているか。</param>
    /// <param name="getEnvironmentVariable">環境変数の取得。</param>
    public static bool UseColor(bool isTerminal, Func<string, string?> getEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        return isTerminal
            && string.IsNullOrEmpty(getEnvironmentVariable("NO_COLOR"))
            && !string.Equals(getEnvironmentVariable("TERM"), "dumb", StringComparison.Ordinal);
    }

    /// <summary>
    /// コンソールへ出す文字列を ASCII の 1 行にする（仕様書 2.1）。
    /// 印字可能な ASCII（0x20〜0x7E）以外（日本語・制御文字・改行を含む）を、1 文字（サロゲートペアも 1 つ）につき <c>?</c> にする。
    /// </summary>
    /// <param name="text">元の文字列。</param>
    public static string ToAscii(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (IsPrintableAscii(text))
        {
            return text;
        }
        var builder = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            builder.Append(rune.Value is >= 0x20 and <= 0x7E ? (char)rune.Value : '?');
        }
        return builder.ToString();
    }

    /// <summary>印字可能な ASCII（0x20〜0x7E）だけか。</summary>
    internal static bool IsPrintableAscii(string value) => value.All(c => c is >= ' ' and <= '~');

    /// <summary>秘密を伏せて ASCII にする（<see cref="Result"/> が書く内容。書かずに確かめたいときに使う）。</summary>
    internal string Sanitize(string text) => Clean(text);

    /// <summary>
    /// 改行を変えずに stdout へ書く（シェルのスクリプトは LF のままにする。CR が混ざると bash・zsh が読めない）。
    /// 呼び出し側が ASCII であることを保証する。
    /// </summary>
    internal void WriteText(string text)
    {
        lock (_gate)
        {
            _stdout.Write(text);
            _stdoutWritten = true;
        }
    }

    // 秘密を伏せてから ASCII の 1 行にする。先に ASCII 化すると、非 ASCII を含む秘密に一致しなくなる
    private string Clean(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return ToAscii(_redact is null ? text : _redact(text));
    }

    private void WritePrefixed(TextWriter writer, bool color, string prefix, string? colorStart, string message)
    {
        var text = Clean(message);
        // 色は接頭辞の部分だけ。直後に戻すので、メッセージは既定の色のまま
        var line = color && colorStart is not null
            ? $"{colorStart}{prefix}{Reset} {text}"
            : $"{prefix} {text}";
        WriteLine(writer, line);
    }

    private void WriteLine(TextWriter writer, string line)
    {
        lock (_gate)
        {
            writer.WriteLine(line);
            if (ReferenceEquals(writer, _stdout))
            {
                _stdoutWritten = true;
            }
        }
    }
}
