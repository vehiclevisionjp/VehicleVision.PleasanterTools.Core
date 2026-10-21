using System.Collections.Immutable;

namespace VehicleVision.PleasanterTools.Core.Web;

/// <summary>
/// <see cref="PleasanterSessionVerifier"/> が読む、Pleasanter のログイン（SSO）の設定。
/// </summary>
/// <remarks>
/// <para>
/// **設定の読み取り（環境変数の名前・既定値・誤りの伝え方）はアプリに残す。** 鍵の名前はアプリ名を含み、
/// 誤りの伝え方（多言語の文言の鍵を持つ例外か、日本語の <see cref="InvalidOperationException"/> か）も
/// アプリごとに違うため（<c>_documents/Core.Web-共通化の方針.md</c>）。
/// </para>
/// <para>
/// 各アプリの <c>PleasanterSsoOptions</c> は、ここに並ぶ名前と型をすでに持っている。
/// **クラスの宣言にこのインターフェイスを足すだけで渡せる。**
/// </para>
/// </remarks>
public interface IPleasanterSsoVerificationOptions
{
    /// <summary>Pleasanter のログインを使うか。**使わないなら問い合わせない。**</summary>
    bool Enabled { get; }

    /// <summary>本アプリのサーバから Pleasanter へ問い合わせるときの URL（サーバ間）。**末尾は <c>/</c>。**</summary>
    Uri? InternalBaseUrl { get; }

    /// <summary>問い合わせ全体（代わりの経路・所属の確認を含む）を待つ長さ。</summary>
    TimeSpan Timeout { get; }

    /// <summary>許可する組織の ID。組織・グループのどちらにも指定が無ければ、所属は確かめない。</summary>
    ImmutableArray<int> AllowedDeptIds { get; }

    /// <summary>許可するグループの ID（子グループも含めてたどる）。</summary>
    ImmutableArray<int> AllowedGroupIds { get; }

    /// <summary>この名前の cookie を Pleasanter へ転送してよいか。</summary>
    /// <remarks>⚠️ **本アプリ自身の cookie には <c>false</c> を返すこと。** 本アプリのセッションを Pleasanter へ渡さない。</remarks>
    bool ShouldForwardCookie(string name);
}
