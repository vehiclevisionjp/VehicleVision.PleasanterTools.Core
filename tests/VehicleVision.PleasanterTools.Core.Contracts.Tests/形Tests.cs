using System.Reflection;

namespace VehicleVision.PleasanterTools.Core.Contracts.Tests;

/// <summary>Contracts が「定数と読み取り専用の対応表だけで、処理を持たない」ままであることを守る。</summary>
public sealed class 形Tests
{
    private static readonly Assembly ContractsAssembly = typeof(PleasanterEnvironmentNames).Assembly;

    [Fact]
    public void 公開の型はすべて静的クラス()
    {
        Assert.All(ContractsAssembly.GetExportedTypes(), t => Assert.True(t.IsAbstract && t.IsSealed, t.FullName));
    }

    [Fact]
    public void 公開のメソッドは読み取りのプロパティの取得子だけ()
    {
        var methods = ContractsAssembly.GetExportedTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(m => !m.IsSpecialName || !m.Name.StartsWith("get_", StringComparison.Ordinal))
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}");
        Assert.Empty(methods);
    }

    [Fact]
    public void 参照するアセンブリは標準ライブラリだけ()
    {
        var names = ContractsAssembly.GetReferencedAssemblies().Select(a => a.Name!).ToArray();
        Assert.All(names, n => Assert.True(
            n == "System.Runtime" || n == "System.Collections" || n == "netstandard" || n.StartsWith("System.", StringComparison.Ordinal),
            n));
    }
}
