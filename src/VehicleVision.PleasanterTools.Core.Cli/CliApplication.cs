using VehicleVision.PleasanterTools.Core.Contracts;

namespace VehicleVision.PleasanterTools.Core.Cli;

/// <summary>
/// 実行の入口。解析 → ヘルプ・版・使い方の誤り → ハンドラーの実行までを行い、終了コードを返す。
/// 各ツールの <c>Main</c> は、<see cref="RunWithConsoleAsync"/> の戻り値をそのまま返せばよい。
/// </summary>
public static class CliApplication
{
    /// <summary>
    /// 解析 → ヘルプ・版・使い方の誤り → ハンドラーの実行までを行い、終了コードを返す。
    /// 使い方の誤りと <see cref="UsageException"/> は 2、想定外の例外は <c>[ERROR]</c>（例外の文言は出さない）と 1、
    /// <paramref name="cancellationToken"/> の取り消し（Ctrl+C）は 130。優先は 130 &gt; 1 &gt; 3 &gt; 0。
    /// </summary>
    /// <param name="tool">ツールの定義。</param>
    /// <param name="handlers">コマンド名（定義の綴り）からハンドラーへの対応。全コマンドに 1 つずつ要る。</param>
    /// <param name="args">引数。</param>
    /// <param name="stdout">結果の出力先。ヘルプ・版もここ。</param>
    /// <param name="stderr">診断の出力先。エラーはここ。</param>
    /// <param name="input">入力。</param>
    /// <param name="getEnvironmentVariable">環境変数の取得。</param>
    /// <param name="cancellationToken">取り消しの合図（Ctrl+C）。</param>
    /// <exception cref="ArgumentException">ハンドラーが定義と合わない（呼び出し側の誤り）。</exception>
    public static Task<int> RunAsync(
        ToolSpec tool,
        IReadOnlyDictionary<string, CommandHandler> handlers,
        IReadOnlyList<string> args,
        TextWriter stdout,
        TextWriter stderr,
        TextReader input,
        Func<string, string?> getEnvironmentVariable,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        // 端末ではない（色なし）として扱う。端末の判定と秘密の伏せは、CliOutput を渡すオーバーロードで行う
        var output = new CliOutput(stdout, stderr, getEnvironmentVariable, false, false);
        return RunAsync(tool, handlers, args, output, input, getEnvironmentVariable, cancellationToken);
    }

