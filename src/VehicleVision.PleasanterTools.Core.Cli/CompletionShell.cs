namespace VehicleVision.PleasanterTools.Core.Cli;

/// <summary>補完のスクリプトを生成するシェル（仕様書 5.5）。</summary>
public enum CompletionShell
{
    /// <summary>PowerShell（Windows PowerShell 5.1 と PowerShell 7）。</summary>
    PowerShell,

    /// <summary>bash（3.2 以降。<c>bash-completion</c> は不要）。</summary>
    Bash,

    /// <summary>zsh（<c>compinit</c> の後に読み込む）。</summary>
    Zsh,
}
