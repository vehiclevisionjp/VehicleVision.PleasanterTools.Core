using System.Reflection;
using System.Text.RegularExpressions;
using FluentMigrator;

namespace VehicleVision.PleasanterTools.Core.Data.Tests;

/// <summary>Core の共有マイグレーションの規約（番号・表の接頭辞）を、機械で守らせる。</summary>
/// <remarks>
/// アプリは 4 桁（1〜9999）、<b>Core は 5 桁（10001〜99999）</b>、アプリの「Core より後」の移行は 6 桁（100000〜）。
/// 桁で範囲が分かれるので、アプリと衝突しない。表は <c>Core_</c> 接頭辞（アプリの表と衝突しない）。
/// </remarks>
public sealed partial class CoreMigrationRulesTests
{
    private static readonly Type[] CoreMigrations =
        [.. typeof(DatabaseMigrator).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<MigrationAttribute>() is not null)];

    [GeneratedRegex("Create\\.Table\\(\"(?<name>[^\"]+)\"\\)")]
    private static partial Regex CreateTable();

    [Fact]
    public void Coreの共有マイグレーションは_5桁の番号()
    {
        Assert.NotEmpty(CoreMigrations);
        foreach (var type in CoreMigrations)
        {
            var version = type.GetCustomAttribute<MigrationAttribute>()!.Version;
            Assert.True(version is >= 10001 and <= 99999, $"{type.Name} の番号 {version} は 10001〜99999 の範囲外");
            Assert.StartsWith($"M{version}_", type.Name, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Coreの共有マイグレーションの番号は重ならない()
    {
        var versions = CoreMigrations.Select(t => t.GetCustomAttribute<MigrationAttribute>()!.Version).ToList();

        Assert.Equal(versions.Count, versions.Distinct().Count());
    }

    [Fact]
    public void 共有表は_Core_接頭辞()
    {
        // ソースに書かれた表名を見る（Create.Table("…") の引数）
        var directory = Path.Combine(FindRepositoryRoot(), "src", "VehicleVision.PleasanterTools.Core.Data", "Migrations");
        var tables = Directory.GetFiles(directory, "*.cs")
            .SelectMany(f => CreateTable().Matches(File.ReadAllText(f)).Select(m => m.Groups["name"].Value))
            .ToList();

        Assert.NotEmpty(tables);
        Assert.All(tables, name => Assert.StartsWith("Core_", name, StringComparison.Ordinal));
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "global.json")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("リポジトリのルートが見つからない");
    }
}
