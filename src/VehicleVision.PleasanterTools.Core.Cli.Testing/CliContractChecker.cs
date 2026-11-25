using System.Text.Json;
using System.Text.RegularExpressions;
using VehicleVision.PleasanterTools.Core.Contracts;

namespace VehicleVision.PleasanterTools.Core.Cli.Testing;

/// <summary>規則を 1 つ検査した結果。既知の違反（<see cref="CliContract.KnownViolations"/>）との突き合わせ済み。</summary>
/// <param name="Found">見つかった違反すべて。</param>
/// <param name="Unexpected">既知ではない違反（あってはならない）。</param>
/// <param name="Resolved">既知として一覧にあるのに、もう検出されないもの（一覧から外す）。</param>
public sealed record CliContractEvaluation(
    IReadOnlyList<ContractViolation> Found,
    IReadOnlyList<ContractViolation> Unexpected,
    IReadOnlyList<KnownViolation> Resolved);

/// <summary>契約の検査。違反を一覧で返す（テストの失敗にするのは <see cref="CliContractTests"/>）。</summary>
public static partial class CliContractChecker
{
    private static readonly int[] AllowedExitCodes =
    [
        ExitCodeValues.Success,
        ExitCodeValues.Failure,
        ExitCodeValues.UsageError,
        ExitCodeValues.Partial,
        ExitCodeValues.Interrupted,
    ];

    private static readonly string[] FrameKeys =
        [JsonKeys.Command, JsonKeys.Ok, JsonKeys.ExitCode, JsonKeys.Errors, JsonKeys.Warnings];

    private static readonly string[] VersionKeys = [JsonKeys.Command, JsonKeys.Ok, JsonKeys.Version];

    private static readonly string[] IssueKeys = [JsonKeys.Code, JsonKeys.Message, JsonKeys.Path];

    [GeneratedRegex("^[A-Z][A-Za-z0-9]*$")]
    private static partial Regex PascalCase();

    [GeneratedRegex("^[a-z][A-Za-z0-9]*$")]
    private static partial Regex CamelCase();

    /// <summary>規則を検査し、既知の違反と突き合わせる。</summary>
    /// <param name="contract">検査する CLI。</param>
    /// <param name="rule">規則（<see cref="CliContractRules"/>）。</param>
    public static async Task<CliContractEvaluation> EvaluateAsync(CliContract contract, string rule)
    {
        var found = await CheckAsync(contract, rule);
        var known = contract.KnownViolations.Where(item => item.Rule == rule).ToList();
        var unexpected = found.Where(v => !known.Any(k => k.Case == v.Case)).ToList();
        var resolved = known.Where(k => !found.Any(v => v.Case == k.Case)).ToList();
        return new CliContractEvaluation(found, unexpected, resolved);
    }

