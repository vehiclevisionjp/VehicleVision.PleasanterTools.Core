namespace VehicleVision.PleasanterTools.Core.Cli;

/// <summary>使い方の誤り。終了コードは <see cref="ExitCodes.UsageError"/>。メッセージは ASCII の英語 1 行にする。</summary>
/// <param name="message">メッセージ。秘密（<c>=</c> の後ろの値など）を含めない。</param>
public sealed class UsageException(string message) : Exception(message);