    /// <summary>
    /// <see cref="CliOutput"/>（接頭辞・色・ASCII 化・秘密の伏せ）で出力する <see cref="RunAsync(ToolSpec, IReadOnlyDictionary{string, CommandHandler}, IReadOnlyList{string}, TextWriter, TextWriter, TextReader, Func{string, string?}, CancellationToken)"/>。
    /// <c>--version --json</c> と、<c>--json</c> が解釈できたときの使い方の誤りは、枠つきの JSON で stdout へ出す（仕様書 3.2、決定 U4）。
    /// </summary>
    /// <param name="tool">ツールの定義。</param>
    /// <param name="handlers">コマンド名（定義の綴り）からハンドラーへの対応。</param>
    /// <param name="args">引数。</param>
    /// <param name="output">出力。</param>
    /// <param name="input">入力。</param>
    /// <param name="getEnvironmentVariable">環境変数の取得。</param>
    /// <param name="cancellationToken">取り消しの合図（Ctrl+C）。</param>
    /// <exception cref="ArgumentException">ハンドラーが定義と合わない（呼び出し側の誤り）。</exception>
    public static async Task<int> RunAsync(
        ToolSpec tool,
        IReadOnlyDictionary<string, CommandHandler> handlers,
        IReadOnlyList<string> args,
        CliOutput output,
        TextReader input,
        Func<string, string?> getEnvironmentVariable,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        foreach (var command in tool.Commands)
        {
            if (!handlers.ContainsKey(command.Name))
            {
                throw new ArgumentException($"No handler for command '{command.Name}'.", nameof(handlers));
            }
        }

        // 引数なしでコマンドが必要なときは、短い使い方を stderr に出して 2
        if (args.Count == 0)
        {
            WriteLines(output.Progress, HelpText.ShortUsage(tool));
            return ExitCodeValues.UsageError;
        }

        var parsed = CommandLineParser.Parse(tool, args, getEnvironmentVariable);
        switch (parsed.Outcome)
        {
            case ParseOutcome.Help:
                WriteLines(output.Result, parsed.Text!);
                return ExitCodeValues.Success;
            case ParseOutcome.Version:
                if (parsed.JsonRequested)
                {
                    output.WriteVersionJson(tool.Version);
                }
                else
                {
                    WriteLines(output.Result, parsed.Text!);
                }
                return ExitCodeValues.Success;
            case ParseOutcome.Error:
                WriteUsageError(output, tool, parsed.CommandName, parsed.Text!, parsed.JsonRequested);
                return ExitCodeValues.UsageError;
        }

        var context = new CommandContext(parsed.Options, output, input, getEnvironmentVariable);
        int code;
        try
        {
            code = await handlers[parsed.CommandName!](context, cancellationToken).ConfigureAwait(false);
        }
        catch (UsageException ex)
        {
            WriteUsageError(output, tool, parsed.CommandName, ex.Message, parsed.JsonRequested);
            return ExitCodeValues.UsageError;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ExitCodeValues.Interrupted;
        }
        catch (Exception)
        {
            // 例外の文言は秘密を含み得るので出さない（仕様書 4 章）
            output.Error("An unexpected error occurred.");
            if (parsed.JsonRequested && !output.HasWrittenToStdout)
            {
                // 出力済みなら、枠を足すと JSON が 2 つになるので出さない
                output.WriteJson(
                    parsed.CommandName!,
                    false,
                    ExitCodeValues.Failure,
                    [new CliIssue(ErrorCodes.UnexpectedError, "An unexpected error occurred.")],
                    []);
            }
            code = ExitCodeValues.Failure;
        }

        // 中断の合図が出ていれば、ハンドラーの戻り値より優先する（130 > 1 > 3 > 0）
        return cancellationToken.IsCancellationRequested ? ExitCodeValues.Interrupted : code;
    }

    /// <summary>
    /// コンソール（<see cref="Console.Out"/>・<see cref="Console.Error"/>・<see cref="Console.In"/>・環境変数）で
    /// <see cref="RunAsync"/> を実行する。Ctrl+C は、1 回目で取り消しの合図を出して安全な所で止め（130）、
    /// 2 回目ですぐ終了する（130）。
    /// </summary>
    public static async Task<int> RunWithConsoleAsync(
        ToolSpec tool,
        IReadOnlyDictionary<string, CommandHandler> handlers,
        IReadOnlyList<string> args)
    {
        using var cancellation = new CancellationTokenSource();
        var output = CliOutput.ForConsole();
        var coordinator = new InterruptCoordinator(
            cancellation,
            () => output.Warn("Interrupted. Stopping at a safe point. Press Ctrl+C again to exit immediately."),
            () => Environment.Exit(ExitCodeValues.Interrupted));
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            // 既定の強制終了を止め、1 回目は安全に止める
            e.Cancel = true;
            coordinator.Interrupt();
        };

        Console.CancelKeyPress += onCancel;
        try
        {
            return await RunAsync(
                tool, handlers, args, output, Console.In,
                Environment.GetEnvironmentVariable, cancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    private static void WriteUsageError(CliOutput output, ToolSpec tool, string? commandName, string message, bool json)
    {
        output.Error(message);
        output.Progress($"Run '{tool.Name} --help' for usage.");
        if (json)
        {
            // コマンドが特定できないとき（コマンド名より前の誤り）は、ツール名を command に入れる。code は Usage（案）
            output.WriteJson(
                commandName ?? tool.Name,
                false,
                ExitCodeValues.UsageError,
                [new CliIssue(ErrorCodes.Usage, CliOutput.ToAscii(message))],
                []);
        }
    }

    /// <summary>行ごとに書く（改行は書き込み先の設定に従う）。</summary>
    private static void WriteLines(Action<string> write, string text)
    {
        foreach (var line in text.TrimEnd('\n').Split('\n'))
        {
            write(line);
        }
    }
}
