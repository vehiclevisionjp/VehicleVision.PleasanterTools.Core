using VehicleVision.PleasanterTools.Core.Cli.Testing;

namespace VehicleVision.PleasanterTools.Core.Data.Tests;

/// <summary>
/// <see cref="MigrationCommand"/> に CLI の契約テスト（Core.Cli.Testing）を当てる。
/// 現状の仕様とのずれは「既知の違反」として固定する。#175 で直したら、一覧から外す（外さないと失敗する）。
/// </summary>
[Collection("MigrationCommand console")]
public sealed class MigrationCommandContractTests : CliContractTests, IDisposable
{
    private const string Issue = "#175";

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

    // MigrationCommand は Console へ直接書くので、契約テストの stdout・stderr へ振り替えて包む
    private async Task<int> RunCommand(
        IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr, TextReader input, Func<string, string?> env, CancellationToken token)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            return await MigrationCommand.RunAsync(
                migrator, DatabaseProvider.Sqlite, connection ?? Fresh, [.. args], cancellationToken: token);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

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
            // 未適用ありは「検査の失敗」の 1 として扱う（仕様書 4 章との照合は #175 の未決）
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
        KnownViolations =
        [
            new KnownViolation(
                CliContractRules.Prefixes, "status-applied", "適用済みの要約に [OK] の接頭辞がない（仕様書 2.3）。", Issue),
            new KnownViolation(
                CliContractRules.Prefixes, "migrate-applies", "適用完了の要約に [OK] の接頭辞がない（仕様書 2.3）。", Issue),
            new KnownViolation(
                CliContractRules.Prefixes, "wait-timeout", "待機期限切れの失敗に [ERROR] の接頭辞がない（仕様書 2.3）。", Issue),
            new KnownViolation(
                CliContractRules.ExitCodes, "wait-timeout", "待機期限切れが 2。実行時の失敗なので 1（仕様書 4 章・4.1）。", Issue),
            new KnownViolation(
                CliContractRules.ArgumentForms, "wait-for-db: --migrate --wait-for-db 1",
                "--wait-for-db N（空白区切り）を受けない。=（イコール）の形だけ（仕様書 5.1・8 章）。", Issue),
        ],
    };

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(file);
        File.Delete(file + ".migration-lock.sqlite");
    }
}
