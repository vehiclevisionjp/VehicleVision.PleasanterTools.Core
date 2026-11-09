namespace VehicleVision.PleasanterTools.Core.Cli;

/// <summary>
/// 引数の解析（仕様書 5.1〜5.4）。BCL だけで書く。プロセスを起動せずに試験できるよう、環境変数は差し替えられる。
/// </summary>
public static class CommandLineParser
{
    /// <summary>
    /// 引数を解析する。引数の誤りは例外にせず、<see cref="ParseOutcome.Error"/> と <see cref="ParseResult.Text"/> で返す。
    /// </summary>
    /// <param name="tool">ツールの定義。</param>
    /// <param name="args">引数（先頭はコマンド名。実行ファイル名は含めない）。</param>
    /// <param name="getEnvironmentVariable">環境変数の取得。省略時は環境から読む。試験では差し替える。</param>
    /// <exception cref="ArgumentException">定義に誤りがある（<see cref="ToolSpec"/> を <c>with</c> で書き換えた場合など）。</exception>
    public static ParseResult Parse(
        ToolSpec tool,
        IReadOnlyList<string> args,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(args);
        if (args.Any(arg => arg is null))
        {
            throw new ArgumentException("Arguments must not contain null.", nameof(args));
        }
        ToolValidator.Validate(tool.Name, tool.Description, tool.Version, tool.Commands, tool.CommonOptions);

        var getEnv = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
        var common = tool.CommonOptions ?? [];
        var commonLookup = new OptionLookup(common, [OptionLookup.VersionOption]);

        var commandIndex = FindCommandIndex(commonLookup, args);
        var command = commandIndex >= 0 ? FindCommand(tool, args[commandIndex]) : null;

        // 先頭の help（互換）。後ろにコマンド名があれば、そのコマンドのヘルプ
        if (args.Count > 0 && args[0].Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            var target = args.Count > 1 ? FindCommand(tool, args[1]) : null;
            return HelpResult(tool, target);
        }

        var lookup = command is null
            ? commonLookup
            : new OptionLookup(common, command.Options, [OptionLookup.VersionOption]);
        var switches = ScanSwitches(lookup, args);

        // ヘルプ・版は、エラーより先に判定する
        if (switches.Help)
        {
            return HelpResult(tool, command);
        }
        if (switches.Version)
        {
            return new ParseResult(ParseOutcome.Version, null, ParsedOptions.Empty, $"{tool.Name} {tool.Version}", switches.Json);
        }

        try
        {
            if (commandIndex < 0)
            {
                throw new UsageException("No command was given.");
            }
            if (command is null)
            {
                var candidate = commonLookup.Classify(args[commandIndex]);
                throw new UsageException(candidate.Kind == TokenKind.UnknownLongOption
                    ? $"Unknown option: {CliOutput.ToAscii(candidate.DisplayName)}"
                    : $"Unknown command: {CliOutput.ToAscii(candidate.DisplayName)}");
            }
            return ParseCommand(tool, command, lookup, args, commandIndex, getEnv, switches.Json);
        }
        catch (UsageException ex)
        {
            return new ParseResult(ParseOutcome.Error, command?.Name, ParsedOptions.Empty, ex.Message, switches.Json);
        }
    }

    private static ParseResult HelpResult(ToolSpec tool, CommandSpec? command) =>
        new(
            ParseOutcome.Help,
            command?.Name,
            ParsedOptions.Empty,
            command is null ? HelpText.ForTool(tool) : HelpText.ForCommand(tool, command),
            false);

