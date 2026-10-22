using System.Collections.Frozen;

namespace VehicleVision.PleasanterTools.Core.Web;

/// <summary>役割と権限の表。内容と役割名の解釈はアプリが担う。</summary>
public sealed class AdminPermissions<TRole> where TRole : notnull
{
    private readonly FrozenDictionary<TRole, FrozenSet<string>> byRole;

    public AdminPermissions(IEnumerable<KeyValuePair<TRole, IEnumerable<string>>> permissions,
        IEqualityComparer<TRole>? roleComparer = null)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        byRole = permissions.ToFrozenDictionary(pair => pair.Key,
            pair => pair.Value.ToFrozenSet(StringComparer.Ordinal), roleComparer);
    }

    public IReadOnlyCollection<string> Of(TRole? role) =>
        role is not null && byRole.TryGetValue(role, out var permissions) ? permissions : FrozenSet<string>.Empty;

    public bool Has(TRole? role, string permission) => Of(role).Contains(permission);

    public static string PolicyOf(string permission) => $"Admin.Session.Permission:{permission}";
}
