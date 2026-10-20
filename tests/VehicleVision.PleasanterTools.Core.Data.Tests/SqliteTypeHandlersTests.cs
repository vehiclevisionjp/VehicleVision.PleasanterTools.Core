using Dapper;

namespace VehicleVision.PleasanterTools.Core.Data.Tests;

/// <summary>アプリのモデル型を、SQLite の文字列・整数から組み立てられること。</summary>
/// <remarks>
/// **退行を押さえる。** Core.Data へ移したとき、型の対応表の登録が Core.Data のアセンブリだけを走査し、
/// アプリの record を Dapper が組み立てられなくなった
/// （<c>A parameterless default constructor or one matching signature … is needed</c>）。
/// Docker が要らないので、Core の CI で毎回試せる。
/// </remarks>
public sealed class SqliteTypeHandlersTests : IDisposable
{
    private readonly string databasePath =
        Path.Combine(Path.GetTempPath(), $"core-data-tests-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task アプリのモデルを文字列と整数から組み立てる()
    {
        // **ファクトリを先に作る**（SQLite の登録が先に済む）。アセンブリを知らせるのは、そのあと。
        // 登録済みのあとで知らせても、すぐ反映されること
        var factory = new DbConnectionFactory(
            DatabaseProvider.Sqlite,
            $"Data Source={databasePath}");
        SqliteTypeHandlers.AddModelAssemblies(typeof(Sample).Assembly);

        await using var connection = factory.Create();
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
            CREATE TABLE Sample (Id TEXT, At TEXT, Flag INTEGER, Color INTEGER, Name TEXT);
            INSERT INTO Sample VALUES ('0d6e3b3e-5b3a-4f3e-9d0b-2f3f3b1c9a10', '2026-10-20T01:02:03.0000000Z', 1, 2, 'abc');
            """);

        var sample = await connection.QuerySingleAsync<Sample>("SELECT Id, At, Flag, Color, Name FROM Sample");

        Assert.Equal(Guid.Parse("0d6e3b3e-5b3a-4f3e-9d0b-2f3f3b1c9a10"), sample.Id);
        Assert.Equal(new DateTime(2026, 10, 20, 1, 2, 3, DateTimeKind.Utc), sample.At.ToUniversalTime());
        Assert.True(sample.Flag);
        Assert.Equal(Shade.Dark, sample.Color);
        Assert.Equal("abc", sample.Name);
    }

    [Fact]
    public void 何度知らせても害がない()
    {
        SqliteTypeHandlers.AddModelAssemblies(typeof(Sample).Assembly);
        SqliteTypeHandlers.AddModelAssemblies(typeof(Sample).Assembly, typeof(DatabaseProvider).Assembly);
        SqliteTypeHandlers.Register();
        SqliteTypeHandlers.Register();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>試験用のモデル。名前つきコンストラクタを持つ record（アプリの SurveyDraft などと同じ形）。</summary>
    public sealed record Sample(Guid Id, DateTime At, bool Flag, Shade Color, string Name);

    /// <summary>試験用の列挙型。</summary>
    public enum Shade
    {
        Light = 1,
        Dark = 2,
    }
}