    private static CommandSpec? FindCommand(ToolSpec tool, string token) =>
        tool.Commands.FirstOrDefault(command => command.Name.Equals(token, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// コマンド名の位置を探す。先頭から、共通のオプション（と値）を読み飛ばし、最初にそれ以外になったトークン。
    /// 無ければ -1。
    /// </summary>
    private static int FindCommandIndex(OptionLookup commonLookup, IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var token = commonLookup.Classify(args[i]);
            switch (token.Kind)
            {
                case TokenKind.Terminator:
                    return -1;
                case TokenKind.Help:
                    continue;
                case TokenKind.Option:
                    if (ConsumesNext(token, commonLookup, args, i))
                    {
                        i++;
                    }
                    continue;
                default:
                    return i;
            }
        }
        return -1;
    }

    private readonly record struct Switches(bool Help, bool Version, bool Json);

    /// <summary><c>--</c> より前に、ヘルプ・版・<c>--json</c> があるか。値の位置に置かれたヘルプも、ヘルプとして扱う。</summary>
    private static Switches ScanSwitches(OptionLookup lookup, IReadOnlyList<string> args)
    {
        bool help = false, version = false, json = false;
        for (var i = 0; i < args.Count; i++)
        {
            var token = lookup.Classify(args[i]);
            if (token.Kind == TokenKind.Terminator)
            {
                break;
            }
            if (token.Kind == TokenKind.Help)
            {
                help = true;
            }
            else if (token.Kind == TokenKind.Option)
            {
                if (ReferenceEquals(token.Option, OptionLookup.VersionOption))
                {
                    version = true;
                }
                else if (token.Option!.Name == StandardOptions.JsonName
                    && token.Option.Kind == OptionKind.Flag
                    && token.InlineValue is null)
                {
                    json = true;
                }
                if (ConsumesNext(token, lookup, args, i))
                {
                    i++;
                }
            }
        }
        return new Switches(help, version, json);
    }

    /// <summary>
    /// 値を取るオプションが、次のトークンを値として取るか。常に取る（<c>--out /tmp/x</c>、<c>--offset -1</c>）。
    /// 例外は、次が名前の分かる <c>--</c> 始まりのオプションのとき（値の書き忘れ）と、ヘルプのとき。
    /// </summary>
    private static bool ConsumesNext(ArgumentToken token, OptionLookup lookup, IReadOnlyList<string> args, int index) =>
        token.Option!.Kind == OptionKind.Value
        && token.InlineValue is null
        && index + 1 < args.Count
        && !lookup.IsKnownLongOption(args[index + 1])
        && !OptionLookup.IsHelp(args[index + 1]);

    private static ParseResult ParseCommand(
        ToolSpec tool,
        CommandSpec command,
        OptionLookup lookup,
        IReadOnlyList<string> args,
        int commandIndex,
        Func<string, string?> getEnv,
        bool json)
    {
        var given = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var positionals = new List<string>();
        var terminated = false;

        void AddPositional(string value, string displayName)
        {
            if (!command.AcceptsPositionals)
            {
                throw new UsageException($"Unknown option or argument: {CliOutput.ToAscii(displayName)}");
            }
            if (positionals.Count >= command.MaxPositionals)
            {
                throw new UsageException($"Too many arguments. At most {command.MaxPositionals} positional argument(s) allowed.");
            }
            positionals.Add(value);
        }

        for (var i = 0; i < args.Count; i++)
        {
            if (i == commandIndex)
            {
                continue;
            }
            var text = args[i];
            if (terminated)
            {
                AddPositional(text, text.Length > 0 ? new OptionLookup().Classify(text).DisplayName : text);
                continue;
            }

            var token = lookup.Classify(text);
            switch (token.Kind)
            {
                case TokenKind.Terminator:
                    terminated = true;
                    break;
                case TokenKind.UnknownLongOption:
                    throw new UsageException($"Unknown option: {CliOutput.ToAscii(token.DisplayName)}");
                case TokenKind.Positional:
                    AddPositional(text, token.DisplayName);
                    break;
                case TokenKind.Option:
                    var option = token.Option!;
                    if (given.ContainsKey(option.Name))
                    {
                        throw new UsageException($"Option --{option.Name} was specified more than once.");
                    }
                    if (option.Kind == OptionKind.Flag)
                    {
                        if (token.InlineValue is not null)
                        {
                            throw new UsageException($"Option --{option.Name} does not take a value.");
                        }
                        given[option.Name] = string.Empty;
                    }
                    else if (token.InlineValue is not null)
                    {
                        given[option.Name] = token.InlineValue.Length > 0
                            ? token.InlineValue
                            : throw new UsageException($"Option --{option.Name} requires a value.");
                    }
                    else if (ConsumesNext(token, lookup, args, i))
                    {
                        given[option.Name] = args[++i];
                    }
                    else
                    {
                        throw new UsageException($"Option --{option.Name} requires a value.");
                    }
                    break;
                default:
                    // Help は ScanSwitches で先に処理済みなので、ここには来ない
                    throw new InvalidOperationException("Unexpected token kind.");
            }
        }

        var allOptions = (tool.CommonOptions ?? []).Concat(command.Options).ToList();
        var options = new ParsedOptions(allOptions, given, positionals, getEnv);
        var missing = options.FirstMissingRequired(allOptions);
        if (missing is not null)
        {
            throw new UsageException($"Option --{missing} is required.");
        }
        return new ParseResult(ParseOutcome.Run, command.Name, options, null, json);
    }
}
