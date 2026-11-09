namespace VehicleVision.PleasanterTools.Core.Cli;

/// <summary>
/// ツールの定義の検査。誤りは定義の時点（<see cref="ToolSpec"/> を作るとき・解析の前）で
/// <see cref="ArgumentException"/> にする。メッセージは英語。
/// </summary>
internal static class ToolValidator
{
    /// <summary>コマンド名・オプション名に使えない予約語。</summary>
    private static readonly string[] ReservedNames = ["help", "version"];

    /// <summary>短縮形に使えない文字（<c>-h</c> はヘルプ）。</summary>
    private const string ReservedShortName = "h";

    /// <summary>オプションの長い名前の最小の長さ（1 文字は短縮形と区別できないため）。</summary>
    private const int MinOptionNameLength = 2;

    public static void Validate(
        string name,
        string description,
        string version,
        IReadOnlyList<CommandSpec> commands,
        IReadOnlyList<OptionSpec>? commonOptions)
    {
        RequireToken(name, "Tool name");
        RequireLine(description, "Tool description");
        RequireToken(version, "Tool version");
        ArgumentNullException.ThrowIfNull(commands);
        if (commands.Count == 0)
        {
            throw Invalid("A tool needs at least one command.");
        }

        var common = commonOptions ?? [];
        ValidateOptionList(common, "common options", []);

        var commandNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in commands)
        {
            ArgumentNullException.ThrowIfNull(command);
            RequireName(command.Name, 1, "Command name");
            RequireNotReserved(command.Name, "Command name");
            RequireLine(command.Summary, $"Summary of command '{command.Name}'");
            if (!commandNames.Add(command.Name))
            {
                throw Invalid($"Command name '{command.Name}' is defined more than once (names are case-insensitive).");
            }
            if (command.AcceptsPositionals && command.MaxPositionals < 1)
            {
                throw Invalid($"MaxPositionals of command '{command.Name}' must be 1 or more.");
            }
            ArgumentNullException.ThrowIfNull(command.Options);
            ValidateOptionList(command.Options, $"command '{command.Name}'", common);
        }
    }

    private static void ValidateOptionList(
        IReadOnlyList<OptionSpec> options, string where, IReadOnlyList<OptionSpec> alreadyDefined)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var shortNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var other in alreadyDefined)
        {
            names.Add(other.Name);
            if (other.ShortName is not null)
            {
                shortNames.Add(other.ShortName);
            }
        }

        foreach (var option in options)
        {
            ArgumentNullException.ThrowIfNull(option);
            RequireName(option.Name, MinOptionNameLength, "Option name");
            RequireNotReserved(option.Name, "Option name");
            RequireLine(option.Description, $"Description of option '{option.Name}'");
            if (!names.Add(option.Name))
            {
                throw Invalid($"Option name '{option.Name}' is defined more than once in {where} (names are case-insensitive).");
            }
            if (option.ShortName is not null)
            {
                if (option.ShortName.Length != 1 || option.ShortName[0] is not (>= 'a' and <= 'z'))
                {
                    throw Invalid($"Short name of option '{option.Name}' must be one lowercase letter a-z.");
                }
                if (option.ShortName == ReservedShortName)
                {
                    throw Invalid($"Short name 'h' is reserved for help (option '{option.Name}').");
                }
                if (!shortNames.Add(option.ShortName))
                {
                    throw Invalid($"Short name '{option.ShortName}' is defined more than once in {where}.");
                }
            }
            if (option.EnvironmentVariable is not null)
            {
                RequireToken(option.EnvironmentVariable, $"Environment variable of option '{option.Name}'");
                if (option.EnvironmentVariable.Contains('=', StringComparison.Ordinal))
                {
                    throw Invalid($"Environment variable of option '{option.Name}' must not contain '='.");
                }
            }
            if (option.ValueName is not null)
            {
                RequireToken(option.ValueName, $"Value name of option '{option.Name}'");
            }
            if (option.DefaultValue is not null)
            {
                RequireLine(option.DefaultValue, $"Default value of option '{option.Name}'");
            }
            if (option.Kind == OptionKind.Flag
                && (option.Required || option.ValueName is not null || option.DefaultValue is not null))
            {
                throw Invalid($"Option '{option.Name}' is a flag; Required, ValueName and DefaultValue apply to value options only.");
            }
        }
    }

    private static void RequireName(string? value, int minLength, string what)
    {
        if (string.IsNullOrEmpty(value) || value.Length < minLength)
        {
            throw Invalid($"{what} must have at least {minLength} character(s).");
        }
        if (value[0] is not (>= 'a' and <= 'z')
            || !value.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
        {
            throw Invalid($"{what} '{CliOutput.ToAscii(value)}' must match [a-z][a-z0-9-]* (lowercase letters, digits and hyphens).");
        }
    }

    private static void RequireNotReserved(string value, string what)
    {
        if (ReservedNames.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            throw Invalid($"{what} '{value}' is reserved.");
        }
    }

    /// <summary>空白・制御文字を含まない ASCII の 1 語。</summary>
    private static void RequireToken(string? value, string what)
    {
        if (string.IsNullOrEmpty(value) || !CliOutput.IsPrintableAscii(value) || value.Contains(' ', StringComparison.Ordinal))
        {
            throw Invalid($"{what} must be a non-empty ASCII word without spaces.");
        }
    }

    /// <summary>ASCII の印字可能文字だけの 1 行。</summary>
    private static void RequireLine(string? value, string what)
    {
        if (string.IsNullOrEmpty(value) || !CliOutput.IsPrintableAscii(value))
        {
            throw Invalid($"{what} must be a non-empty single line of printable ASCII.");
        }
    }

    private static ArgumentException Invalid(string message) => new(message);
}
