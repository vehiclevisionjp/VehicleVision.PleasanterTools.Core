# zsh completion for sample-tool (generated; do not edit).
# Usage (after compinit): source <(sample-tool completion zsh)
# Completes commands, --options, enumerated values and file names, and dynamic values (calls 'sample-tool __complete').

_sample_tool_complete_dynamic() {
    local -a candidates
    candidates=("${(@f)$("$_vv_tool" __complete "$1" "$PREFIX" 2>/dev/null)}")
    candidates=(${candidates:#})
    (( $#candidates )) && compadd -a candidates
}

_sample_tool_complete() {
    local curcontext="$curcontext" state state_descr line ret=1
    local _vv_tool="$words[1]"
    typeset -A opt_args
    local -a commands
    commands=(
        'run:Run the sample. It'\''s [fancy]: yes'
        'files:Takes files.'
        'plain:Nothing.'
        'completion:Write a shell completion script to stdout (powershell, bash or zsh).'
    )
    _arguments -C \
        '(-h --help)'{-h,--help}'[Show this help.]' \
        '--version[Show the version.]' \
        '(--json)--json[Write the result as one JSON object to stdout.]' \
        '1: :->command' \
        '*:: :->args' && ret=0
    case $state in
        command) _describe -t commands 'sample-tool command' commands && ret=0 ;;
        args)
            case $line[1] in
                run) _arguments \
                    '(-h --help)'{-h,--help}'[Show this help.]' \
                    '--version[Show the version.]' \
                    '(--json)--json[Write the result as one JSON object to stdout.]' \
                    '(-d --dry-run)'{-d,--dry-run}'[Show what would be done.]' \
                    '(-o --out)'{-o,--out=}'[Output path.]:PATH:_files' \
                    '(--mode)--mode=[Mode: fast or slow \[x\].]:VALUE:(fast slow Mixed.1)' \
                    '(--table)--table=[Table name.]:TABLE:{_sample_tool_complete_dynamic table}' \
                    '(--limit)--limit=[Limit.]:VALUE: ' && ret=0 ;;
                files) _arguments \
                    '(-h --help)'{-h,--help}'[Show this help.]' \
                    '--version[Show the version.]' \
                    '(--json)--json[Write the result as one JSON object to stdout.]' \
                    '(-v --verbose)'{-v,--verbose}'[Verbose.]' \
                    '*:file:_files' && ret=0 ;;
                plain) _arguments \
                    '(-h --help)'{-h,--help}'[Show this help.]' \
                    '--version[Show the version.]' \
                    '(--json)--json[Write the result as one JSON object to stdout.]' && ret=0 ;;
                completion) _arguments \
                    '(-h --help)'{-h,--help}'[Show this help.]' \
                    '--version[Show the version.]' \
                    '(--json)--json[Write the result as one JSON object to stdout.]' \
                    '*:value:(powershell bash zsh)' && ret=0 ;;
            esac ;;
    esac
    return ret
}

if (( $+functions[compdef] )); then
    compdef _sample_tool_complete sample-tool
else
    print -u2 'Run compinit before loading this completion script.'
fi
