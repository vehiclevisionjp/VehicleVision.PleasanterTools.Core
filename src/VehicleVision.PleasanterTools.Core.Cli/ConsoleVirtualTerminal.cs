using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace VehicleVision.PleasanterTools.Core.Cli;

/// <summary>
/// Windows のコンソールで、ANSI のエスケープシーケンスを解釈する仮想端末の処理
/// （<c>ENABLE_VIRTUAL_TERMINAL_PROCESSING</c>）を有効にする（仕様書 2.4、決定 Core#176 Q2）。
/// </summary>
/// <remarks>
/// BCL には、コンソールのモードを変える API が無い（<see cref="Console"/> は読み取りだけ）。
/// そのため Windows の <c>kernel32</c> を、最小限（3 関数）の P/Invoke で呼ぶ。追加の NuGet パッケージは要らず、
/// 「BCL だけに依存する」方針とは両立する。Windows 以外では何もせず、有効とみなす。
/// 有効にしたモードは、プロセスの終了後も戻さない（Windows Terminal の ConPTY では元から有効。
/// 従来のコンソールでは、親のシェルがプロンプトのたびに自分のモードへ戻す）。
/// </remarks>
internal static class ConsoleVirtualTerminal
{
    private const int StdOutputHandle = -11;
    private const int StdErrorHandle = -12;
    private const uint EnableVirtualTerminalProcessing = 0x0004;

    /// <summary>
    /// 出力先で ANSI の色を使えるようにする。Windows 以外は常に真。Windows では、仮想端末の処理が有効
    /// （元から、またはここで有効にできた）なら真。コンソールではない・有効にできないときは偽。
    /// </summary>
    /// <param name="isStandardError">stderr なら真、stdout なら偽。</param>
    public static bool TryEnable(bool isStandardError)
    {
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        return TryEnableOnWindows(isStandardError);
    }

    [SupportedOSPlatform("windows")]
    private static bool TryEnableOnWindows(bool isStandardError)
    {
        try
        {
            var handle = GetStdHandle(isStandardError ? StdErrorHandle : StdOutputHandle);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1) || !GetConsoleMode(handle, out var mode))
            {
                return false;
            }

            return (mode & EnableVirtualTerminalProcessing) != 0
                || SetConsoleMode(handle, mode | EnableVirtualTerminalProcessing);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int standardHandle);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetConsoleMode(IntPtr handle, out uint mode);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleMode(IntPtr handle, uint mode);
}
