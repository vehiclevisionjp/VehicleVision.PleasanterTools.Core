namespace VehicleVision.PleasanterTools.Core.Cli;

/// <summary>補完のスクリプトの生成で共通に使う、定義の読み取りと文字列の補助。</summary>
internal static class CompletionModel
{
    /// <summary>部品が常に受ける <c>--help</c> の説明（ヘルプと同じ文言）。</summary>
    public const string HelpDescription = "Show this help.";

    public static string VersionDescription => OptionLookup.VersionOption.Description;

    /// <summary>共通のオプションと、全コマンドのオプション。</summary>
    public static IEnumerable<OptionSpec> AllOptions(ToolSpec tool) =>
        (tool.CommonOptions ?? []).Concat(tool.Commands.SelectMany(command => command.Options));

    public static bool HasDynamic(ToolSpec tool) =>
        AllOptions(tool).Any(option => option.ValueCompletion == OptionValueCompletion.Dynamic);

    /// <summary>シェルの関数名・変数名に使える形（英数字と <c>_</c>）にする。</summary>
    public static string Identifier(string toolName) =>
        new(toolName.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray());

    /// <summary>値の種類（スクリプト内の分類）。<c>flag</c>・<c>enum</c>・<c>file</c>・<c>dynamic</c>・<c>none</c>。</summary>
    public static string ValueKind(OptionSpec option)
    {
        if (option.Kind == OptionKind.Flag)
        {
            return "flag";
        }
        if (option.Values is not null)
        {
            return "enum";
        }
        return option.ValueCompletion switch
        {
            OptionValueCompletion.File => "file",
            OptionValueCompletion.Dynamic => "dynamic",
            _ => "none",
        };
    }

    /// <summary>PowerShell の単一引用符の文字列。</summary>
    public static string PowerShellString(string text) => "'" + text.Replace("'", "''", StringComparison.Ordinal) + "'";

    /// <summary>zsh の単一引用符の文字列の中身（<c>'</c> を <c>'\''</c> にする）。</summary>
    public static string ZshQuoteInner(string text) => text.Replace("'", "'\\''", StringComparison.Ordinal);

    /// <summary>zsh の <c>_arguments</c> の <c>[説明]</c> の中身。<c>\</c> と <c>[</c>・<c>]</c> を逃がす。</summary>
    public static string ZshExplanation(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal);
}
