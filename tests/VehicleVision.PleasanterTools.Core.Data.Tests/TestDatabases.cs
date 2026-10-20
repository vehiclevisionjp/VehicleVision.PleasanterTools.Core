using System.Reflection;
using Xunit.Sdk;

namespace VehicleVision.PleasanterTools.Core.Data.Tests;

/// <summary>
/// 結合テストの対象 RDBMS。**SQLite は常に流す。** 他は接続文字列の環境変数があるときだけ流す。
/// </summary>
/// <remarks>
/// ⚠️ **環境変数を設定していないと、その RDBMS のテストは何も流れずに緑になる。その緑を「通った」と読まないこと。**
/// CI は <c>COREDATA_REQUIRE_ALL_RDBMS=1</c> を設定し、未設定の RDBMS があれば
/// <see cref="TestDatabasesTests.CIでは全RDBMSが設定されている"/> を落とす。
/// </remarks>
public static class TestDatabases
{
    public const string RequireAllKey = "COREDATA_REQUIRE_ALL_RDBMS";

    /// <summary>共有の DB の表を触るテストクラスを直列にする xUnit のコレクション名（クラスをまたいで表を空にし合わないように）。</summary>
    public const string CollectionName = "RDBMS";

    public static string? ConnectionString(DatabaseProvider provider) => provider switch
    {
        DatabaseProvider.Sqlite => null,
        DatabaseProvider.SqlServer => Environment.GetEnvironmentVariable("COREDATA_TEST_SQLSERVER"),
        DatabaseProvider.PostgreSql => Environment.GetEnvironmentVariable("COREDATA_TEST_POSTGRESQL"),
        DatabaseProvider.MySql => Environment.GetEnvironmentVariable("COREDATA_TEST_MYSQL"),
        _ => throw new NotSupportedException(),
    };

    public static bool IsEnabled(DatabaseProvider provider) =>
        provider is DatabaseProvider.Sqlite || !string.IsNullOrWhiteSpace(ConnectionString(provider));

    private static readonly object MigrationLock = new();

    /// <summary>
    /// Core の共有マイグレーションを当てる。**共有の DB へ同時に当てると「表が既にある」で落ちる**ので、プロセスの中で直列にする。
    /// </summary>
    public static void Migrate(DatabaseProvider provider, string connectionString)
    {
        lock (MigrationLock)
        {
            Migrator.MigrateUp(provider, connectionString);
        }
    }

    /// <summary>テスト用のマイグレーター（アプリの分は無く、Core の共有マイグレーションだけが当たる）。</summary>
    public static DatabaseMigrator Migrator { get; } =
        new(typeof(TestDatabases).Assembly, "core-data-tests-migrations");
}

/// <summary>設定された RDBMS だけをテストの引数に流す。</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class EnabledDatabasesAttribute : DataAttribute
{
    public override IEnumerable<object[]> GetData(MethodInfo testMethod) =>
        Enum.GetValues<DatabaseProvider>().Where(TestDatabases.IsEnabled).Select(p => new object[] { p });
}

/// <summary>RDBMS ごとの接続先を用意する。SQLite は、テストごとの一時ファイル。</summary>
public abstract class DatabaseTestBase : IDisposable
{
    private readonly string sqlitePath = Path.Combine(Path.GetTempPath(), $"coredata-{Guid.NewGuid():N}.db");

    protected string ConnectionStringFor(DatabaseProvider provider) =>
        provider is DatabaseProvider.Sqlite
            ? $"Data Source={sqlitePath};Pooling=False"
            : TestDatabases.ConnectionString(provider)!;

    /// <summary>マイグレーションを当てた、その RDBMS の接続のファクトリ。</summary>
    protected DbConnectionFactory MigratedFactoryFor(DatabaseProvider provider)
    {
        TestDatabases.Migrate(provider, ConnectionStringFor(provider));
        return new DbConnectionFactory(provider, ConnectionStringFor(provider));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(sqlitePath);
        GC.SuppressFinalize(this);
    }
}

public sealed class TestDatabasesTests
{
    [Fact]
    public void CIでは全RDBMSが設定されている()
    {
        if (Environment.GetEnvironmentVariable(TestDatabases.RequireAllKey) != "1")
        {
            return;
        }

        var missing = Enum.GetValues<DatabaseProvider>().Where(p => !TestDatabases.IsEnabled(p)).ToList();
        Assert.True(missing.Count == 0, $"接続文字列が未設定の RDBMS がある（何も試さずに緑になる）: {string.Join(", ", missing)}");
    }
}
