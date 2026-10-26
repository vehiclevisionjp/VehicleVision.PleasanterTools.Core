namespace VehicleVision.PleasanterTools.Core.Web.Tests;

public sealed class AdminPermissionsTests
{
    [Fact]
    public void 表を受け取り知らない役割は空で権限の大小を区別する()
    {
        var table = new Dictionary<string, IEnumerable<string>>
        {
            ["Owner"] = ["items.read", "items.write", "items.read"],
            ["Reader"] = ["items.read"],
        };
        var permissions = new AdminPermissions<string>(table, StringComparer.Ordinal);
        Assert.Equal(2, permissions.Of("Owner").Count);
        Assert.True(permissions.Has("Owner", "items.write"));
        Assert.False(permissions.Has("Reader", "items.write"));
        Assert.False(permissions.Has("Owner", "Items.Read"));
        Assert.Empty(permissions.Of("owner"));
        Assert.Empty(permissions.Of("unknown"));
        Assert.Empty(permissions.Of(null));
        table["Owner"] = ["elevated"];
        Assert.False(permissions.Has("Owner", "elevated"));
        Assert.Equal("Admin.Session.Permission:items.read", AdminPermissions<string>.PolicyOf("items.read"));
    }

    [Fact]
    public void 列挙の名前や数値の解釈はアプリに残し表は型付きで引ける()
    {
        var permissions = new AdminPermissions<int>(new Dictionary<int, IEnumerable<string>>
        {
            [1] = ["items.read"],
            [2] = ["items.read", "items.write"],
        });
        Assert.True(permissions.Has(2, "items.write"));
        Assert.False(permissions.Has(1, "items.write"));
        Assert.Empty(permissions.Of(99));
    }
}