    /// <summary>規則を検査して、違反をすべて返す（既知かどうかは問わない）。</summary>
    /// <param name="contract">検査する CLI。</param>
    /// <param name="rule">規則（<see cref="CliContractRules"/>）。</param>
    public static async Task<IReadOnlyList<ContractViolation>> CheckAsync(CliContract contract, string rule)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(rule);
        var violations = new List<ContractViolation>();
        switch (rule)
        {
            case CliContractRules.ArgumentForms:
                await CheckArgumentFormsAsync(contract, violations);
                break;
            case CliContractRules.Ascii or CliContractRules.Streams or CliContractRules.Prefixes
                or CliContractRules.ExitCodes or CliContractRules.Json or CliContractRules.Help
                or CliContractRules.Version or CliContractRules.Secrets:
                foreach (var run in await RunScenariosAsync(contract, rule))
                {
                    CheckRun(contract, rule, run, violations);
                }
                break;
            default:
                throw new ArgumentException($"未知の規則: {rule}", nameof(rule));
        }
        return violations;
    }

    private static async Task<IReadOnlyList<CliRun>> RunScenariosAsync(CliContract contract, string rule)
    {
        var scenarios = contract.Scenarios.AsEnumerable();
        if (contract.Tool is not null)
        {
            scenarios = scenarios.Concat(GeneratedScenarios.For(contract.Tool));
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        var runs = new List<CliRun>();
        foreach (var scenario in scenarios)
        {
            if (!names.Add(scenario.Name))
            {
                throw new ArgumentException($"シナリオの名前が重複しています: {scenario.Name}", nameof(contract));
            }
            // 規則に関係しないシナリオは実行しない（待機などで時間のかかるものがある）
            if (Applies(rule, scenario))
            {
                runs.Add(await CliRun.ExecuteAsync(contract, scenario));
            }
        }
        return runs;
    }

    private static bool Applies(string rule, CliScenario scenario) => rule switch
    {
        CliContractRules.Help => scenario.Role == CliScenarioRole.Help,
        CliContractRules.Version => scenario.Role == CliScenarioRole.Version || scenario.Json == CliJsonKind.Version,
        CliContractRules.Json => scenario.Json != CliJsonKind.None,
        CliContractRules.Secrets => scenario.Secrets.Count > 0,
        _ => true,
    };

    private static void CheckRun(CliContract contract, string rule, CliRun run, List<ContractViolation> violations)
    {
        void Add(string message) => violations.Add(new ContractViolation(rule, run.Scenario.Name, message));
        var scenario = run.Scenario;
        switch (rule)
        {
            case CliContractRules.ExitCodes:
                CheckExitCode(run, Add);
                break;
            case CliContractRules.Ascii:
                CheckAscii("stdout", run.Stdout, Add);
                CheckAscii("stderr", run.Stderr, Add);
                break;
            case CliContractRules.Streams:
                CheckStreams(run, Add);
                break;
            case CliContractRules.Prefixes:
                CheckPrefixes(run, Add);
                break;
            case CliContractRules.Json:
                CheckJson(run, Add);
                break;
            case CliContractRules.Help:
                CheckHelp(contract, run, Add);
                break;
            case CliContractRules.Version:
                CheckVersion(contract, run, Add);
                break;
            case CliContractRules.Secrets:
                foreach (var secret in scenario.Secrets)
                {
                    if (run.Stdout.Contains(secret, StringComparison.Ordinal) || run.Stderr.Contains(secret, StringComparison.Ordinal))
                    {
                        Add("秘密が出力に含まれている。");
                    }
                }
                break;
        }
    }

    private static void CheckExitCode(CliRun run, Action<string> add)
    {
        if (run.Exception is not null)
        {
            add($"例外が漏れた（終了コードにならない）: {run.Exception.GetType().Name}");
            return;
        }
        var code = run.ExitCode!.Value;
        if (!AllowedExitCodes.Contains(code))
        {
            add($"終了コード {code} は表（0・1・2・3・130）にない。");
        }
        else if (code != run.Scenario.ExitCode)
        {
            add($"終了コードが {code}（期待は {run.Scenario.ExitCode}）。");
        }
    }

    private static void CheckAscii(string stream, string text, Action<string> add)
    {
        var lineNumber = 1;
        foreach (var character in text)
        {
            if (character == '\n')
            {
                lineNumber++;
            }
            else if (character != '\r' && character is < ' ' or > '~')
            {
                add($"{stream} の {lineNumber} 行目に ASCII 以外の文字か制御文字がある（U+{(int)character:X4}）。");
                return;
            }
        }
    }

    private static void CheckStreams(CliRun run, Action<string> add)
    {
        foreach (var line in CliRun.Lines(run.Stdout))
        {
            if (line.StartsWith("[ERROR]", StringComparison.Ordinal) || line.StartsWith("[WARN]", StringComparison.Ordinal))
            {
                add($"診断（{line.Split(' ')[0]}）が stdout に出ている。");
            }
        }
        foreach (var line in CliRun.Lines(run.Stderr))
        {
            if (line.StartsWith("[OK]", StringComparison.Ordinal) || line.StartsWith("[INFO]", StringComparison.Ordinal))
            {
                add($"結果（{line.Split(' ')[0]}）が stderr に出ている。");
            }
        }
        if (run.Scenario.Outcome == CliOutcome.UsageError && run.Scenario.Json == CliJsonKind.None && CliRun.Lines(run.Stdout).Count > 0)
        {
            add("使い方の誤りで、何も実行していないのに stdout へ出力している。");
        }
    }

    private static void CheckPrefixes(CliRun run, Action<string> add)
    {
        var scenario = run.Scenario;
        if (scenario.Outcome is CliOutcome.Failed or CliOutcome.UsageError
            && scenario.RequiresErrorLine
            && !CliRun.Lines(run.Stderr).Any(line => line.StartsWith("[ERROR] ", StringComparison.Ordinal)))
        {
            add("失敗なのに stderr に [ERROR] の行がない。");
        }
        if (scenario.Outcome == CliOutcome.Success && scenario.RequiresOkLine && scenario.Json == CliJsonKind.None
            && !CliRun.Lines(run.Stdout).Any(line => line.StartsWith("[OK] ", StringComparison.Ordinal)))
        {
            add("成功なのに stdout に [OK] の行がない。");
        }
    }

    private static void CheckHelp(CliContract contract, CliRun run, Action<string> add)
    {
        if (run.Exception is not null)
        {
            return;
        }
        if (CliRun.Lines(run.Stderr).Count > 0)
        {
            add("ヘルプが stderr に出ている（stdout へ出す）。");
        }
        if (CliRun.Lines(run.Stdout).Count == 0)
        {
            add("ヘルプが stdout に出ていない。");
        }
        else if (contract.Tool is not null && !run.Stdout.Contains(contract.Tool.Name, StringComparison.Ordinal))
        {
            add("ヘルプにツール名がない。");
        }
    }

    private static void CheckVersion(CliContract contract, CliRun run, Action<string> add)
    {
        if (run.Exception is not null || contract.Tool is null)
        {
            return;
        }
        var expected = $"{contract.Tool.Name} {contract.Tool.Version}";
        if (run.Scenario.Role == CliScenarioRole.Version)
        {
            var lines = CliRun.Lines(run.Stdout);
            if (lines.Count != 1 || lines[0] != expected)
            {
                add($"--version の stdout が「{expected}」の 1 行ではない。");
            }
            if (CliRun.Lines(run.Stderr).Count > 0)
            {
                add("--version が stderr に出力している。");
            }
            return;
        }
        try
        {
            using var document = JsonDocument.Parse(run.Stdout);
            if (!document.RootElement.TryGetProperty(JsonKeys.Version, out var version)
                || version.ValueKind != JsonValueKind.String || version.GetString() != contract.Tool.Version)
            {
                add($"--version --json の version が「{contract.Tool.Version}」ではない。");
            }
        }
        catch (JsonException)
        {
            // 形の違反は Json の規則が報告する
        }
    }

    private static void CheckJson(CliRun run, Action<string> add)
    {
        if (run.Exception is not null)
        {
            return;
        }
        var scenario = run.Scenario;
        var text = run.Stdout.TrimEnd('\r', '\n');
        if (text.Length == 0)
        {
            add("--json なのに stdout が空。");
            return;
        }
        if (text.Contains('\n', StringComparison.Ordinal))
        {
            add("--json の stdout が 1 行の JSON だけではない（JSON 以外の行が混ざっているか、複数ある）。");
            return;
        }
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            add("--json の stdout が JSON として読めない。");
            return;
        }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                add("--json の stdout が JSON のオブジェクトではない。");
                return;
            }
            var names = root.EnumerateObject().Select(property => property.Name).ToList();
            if (names.Count != names.Distinct(StringComparer.Ordinal).Count())
            {
                add("同じ名前の項目が重複している（固有の項目が枠の名前を使っていないか）。");
            }
            var keys = scenario.Json == CliJsonKind.Version ? VersionKeys : FrameKeys;
            if (!names.Take(keys.Length).SequenceEqual(keys) || (scenario.Json == CliJsonKind.Version && names.Count != keys.Length))
            {
                add($"枠の項目が {string.Join("・", keys)} の順に揃っていない（実際: {string.Join("・", names)}）。");
                return;
            }
            if (root.GetProperty(JsonKeys.Command).ValueKind != JsonValueKind.String)
            {
                add("command が文字列ではない。");
            }
            else if (scenario.Json == CliJsonKind.Version && root.GetProperty(JsonKeys.Command).GetString() != "version")
            {
                add("--version --json の command が version ではない。");
            }
            else if (scenario.Command is not null && root.GetProperty(JsonKeys.Command).GetString() != scenario.Command)
            {
                add($"command が「{scenario.Command}」ではない。");
            }
            if (root.GetProperty(JsonKeys.Ok).ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                add("ok が真偽値ではない。");
                return;
            }
            if (scenario.Json == CliJsonKind.Version)
            {
                return;
            }
            CheckFrameBody(run, root, names, add);
        }
    }

    private static void CheckFrameBody(CliRun run, JsonElement root, List<string> names, Action<string> add)
    {
        var ok = root.GetProperty(JsonKeys.Ok).GetBoolean();
        var exitCodeElement = root.GetProperty(JsonKeys.ExitCode);
        if (exitCodeElement.ValueKind != JsonValueKind.Number || !exitCodeElement.TryGetInt32(out var exitCode))
        {
            add("exitCode が整数ではない。");
            return;
        }
        if (run.ExitCode is { } actual && exitCode != actual)
        {
            add($"exitCode（{exitCode}）が実際の終了コード（{actual}）と違う。");
        }
        if (ok != (exitCode == ExitCodeValues.Success))
        {
            add("ok が終了コード 0 のときだけ真になっていない。");
        }
        var errorCount = CheckIssues(root, JsonKeys.Errors, add);
        CheckIssues(root, JsonKeys.Warnings, add);
        if (exitCode == ExitCodeValues.Success && errorCount > 0)
        {
            add("成功なのに errors が空ではない。");
        }
        if (exitCode is ExitCodeValues.Failure or ExitCodeValues.UsageError && errorCount == 0)
        {
            add("失敗なのに errors が空。");
        }
        foreach (var name in names.Skip(FrameKeys.Length))
        {
            if (!CamelCase().IsMatch(name))
            {
                add($"固有の項目の名前「{name}」が camelCase ではない。");
            }
        }
    }

    private static int CheckIssues(JsonElement root, string key, Action<string> add)
    {
        var element = root.GetProperty(key);
        if (element.ValueKind != JsonValueKind.Array)
        {
            add($"{key} が配列ではない。");
            return 0;
        }
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                add($"{key} の要素がオブジェクトではない。");
                continue;
            }
            foreach (var property in item.EnumerateObject())
            {
                if (!IssueKeys.Contains(property.Name))
                {
                    add($"{key} の要素に未定義の項目「{property.Name}」がある。");
                }
            }
            if (!item.TryGetProperty(JsonKeys.Code, out var code) || code.ValueKind != JsonValueKind.String
                || !PascalCase().IsMatch(code.GetString()!))
            {
                add($"{key} の code が PascalCase の文字列ではない。");
            }
            if (!item.TryGetProperty(JsonKeys.Message, out var message) || message.ValueKind != JsonValueKind.String)
            {
                add($"{key} の message が文字列ではない。");
            }
        }
        return element.GetArrayLength();
    }

    private static async Task CheckArgumentFormsAsync(CliContract contract, List<ContractViolation> violations)
    {
        foreach (var group in contract.ArgumentForms)
        {
            (int? Code, string Stdout, Exception? Exception)? baseline = null;
            foreach (var form in group.Forms)
            {
                var (code, stdout, _, exception) = await CliRun.InvokeAsync(
                    contract, form, group.Environment, string.Empty, false, group.Arrange);
                var current = (code, stdout, exception);
                if (baseline is null)
                {
                    baseline = current;
                    continue;
                }
                var name = $"{group.Name}: {string.Join(' ', form)}";
                if (exception is not null != baseline.Value.Exception is not null
                    || code != baseline.Value.Code
                    || stdout != baseline.Value.Stdout)
                {
                    violations.Add(new ContractViolation(
                        CliContractRules.ArgumentForms,
                        name,
                        $"基準（{string.Join(' ', group.Forms[0])}）と結果が違う（終了コード {Describe(baseline.Value.Code, baseline.Value.Exception)} と {Describe(code, exception)}）。"));
                }
            }
        }
    }

    private static string Describe(int? code, Exception? exception) =>
        exception is null ? code.ToString()! : $"例外 {exception.GetType().Name}";
}
