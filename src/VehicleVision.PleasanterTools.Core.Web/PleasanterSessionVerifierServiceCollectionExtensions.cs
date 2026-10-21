using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace VehicleVision.PleasanterTools.Core.Web;

/// <summary><see cref="PleasanterSessionVerifier"/> を DI へ登録する。</summary>
public static class PleasanterSessionVerifierServiceCollectionExtensions
{
    /// <summary>
    /// <see cref="IPleasanterSessionVerifier"/> と、問い合わせに使う HttpClient（<see cref="PleasanterSessionVerifier.HttpClientName"/>）を登録する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ **HttpClient は cookie を覚えない・転送を追わない。** 覚えると、ある管理者の cookie が別の管理者の問い合わせへ混ざる
    /// （cookie は要求ごとに <c>Cookie</c> ヘッダで付ける）。転送を追うと、ログイン画面への転送などを成功と取り違える。
    /// **この設定を各アプリで書き写さないために、ここで登録する。**
    /// </para>
    /// <para>
    /// 時間切れは要求ごとに設定の値で掛ける（<see cref="IPleasanterSsoVerificationOptions.Timeout"/>）。HttpClient の側は無限にしておく。
    /// </para>
    /// <para>
    /// **<see cref="IPleasanterApiConnectionSource"/> はアプリが登録すること。** <see cref="TimeProvider"/> が登録されていれば、それを使う。
    /// </para>
    /// </remarks>
    public static IServiceCollection AddPleasanterSessionVerifier(
        this IServiceCollection services,
        PleasanterSessionVerifierSettings settings)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        services
            .AddHttpClient(PleasanterSessionVerifier.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                UseCookies = false,
                AllowAutoRedirect = false,
            });

        var category = string.IsNullOrWhiteSpace(settings.LoggerCategory)
            ? typeof(PleasanterSessionVerifier).FullName!
            : settings.LoggerCategory;

        services.AddSingleton<IPleasanterSessionVerifier>(provider => new PleasanterSessionVerifier(
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetRequiredService<IPleasanterApiConnectionSource>(),
            settings,
            provider.GetRequiredService<ILoggerFactory>().CreateLogger(category),
            provider.GetService<TimeProvider>()));

        return services;
    }
}
