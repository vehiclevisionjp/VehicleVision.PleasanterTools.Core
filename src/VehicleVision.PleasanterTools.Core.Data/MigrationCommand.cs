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
    /// <returns>0: 適用済み／適用完了、1: 状態の確認で未適用あり、2: DB の待機期限切れ。</returns>
    public static async Task<int> RunAsync(
        DatabaseMigrator migrator,
        DatabaseProvider provider,
        string connectionString,
        string[] args,
        TimeSpan? lockTimeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(migrator);
        var apply = args.Contains(ApplyArgument, StringComparer.Ordinal);
        var wait = WaitFor(args);
        if (wait > TimeSpan.Zero)
        {
            Console.WriteLine($"Waiting for the database (up to {wait.TotalSeconds:0}s)...");
            var failure = await migrator.WaitForDatabaseAsync(provider, connectionString, wait, cancellationToken)
                .ConfigureAwait(false);
            if (failure is not null)
            {
                await Console.Error.WriteLineAsync(
                    $"Database not reachable after {wait.TotalSeconds:0}s: {failure.Message}").ConfigureAwait(false);
                return 2;
            }
        }

        var pending = migrator.PendingMigrations(provider, connectionString);
        if (pending.Count == 0)
        {
            Console.WriteLine($"All migrations are applied ({provider}).");
            return 0;
        }
        Console.WriteLine($"Pending migrations ({provider}):");
        foreach (var migration in pending)
        {
            Console.WriteLine($"  - {migration}");
        }
        if (!apply)
        {
            return 1;
        }
        var result = await migrator.MigrateUpWithLockAsync(
            provider, connectionString, lockTimeout ?? AppDatabase.DefaultMigrationLockTimeout,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"Applied {result.AppliedCount} migration(s).");
        return 0;
    }

    private static TimeSpan WaitFor(string[] args)
    {
        const string prefix = "--wait-for-db=";
        var argument = args.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal));
        return argument is not null && int.TryParse(argument[prefix.Length..], out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.Zero;
    }
}
