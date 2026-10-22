using System.Reflection;
using FluentMigrator.Runner;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace VehicleVision.PleasanterTools.Core.Data;

/// <summary>スキーマを適用する。</summary>
/// <remarks>
/// <para>起動時に適用するときは、DB ごとの排他を取って同時実行を防ぐ。</para>
/// <para>
/// **前方のみ。** ロールバック用のスクリプトに頼らない。
/// </para>
/// </remarks>
public sealed class DatabaseMigrator
{
    private readonly Assembly migrationsAssembly;
    private readonly string lockName;

    /// <summary>マイグレーションの置き場とロック名を決めて作る。</summary>
    /// <param name="migrationsAssembly">FluentMigrator のマイグレーションを持つアセンブリ。**アプリごとに渡す。**</param>
    /// <param name="lockName">起動時の排他に使う名前（例: <c>app-startup-migrations</c>）。**アプリごとに別にする。**</param>
    public DatabaseMigrator(Assembly migrationsAssembly, string lockName)
    {
        ArgumentNullException.ThrowIfNull(migrationsAssembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(lockName);

        this.migrationsAssembly = migrationsAssembly;
        this.lockName = lockName;
    }

    /// <summary>排他を取って未適用のマイグレーションをすべて適用する。</summary>
    public async Task<MigrationApplyResult> MigrateUpWithLockAsync(
        DatabaseProvider provider,
        string connectionString,
        TimeSpan lockTimeout,
        Action<MigrationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Invoke(MigrationProgress.WaitingForLock);
        await using var migrationLock = await DatabaseMigrationLock.AcquireAsync(
            provider,
            connectionString,
            lockName,
            lockTimeout,
            cancellationToken).ConfigureAwait(false);

        progress?.Invoke(MigrationProgress.LockAcquired);
        var pending = PendingMigrations(provider, connectionString);
        if (pending.Count == 0)
        {
            return new MigrationApplyResult(0, []);
        }

        progress?.Invoke(new MigrationProgress(MigrationProgressKind.Applying, pending.Count));
        MigrateUp(provider, connectionString);
        return new MigrationApplyResult(pending.Count, pending);
    }

    /// <summary>未適用のマイグレーションをすべて適用する。</summary>
    public void MigrateUp(DatabaseProvider provider, string connectionString)
    {
        using var services = BuildServices(provider, connectionString);
        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
    }

    /// <summary>適用済みかどうかを確かめる。</summary>
    public bool HasPendingMigrations(DatabaseProvider provider, string connectionString)
    {
        using var services = BuildServices(provider, connectionString);
        using var scope = services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IMigrationRunner>().HasMigrationsToApplyUp();
    }

    /// <summary>まだ当たっていないマイグレーションの名前。</summary>
    /// <remarks>
    /// <para>
    /// **「当て忘れている」と言うだけでは直せない。** 何が足りないかまで見せる。
    /// マイグレーション自体は適用しない。版の表が無い DB では、版のローダーが空の表を作ることがある。
    /// </para>
    /// <para>
    /// ⚠️ **説明文（<c>[Migration(20, "…")]</c> の第 2 引数）を出さないこと。**
    /// あれは日本語で、**Azure の Kudu の Debug console では丸ごと文字化けする**
    /// （Issue #225 で出力を英語にしたのに、ここだけ日本語が漏れていた）。
    /// </para>
    /// <para>
    /// **代わりに型の名前を出す**（<c>M0020_TestPublishedResponses</c>）。
    /// ASCII なので化けず、**そのままファイル名として探せる。**
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> PendingMigrations(
        DatabaseProvider provider,
        string connectionString)
    {
        using var services = BuildServices(provider, connectionString);
        using var scope = services.CreateScope();

        var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
        var applied = scope.ServiceProvider.GetRequiredService<IVersionLoader>().VersionInfo;

        return [.. runner.MigrationLoader.LoadMigrations()
            .Where(migration => !applied.HasAppliedMigration(migration.Key))
            .Select(migration => $"{migration.Key} {migration.Value.Migration.GetType().Name}")];
    }

    /// <summary>管理画面へ表示するマイグレーションの適用状況。</summary>
    public MigrationStatus GetStatus(
        DatabaseProvider provider,
        string connectionString)
    {
        using var services = BuildServices(provider, connectionString);
        using var scope = services.CreateScope();

        var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
        var versionInfo = scope.ServiceProvider.GetRequiredService<IVersionLoader>().VersionInfo;
        var migrations = runner.MigrationLoader.LoadMigrations();
        var applied = versionInfo.AppliedMigrations().ToHashSet();
        var latestVersion = migrations.Count == 0 ? 0 : migrations.Keys.Max();
        var appliedVersion = applied.Count == 0 ? (long?)null : applied.Max();
        var pendingCount = migrations.Keys.Count(version => !applied.Contains(version));
        DateTime? lastAppliedAt = null;

        if (appliedVersion is not null)
        {
            var factory = new DbConnectionFactory(provider, connectionString);
            using var connection = factory.Create();
            connection.Open();
            lastAppliedAt = connection.QuerySingleOrDefault<DateTime?>(
                SqlDialect.Format(provider, SqlDialect.LastMigrationAppliedAt));
        }

        return new MigrationStatus(
            appliedVersion,
            latestVersion,
            pendingCount,
            lastAppliedAt is null ? null : DbTime.AsUtc(lastAppliedAt));
    }

    /// <summary>DB が接続を受けるようになるまで待つ。</summary>
    /// <remarks>
    /// <para>
    /// **コンテナで一緒に立ち上げると、DB より先にこちらが動き出す。**
    /// マイグレーションだけは待ってから当てないと、順番だけの理由で失敗する。
    /// </para>
    /// <para>
    /// **待つのはここだけ。** アプリ本体は待たない（設定の誤りを起動の遅さで隠さないため）。
    /// </para>
    /// </remarks>
    /// <returns>待った末に繋がらなかったときの例外。繋がれば <c>null</c>。</returns>
    public async Task<Exception?> WaitForDatabaseAsync(
        DatabaseProvider provider,
        string connectionString,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var factory = new DbConnectionFactory(provider, connectionString);
        var deadline = DateTimeOffset.UtcNow + timeout;
        Exception? last = null;

        while (true)
        {
            try
            {
                await using var connection = factory.Create();
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }

            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // **繋がらない理由は待てば消えることも消えないこともある。**
                // 見分けが付かないので、期限まではどちらも待つ
                last = exception;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                return last;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>走査するアセンブリ。アプリのマイグレーションと、Core.Data の共有マイグレーション。</summary>
    private Assembly[] ScannedAssemblies =>
        migrationsAssembly == typeof(DatabaseMigrator).Assembly
            ? [migrationsAssembly]
            : [migrationsAssembly, typeof(DatabaseMigrator).Assembly];

    private ServiceProvider BuildServices(DatabaseProvider provider, string connectionString) =>
        new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(builder =>
            {
                var configured = provider switch
                {
                    DatabaseProvider.SqlServer => builder.AddSqlServer(),
                    DatabaseProvider.PostgreSql => builder.AddPostgres(),
                    DatabaseProvider.MySql => builder.AddMySql8(),
                    DatabaseProvider.Sqlite => builder.AddSQLite(),
                    _ => throw new NotSupportedException($"対応していない RDBMS: {provider}"),
                };

                configured
                    .WithGlobalConnectionString(connectionString)
                    // アプリの分に加えて、Core.Data 自身の共有マイグレーション（M10001〜、Core_ 接頭辞）も当てる
                    .ScanIn(ScannedAssemblies).For.Migrations();
            })
            .BuildServiceProvider(validateScopes: false);
}

/// <summary>排他付きマイグレーションの結果。</summary>
public sealed record MigrationApplyResult(
    int AppliedCount,
    IReadOnlyList<string> AppliedMigrations);

/// <summary>マイグレーション処理の進捗。</summary>
public sealed record MigrationProgress(MigrationProgressKind Kind, int MigrationCount = 0)
{
    public static readonly MigrationProgress WaitingForLock =
        new(MigrationProgressKind.WaitingForLock);
    public static readonly MigrationProgress LockAcquired =
        new(MigrationProgressKind.LockAcquired);
}

/// <summary>マイグレーション処理の段階。</summary>
public enum MigrationProgressKind
{
    WaitingForLock,
    LockAcquired,
    Applying,
}

/// <summary>DB スキーマの適用状況。</summary>
public sealed record MigrationStatus(
    long? AppliedVersion,
    long LatestVersion,
    int PendingCount,
    DateTime? LastAppliedAt);
