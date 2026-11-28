using System.Xml.Linq;

namespace VehicleVision.PleasanterTools.Core.Security.Tests;

/// <summary>
/// Core.Security は、CLI（ASP.NET Core を持たないプロセス）からも参照できることが存在理由。
/// ASP.NET Core の共有フレームワークや外部パッケージへの依存が入り込んだら、ここで落とす。
/// </summary>
public sealed class AspNetCoreIndependenceTests
{
    [Fact]
    public void 参照するアセンブリにAspNetCoreが含まれない()
    {
        var references = typeof(SecretProtector).Assembly.GetReferencedAssemblies()
            .Select(name => name.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(references, name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.StartsWith("Microsoft.Extensions", StringComparison.Ordinal));
    }

    [Fact]
    public void プロジェクトはFrameworkReferenceもPackageReferenceもProjectReferenceも持たない()
    {
        var csproj = FindCsproj();
        var document = XDocument.Load(csproj);

        Assert.Empty(document.Descendants("FrameworkReference"));
        Assert.Empty(document.Descendants("PackageReference"));
        Assert.Empty(document.Descendants("ProjectReference"));
    }

    private static string FindCsproj()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src",
                "VehicleVision.PleasanterTools.Core.Security", "VehicleVision.PleasanterTools.Core.Security.csproj");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Core.Security の csproj が見つからない。");
    }
}
