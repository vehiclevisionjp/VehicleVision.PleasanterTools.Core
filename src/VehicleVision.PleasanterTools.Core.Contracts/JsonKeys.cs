namespace VehicleVision.PleasanterTools.Core.Contracts;

/// <summary><c>--json</c> の枠（仕様書 3 章）のキー名。固有の項目の名前は、ここにある名前を使わない。</summary>
public static class JsonKeys
{
    /// <summary>コマンド名。</summary>
    public const string Command = "command";

    /// <summary>終了コードが 0 のとき真。</summary>
    public const string Ok = "ok";

    /// <summary>終了コード。</summary>
    public const string ExitCode = "exitCode";

    /// <summary>失敗・問題の一覧。</summary>
    public const string Errors = "errors";

    /// <summary>警告の一覧。</summary>
    public const string Warnings = "warnings";

    /// <summary><c>--version --json</c> のバージョン。</summary>
    public const string Version = "version";

    /// <summary><c>errors</c>・<c>warnings</c> の要素の、固定の文字列のコード。</summary>
    public const string Code = "code";

    /// <summary><c>errors</c>・<c>warnings</c> の要素の、英語の 1 行の文言。</summary>
    public const string Message = "message";

    /// <summary><c>errors</c>・<c>warnings</c> の要素の、場所を示す任意の文字列。</summary>
    public const string Path = "path";
}
