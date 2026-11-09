using System.Diagnostics;

namespace VehicleVision.PleasanterTools.Core.Cli.Tests;

/// <summary>
/// 実際の bash・PowerShell で、生成した補完スクリプトを読み込んで候補を確かめる（列挙・ファイル名・オプション名・コマンド名）。
/// 動的な値（<c>__complete</c> の呼び戻し）は、ツール本体の実行ファイルが要るのでここでは確かめない。
/// zsh・Windows・macOS は CI に入れない（手元での確認）。
/// </summary>
/// <remarks>
/// シェルが見つからないときは何もせずに通す。ただし CI は <c>CLI_COMPLETION_REQUIRE_SHELLS=1</c> を設定し、
/// 見つからなければ失敗にする（設定しないと、実機の試験が流れずに緑になるため）。
/// 実行ファイルは PATH から探す。<c>CLI_COMPLETION_BASH</c>・<c>CLI_COMPLETION_PWSH</c> で明示できる（Windows の
/// <c>bash</c> が WSL の入口に解決されるときなど）。
/// </remarks>
public sealed class シェルでの補完Tests : IDisposable
{
    private const string RequireKey = "CLI_COMPLETION_REQUIRE_SHELLS";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "completion-" + Guid.NewGuid().ToString("N"));

    public シェルでの補完Tests()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "work", "dir1"));
        File.WriteAllText(Path.Combine(_directory, "work", "alpha.txt"), "x");
        File.WriteAllText(Path.Combine(_directory, "work", "beta.txt"), "x");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 後片付けの失敗は試験の結果にしない
        }
    }

    private static string? Find(string environmentKey, string name)
    {
        var overridden = Environment.GetEnvironmentVariable(environmentKey);
        if (!string.IsNullOrEmpty(overridden))
        {
            return overridden;
        }
        var fileName = OperatingSystem.IsWindows() ? name + ".exe" : name;
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    /// <summary>見つかれば実行ファイル、無ければ（必須でなければ）null。必須で無ければ失敗にする。</summary>
    private static string? Require(string environmentKey, string name)
    {
        var path = Find(environmentKey, name);
        if (path is null && Environment.GetEnvironmentVariable(RequireKey) == "1")
        {
            Assert.Fail($"{name} was not found, but {RequireKey}=1 requires it.");
        }
        return path;
    }

    private static string[] Run(string executable, string workingDirectory, params string[] arguments)
    {
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromSeconds(120)))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("The shell did not finish in time.");
        }
        Assert.True(process.ExitCode == 0, $"exit {process.ExitCode}: {stderr.Result}");
        return stdout.Result.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>入力（末尾が空白なら、入力中の語は空）と、シェル共通の期待値（区切りは <c>|</c>）。</summary>
    private static readonly (string Line, string[] Expected)[] Cases =
    [
        ("sample-tool ", ["run", "files", "plain", "completion"]),
        ("sample-tool r", ["run"]),
        ("sample-tool --", ["--json", "--help", "--version"]),
        ("sample-tool run --m", ["--mode"]),
        ("sample-tool run --dry-run --", ["--out", "--mode", "--table", "--limit", "--json", "--help", "--version"]),
        ("sample-tool run -d --d", []),
        ("sample-tool run --mode ", ["fast", "slow", "Mixed.1"]),
        ("sample-tool run --mode s", ["slow"]),
        ("sample-tool --json run --m", ["--mode"]),
        ("sample-tool completion ", ["powershell", "bash", "zsh"]),
    ];

    private static readonly string[] FileCases =
    [
        "sample-tool run --out ",
        "sample-tool files ",
        "sample-tool files --verbose ",
    ];

    private static string Normalize(string item) =>
        item.Trim('\'').Replace(".\\", string.Empty, StringComparison.Ordinal).Replace("./", string.Empty, StringComparison.Ordinal);

    private static void Verify(string[] output)
    {
        var results = output.ToDictionary(
            line => line[1..line.IndexOf("] => ", StringComparison.Ordinal)],
            line => line[(line.IndexOf("] => ", StringComparison.Ordinal) + 5)..].Split('|', StringSplitOptions.RemoveEmptyEntries).Select(Normalize).ToList());

        foreach (var (line, expected) in Cases)
        {
            var actual = results[line];
            Assert.True(
                expected.OrderBy(item => item, StringComparer.Ordinal).SequenceEqual(actual.OrderBy(item => item, StringComparer.Ordinal)),
                $"[{line}] expected: {string.Join(' ', expected)} / actual: {string.Join(' ', actual)}");
        }
        foreach (var line in FileCases)
        {
            var actual = results[line];
            Assert.Contains("alpha.txt", actual);
            Assert.Contains("beta.txt", actual);
            Assert.Contains("dir1", actual);
            Assert.DoesNotContain("--out", actual);
        }
    }

    private IEnumerable<string> AllLines() => Cases.Select(c => c.Line).Concat(FileCases);

    [Fact]
    public void bashで列挙とファイル名を補完する()
    {
        var bash = Require("CLI_COMPLETION_BASH", "bash");
        if (bash is null)
        {
            return;
        }
        File.WriteAllText(Path.Combine(_directory, "completion.bash"), Completion.Generate(CompletionTestTool.Spec, CompletionShell.Bash));
        File.WriteAllText(Path.Combine(_directory, "inputs.txt"), string.Join('\n', AllLines()) + "\n");
        File.WriteAllText(
            Path.Combine(_directory, "harness.sh"),
            """
            source ../completion.bash
            complete -p sample-tool | grep -q '_sample_tool_complete' || { echo "not registered" >&2; exit 1; }
            while IFS= read -r line; do
                read -r -a words <<< "$line"
                if [[ "$line" == *" " ]]; then words+=(""); fi
                COMP_WORDS=("${words[@]}")
                COMP_CWORD=$((${#words[@]} - 1))
                COMPREPLY=()
                _sample_tool_complete
                joined=""
                for item in "${COMPREPLY[@]}"; do joined="$joined|$item"; done
                echo "[$line] => $joined"
            done < ../inputs.txt
            """.ReplaceLineEndings("\n"));
        Verify(Run(bash, Path.Combine(_directory, "work"), "../harness.sh"));
    }

    [Fact]
    public void PowerShellで列挙とファイル名を補完する()
    {
        var pwsh = Require("CLI_COMPLETION_PWSH", "pwsh");
        if (pwsh is null)
        {
            return;
        }
        File.WriteAllText(Path.Combine(_directory, "completion.ps1"), Completion.Generate(CompletionTestTool.Spec, CompletionShell.PowerShell));
        File.WriteAllText(Path.Combine(_directory, "inputs.txt"), string.Join('\n', AllLines()) + "\n");
        File.WriteAllText(
            Path.Combine(_directory, "harness.ps1"),
            """
            . (Join-Path $PSScriptRoot 'completion.ps1')
            foreach ($line in Get-Content (Join-Path $PSScriptRoot 'inputs.txt')) {
                $r = TabExpansion2 -inputScript $line -cursorColumn $line.Length
                $joined = ($r.CompletionMatches | ForEach-Object { $_.CompletionText }) -join '|'
                "[$line] => $joined"
            }
            """.ReplaceLineEndings("\n"));
        // 入力の末尾の空白は Get-Content が残す
        Verify(Run(pwsh, Path.Combine(_directory, "work"), "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(_directory, "harness.ps1")));
    }
}
