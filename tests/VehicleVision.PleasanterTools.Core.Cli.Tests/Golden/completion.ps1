# PowerShell completion for sample-tool (generated; do not edit).
# Usage: sample-tool completion powershell | Out-String | Invoke-Expression
# Completes commands, --options, enumerated values and file names, and dynamic values (calls 'sample-tool __complete').
# Option values are completed in the '--name value' form only.

Register-ArgumentCompleter -Native -CommandName 'sample-tool','sample-tool.exe' -ScriptBlock {
    param($wordToComplete, $commandAst, $cursorPosition)

    $common = @(
        @{ Name = 'json'; Short = $null; Kind = 'flag'; Values = @(); Description = 'Write the result as one JSON object to stdout.' }
    )
    $commands = [ordered]@{
        'run' = @{ Summary = 'Run the sample. It''s [fancy]: yes'; Positional = 'none'; PositionalValues = @(); Options = @(
        @{ Name = 'dry-run'; Short = 'd'; Kind = 'flag'; Values = @(); Description = 'Show what would be done.' }
        @{ Name = 'out'; Short = 'o'; Kind = 'file'; Values = @(); Description = 'Output path.' }
        @{ Name = 'mode'; Short = $null; Kind = 'enum'; Values = @('fast','slow','Mixed.1'); Description = 'Mode: fast or slow [x].' }
        @{ Name = 'table'; Short = $null; Kind = 'dynamic'; Values = @(); Description = 'Table name.' }
        @{ Name = 'limit'; Short = $null; Kind = 'none'; Values = @(); Description = 'Limit.' }
        ) }
        'files' = @{ Summary = 'Takes files.'; Positional = 'file'; PositionalValues = @(); Options = @(
        @{ Name = 'verbose'; Short = 'v'; Kind = 'flag'; Values = @(); Description = 'Verbose.' }
        ) }
        'plain' = @{ Summary = 'Nothing.'; Positional = 'none'; PositionalValues = @(); Options = @(
        ) }
        'completion' = @{ Summary = 'Write a shell completion script to stdout (powershell, bash or zsh).'; Positional = 'enum'; PositionalValues = @('powershell','bash','zsh'); Options = @(
        ) }
    }

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
            'dynamic' {
                $saved = $global:LASTEXITCODE
                $lines = @()
                try { $lines = @(& $tool __complete $pending.Name $wordToComplete 2>$null) } catch { $lines = @() }
                $global:LASTEXITCODE = $saved
                $lines | Where-Object { $_ } | ForEach-Object { New-Result $_ $pending.Description 'ParameterValue' }
                return
            }            default { return }   # file (and none): fall back to PowerShell's file name completion
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
}
