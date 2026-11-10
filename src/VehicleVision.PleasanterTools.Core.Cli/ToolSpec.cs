namespace VehicleVision.PleasanterTools.Core.Cli;

/// <summary>
/// ツールの定義。ツール名・説明・版は呼び出し側が渡す（Core にアプリ名を書かない）。
/// 定義に誤り（名前の形・重複・予約語・ASCII 以外）があれば、作った時点で <see cref="ArgumentException"/> を投げる。
/// </summary>
public sealed record ToolSpec
{
    /// <summary>ツールの定義を作る。</summary>
    /// <param name="name">ツール名。ヘルプ・<c>--version</c>・エラーの案内に出る。</param>
    /// <param name="description">ツールの 1 行の説明（ASCII）。</param>
    /// <param name="version">版（ASCII）。<c>--version</c> に出る。</param>
    /// <param name="commands">コマンド。1 つ以上。</param>
    /// <param name="commonOptions">全コマンドで使えるオプション（<see cref="StandardOptions.Json"/> など）。</param>
    public ToolSpec(
        string name,
        string description,
        string version,
        IReadOnlyList<CommandSpec> commands,
        IReadOnlyList<OptionSpec>? commonOptions = null)
    {
        ToolValidator.Validate(name, description, version, commands, commonOptions);
        Name = name;
        Description = description;
        Version = version;
        Commands = commands;
        CommonOptions = commonOptions;
    }

    /// <summary>ツール名。</summary>
    public string Name { get; init; }

    /// <summary>ツールの 1 行の説明（ASCII）。</summary>
    public string Description { get; init; }

    /// <summary>版（ASCII）。</summary>
    public string Version { get; init; }

    /// <summary>コマンド。</summary>
    public IReadOnlyList<CommandSpec> Commands { get; init; }

    /// <summary>全コマンドで使えるオプション。</summary>
    public IReadOnlyList<OptionSpec>? CommonOptions { get; init; }
}
