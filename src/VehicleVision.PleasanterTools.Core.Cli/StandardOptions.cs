namespace VehicleVision.PleasanterTools.Core.Cli;

/// <summary>
/// 製品群で共通のオプションの定義。<c>--help</c>・<c>--version</c> は部品が常に受けるので、ここには無い。
/// <see cref="Json"/> は <see cref="ToolSpec.CommonOptions"/> へ、<see cref="Yes"/> は確認の質問を持つコマンドの
/// <see cref="CommandSpec.Options"/> へ入れる（宣言していないコマンドで指定したら使い方の誤り）。
/// </summary>
public static class StandardOptions
{
    /// <summary><see cref="Json"/> の名前。</summary>
    public const string JsonName = "json";

    /// <summary><see cref="Yes"/> の名前。</summary>
    public const string YesName = "yes";

    /// <summary>結果を 1 つの JSON オブジェクトで stdout へ出す。</summary>
    public static OptionSpec Json { get; } = new()
    {
        Name = JsonName,
        Description = "Write the result as one JSON object to stdout.",
    };

    /// <summary>確認の質問を省く。</summary>
    public static OptionSpec Yes { get; } = new()
    {
        Name = YesName,
        ShortName = "y",
        Description = "Answer yes to the confirmation prompt.",
    };
}
