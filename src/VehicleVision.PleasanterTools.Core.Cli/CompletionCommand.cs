using VehicleVision.PleasanterTools.Core.Contracts;

namespace VehicleVision.PleasanterTools.Core.Cli;

/// <summary>
/// 各ツールが組み込む <c>completion &lt;shell&gt;</c> コマンド（仕様書 5.5、決定 N1）。
/// そのシェル用の補完スクリプトを stdout へ出す。<see cref="Spec"/> を <see cref="ToolSpec.Commands"/> へ足し、
/// <see cref="CreateHandler"/> をハンドラーの対応へ足す。
/// </summary>
/// <example>
/// <code>
/// var tool = new ToolSpec("my-tool", "...", "1.0.0", [.., CompletionCommand.Spec]);
/// var handlers = new Dictionary&lt;string, CommandHandler&gt; { ..., [CompletionCommand.Name] = CompletionCommand.CreateHandler(tool) };
/// </code>
/// </example>
public static class CompletionCommand
{
    /// <summary>コマンド名。</summary>
    public const string Name = "completion";

    /// <summary>コマンドの定義。位置引数は 1 つ（<c>powershell</c>・<c>bash</c>・<c>zsh</c>）。</summary>
    public static CommandSpec Spec { get; } = new(
        Name,
        "Write a shell completion script to stdout (powershell, bash or zsh).",
        [],
        AcceptsPositionals: true,
        MaxPositionals: 1,
        PositionalValues: ["powershell", "bash", "zsh"]);

    /// <summary>ハンドラーを作る。<paramref name="tool"/> は <see cref="Spec"/> を含む定義（スクリプトの元）。</summary>
    public static CommandHandler CreateHandler(ToolSpec tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return (context, _) =>
        {
            var name = context.Options.Positionals.Count == 1 ? context.Options.Positionals[0] : null;
            if (name is null)
            {
                throw new UsageException("A shell name is required: powershell, bash or zsh.");
            }
            if (!Completion.TryParseShell(name, out var shell))
            {
                throw new UsageException($"Unknown shell: {CliOutput.ToAscii(name)}. Use powershell, bash or zsh.");
            }
            // 改行は \n のまま出す（bash・zsh は \r を読めない）
            context.Output.WriteText(Completion.Generate(tool, shell));
            return Task.FromResult(ExitCodeValues.Success);
        };
    }
}
