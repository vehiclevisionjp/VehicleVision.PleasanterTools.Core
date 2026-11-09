namespace VehicleVision.PleasanterTools.Core.Cli;

/// <summary>
/// Ctrl+C の扱い（仕様書 4 章、決定 U18）。1 回目は取り消しの合図を出して安全な所で止めさせ、
/// 2 回目はすぐ終了する。コンソールに依存させないので、試験で回数と動きを確かめられる。
/// </summary>
internal sealed class InterruptCoordinator(CancellationTokenSource cancellation, Action onFirst, Action forceExit)
{
    private int count;

    /// <summary>Ctrl+C が押されたときに呼ぶ。1 回目は取り消し、2 回目以降は強制終了。</summary>
    public void Interrupt()
    {
        if (Interlocked.Increment(ref count) == 1)
        {
            onFirst();
            cancellation.Cancel();
        }
        else
        {
            forceExit();
        }
    }
}
