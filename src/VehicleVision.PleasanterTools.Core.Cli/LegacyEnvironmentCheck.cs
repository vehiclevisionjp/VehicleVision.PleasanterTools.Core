using VehicleVision.PleasanterTools.Core.Contracts;

namespace VehicleVision.PleasanterTools.Core.Cli;

/// <summary>
/// 旧名の検出（仕様書 6.1、決定 U11・N2）。旧名が設定されていれば、新名との併用を含めて報告する。
/// 空の環境変数は未設定として扱う（5.6）。
/// </summary>
public static class LegacyEnvironmentCheck
{
    /// <summary>
    /// 設定されている旧名ごとに <c>Environment variable X is no longer supported. Use Y.</c> を返す。
    /// 順序は <paramref name="legacy"/> の列挙順。無ければ空。
    /// </summary>
    /// <param name="getEnvironmentVariable">環境変数の取得。試験では差し替える。</param>
    /// <param name="legacy">旧名 → 新名。省略時は <see cref="PleasanterEnvironmentNames.Legacy"/>。ツール固有の対応は呼び出し側が足して渡す。</param>
    public static IReadOnlyList<string> Find(
        Func<string, string?> getEnvironmentVariable,
        IReadOnlyDictionary<string, string>? legacy = null)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        var messages = new List<string>();
        foreach (var (oldName, newName) in legacy ?? PleasanterEnvironmentNames.Legacy)
        {
            if (!string.IsNullOrEmpty(getEnvironmentVariable(oldName)))
            {
                messages.Add($"Environment variable {oldName} is no longer supported. Use {newName}.");
            }
        }

        return messages;
    }

    /// <summary>
    /// 旧名が 1 つでもあれば、すべて <c>[ERROR]</c> で出して <see cref="ExitCodeValues.UsageError"/> を返す。無ければ <see cref="ExitCodeValues.Success"/>。
    /// </summary>
    public static int Report(
        CliOutput output,
        Func<string, string?> getEnvironmentVariable,
        IReadOnlyDictionary<string, string>? legacy = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        var messages = Find(getEnvironmentVariable, legacy);
        foreach (var message in messages)
        {
            output.Error(message);
        }

        return messages.Count == 0 ? ExitCodeValues.Success : ExitCodeValues.UsageError;
    }
}
