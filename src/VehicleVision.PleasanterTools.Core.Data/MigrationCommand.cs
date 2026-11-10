using System.Globalization;

namespace VehicleVision.PleasanterTools.Core.Data;

/// <summary>コマンドラインからマイグレーションを当てる／状態を見る。</summary>
public static class MigrationCommand
{
    public const string ApplyArgument = "--migrate";
    public const string StatusArgument = "--migrate-status";

    /// <summary>完全一致で合図を見分ける。</summary>
    public static bool IsRequested(string[] args) =>
        args.Contains(ApplyArgument, StringComparer.Ordinal)
        || args.Contains(StatusArgument, StringComparer.Ordinal);

    /// <summary>状態を表示し、--migrate がある場合だけ排他を取って当てる。</summary>
    /// <param name="migrator">アプリのアセンブリとロック名を持つマイグレーター。</param>
    /// <param name="provider">RDBMS。</param>
    /// <param name="connectionString">解決済みの接続文字列。</param>
    /// <param name="args">起動引数。呼び出し前に <see cref="IsRequested"/> で判別する。</param>
    /// <param name="lockTimeout">省略時は <see cref="AppDatabase.DefaultMigrationLockTimeout"/>。</param>
    /// <param name="cancellationToken">中止の合図。</param>
    /// <param name="output">結果（状態・要約・<c>[OK]</c>）の出力先。省略時は <see cref="Console.Out"/>。</param>
    /// <param name="error">進行表示・失敗（<c>[ERROR]</c>）の出力先。省略時は <see cref="Console.Error"/>。</param>
    /// <returns>0: 適用済み／適用完了、1: 状態の確認で未適用あり、または DB の待機期限切れ。</returns>
    /// <remarks>
    /// 文言は英語・ASCII。結果は stdout、進行表示と失敗は stderr（CLI の共通仕様書 2 章）。
    /// 待機期限切れの <c>[ERROR]</c> は、固定の文言と例外の型名だけ（例外のメッセージは出さない）。
    /// <c>--help</c>・<c>--version</c>・<c>--json</c>・<c>--yes</c>・中断（130）は受けない。
    /// 待機の指定は <c>--wait-for-db N</c>・<c>--wait-for-db=N</c>・<c>--wait-for-db:N</c> の 3 書式（最初の指定を採る）。
    /// </remarks>
    public static async Task<int> RunAsync(
        DatabaseMigrator migrator,
        DatabaseProvider provider,
        string connectionString,
        string[] args,
        TimeSpan? lockTimeout = null,
        CancellationToken cancellationToken = default,
        TextWriter? output = null,
        TextWriter? error = null)
    {
        ArgumentNullException.ThrowIfNull(migrator);
        output ??= Console.Out;
        error ??= Console.Error;
        var apply = args.Contains(ApplyArgument, StringComparer.Ordinal);
        var wait = WaitFor(args);
        if (wait > TimeSpan.Zero)
        {
            await error.WriteLineAsync($"Waiting for the database (up to {wait.TotalSeconds:0}s)...").ConfigureAwait(false);
            var failure = await migrator.WaitForDatabaseAsync(provider, connectionString, wait, cancellationToken)
                .ConfigureAwait(false);
            if (failure is not null)
            {
                // ドライバーの例外メッセージは出さない（接続文字列などの秘密が混ざり得る。仕様書 2.1）。型名だけを示す
                await error.WriteLineAsync(
                    $"[ERROR] Database not reachable after {wait.TotalSeconds:0}s ({ToAscii(failure.GetType().Name)}).")
                    .ConfigureAwait(false);
                return 1;
            }
        }

        var pending = migrator.PendingMigrations(provider, connectionString);
        if (pending.Count == 0)
        {
            await output.WriteLineAsync($"[OK] All migrations are applied ({provider}).").ConfigureAwait(false);
            return 0;
        }
        await output.WriteLineAsync($"Pending migrations ({provider}):").ConfigureAwait(false);
        foreach (var migration in pending)
        {
            await output.WriteLineAsync($"  - {migration}").ConfigureAwait(false);
        }
        if (!apply)
        {
            return 1;
        }
        var result = await migrator.MigrateUpWithLockAsync(
            provider, connectionString, lockTimeout ?? AppDatabase.DefaultMigrationLockTimeout,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync($"[OK] Applied {result.AppliedCount} migration(s).").ConfigureAwait(false);
        return 0;
    }

    private const string WaitName = "--wait-for-db";

    /// <summary>最初の指定だけを見る。読めない値（0 以下・数字でない・範囲外・値なし）は待たない。</summary>
    private static TimeSpan WaitFor(string[] args)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            string? value;
            if (string.Equals(arg, WaitName, StringComparison.Ordinal))
            {
                value = index + 1 < args.Length ? args[index + 1] : null;
            }
            else if (arg.Length > WaitName.Length
                && arg.StartsWith(WaitName, StringComparison.Ordinal)
                && arg[WaitName.Length] is '=' or ':')
            {
                value = arg[(WaitName.Length + 1)..];
            }
            else
            {
                continue;
            }

            return value is not null
                && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) && seconds > 0
                ? TimeSpan.FromSeconds(seconds)
                : TimeSpan.Zero;
        }
        return TimeSpan.Zero;
    }

    /// <summary>ASCII 以外と制御文字を <c>?</c> にして 1 行にする（仕様書 2.1）。</summary>
    private static string ToAscii(string message) =>
        string.Create(message.Length, message, static (span, source) =>
        {
            for (var index = 0; index < span.Length; index++)
            {
                span[index] = source[index] is >= ' ' and <= '~' ? source[index] : '?';
            }
        });
}
