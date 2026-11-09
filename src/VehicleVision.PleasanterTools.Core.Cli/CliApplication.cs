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
    public static async Task<int> RunAsync(
        ToolSpec tool,
        IReadOnlyDictionary<string, CommandHandler> handlers,
        IReadOnlyList<string> args,
        TextWriter stdout,
        TextWriter stderr,
        TextReader input,
        Func<string, string?> getEnvironmentVariable,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);
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
            WriteLines(stderr, HelpText.ShortUsage(tool));
            return ExitCodes.UsageError;
        }

        var parsed = CommandLineParser.Parse(tool, args, getEnvironmentVariable);
        switch (parsed.Outcome)
        {
            case ParseOutcome.Help:
            case ParseOutcome.Version:
                WriteLines(stdout, parsed.Text!);
                return ExitCodes.Success;
            case ParseOutcome.Error:
                WriteUsageError(stderr, tool, parsed.Text!);
                return ExitCodes.UsageError;
        }

        var context = new CommandContext(parsed.Options, stdout, stderr, input, getEnvironmentVariable);
        int code;
        try
        {
            code = await handlers[parsed.CommandName!](context, cancellationToken).ConfigureAwait(false);
        }
        catch (UsageException ex)
        {
            WriteUsageError(stderr, tool, ex.Message);
            return ExitCodes.UsageError;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ExitCodes.Interrupted;
        }
        catch (Exception)
        {
            // 例外の文言は秘密を含み得るので出さない（仕様書 4 章）
            await stderr.WriteLineAsync("[ERROR] An unexpected error occurred.").ConfigureAwait(false);
            code = ExitCodes.Failure;
        }

        // 中断の合図が出ていれば、ハンドラーの戻り値より優先する（130 > 1 > 3 > 0）
        return cancellationToken.IsCancellationRequested ? ExitCodes.Interrupted : code;
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
        var coordinator = new InterruptCoordinator(
            cancellation,
            () => Console.Error.WriteLine("[WARN] Interrupted. Stopping at a safe point. Press Ctrl+C again to exit immediately."),
            () => Environment.Exit(ExitCodes.Interrupted));
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
                tool, handlers, args, Console.Out, Console.Error, Console.In,
                Environment.GetEnvironmentVariable, cancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    private static void WriteUsageError(TextWriter stderr, ToolSpec tool, string message)
    {
        stderr.WriteLine($"[ERROR] {AsciiText.Sanitize(message)}");
        stderr.WriteLine($"Run '{tool.Name} --help' for usage.");
    }

    /// <summary>行ごとに書く（改行は書き込み先の設定に従う）。</summary>
    private static void WriteLines(TextWriter writer, string text)
    {
        foreach (var line in text.TrimEnd('\n').Split('\n'))
        {
            writer.WriteLine(line);
        }
    }
}
