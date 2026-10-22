using System.Globalization;
using Microsoft.Data.Sqlite;

namespace VehicleVision.PleasanterTools.Core.Data.Tests;

/// <summary>アプリの合図・再適用の試験を移植し、状態と出力の契約を追加。</summary>
[Collection("MigrationCommand console")]
public sealed class MigrationCommandTests : IDisposable
{
    private readonly string file = Path.Combine(Path.GetTempPath(), $"core-migrate-{Guid.NewGuid():N}.db");
    private readonly DatabaseMigrator migrator = new(typeof(DatabaseMigrator).Assembly, "test-command-migrations");
    private string Connection => $"Data Source={file};Pooling=False";

    [Theory]
    [InlineData("--migrate", true)]
    [InlineData("--migrate-status", true)]
    [InlineData("--migrated", false)]
    [InlineData("--MIGRATE", false)]
    [InlineData("--generate-secret-key", false)]
    [InlineData("--wait-for-db=30", false)]
    public void 合図は完全一致(string arg, bool expected)
    {
        Assert.Equal(expected, MigrationCommand.IsRequested([arg]));
        Assert.False(MigrationCommand.IsRequested([]));
    }

    private async Task<(int Code, string Output, string Error)> Run(
        string[] args, string? connection = null, TimeSpan? lockTimeout = null, CancellationToken cancellationToken = default)
    {
        var output = Console.Out;
        var error = Console.Error;
        using var capturedOutput = new StringWriter(CultureInfo.InvariantCulture);
        using var capturedError = new StringWriter(CultureInfo.InvariantCulture);
        try
        {
            Console.SetOut(capturedOutput);
            Console.SetError(capturedError);
            var code = await MigrationCommand.RunAsync(
                migrator, DatabaseProvider.Sqlite, connection ?? Connection, args, lockTimeout, cancellationToken);
            return (code, capturedOutput.ToString(), capturedError.ToString());
        }
        finally
        {
            Console.SetOut(output);
            Console.SetError(error);
        }
    }

    [Fact]
    public async Task 状態の確認はスキーマを適用せず未適用を列挙して終了コード1()
    {
        var pending = migrator.PendingMigrations(DatabaseProvider.Sqlite, Connection);
        var result = await Run(["--migrate-status"]);
        Assert.Equal(1, result.Code);
        Assert.Equal("Pending migrations (Sqlite):" + Environment.NewLine
            + string.Concat(pending.Select(name => $"  - {name}{Environment.NewLine}")), result.Output);
        Assert.Empty(result.Error);
        using var db = new SqliteConnection(Connection);
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name <> 'VersionInfo'";
        Assert.Equal(0L, command.ExecuteScalar());
        command.CommandText = "SELECT COUNT(*) FROM VersionInfo";
        Assert.Equal(0L, command.ExecuteScalar());
    }

    [Fact]
    public async Task 当てて表を作り再適用は何も壊さない()
    {
        var pending = migrator.PendingMigrations(DatabaseProvider.Sqlite, Connection);
        var applied = await Run(["--migrate"]);
        Assert.Equal(0, applied.Code);
        Assert.Equal("Pending migrations (Sqlite):" + Environment.NewLine
            + string.Concat(pending.Select(name => $"  - {name}{Environment.NewLine}"))
            + $"Applied {pending.Count} migration(s).{Environment.NewLine}", applied.Output);
        Assert.Empty(applied.Error);
        foreach (var arg in new[] { "--migrate", "--migrate-status" })
        {
            var again = await Run([arg]);
            Assert.Equal(0, again.Code);
            Assert.Equal($"All migrations are applied (Sqlite).{Environment.NewLine}", again.Output);
            Assert.Empty(again.Error);
        }
        using var db = new SqliteConnection(Connection);
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Core_AdminUsers";
        Assert.Equal(0L, command.ExecuteScalar());
    }

    [Fact]
    public async Task 両方の合図があれば適用を優先する()
    {
        Assert.Equal(0, (await Run(["--migrate-status", "--migrate"])).Code);
        Assert.Empty(migrator.PendingMigrations(DatabaseProvider.Sqlite, Connection));
    }

    [Theory]
    [InlineData("--wait-for-db=0")]
    [InlineData("--wait-for-db=-1")]
    [InlineData("--wait-for-db=no")]
    [InlineData("--wait-for-db=2147483648")]
    public async Task 既存どおり読めない待機秒数は待たない(string wait)
    {
        var result = await Run(["--migrate-status", wait]);
        Assert.Equal(1, result.Code);
        Assert.DoesNotContain("Waiting", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 待機の成功と最初の指定の採用()
    {
        var result = await Run(["--migrate-status", "--wait-for-db=1", "--wait-for-db=9"]);
        Assert.Equal(1, result.Code);
        Assert.StartsWith($"Waiting for the database (up to 1s)...{Environment.NewLine}", result.Output);
    }

    [Fact]
    public async Task 繋がらないDBは終了コード2と標準エラー()
    {
        // 外部 DB へは接続せず、存在しないファイルを読み取り専用で開く。
        var result = await Run(["--migrate", "--wait-for-db=1"], Connection + ";Mode=ReadOnly");
        Assert.Equal(2, result.Code);
        Assert.Equal($"Waiting for the database (up to 1s)...{Environment.NewLine}", result.Output);
        Assert.StartsWith("Database not reachable after 1s: ", result.Error);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task 待機の中止は終了コードへ変えず呼び出し元へ伝える()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Run(["--migrate", "--wait-for-db=1"], cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task 呼び出し側のロック待機時間を使い状態の確認はロックを取らない()
    {
        await using var held = await DatabaseMigrationLock.AcquireAsync(
            DatabaseProvider.Sqlite, Connection, "test-command-migrations", TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert.Equal(1, (await Run(["--migrate-status"])).Code);
        await Assert.ThrowsAsync<TimeoutException>(() => Run(["--migrate"], lockTimeout: TimeSpan.FromMilliseconds(10)));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(file);
        File.Delete(file + ".migration-lock.sqlite");
    }
}

[CollectionDefinition("MigrationCommand console", DisableParallelization = true)]
public sealed class MigrationCommandConsoleCollection;
