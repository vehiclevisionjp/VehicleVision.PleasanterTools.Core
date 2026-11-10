namespace VehicleVision.PleasanterTools.Core.Cli;

/// <summary>解析の結果の種類。</summary>
public enum ParseOutcome
{
    /// <summary>コマンドを実行する。</summary>
    Run,

    /// <summary>ヘルプを出す。</summary>
    Help,

    /// <summary>版を出す。</summary>
    Version,

    /// <summary>使い方の誤り。</summary>
    Error,
}
