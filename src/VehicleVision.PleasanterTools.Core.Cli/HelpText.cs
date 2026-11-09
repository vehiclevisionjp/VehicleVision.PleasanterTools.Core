using System.Text;

namespace VehicleVision.PleasanterTools.Core.Cli;

/// <summary>ヘルプを、ツールの定義から組み立てる（仕様書 5.3）。全文が ASCII。行の区切りは <c>\n</c>。</summary>
internal static class HelpText
{
    private const string Indent = "  ";

    private const string ExitCodeLine =
        "  0 success, 1 failure, 2 usage error, 3 partially done, 130 interrupted.";

    private const string FormNote = "Options can also be written as -name or /name.";

    /// <summary>ツール全体のヘルプ。</summary>
    public static string ForTool(ToolSpec tool)
    {
        var builder = new StringBuilder();
        builder.Append(tool.Name).Append(" - ").Append(tool.Description).Append("\n\n");
        builder.Append("Usage:\n");
        builder.Append(Indent).Append(tool.Name).Append(" <command> [options]\n");
        builder.Append(Indent).Append(tool.Name).Append(" <command> --help\n");
        builder.Append(Indent).Append(tool.Name).Append(" --help | --version\n\n");

        builder.Append("Commands:\n");
        var width = tool.Commands.Max(command => command.Name.Length) + 2;
        foreach (var command in tool.Commands)
        {
            builder.Append(Indent).Append(command.Name.PadRight(width)).Append(command.Summary).Append('\n');
        }
        builder.Append('\n');

        AppendCommonOptions(builder, tool);
        builder.Append(FormNote).Append("\n\n");
        builder.Append("Exit codes:\n").Append(ExitCodeLine).Append('\n');
        return builder.ToString();
    }

    /// <summary>1 つのコマンドのヘルプ。</summary>
    public static string ForCommand(ToolSpec tool, CommandSpec command)
    {
        var builder = new StringBuilder();
        builder.Append(tool.Name).Append(' ').Append(command.Name).Append(" - ").Append(command.Summary).Append("\n\n");
        builder.Append("Usage:\n");
        builder.Append(Indent).Append(tool.Name).Append(' ').Append(command.Name).Append(" [options]");
        if (command.AcceptsPositionals)
        {
            builder.Append(" [arguments]");
        }
        builder.Append("\n\n");

        if (command.Options.Count > 0)
        {
            builder.Append("Options for '").Append(command.Name).Append("':\n");
            AppendRows(builder, command.Options.Select(Row).ToList());
            builder.Append('\n');
        }

        AppendCommonOptions(builder, tool);
        builder.Append(FormNote).Append("\n\n");
        builder.Append("Exit codes:\n").Append(ExitCodeLine).Append('\n');
        return builder.ToString();
    }

    /// <summary>コマンドが必要なのに引数が無いときに、stderr へ出す短い使い方。</summary>
    public static string ShortUsage(ToolSpec tool)
    {
        var builder = new StringBuilder();
        builder.Append(tool.Name).Append(" - ").Append(tool.Description).Append("\n\n");
        builder.Append("Usage:\n");
        builder.Append(Indent).Append(tool.Name).Append(" <command> [options]\n");
        builder.Append(Indent).Append(tool.Name).Append(" --help | --version\n\n");
        builder.Append("Commands: ").Append(string.Join(", ", tool.Commands.Select(command => command.Name))).Append('\n');
        builder.Append("Run '").Append(tool.Name).Append(" --help' for more information.\n");
        return builder.ToString();
    }

    private static void AppendCommonOptions(StringBuilder builder, ToolSpec tool)
    {
        var rows = new List<(string Left, string Right)>
        {
            ("-h, --help, -?, /?", "Show this help."),
            ("    --version", OptionLookup.VersionOption.Description),
        };
        rows.AddRange((tool.CommonOptions ?? []).Select(Row));
        builder.Append("Common options:\n");
        AppendRows(builder, rows);
        builder.Append('\n');
    }

    private static void AppendRows(StringBuilder builder, IReadOnlyList<(string Left, string Right)> rows)
    {
        var width = rows.Max(row => row.Left.Length) + 2;
        foreach (var (left, right) in rows)
        {
            builder.Append(Indent).Append(left.PadRight(width)).Append(right).Append('\n');
        }
    }

    private static (string Left, string Right) Row(OptionSpec option)
    {
        var left = new StringBuilder();
        left.Append(option.ShortName is null ? "    " : $"-{option.ShortName}, ");
        left.Append("--").Append(option.Name);
        if (option.Kind == OptionKind.Value)
        {
            left.Append(" <").Append(option.ValueName ?? "VALUE").Append('>');
        }

        var right = new StringBuilder(option.Description);
        if (option.Required)
        {
            right.Append(" (required)");
        }
        if (option.DefaultValue is not null)
        {
            right.Append(" (default: ").Append(option.DefaultValue).Append(')');
        }
        if (option.EnvironmentVariable is not null)
        {
            right.Append(" [env ").Append(option.EnvironmentVariable).Append(']');
        }
        return (left.ToString(), right.ToString());
    }
}
