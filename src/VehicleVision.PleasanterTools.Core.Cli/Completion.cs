using System.Text;

namespace VehicleVision.PleasanterTools.Core.Cli;

/// <summary>
/// シェルの補完（仕様書 5.5）。スクリプトは <see cref="ToolSpec"/> から生成し、手書きの補完を持たない。
/// コマンド名・オプション名・列挙の値・ファイル名はスクリプトが解決する。動的な値だけが、
/// <c>ツール名 __complete 〈オプション名〉 〈入力中の語〉</c> を呼び戻して <see cref="CompletionProvider"/> から得る。
/// </summary>
public static class Completion
{
    /// <summary>隠しコマンドの名前。ツールのコマンド名にはできない（先頭が <c>_</c>）ので、衝突しない。</summary>
    internal const string HiddenCommandName = "__complete";

    /// <summary>
    /// 補完のスクリプトを生成する。全文が ASCII で、行の区切りは <c>\n</c>。
    /// </summary>
    /// <param name="tool">ツールの定義。名前は英数字で始まり、英数字・<c>.</c>・<c>_</c>・<c>-</c> だけ。</param>
    /// <param name="shell">対象のシェル。</param>
    /// <exception cref="ArgumentException">ツール名がスクリプトへ安全に埋め込めない。</exception>
    public static string Generate(ToolSpec tool, CompletionShell shell)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (tool.Name.Length == 0
            || !char.IsAsciiLetterOrDigit(tool.Name[0])
            || !tool.Name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
        {
            throw new ArgumentException(
                "Tool name must be letters, digits, '.', '_' and '-' (starting with a letter or digit) to generate a completion script.",
                nameof(tool));
        }
        ToolValidator.Validate(tool.Name, tool.Description, tool.Version, tool.Commands, tool.CommonOptions);

        var text = shell switch
        {
            CompletionShell.PowerShell => PowerShellCompletionScript.Generate(tool),
            CompletionShell.Bash => BashCompletionScript.Generate(tool),
            CompletionShell.Zsh => ZshCompletionScript.Generate(tool),
            _ => throw new ArgumentOutOfRangeException(nameof(shell)),
        };
        return text.ReplaceLineEndings("\n");
    }

    /// <summary>
    /// シェル名を解釈する（大文字小文字を区別しない）。<c>powershell</c>・<c>pwsh</c>・<c>bash</c>・<c>zsh</c>。
    /// </summary>
    public static bool TryParseShell(string? name, out CompletionShell shell)
    {
        switch (name?.ToLowerInvariant())
        {
            case "powershell" or "pwsh":
                shell = CompletionShell.PowerShell;
                return true;
            case "bash":
                shell = CompletionShell.Bash;
                return true;
            case "zsh":
                shell = CompletionShell.Zsh;
                return true;
            default:
                shell = default;
                return false;
        }
    }

    /// <summary>
    /// 動的な値の候補を得る（<c>__complete</c> の中身）。定義に無い・動的でないオプション、
    /// 例外、時間超過、取り消しのときは、空を返す（例外にしない）。
    /// 返す候補は、入力中の語で始まり（大文字小文字は区別しない）、シェルが引用なしで扱える ASCII の 1 語（英数字と
    /// <c>. _ : / @ + , = % ~ -</c>）だけ。重複は除く。
    /// </summary>
    /// <param name="tool">ツールの定義。</param>
    /// <param name="settings">提供元と時間制限。</param>
    /// <param name="optionName">オプション名（接頭辞なし）。</param>
    /// <param name="currentWord">入力中の語。</param>
    /// <param name="cancellationToken">取り消しの合図（Ctrl+C）。</param>
    public static async Task<IReadOnlyList<string>> CompleteAsync(
        ToolSpec tool,
        CompletionSettings settings,
        string optionName,
        string currentWord,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(settings);
        try
        {
            var option = CompletionModel.AllOptions(tool).FirstOrDefault(candidate =>
                candidate.ValueCompletion == OptionValueCompletion.Dynamic
                && candidate.Name.Equals(optionName, StringComparison.OrdinalIgnoreCase));
            if (option is null)
            {
                return [];
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(settings.Timeout);
            // 同期的に長く待つ提供元でも制限を守れるよう、別のスレッドで走らせる。取り消しを無視する提供元も待たない
            var work = Task.Run(() => settings.Provider(option.Name, currentWord, linked.Token), CancellationToken.None);
            var finished = await Task.WhenAny(work, Task.Delay(settings.Timeout, cancellationToken)).ConfigureAwait(false);
            if (finished != work)
            {
                await linked.CancelAsync().ConfigureAwait(false);
                // 取り残した処理の例外を観測済みにする（何も出さない）
                _ = work.ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
                return [];
            }

            var candidates = await work.ConfigureAwait(false);
            return candidates is null
                ? []
                : candidates
                    .Where(candidate => IsSafeCandidate(candidate)
                        && candidate.StartsWith(currentWord, StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
        }
        catch (Exception)
        {
            // 失敗は何も出さない（例外の文言は秘密を含み得る）
            return [];
        }
    }

    /// <summary>引用なしで、3 つのシェルの補完へ安全に渡せる候補か。</summary>
    internal static bool IsSafeCandidate(string? candidate) =>
        !string.IsNullOrEmpty(candidate)
        && candidate.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or ':' or '/' or '@' or '+' or ',' or '=' or '%' or '~' or '-');

    /// <summary>
    /// 隠しコマンド <c>__complete 〈オプション名〉 [〈入力中の語〉]</c> を処理する。候補を 1 行 1 つで stdout へ書く。
    /// 失敗・時間超過でも何も出さず、stderr へは書かず、終了コードは 0。<paramref name="output"/> の
    /// 秘密の伏せ（redact）で値が変わる候補は出さない。
    /// </summary>
    internal static async Task<int> RunHiddenCommandAsync(
        ToolSpec tool,
        CompletionSettings settings,
        IReadOnlyList<string> args,
        CliOutput output,
        CancellationToken cancellationToken)
    {
        try
        {
            if (args.Count >= 1)
            {
                var word = args.Count >= 2 ? args[1] : string.Empty;
                var candidates = await CompleteAsync(tool, settings, args[0], word, cancellationToken).ConfigureAwait(false);
                var text = new StringBuilder();
                foreach (var candidate in candidates)
                {
                    if (output.Sanitize(candidate) == candidate)
                    {
                        text.Append(candidate).Append('\n');
                    }
                }
                if (text.Length > 0)
                {
                    output.WriteText(text.ToString());
                }
            }
        }
        catch (Exception)
        {
            // 何も出さない
        }
        return 0;
    }
}
