using System.Data;
using System.Globalization;
using System.Reflection;
using Dapper;

namespace VehicleVision.PleasanterTools.Core.Data;

/// <summary>SQLite が文字列・整数で返す値を、Dapper のモデル型へ戻す。</summary>
/// <remarks>
/// <para>
/// 登録するのは、<b>列挙型</b>と、<b>名前つきコンストラクタを持つ型（record など）</b>。
/// これらは、走査したアセンブリの中にあるものだけが対象になる。
/// </para>
/// <para>
/// <b>アプリのモデル型があるアセンブリは、アプリが <see cref="AddModelAssemblies"/> で知らせる。</b>
/// Core.Data のアセンブリだけを走査すると、アプリのモデルを Dapper が組み立てられない
/// （<c>A parameterless default constructor or one matching signature … is needed</c>）。
/// 知らせるのは 1 回でよい（アセンブリの読み込み時に呼ぶ <c>[ModuleInitializer]</c> が向く）。
/// </para>
/// <para>
/// <b>SQLite のときだけ効く。</b> 実際の登録は、SQLite の接続を作るとき（<see cref="DbConnectionFactory"/>）に行う。
/// ほかの RDBMS の挙動は変えない。
/// </para>
/// </remarks>
public static class SqliteTypeHandlers
{
    private static readonly object Sync = new();
    private static readonly HashSet<Assembly> KnownAssemblies = [typeof(DatabaseProvider).Assembly];
    private static readonly HashSet<Assembly> RegisteredAssemblies = [];
    private static bool _active;

    /// <summary>アプリのモデル型があるアセンブリを知らせる。何度呼んでもよい。</summary>
    /// <remarks>
    /// SQLite の登録がすでに済んでいれば、すぐ反映する。まだなら、登録するときに反映する。
    /// </remarks>
    public static void AddModelAssemblies(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        lock (Sync)
        {
            foreach (var assembly in assemblies)
            {
                KnownAssemblies.Add(assembly);
            }

            if (_active)
            {
                RegisterKnown();
            }
        }
    }

    /// <summary>SQLite 用の型の対応表を登録する。<see cref="DbConnectionFactory"/> が呼ぶ。</summary>
    public static void Register()
    {
        lock (Sync)
        {
            if (!_active)
            {
                SqlMapper.AddTypeHandler(new GuidHandler());
                SqlMapper.AddTypeHandler(new DateTimeHandler());
                SqlMapper.AddTypeHandler(new BooleanHandler());
                _active = true;
            }

            RegisterKnown();
        }
    }

    private static void RegisterKnown()
    {
        foreach (var assembly in KnownAssemblies.ToArray())
        {
            if (!RegisteredAssemblies.Add(assembly))
            {
                continue;
            }

            foreach (var enumType in assembly.GetTypes().Where(type => type.IsEnum))
            {
                var handlerType = typeof(EnumHandler<>).MakeGenericType(enumType);
                SqlMapper.AddTypeHandler(
                    enumType,
                    (SqlMapper.ITypeHandler)Activator.CreateInstance(handlerType)!);
            }

            foreach (var type in assembly.GetTypes().Where(HasNamedConstructor))
            {
                SqlMapper.SetTypeMap(type, new ConvertingConstructorTypeMap(type));
            }
        }
    }

    private static bool HasNamedConstructor(Type type)
    {
        var propertyNames = type.GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return type.GetConstructors().Any(constructor =>
            constructor.GetParameters() is { Length: > 0 } parameters
            && parameters.All(parameter =>
                parameter.Name is not null && propertyNames.Contains(parameter.Name)));
    }

    private sealed class ConvertingConstructorTypeMap(Type type) : SqlMapper.ITypeMap
    {
        private readonly DefaultTypeMap _default = new(type);

        public System.Reflection.ConstructorInfo? FindConstructor(
            string[] names,
            Type[] types) =>
            _default.FindConstructor(names, types)
            ?? type.GetConstructors().FirstOrDefault(constructor =>
            {
                var parameters = constructor.GetParameters();
                return parameters.Length == names.Length
                    && parameters.Select(parameter => parameter.Name)
                        .SequenceEqual(names, StringComparer.OrdinalIgnoreCase);
            });

        public System.Reflection.ConstructorInfo? FindExplicitConstructor() =>
            _default.FindExplicitConstructor();

        public SqlMapper.IMemberMap? GetConstructorParameter(
            System.Reflection.ConstructorInfo constructor,
            string columnName) =>
            _default.GetConstructorParameter(constructor, columnName);

        public SqlMapper.IMemberMap? GetMember(string columnName) =>
            _default.GetMember(columnName);
    }

    private sealed class GuidHandler : SqlMapper.TypeHandler<Guid>
    {
        public override Guid Parse(object value) => value switch
        {
            Guid guid => guid,
            byte[] bytes => new Guid(bytes),
            _ => Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!),
        };

        public override void SetValue(IDbDataParameter parameter, Guid value) =>
            parameter.Value = value;
    }

    private sealed class DateTimeHandler : SqlMapper.TypeHandler<DateTime>
    {
        public override DateTime Parse(object value) => value switch
        {
            DateTime dateTime => dateTime,
            _ => DateTime.Parse(
                Convert.ToString(value, CultureInfo.InvariantCulture)!,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind),
        };

        public override void SetValue(IDbDataParameter parameter, DateTime value) =>
            parameter.Value = value;
    }

    private sealed class BooleanHandler : SqlMapper.TypeHandler<bool>
    {
        public override bool Parse(object value) =>
            Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0;

        public override void SetValue(IDbDataParameter parameter, bool value) =>
            parameter.Value = value;
    }

    private sealed class EnumHandler<TEnum> : SqlMapper.TypeHandler<TEnum>
        where TEnum : struct, Enum
    {
        public override TEnum Parse(object value) =>
            (TEnum)Enum.ToObject(
                typeof(TEnum),
                Convert.ToInt64(value, CultureInfo.InvariantCulture));

        public override void SetValue(IDbDataParameter parameter, TEnum value) =>
            parameter.Value = Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }
}
