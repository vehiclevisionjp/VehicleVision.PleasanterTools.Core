using System.Text;

namespace VehicleVision.PleasanterTools.Core.Cli;

/// <summary>
/// PowerShell の補完スクリプトの生成。<c>Register-ArgumentCompleter -Native</c> で登録する。
/// Windows PowerShell 5.1 でも動くよう、3 項演算子・<c>??</c> を使わない。
/// ファイル名は、何も返さずに PowerShell の標準のファイル名補完（フォールバック）に任せる。
/// </summary>
internal static class PowerShellCompletionScript
{
    public static string Generate(ToolSpec tool)
    {
        var common = tool.CommonOptions ?? [];
        var q = CompletionModel.PowerShellString;
        var b = new StringBuilder();

        b.Append("# PowerShell completion for ").Append(tool.Name).Append(" (generated; do not edit).\n");
        b.Append("# Usage: ").Append(tool.Name).Append(" completion powershell | Out-String | Invoke-Expression\n");
        b.Append("# Completes commands, --options, enumerated values and file names");
        b.Append(CompletionModel.HasDynamic(tool) ? ", and dynamic values (calls '" + tool.Name + " __complete').\n" : ".\n");
        b.Append("# Option values are completed in the '--name value' form only.\n\n");

        b.Append("Register-ArgumentCompleter -Native -CommandName ").Append(q(tool.Name)).Append(',').Append(q(tool.Name + ".exe")).Append(" -ScriptBlock {\n");
        b.Append("    param($wordToComplete, $commandAst, $cursorPosition)\n\n");

        // 定義。Kind は flag / enum / file / dynamic / none
        b.Append("    $common = @(\n");
        foreach (var option in common)
        {
            AppendOption(b, option);
        }
        b.Append("    )\n");
        b.Append("    $commands = [ordered]@{\n");
        foreach (var command in tool.Commands)
        {
            b.Append("        ").Append(q(command.Name)).Append(" = @{ Summary = ").Append(q(command.Summary));
            b.Append("; Positional = ").Append(command.AcceptsPositionals ? (command.PositionalValues is null ? "'file'" : "'enum'") : "'none'");
            b.Append("; PositionalValues = @(").Append(string.Join(',', (command.PositionalValues ?? []).Select(q))).Append(')');
            b.Append("; Options = @(\n");
            foreach (var option in command.Options)
            {
                AppendOption(b, option);
            }
            b.Append("        ) }\n");
        }
        b.Append("    }\n\n");

        b.Append(Body.Replace("@@DYNAMIC@@", CompletionModel.HasDynamic(tool) ? DynamicBranch : string.Empty, StringComparison.Ordinal));
        b.Append("}\n");
        return b.ToString();
    }

    private static void AppendOption(StringBuilder b, OptionSpec option)
    {
        var q = CompletionModel.PowerShellString;
        b.Append("        @{ Name = ").Append(q(option.Name));
        b.Append("; Short = ").Append(option.ShortName is null ? "$null" : q(option.ShortName));
        b.Append("; Kind = ").Append(q(CompletionModel.ValueKind(option)));
        b.Append("; Values = @(").Append(string.Join(',', (option.Values ?? []).Select(q))).Append(')');
        b.Append("; Description = ").Append(q(option.Description)).Append(" }\n");
    }

    // 動的な値の呼び戻し。動的なオプションがあるツールだけが持つ。結果が無ければ何も返さない
    private const string DynamicBranch = """
            'dynamic' {
                $saved = $global:LASTEXITCODE
                $lines = @()
                try { $lines = @(& $tool __complete $pending.Name $wordToComplete 2>$null) } catch { $lines = @() }
                $global:LASTEXITCODE = $saved
                $lines | Where-Object { $_ } | ForEach-Object { New-Result $_ $pending.Description 'ParameterValue' }
                return
            }
""";

    // 補完の本体。定義（$common・$commands）の後に置く
    private const string Body = """
    $tool = $commandAst.CommandElements[0].Extent.Text.Trim([char]39, [char]34)
    $words = @($commandAst.CommandElements | Select-Object -Skip 1 |
        Where-Object { $_.Extent.EndOffset -lt $cursorPosition } |
        ForEach-Object { $_.Extent.Text.Trim([char]39, [char]34) })

    function New-Result($text, $tip, $type) {
        [System.Management.Automation.CompletionResult]::new($text, $text, $type, $tip)
    }

    function Find-Option($options, $word) {
        foreach ($o in $options) {
            if ($word -eq ('--' + $o.Name)) { return $o }
            if ($o.Short -and $word -ceq ('-' + $o.Short)) { return $o }
        }
        return $null
    }

    $cmdName = $null
    $used = @{}
    $pending = $null
    $ended = $false
    foreach ($w in $words) {
        if ($pending) { $pending = $null; continue }
        if ($ended) { continue }
        if ($w -eq '--') { $ended = $true; continue }
        if ($w.StartsWith('-')) {
            $options = @($common)
            if ($cmdName) { $options += $commands[$cmdName].Options }
            $o = Find-Option $options $w
            if ($o) {
                $used[$o.Name] = $true
                if ($o.Kind -ne 'flag') { $pending = $o }
            }
        } elseif (-not $cmdName) {
            foreach ($key in $commands.Keys) {
                if ($key -eq $w) { $cmdName = $key }
            }
            if (-not $cmdName) { $cmdName = '?' }
        }
    }

    # An option is waiting for its value.
    if ($pending) {
        switch ($pending.Kind) {
            'enum' {
                $pending.Values | Where-Object { $_ -like "$wordToComplete*" } |
                    ForEach-Object { New-Result $_ $pending.Description 'ParameterValue' }
                return
            }
@@DYNAMIC@@            default { return }   # file (and none): fall back to PowerShell's file name completion
        }
    }

    $known = $cmdName -and $commands.Contains($cmdName)
    if (-not $ended -and $wordToComplete.StartsWith('-')) {
        $options = @($common)
        if ($known) { $options += $commands[$cmdName].Options }
        $options | Where-Object { -not $used[$_.Name] -and ('--' + $_.Name) -like "$wordToComplete*" } |
            ForEach-Object { New-Result ('--' + $_.Name) $_.Description 'ParameterName' }
        foreach ($h in @(@('--help', 'Show this help.'), @('--version', 'Show the version.'))) {
            if ($h[0] -like "$wordToComplete*") { New-Result $h[0] $h[1] 'ParameterName' }
        }
        return
    }
    if (-not $cmdName) {
        $commands.Keys | Where-Object { $_ -like "$wordToComplete*" } |
            ForEach-Object { New-Result $_ $commands[$_].Summary 'ParameterValue' }
        return
    }
    if ($known) {
        switch ($commands[$cmdName].Positional) {
            'file' { return }
            'enum' {
                $commands[$cmdName].PositionalValues | Where-Object { $_ -like "$wordToComplete*" } |
                    ForEach-Object { New-Result $_ $commands[$cmdName].Summary 'ParameterValue' }
                return
            }
        }
        if (-not $ended -and -not $wordToComplete) {
            $commands[$cmdName].Options | Where-Object { -not $used[$_.Name] } |
                ForEach-Object { New-Result ('--' + $_.Name) $_.Description 'ParameterName' }
        }
    }

""";
}
