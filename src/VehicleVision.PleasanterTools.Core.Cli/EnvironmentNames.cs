namespace VehicleVision.PleasanterTools.Core.Cli;

/// <summary>環境変数の名前の導き方と値の読み方の補助（仕様書 6.1）。</summary>
public static class EnvironmentNames
{
    /// <summary>
    /// ツール ID（小文字・ハイフン区切り）から接頭辞を導く。<c>binary-migrator</c> → <c>BINARY_MIGRATOR_</c>。
    /// Core.Data の <c>AppDatabase</c> と同じ規則（大文字にして <c>-</c> を <c>_</c> にし、<c>_</c> を付ける）。
    /// </summary>
    public static string Prefix(string toolId)
    {
        ArgumentException.ThrowIfNullOrEmpty(toolId);
        return toolId.ToUpperInvariant().Replace('-', '_') + "_";
    }

    /// <summary>真偽の読み方。<c>true</c>（大文字小文字を問わない）だけが真。未設定・空・ほかの値は偽。前後の空白は無視しない。</summary>
    public static bool IsTrue(string? value) => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
