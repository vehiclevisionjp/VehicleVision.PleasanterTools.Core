# bash completion for sample-tool (generated; do not edit).
# Usage: source <(sample-tool completion bash)
# Completes commands, --options, enumerated values and file names, and dynamic values (calls 'sample-tool __complete').
# Option values are completed in the '--name value' form only.

_sample_tool_complete_lookup() {
    lname=""; vkind=""; vvalues=""
    case "$1:$2" in
        run:--dry-run|run:-d) lname=dry-run; vkind=flag ;;
        run:--out|run:-o) lname=out; vkind=file ;;
        run:--mode) lname=mode; vkind=enum; vvalues="fast slow Mixed.1" ;;
        run:--table) lname=table; vkind=dynamic ;;
        run:--limit) lname=limit; vkind=none ;;
        files:--verbose|files:-v) lname=verbose; vkind=flag ;;
        *:--json) lname=json; vkind=flag ;;
    esac
}

_sample_tool_complete_options() {
    opts=""
    case "$1" in
        run) opts="--dry-run --out --mode --table --limit" ;;
        files) opts="--verbose" ;;
        plain) opts="" ;;
        completion) opts="" ;;
    esac
}

_sample_tool_complete_positional() {
    pkind="none"; pvalues=""
    case "$1" in
        files) pkind=file ;;
        completion) pkind=enum; pvalues="powershell bash zsh" ;;
    esac
}

_sample_tool_complete_files() {
    local f
    COMPREPLY=()
    while IFS= read -r f; do COMPREPLY+=("$f"); done < <(compgen -f -- "$cur")
    if type compopt >/dev/null 2>&1; then compopt -o filenames 2>/dev/null; fi
}

_sample_tool_complete_dynamic() {
    local f
    COMPREPLY=()
    while IFS= read -r f; do COMPREPLY+=("$f"); done < <("${COMP_WORDS[0]}" __complete "$1" "$cur" 2>/dev/null)
}

_sample_tool_complete() {
    local cur="${COMP_WORDS[COMP_CWORD]}"
    local commands="run files plain completion"
    local common="--help --version --json"
    local cmd="" w i expect="" ended="" used=" " lname vkind vvalues pkind pvalues opts o list
    # Walk the words before the cursor. When 'expect' is set, lname/vkind describe the option waiting for a value.
    for ((i = 1; i < COMP_CWORD; i++)); do
        w="${COMP_WORDS[i]}"
        if [[ -n "$expect" ]]; then expect=""; continue; fi
        if [[ -n "$ended" ]]; then continue; fi
        case "$w" in
            --) ended=1 ;;
            -*)
                _sample_tool_complete_lookup "$cmd" "$w"
                if [[ -n "$lname" ]]; then
                    used="$used$lname "
                    if [[ "$vkind" != flag ]]; then expect=1; fi
                fi ;;
            *) if [[ -z "$cmd" ]]; then cmd="$w"; fi ;;
        esac
    done

    COMPREPLY=()
    if [[ -n "$expect" ]]; then
        case "$vkind" in
            enum) COMPREPLY=($(compgen -W "$vvalues" -- "$cur")) ;;
            file) _sample_tool_complete_files ;;
            dynamic) _sample_tool_complete_dynamic "$lname" ;;
        esac
        return 0
    fi

    list=""
    if [[ -z "$ended" && "$cur" == -* ]]; then
        list="WANT_OPTIONS"
    elif [[ -z "$cmd" ]]; then
        COMPREPLY=($(compgen -W "$commands" -- "$cur"))
        return 0
    else
        _sample_tool_complete_positional "$cmd"
        case "$pkind" in
            file) _sample_tool_complete_files; return 0 ;;
            enum) COMPREPLY=($(compgen -W "$pvalues" -- "$cur")); return 0 ;;
            *) if [[ -z "$cur" && -z "$ended" ]]; then list="WANT_OPTIONS"; fi ;;
        esac
    fi

    if [[ "$list" == WANT_OPTIONS ]]; then
        list=""
        _sample_tool_complete_options "$cmd"
        for o in $opts $common; do
            case "$used" in
                *" ${o#--} "*) ;;
                *) list="$list $o" ;;
            esac
        done
        COMPREPLY=($(compgen -W "$list" -- "$cur"))
    fi
    return 0
}

complete -F _sample_tool_complete sample-tool
