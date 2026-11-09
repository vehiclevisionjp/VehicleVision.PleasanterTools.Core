namespace VehicleVision.PleasanterTools.Core.Cli;

/// <summary><c>--json</c> の <c>errors</c>・<c>warnings</c> の要素（仕様書 3.1・3.2）。</summary>
/// <param name="Code">PascalCase の固定の文字列。コードの一覧はツールごとの文書に書く。</param>
/// <param name="Message">英語の 1 行。秘密を含めない。</param>
/// <param name="Path">場所を示す任意の文字列（<c>sources[1].id</c> など）。</param>
public sealed record CliIssue(string Code, string Message, string? Path = null);
