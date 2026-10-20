namespace VehicleVision.PleasanterTools.Core.Data.Tests;

/// <summary>アプリ ID から DB の名前を導く規約。</summary>
/// <remarks>
/// **この規約を、アプリが 20〜30 個に増えても守れるように、機械で押さえる。**
/// 既存のアプリ（questionnaire・idp・atomfeed・calendarfeed）が手書きしていた値と、同じ名前になることも確かめる。
/// </remarks>
public sealed class AppDatabaseTests : IDisposable
{
    private readonly string contentRoot =
        Path.Combine(Path.GetTempPath(), $"core-appdb-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("questionnaire", "questionnaire.db", "QUESTIONNAIRE_", "questionnaire-startup-migrations")]
    [InlineData("idp", "idp.db", "IDP_", "idp-startup-migrations")]
    [InlineData("atomfeed", "atomfeed.db", "ATOMFEED_", "atomfeed-startup-migrations")]
    [InlineData("calendarfeed", "calendarfeed.db", "CALENDARFEED_", "calendarfeed-startup-migrations")]
    public void 名前はアプリ_ID_から規約で決まる(string appId, string sqliteFile, string prefix, string lockName)
    {
        var database = new AppDatabase(appId, typeof(AppDatabaseTests).Assembly);

        Assert.Equal(appId, database.AppId);
        Assert.Equal(sqliteFile, database.DefaultSqliteFileName);
        Assert.Equal(prefix, database.SettingPrefix);
        Assert.Equal(prefix + "DB_PROVIDER", database.ProviderSetting);
        Assert.Equal(prefix + "DB_CONNECTIONSTRING", database.ConnectionStringSetting);
        Assert.Equal(prefix + "DB_ALLOW_INSECURE", database.AllowInsecureSetting);
        Assert.Equal(lockName, database.MigrationLockName);
    }

    [Fact]
    public void ハイフンは設定名では下線になる()
    {
        var database = new AppDatabase("my-app", typeof(AppDatabaseTests).Assembly);

        Assert.Equal("my-app.db", database.DefaultSqliteFileName);
        Assert.Equal("MY_APP_DB_PROVIDER", database.ProviderSetting);
        Assert.Equal("my-app-startup-migrations", database.MigrationLockName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Questionnaire")]
    [InlineData("1app")]
    [InlineData("a_b")]
    [InlineData("a--b")]
    [InlineData("-a")]
    [InlineData("a-")]
    [InlineData("a b")]
    public void 規約に合わないアプリ_ID_は断る(string appId)
    {
        Assert.Throws<ArgumentException>(() => new AppDatabase(appId, typeof(AppDatabaseTests).Assembly));
    }

    [Fact]
    public void アプリ_ID_が違えばロック名も違う()
    {
        var first = new AppDatabase("alpha", typeof(AppDatabaseTests).Assembly);
        var second = new AppDatabase("beta", typeof(AppDatabaseTests).Assembly);

        Assert.NotEqual(first.MigrationLockName, second.MigrationLockName);
    }

    [Fact]
    public void RDBMS_の読み取りは_未設定なら既定_読めない値は止める()
    {
        var database = new AppDatabase("sample", typeof(AppDatabaseTests).Assembly);

        Assert.Equal(DatabaseProvider.Sqlite, database.ParseProvider(null, DatabaseProvider.Sqlite));
        Assert.Equal(DatabaseProvider.SqlServer, database.ParseProvider("  ", DatabaseProvider.SqlServer));
        Assert.Equal(DatabaseProvider.PostgreSql, database.ParseProvider(" postgresql ", DatabaseProvider.Sqlite));

        var exception = Assert.Throws<InvalidOperationException>(
            () => database.ParseProvider("oracle", DatabaseProvider.Sqlite));
        Assert.Contains("SAMPLE_DB_PROVIDER", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SQLite_の接続文字列は_App_Data_配下の既定のファイルになる()
    {
        var database = new AppDatabase("sample", typeof(AppDatabaseTests).Assembly);

        var connectionString = database.ResolveConnectionString(DatabaseProvider.Sqlite, null, contentRoot);

        Assert.Contains(Path.Combine(contentRoot, "App_Data", "sample.db"), connectionString, StringComparison.Ordinal);
        Assert.True(Directory.Exists(Path.Combine(contentRoot, "App_Data")));

        // 設定があれば、そのまま使う
        Assert.Equal("Data Source=x.db", database.ResolveConnectionString(DatabaseProvider.Sqlite, "Data Source=x.db", contentRoot));
    }

    [Fact]
    public void SQLite_以外で接続文字列が無ければ_設定名を示して止める()
    {
        var database = new AppDatabase("sample", typeof(AppDatabaseTests).Assembly);

        var exception = Assert.Throws<InvalidOperationException>(
            () => database.ResolveConnectionString(DatabaseProvider.PostgreSql, null, contentRoot));

        Assert.Contains("SAMPLE_DB_CONNECTIONSTRING", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 暗号化されていない接続は_緩める設定名を示して止める()
    {
        var database = new AppDatabase("sample", typeof(AppDatabaseTests).Assembly);
        const string insecure = "Server=db;Database=d;User Id=u;Password=p;Encrypt=False";

        var exception = Assert.Throws<InvalidOperationException>(
            () => database.EnsureSecure(DatabaseProvider.SqlServer, insecure, allowInsecure: false));
        Assert.Contains("SAMPLE_DB_ALLOW_INSECURE", exception.Message, StringComparison.Ordinal);

        // 緩める設定が true なら通る
        database.EnsureSecure(DatabaseProvider.SqlServer, insecure, allowInsecure: true);
    }

    public void Dispose()
    {
        if (Directory.Exists(contentRoot))
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }
}
