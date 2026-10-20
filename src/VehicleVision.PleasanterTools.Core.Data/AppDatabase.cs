using System.Reflection;
using System.Text.RegularExpressions;

namespace VehicleVision.PleasanterTools.Core.Data;

/// <summary>
/// アプリ ID から、DB の名前（設定名・ファイル名・ロック名）を<b>規約で導く</b>。
/// </summary>
/// <remarks>
/// <para>
/// アプリごとに、これらの名前を手書きしない。書き写しは、直した側と直していない側を生み、アプリが増えるほど
/// 取り違える（20〜30 個に増えても、調整や手書きが増えないようにする）。
/// <b>アプリ ID とマイグレーションのアセンブリを渡せば、残りは規約で決まる。</b>
/// </para>
/// <para>
/// 規約（<c>&lt;id&gt;</c> はアプリ ID、<c>&lt;ID&gt;</c> はその大文字。ハイフンは下線にする）:
/// </para>
/// <list type="table">
/// <listheader><term>値</term><description>規約</description></listheader>
/// <item><term>SQLite の既定ファイル名</term><description><c>&lt;id&gt;.db</c></description></item>
/// <item><term>RDBMS を選ぶ設定名</term><description><c>&lt;ID&gt;_DB_PROVIDER</c></description></item>
/// <item><term>接続文字列の設定名</term><description><c>&lt;ID&gt;_DB_CONNECTIONSTRING</c></description></item>
/// <item><term>暗号化を緩める設定名</term><description><c>&lt;ID&gt;_DB_ALLOW_INSECURE</c></description></item>
/// <item><term>マイグレーションの排他のロック名</term><description><c>&lt;id&gt;-startup-migrations</c></description></item>
/// </list>
/// <para>
/// <b>ロック名はアプリごとに別にする。</b> 同じ DB サーバを複数のアプリが使うとき、名前が同じだと、
/// 別のアプリのマイグレーションを待たせる。アプリ ID が一意なら、ロック名も衝突しない。
/// </para>
/// <para>
/// 使い方: アプリのデータ層に 1 つだけ持つ。
/// </para>
/// <code>
/// public static class QuestionnaireDatabase
/// {
///     public static AppDatabase Instance { get; } = new("questionnaire", typeof(QuestionnaireDatabase).Assembly);
///
///     // アセンブリの読み込み時に作る（SQLite のモデル型の登録が、必ず先に済む）
///     [ModuleInitializer]
///     internal static void Initialize() => _ = Instance;
/// }
/// </code>
/// <para>
/// メールの環境変数の接頭辞（<c>&lt;ID&gt;_MAIL_</c>）は、Core.Mail と Core.Data が互いを参照しないため、ここには含めない。
/// </para>
/// </remarks>
public sealed partial class AppDatabase
{
    /// <summary>排他ロックを待つ長さの既定。マイグレーション自体は短いが、大きい DB の変更を待つことがある。</summary>
    public static readonly TimeSpan DefaultMigrationLockTimeout = TimeSpan.FromMinutes(5);

