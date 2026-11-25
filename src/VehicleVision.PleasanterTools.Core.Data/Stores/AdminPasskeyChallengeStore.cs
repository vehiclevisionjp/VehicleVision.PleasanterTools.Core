using System.Security.Cryptography;
using Dapper;

namespace VehicleVision.PleasanterTools.Core.Data;

/// <summary>チャレンジの用途。<b>DB へ文字列で入る。</b></summary>
public static class PasskeyChallengePurposes
{
    /// <summary>登録（持ち主はログイン中の本人）。</summary>
    public const string Register = "register";

    /// <summary>ログイン（ディスコバラブル資格情報。問い合わせの時点では誰か分からない）。</summary>
    public const string SignIn = "signin";
}

/// <summary>パスキーのチャレンジの保管先。<b>一度限り・短い期限</b>で、サーバ側に持つ。</summary>
/// <remarks>
/// 保管先を RDBMS の表にしたのは、複数台が同じ DB を共有するため（発行した台と検証する台が違っても通る）。
/// Redis など別の保管先が要るアプリは、このインターフェースを実装して差し替えられる。
/// </remarks>
public interface IAdminPasskeyChallengeStore
{
    /// <summary>既定の有効期間（5 分。#213 の決定 7/9）。</summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(5);

    /// <summary>チャレンジを保管する。<b>DB には SHA-256 だけ</b>置く。</summary>
    /// <param name="challenge">チャレンジ（生のバイト列）。</param>
    /// <param name="purpose">用途（<see cref="PasskeyChallengePurposes"/>）。</param>
    /// <param name="userId">登録なら持ち主。ログインなら <c>null</c>。</param>
    /// <param name="lifetime">有効期間。</param>
    /// <param name="cancellationToken">キャンセル。</param>
    /// <returns>保管の完了。</returns>
    Task StoreAsync(
        byte[] challenge, string purpose, Guid? userId, TimeSpan lifetime, CancellationToken cancellationToken = default);

    /// <summary>
    /// チャレンジを消費する。<b>用途・持ち主が合い、期限内で、まだ使われていないときだけ <c>true</c>。</b>
    /// 1 文の DELETE の件数で決めるので、同じチャレンジを同時に 2 回通すことはできない。
    /// </summary>
    Task<bool> ConsumeAsync(byte[] challenge, string purpose, Guid? userId, CancellationToken cancellationToken = default);

    /// <summary>期限切れを消す。消した件数を返す。</summary>
    Task<int> DeleteExpiredAsync(CancellationToken cancellationToken = default);
}

/// <summary>パスキーのチャレンジを RDBMS へ保管する。表は <c>Core_AdminPasskeyChallenges</c>（M10008）。</summary>
/// <param name="connections">接続のファクトリ。</param>
/// <param name="time">現在時刻。<b>試験で固定できるよう、注入する。</b></param>
public sealed class AdminPasskeyChallengeStore(IDbConnectionFactory connections, TimeProvider time) : IAdminPasskeyChallengeStore
{
    private string Sql(string sql) => SqlDialect.Format(connections.Provider, sql);

    private DateTime Now() => DbTime.ForDb(time.GetUtcNow().UtcDateTime);

    private static string Hash(byte[] challenge) => Convert.ToHexStringLower(SHA256.HashData(challenge));

    public async Task StoreAsync(
        byte[] challenge, string purpose, Guid? userId, TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        if (challenge.Length == 0)
        {
            throw new ArgumentException("The challenge must not be empty.", nameof(challenge));
        }

        var now = time.GetUtcNow();
        await using var connection = connections.Create();
        await connection.ExecuteAsync(new CommandDefinition(
            Sql("INSERT INTO [Core_AdminPasskeyChallenges] ([ChallengeHash], [Purpose], [UserId], [CreatedAt], [ExpiresAt]) "
                + "VALUES (@Hash, @Purpose, @UserId, @CreatedAt, @ExpiresAt)"),
            new
            {
                Hash = Hash(challenge),
                Purpose = purpose,
                UserId = userId,
                CreatedAt = DbTime.ForDb(now.UtcDateTime),
                ExpiresAt = DbTime.ForDb((now + lifetime).UtcDateTime),
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<bool> ConsumeAsync(
        byte[] challenge, string purpose, Guid? userId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(challenge);

        await using var connection = connections.Create();
        return await connection.ExecuteAsync(new CommandDefinition(
            userId is null
                ? Sql("DELETE FROM [Core_AdminPasskeyChallenges] "
                    + "WHERE [ChallengeHash] = @Hash AND [Purpose] = @Purpose AND [UserId] IS NULL AND [ExpiresAt] > @Now")
                : Sql("DELETE FROM [Core_AdminPasskeyChallenges] "
                    + "WHERE [ChallengeHash] = @Hash AND [Purpose] = @Purpose AND [UserId] = @UserId AND [ExpiresAt] > @Now"),
            new { Hash = Hash(challenge), Purpose = purpose, UserId = userId, Now = Now() },
            cancellationToken: cancellationToken)).ConfigureAwait(false) > 0;
    }

    public async Task<int> DeleteExpiredAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        return await connection.ExecuteAsync(new CommandDefinition(
            Sql("DELETE FROM [Core_AdminPasskeyChallenges] WHERE [ExpiresAt] <= @Now"), new { Now = Now() },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }
}
