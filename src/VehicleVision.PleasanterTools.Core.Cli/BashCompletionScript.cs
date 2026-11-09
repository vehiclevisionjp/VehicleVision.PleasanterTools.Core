using System.Text;

namespace VehicleVision.PleasanterTools.Core.Cli;

/// <summary>
/// bash の補完スクリプトの生成。<c>bash-completion</c> に依存せず、<c>complete -F</c> と <c>compgen</c> だけで書く
/// （bash 3.2 でも動くよう、連想配列・<c>mapfile</c> を使わない）。bash は説明を表示できないので、説明は使わない。
/// </summary>
internal static class BashCompletionScript
{
    public static string Generate(ToolSpec tool)
    {
        var fn = "_" + CompletionModel.Identifier(tool.Name) + "_complete";
        var common = tool.CommonOptions ?? [];
        var b = new StringBuilder();

        b.Append("# bash completion for ").Append(tool.Name).Append(" (generated; do not edit).\n");
        b.Append("# Usage: source <(").Append(tool.Name).Append(" completion bash)\n");
        b.Append("# Completes commands, --options, enumerated values and file names");
        b.Append(CompletionModel.HasDynamic(tool) ? ", and dynamic values (calls '" + tool.Name + " __complete').\n" : ".\n");
        b.Append("# Option values are completed in the '--name value' form only.\n\n");

        // 名前の解決。lname・vkind・vvalues を呼び出し元の変数へ入れる（bash の動的スコープ）
        b.Append(fn).Append("_lookup() {\n");
        b.Append("    lname=\"\"; vkind=\"\"; vvalues=\"\"\n");
        b.Append("    case \"$1:$2\" in\n");
        foreach (var command in tool.Commands)
        {
            foreach (var option in command.Options)
            {
                AppendLookup(b, command.Name, option);
            }
        }
        foreach (var option in common)
        {
            AppendLookup(b, "*", option);
        }
        b.Append("    esac\n}\n\n");

        b.Append(fn).Append("_options() {\n    opts=\"\"\n    case \"$1\" in\n");
        foreach (var command in tool.Commands)
        {
            var names = command.Options.Select(option => "--" + option.Name);
            b.Append("        ").Append(command.Name).Append(") opts=\"").Append(string.Join(' ', names)).Append("\" ;;\n");
        }
        b.Append("    esac\n}\n\n");

        b.Append(fn).Append("_positional() {\n    pkind=\"none\"; pvalues=\"\"\n    case \"$1\" in\n");
        foreach (var command in tool.Commands.Where(command => command.AcceptsPositionals))
        {
            if (command.PositionalValues is null)
            {
                b.Append("        ").Append(command.Name).Append(") pkind=file ;;\n");
            }
            else
            {
                b.Append("        ").Append(command.Name).Append(") pkind=enum; pvalues=\"")
                    .Append(string.Join(' ', command.PositionalValues)).Append("\" ;;\n");
            }
        }
        b.Append("    esac\n}\n\n");

        b.Append(fn).Append("_files() {\n");
        b.Append("    local f\n    COMPREPLY=()\n");
        b.Append("    while IFS= read -r f; do COMPREPLY+=(\"$f\"); done < <(compgen -f -- \"$cur\")\n");
        b.Append("    if type compopt >/dev/null 2>&1; then compopt -o filenames 2>/dev/null; fi\n}\n\n");

        if (CompletionModel.HasDynamic(tool))
        {
            b.Append(fn).Append("_dynamic() {\n");
            b.Append("    local f\n    COMPREPLY=()\n");
            b.Append("    while IFS= read -r f; do COMPREPLY+=(\"$f\"); done < <(\"${COMP_WORDS[0]}\" __complete \"$1\" \"$cur\" 2>/dev/null)\n}\n\n");
        }

        var commandNames = string.Join(' ', tool.Commands.Select(command => command.Name));
        var commonNames = string.Join(' ', new[] { "--help", "--version" }.Concat(common.Select(option => "--" + option.Name)));

        b.Append(fn).Append("() {\n");
        b.Append("    local cur=\"${COMP_WORDS[COMP_CWORD]}\"\n");
        b.Append("    local commands=\"").Append(commandNames).Append("\"\n");
        b.Append("    local common=\"").Append(commonNames).Append("\"\n");
        b.Append("    local cmd=\"\" w i expect=\"\" ended=\"\" used=\" \" lname vkind vvalues pkind pvalues opts o list\n");
        b.Append("    # Walk the words before the cursor. When 'expect' is set, lname/vkind describe the option waiting for a value.\n");
        b.Append("    for ((i = 1; i < COMP_CWORD; i++)); do\n");
        b.Append("        w=\"${COMP_WORDS[i]}\"\n");
        b.Append("        if [[ -n \"$expect\" ]]; then expect=\"\"; continue; fi\n");
        b.Append("        if [[ -n \"$ended\" ]]; then continue; fi\n");
        b.Append("        case \"$w\" in\n");
        b.Append("            --) ended=1 ;;\n");
        b.Append("            -*)\n");
        b.Append("                ").Append(fn).Append("_lookup \"$cmd\" \"$w\"\n");
        b.Append("                if [[ -n \"$lname\" ]]; then\n");
        b.Append("                    used=\"$used$lname \"\n");
        b.Append("                    if [[ \"$vkind\" != flag ]]; then expect=1; fi\n");
        b.Append("                fi ;;\n");
        b.Append("            *) if [[ -z \"$cmd\" ]]; then cmd=\"$w\"; fi ;;\n");
        b.Append("        esac\n");
        b.Append("    done\n\n");

        b.Append("    COMPREPLY=()\n");
        b.Append("    if [[ -n \"$expect\" ]]; then\n");
        b.Append("        case \"$vkind\" in\n");
        b.Append("            enum) COMPREPLY=($(compgen -W \"$vvalues\" -- \"$cur\")) ;;\n");
        b.Append("            file) ").Append(fn).Append("_files ;;\n");
        if (CompletionModel.HasDynamic(tool))
        {
            b.Append("            dynamic) ").Append(fn).Append("_dynamic \"$lname\" ;;\n");
        }
        b.Append("        esac\n");
        b.Append("        return 0\n");
        b.Append("    fi\n\n");

        b.Append("    list=\"\"\n");
        b.Append("    if [[ -z \"$ended\" && \"$cur\" == -* ]]; then\n");
        b.Append("        list=\"WANT_OPTIONS\"\n");
        b.Append("    elif [[ -z \"$cmd\" ]]; then\n");
        b.Append("        COMPREPLY=($(compgen -W \"$commands\" -- \"$cur\"))\n");
        b.Append("        return 0\n");
        b.Append("    else\n");
        b.Append("        ").Append(fn).Append("_positional \"$cmd\"\n");
        b.Append("        case \"$pkind\" in\n");
        b.Append("            file) ").Append(fn).Append("_files; return 0 ;;\n");
        b.Append("            enum) COMPREPLY=($(compgen -W \"$pvalues\" -- \"$cur\")); return 0 ;;\n");
        b.Append("            *) if [[ -z \"$cur\" && -z \"$ended\" ]]; then list=\"WANT_OPTIONS\"; fi ;;\n");
        b.Append("        esac\n");
        b.Append("    fi\n\n");

        b.Append("    if [[ \"$list\" == WANT_OPTIONS ]]; then\n");
        b.Append("        list=\"\"\n");
        b.Append("        ").Append(fn).Append("_options \"$cmd\"\n");
        b.Append("        for o in $opts $common; do\n");
        b.Append("            case \"$used\" in\n");
        b.Append("                *\" ${o#--} \"*) ;;\n");
        b.Append("                *) list=\"$list $o\" ;;\n");
        b.Append("            esac\n");
        b.Append("        done\n");
        b.Append("        COMPREPLY=($(compgen -W \"$list\" -- \"$cur\"))\n");
        b.Append("    fi\n");
        b.Append("    return 0\n");
        b.Append("}\n\n");

        b.Append("complete -F ").Append(fn).Append(' ').Append(tool.Name).Append('\n');
        return b.ToString();
    }

    private static void AppendLookup(StringBuilder b, string command, OptionSpec option)
    {
        var patterns = new List<string> { $"{command}:--{option.Name}" };
        if (option.ShortName is not null)
        {
            patterns.Add($"{command}:-{option.ShortName}");
        }
        b.Append("        ").Append(string.Join('|', patterns)).Append(") lname=").Append(option.Name)
            .Append("; vkind=").Append(CompletionModel.ValueKind(option));
        if (option.Values is not null)
        {
            b.Append("; vvalues=\"").Append(string.Join(' ', option.Values)).Append('"');
        }
        b.Append(" ;;\n");
    }
}