    /// <summary>アプリ ID の形。小文字の英字で始まり、英数字とハイフン（連続しない）だけ。</summary>
    [GeneratedRegex("^[a-z][a-z0-9]*(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex AppIdPattern();

    /// <summary>名前を導いて作る。</summary>
    /// <param name="appId">アプリ ID（例: <c>questionnaire</c>）。<b>一意にする。</b> 小文字の英字で始まり、英数字とハイフンだけ。</param>
    /// <param name="migrationsAssembly">FluentMigrator のマイグレーションを持つアセンブリ。</param>
    /// <param name="additionalModelAssemblies">
    /// マイグレーションのアセンブリのほかに、SQLite のモデル型（列挙型・record）を持つアセンブリ。
    /// マイグレーションのアセンブリ自身は、渡さなくても対象になる。
    /// </param>
    /// <remarks>
    /// 作るときに、モデル型のアセンブリを SQLite の型の対応表へ知らせる（<see cref="SqliteTypeHandlers.AddModelAssemblies"/>）。
    /// 実際の登録は SQLite の接続を作るときだけで、ほかの RDBMS の挙動は変わらない。
    /// </remarks>
    public AppDatabase(
        string appId,
        Assembly migrationsAssembly,
        params Assembly[] additionalModelAssemblies)
    {
        ArgumentNullException.ThrowIfNull(appId);
        ArgumentNullException.ThrowIfNull(migrationsAssembly);
        ArgumentNullException.ThrowIfNull(additionalModelAssemblies);

        if (!AppIdPattern().IsMatch(appId))
        {
            throw new ArgumentException(
                $"アプリ ID '{appId}' は使えません。小文字の英字で始まり、小文字の英数字とハイフン（連続しない）だけで書くこと（例: questionnaire）。",
                nameof(appId));
        }

        AppId = appId;
        var upper = appId.ToUpperInvariant().Replace('-', '_');
        SettingPrefix = upper + "_";
        DefaultSqliteFileName = appId + ".db";
        ProviderSetting = SettingPrefix + "DB_PROVIDER";
        ConnectionStringSetting = SettingPrefix + "DB_CONNECTIONSTRING";
        AllowInsecureSetting = SettingPrefix + "DB_ALLOW_INSECURE";
        MigrationLockName = appId + "-startup-migrations";

        Migrator = new DatabaseMigrator(migrationsAssembly, MigrationLockName);
        SqliteTypeHandlers.AddModelAssemblies([migrationsAssembly, .. additionalModelAssemblies]);
    }

    /// <summary>アプリ ID。</summary>
    public string AppId { get; }

    /// <summary>設定名の接頭辞（例: <c>QUESTIONNAIRE_</c>）。</summary>
    public string SettingPrefix { get; }

    /// <summary>SQLite の既定ファイル名（<c>&lt;id&gt;.db</c>）。</summary>
    public string DefaultSqliteFileName { get; }

    /// <summary>RDBMS を選ぶ設定名（<c>&lt;ID&gt;_DB_PROVIDER</c>）。</summary>
    public string ProviderSetting { get; }

    /// <summary>接続文字列を与える設定名（<c>&lt;ID&gt;_DB_CONNECTIONSTRING</c>）。</summary>
    public string ConnectionStringSetting { get; }

    /// <summary>暗号化の検査を緩める設定名（<c>&lt;ID&gt;_DB_ALLOW_INSECURE</c>）。</summary>
    public string AllowInsecureSetting { get; }

    /// <summary>起動時のマイグレーションの排他に使う名前（<c>&lt;id&gt;-startup-migrations</c>）。</summary>
    public string MigrationLockName { get; }

    /// <summary>このアプリのマイグレーションを適用する。</summary>
    public DatabaseMigrator Migrator { get; }

    /// <summary>設定値から RDBMS を読む。未設定は <paramref name="defaultProvider"/>。読めない値は黙って落とさず止める。</summary>
    public DatabaseProvider ParseProvider(string? value, DatabaseProvider defaultProvider)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultProvider;
        }

        return Enum.TryParse<DatabaseProvider>(value.Trim(), ignoreCase: true, out var provider)
            && Enum.IsDefined(provider)
            ? provider
            : throw new InvalidOperationException(
                $"{ProviderSetting} の値 '{value}' は対応していません。"
                + $"{string.Join(" / ", Enum.GetNames<DatabaseProvider>())} のいずれかで指定してください。");
    }

    /// <summary>接続文字列を解決する。SQLite だけは、<c>App_Data</c> 配下の既定のファイルを使う。</summary>
    public string ResolveConnectionString(
        DatabaseProvider provider,
        string? configured,
        string contentRootPath) =>
        DatabaseConnectionString.Resolve(
            provider,
            configured,
            contentRootPath,
            DefaultSqliteFileName,
            ConnectionStringSetting);

    /// <summary>接続が暗号化されているかを見る。緩める設定が <c>true</c> のときだけ許す。</summary>
    public void EnsureSecure(DatabaseProvider provider, string connectionString, bool allowInsecure) =>
        ConnectionSecurity.EnsureSecure(provider, connectionString, allowInsecure, AllowInsecureSetting);
}

/// <summary>
/// 解決済みの DB の接続先（RDBMS と接続文字列）。
/// マイグレーションと準備の確認が、接続文字列そのものを要るので、解決した結果を 1 か所で持つ
/// （接続のファクトリは、接続文字列を公開しない）。
/// </summary>
public sealed record DatabaseConnectionInfo(DatabaseProvider Provider, string ConnectionString);
