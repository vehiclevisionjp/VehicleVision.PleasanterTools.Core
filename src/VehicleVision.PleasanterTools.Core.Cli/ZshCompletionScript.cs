using System.Text;

namespace VehicleVision.PleasanterTools.Core.Cli;

/// <summary>
/// zsh の補完スクリプトの生成。<c>_arguments</c>・<c>_describe</c> で書き、<c>compdef</c> で登録する
/// （<c>compinit</c> の後に読み込む）。説明はコマンド・オプションの候補の横に表示される。
/// </summary>
internal static class ZshCompletionScript
{
    public static string Generate(ToolSpec tool)
    {
        var id = CompletionModel.Identifier(tool.Name);
        var fn = "_" + id + "_complete";
        var common = tool.CommonOptions ?? [];
        var b = new StringBuilder();

        b.Append("# zsh completion for ").Append(tool.Name).Append(" (generated; do not edit).\n");
        b.Append("# Usage (after compinit): source <(").Append(tool.Name).Append(" completion zsh)\n");
        b.Append("# Completes commands, --options, enumerated values and file names");
        b.Append(CompletionModel.HasDynamic(tool) ? ", and dynamic values (calls '" + tool.Name + " __complete').\n\n" : ".\n\n");

        if (CompletionModel.HasDynamic(tool))
        {
            b.Append(fn).Append("_dynamic() {\n");
            b.Append("    local -a candidates\n");
            b.Append("    candidates=(\"${(@f)$(\"$_vv_tool\" __complete \"$1\" \"$PREFIX\" 2>/dev/null)}\")\n");
            b.Append("    candidates=(${candidates:#})\n");
            b.Append("    (( $#candidates )) && compadd -a candidates\n");
            b.Append("}\n\n");
        }

        b.Append(fn).Append("() {\n");
        b.Append("    local curcontext=\"$curcontext\" state state_descr line ret=1\n");
        b.Append("    local _vv_tool=\"$words[1]\"\n");
        b.Append("    typeset -A opt_args\n");
        b.Append("    local -a commands\n");
        b.Append("    commands=(\n");
        foreach (var command in tool.Commands)
        {
            b.Append("        '").Append(CompletionModel.ZshQuoteInner(command.Name + ":" + command.Summary.Replace("\\", "\\\\", StringComparison.Ordinal))).Append("'\n");
        }
        b.Append("    )\n");
        b.Append("    _arguments -C \\\n");
        foreach (var spec in BuiltIn().Concat(common.SelectMany(option => Specs(fn, option))))
        {
            b.Append("        ").Append(spec).Append(" \\\n");
        }
        b.Append("        '1: :->command' \\\n");
        b.Append("        '*:: :->args' && ret=0\n");
        b.Append("    case $state in\n");
        b.Append("        command) _describe -t commands '").Append(CompletionModel.ZshQuoteInner(tool.Name)).Append(" command' commands && ret=0 ;;\n");
        b.Append("        args)\n");
        b.Append("            case $line[1] in\n");
        foreach (var command in tool.Commands)
        {
            b.Append("                ").Append(command.Name).Append(") _arguments \\\n");
            var specs = BuiltIn().Concat(common.SelectMany(option => Specs(fn, option)))
                .Concat(command.Options.SelectMany(option => Specs(fn, option))).ToList();
            if (command.AcceptsPositionals)
            {
                specs.Add(command.PositionalValues is null
                    ? "'*:file:_files'"
                    : "'*:value:(" + string.Join(' ', command.PositionalValues) + ")'");
            }
            for (var i = 0; i < specs.Count; i++)
            {
                b.Append("                    ").Append(specs[i]).Append(i < specs.Count - 1 ? " \\\n" : " && ret=0 ;;\n");
            }
        }
        b.Append("            esac ;;\n");
        b.Append("    esac\n");
        b.Append("    return ret\n");
        b.Append("}\n\n");

        b.Append("if (( $+functions[compdef] )); then\n");
        b.Append("    compdef ").Append(fn).Append(' ').Append(tool.Name).Append('\n');
        b.Append("else\n");
        b.Append("    print -u2 'Run compinit before loading this completion script.'\n");
        b.Append("fi\n");
        return b.ToString();
    }

    private static IEnumerable<string> BuiltIn()
    {
        yield return "'(-h --help)'{-h,--help}'[" + CompletionModel.ZshExplanation(CompletionModel.HelpDescription) + "]'";
        yield return "'--version[" + CompletionModel.ZshExplanation(CompletionModel.VersionDescription) + "]'";
    }

    private static IEnumerable<string> Specs(string fn, OptionSpec option)
    {
        var description = CompletionModel.ZshExplanation(option.Description);
        var exclusive = option.ShortName is null
            ? $"(--{option.Name})"
            : $"(-{option.ShortName} --{option.Name})";
        string names;
        string tail;
        if (option.Kind == OptionKind.Flag)
        {
            names = option.ShortName is null ? $"--{option.Name}" : $"{{-{option.ShortName},--{option.Name}}}";
            tail = $"[{description}]";
        }
        else
        {
            names = option.ShortName is null ? $"--{option.Name}=" : $"{{-{option.ShortName},--{option.Name}=}}";
            var message = new string((option.ValueName ?? "VALUE").Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '_').ToArray());
            var action = CompletionModel.ValueKind(option) switch
            {
                "enum" => "(" + string.Join(' ', option.Values!) + ")",
                "file" => "_files",
                "dynamic" => "{" + fn + "_dynamic " + option.Name + "}",
                _ => " ",
            };
            tail = $"[{description}]:{message}:{action}";
        }

        // 短縮形つきは、ブレース展開で 2 つの綴りの仕様になる。引用符の外に出す
        yield return option.ShortName is null
            ? $"'{CompletionModel.ZshQuoteInner(exclusive + names + tail)}'"
            : $"'{CompletionModel.ZshQuoteInner(exclusive)}'{names}'{CompletionModel.ZshQuoteInner(tail)}'";
    }
}
