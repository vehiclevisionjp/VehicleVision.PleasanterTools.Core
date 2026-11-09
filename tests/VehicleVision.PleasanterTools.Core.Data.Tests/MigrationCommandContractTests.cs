using VehicleVision.PleasanterTools.Core.Cli.Testing;

namespace VehicleVision.PleasanterTools.Core.Data.Tests;

/// <summary>
/// <see cref="MigrationCommand"/> に CLI の契約テスト（Core.Cli.Testing）を当てる。
/// 仕様とのずれは無い（既知の違反の一覧は空）。--help・--version・--json・--yes・中断（130）は持たない。
/// </summary>
public sealed class MigrationCommandContractTests : CliContractTests, IDisposable
{
    private readonly string file = Path.Combine(Path.GetTempPath(), $"core-contract-{Guid.NewGuid():N}.db");
    private readonly DatabaseMigrator migrator = new(typeof(DatabaseMigrator).Assembly, "test-contract-migrations");
    private string? connection;

    private string Fresh => $"Data Source={file};Pooling=False";

    // 存在しないファイルを読み取り専用で開く。外部の DB へは接続しない
    private string Unreachable => Fresh + ";Mode=ReadOnly";

    private Task Reset(string? useConnection = null, bool applied = false)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(file);
        File.Delete(file + ".migration-lock.sqlite");
        connection = useConnection ?? Fresh;
        if (applied)
        {
            migrator.MigrateUp(DatabaseProvider.Sqlite, connection);
        }
        return Task.CompletedTask;
    }

    private async Task<int> RunCommand(
        IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr, TextReader input, Func<string, string?> env, CancellationToken token) =>
        await MigrationCommand.RunAsync(
            migrator, DatabaseProvider.Sqlite, connection ?? Fresh, [.. args], cancellationToken: token,
            output: stdout, error: stderr);

    protected override CliContract CreateContract() => new()
    {
        Name = "MigrationCommand",
        Run = RunCommand,
        Scenarios =
        [
            new CliScenario
            {
                Name = "status-applied", Args = ["--migrate-status"], Outcome = CliOutcome.Success,
                RequiresOkLine = true, Arrange = () => Reset(applied: true),
            },
            // 未適用ありは「検査の失敗」の 1（仕様書 4 章）
            new CliScenario
            {
                Name = "status-pending", Args = ["--migrate-status"], Outcome = CliOutcome.CheckFailed,
                Arrange = () => Reset(),
            },
            new CliScenario
            {
                Name = "migrate-applies", Args = ["--migrate"], Outcome = CliOutcome.Success,
                RequiresOkLine = true, Arrange = () => Reset(),
            },
            new CliScenario
            {
                Name = "wait-timeout", Args = ["--migrate", "--wait-for-db=1"], Outcome = CliOutcome.Failed,
                Arrange = () => Reset(Unreachable),
            },
        ],
        ArgumentForms =
        [
            new CliArgumentForms
            {
                Name = "wait-for-db",
                Forms = [["--migrate", "--wait-for-db=1"], ["--migrate", "--wait-for-db", "1"]],
                Arrange = () => Reset(Unreachable),
            },
        ],
    };

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(file);
        File.Delete(file + ".migration-lock.sqlite");
    }
}
