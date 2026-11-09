namespace VehicleVision.PleasanterTools.Core.Cli.Tests;

/// <summary>共通の API キーの読み方の試験（環境変数とファイルのどちらか片方）。</summary>
public sealed class APIキーの読み方Tests
{
    private const string Secret = "s3cret-key-value";

    private static Func<string, string?> Env(params (string Name, string Value)[] values)
    {
        var map = values.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);
        return name => map.GetValueOrDefault(name);
    }

    private static Func<string, string> Files(string content) => _ => content;

    [Fact]
    public void 環境変数のみならその値()
    {
        var key = PleasanterApiKey.Resolve(Env((PleasanterEnvironmentNames.ApiKey, Secret)), _ => throw new InvalidOperationException());
        Assert.Equal(Secret, key);
    }

    [Fact]
    public void ファイルのみならその内容()
    {
        string? readPath = null;
        var key = PleasanterApiKey.Resolve(
            Env((PleasanterEnvironmentNames.ApiKeyFile, "/run/key")),
            p =>
            {
                readPath = p;
                return Secret;
            });
        Assert.Equal(Secret, key);
        Assert.Equal("/run/key", readPath);
    }

    [Fact]
    public void ファイルの前後の空白と改行は除去する()
    {
        var key = PleasanterApiKey.Resolve(Env((PleasanterEnvironmentNames.ApiKeyFile, "k")), Files("  " + Secret + "\r\n\n"));
        Assert.Equal(Secret, key);
    }

    [Fact]
    public void どちらも未設定ならnull()
    {
        Assert.Null(PleasanterApiKey.Resolve(Env(), Files(Secret)));
        Assert.Null(PleasanterApiKey.Resolve(Env((PleasanterEnvironmentNames.ApiKey, ""), (PleasanterEnvironmentNames.ApiKeyFile, "")), Files(Secret)));
    }

    [Fact]
    public void 併用はエラーでキーを含めない()
    {
        var ex = Assert.Throws<UsageException>(() => PleasanterApiKey.Resolve(
            Env((PleasanterEnvironmentNames.ApiKey, Secret), (PleasanterEnvironmentNames.ApiKeyFile, "k")),
            Files(Secret)));
        Assert.Equal("Both VVPT_PLEASANTER_API_KEY and VVPT_PLEASANTER_API_KEY_FILE are set. Set only one.", ex.Message);
        Assert.DoesNotContain(Secret, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(typeof(FileNotFoundException))]
    [InlineData(typeof(DirectoryNotFoundException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(IOException))]
    public void 読めないときはパスだけを出すエラー(Type exceptionType)
    {
        var ex = Assert.Throws<UsageException>(() => PleasanterApiKey.Resolve(
            Env((PleasanterEnvironmentNames.ApiKeyFile, "/run/key")),
            _ => throw (Exception)Activator.CreateInstance(exceptionType)!));
        Assert.Equal("Cannot read the API key file '/run/key'.", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \r\n\t")]
    public void 空のファイルはエラー(string content)
    {
        var ex = Assert.Throws<UsageException>(() => PleasanterApiKey.Resolve(
            Env((PleasanterEnvironmentNames.ApiKeyFile, "/run/key")), Files(content)));
        Assert.Equal("The API key file '/run/key' is empty.", ex.Message);
    }

    [Fact]
    public void 実ファイルを既定で読む()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, Secret + Environment.NewLine);
            Assert.Equal(Secret, PleasanterApiKey.Resolve(Env((PleasanterEnvironmentNames.ApiKeyFile, path))));
            File.Delete(path);
            Assert.Throws<UsageException>(() => PleasanterApiKey.Resolve(Env((PleasanterEnvironmentNames.ApiKeyFile, path))));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
