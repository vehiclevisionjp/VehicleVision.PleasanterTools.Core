using System.Globalization;

namespace VehicleVision.PleasanterTools.Core.Web;

internal static class AuthenticationSettingParser
{
    internal static string Trim(string? value) => value?.Trim() ?? string.Empty;

    internal static string ParseRole(string? value, string key, IReadOnlyList<string> roles, string defaultRole)
    {
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultRole);
        if (roles.Count == 0 || roles.Any(string.IsNullOrWhiteSpace)
            || !roles.Contains(defaultRole, StringComparer.Ordinal))
        {
            throw new ArgumentException("役割の一覧と、その一覧に含まれる既定の役割を指定してください。", nameof(roles));
        }

        var trimmed = Trim(value);
        return trimmed.Length == 0
            ? defaultRole
            : roles.FirstOrDefault(role => string.Equals(role, trimmed, StringComparison.OrdinalIgnoreCase))
                ?? throw UnknownValue(key, trimmed, string.Join(" / ", roles));
    }

    internal static TEnum? ParseEnum<TEnum>(string? value, string key, bool rejectNumeric = false)
        where TEnum : struct, Enum
    {
        var trimmed = Trim(value);
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (!Enum.TryParse<TEnum>(trimmed, ignoreCase: true, out var parsed)
            || !Enum.IsDefined(parsed)
            || (rejectNumeric && int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
        {
            throw UnknownValue(key, trimmed, string.Join(" / ", Enum.GetNames<TEnum>()));
        }

        return parsed;
    }

    internal static AuthenticationSettingException UnknownValue(string key, string value, string names) =>
        new(AuthenticationSettingError.UnknownValue,
            $"{key} に知らない値が入っています: {value}（使えるのは {names}）", [key, value, names]);

    internal static AuthenticationSettingException Required(string enabledKey, string key) =>
        new(AuthenticationSettingError.RequiredWhenEnabled,
            $"{enabledKey} を有効にしたときは {key} が要ります。", [enabledKey, key]);
}
