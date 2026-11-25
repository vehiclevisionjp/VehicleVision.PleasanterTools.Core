using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VehicleVision.PleasanterTools.Core.Web.Testing;

/// <summary>
/// 試験用の仮想認証器。ブラウザと認証器の代わりに、サーバが出したオプションへ応答する
/// （<c>navigator.credentials.create / get</c> の戻りを JSON で作る）。
/// ES256 の鍵を持ち、attestation は <c>none</c>。<b>実機・実ブラウザの代わりではない</b>（#217 の範囲）。
/// </summary>
/// <remarks>
/// 不正な応答（UV の欠落・署名の改ざん・カウンターの逆行・origin の違い・ユーザーハンドルの違い）も作れる。
/// 暗号の検証は、サーバ側のライブラリが通すかを見るためのもので、ここでは検証しない。
/// </remarks>
public sealed class VirtualPasskeyAuthenticator
{
    private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private byte[]? userHandle;

    /// <summary>資格情報の ID（生のバイト列）。</summary>
    public byte[] CredentialId { get; } = RandomNumberGenerator.GetBytes(32);

    /// <summary>いまの署名カウンター。応答のたびに 1 増える（<see cref="UsesCounter"/> が偽なら常に 0）。</summary>
    public uint SignCount { get; set; }

    /// <summary>カウンターを持つ認証器か。偽なら常に 0（同期パスキーなどの実際の挙動）。</summary>
    public bool UsesCounter { get; set; } = true;

    /// <summary>登録時に立てる BE（同期できる）。</summary>
    public bool BackupEligible { get; set; }

    /// <summary>BS（いま同期されている）。</summary>
    public bool BackedUp { get; set; }

    /// <summary>認証器の種類の識別子。</summary>
    public Guid AaGuid { get; } = Guid.NewGuid();

    /// <summary>登録時にサーバが渡したユーザーハンドル（<c>user.id</c>）。</summary>
    public byte[]? UserHandle => userHandle;

    /// <summary>応答に付ける、不正の指定。</summary>
    public sealed record Tamper
    {
        /// <summary>UV フラグを立てるか（偽なら本人確認なしの成功）。</summary>
        public bool UserVerified { get; init; } = true;

        /// <summary>署名を壊すか（assertion のみ）。</summary>
        public bool BreakSignature { get; init; }

        /// <summary>clientDataJSON の origin を差し替える。</summary>
        public string? Origin { get; init; }

        /// <summary>この値を署名カウンターとして使う（逆行の試験）。</summary>
        public uint? SignCount { get; init; }

        /// <summary>ユーザーハンドルを差し替える（assertion のみ）。</summary>
        public byte[]? UserHandle { get; init; }

        /// <summary>BE を差し替える（assertion のみ）。</summary>
        public bool? BackupEligible { get; init; }
    }

    /// <summary>登録のオプション（サーバの JSON）へ応答する。戻りは、サーバへ送る資格情報の JSON。</summary>
    public string Create(string optionsJson, string origin, Tamper? tamper = null)
    {
        tamper ??= new Tamper();
        var options = JsonNode.Parse(optionsJson)!.AsObject();
        var challenge = options["challenge"]!.GetValue<string>();
        var rpId = options["rp"]!["id"]!.GetValue<string>();
        userHandle = Base64Url.DecodeFromChars(options["user"]!["id"]!.GetValue<string>());

        var counter = tamper.SignCount ?? 0;
        var flags = (byte)(0x01 | 0x40 | (tamper.UserVerified ? 0x04 : 0) | (BackupEligible ? 0x08 : 0) | (BackedUp ? 0x10 : 0));
        var authData = AuthenticatorData(rpId, flags, counter, includeCredential: true);
        var clientData = ClientData("webauthn.create", challenge, tamper.Origin ?? origin);

        return new JsonObject
        {
            ["id"] = Base64Url.EncodeToString(CredentialId),
            ["rawId"] = Base64Url.EncodeToString(CredentialId),
            ["type"] = "public-key",
            ["response"] = new JsonObject
            {
                ["attestationObject"] = Base64Url.EncodeToString(AttestationObject(authData)),
                ["clientDataJSON"] = Base64Url.EncodeToString(clientData),
                ["transports"] = new JsonArray("internal"),
            },
            ["clientExtensionResults"] = new JsonObject(),
        }.ToJsonString();
    }

    /// <summary>ログインのオプション（サーバの JSON）へ応答する。戻りは、サーバへ送る応答の JSON。</summary>
    public string Get(string optionsJson, string origin, Tamper? tamper = null)
    {
        tamper ??= new Tamper();
        var options = JsonNode.Parse(optionsJson)!.AsObject();
        var challenge = options["challenge"]!.GetValue<string>();
        var rpId = options["rpId"]!.GetValue<string>();

        uint counter;
        if (tamper.SignCount is { } forced)
        {
            counter = forced;
        }
        else if (UsesCounter)
        {
            counter = ++SignCount;
        }
        else
        {
            counter = 0;
        }

        var eligible = tamper.BackupEligible ?? BackupEligible;
        var flags = (byte)(0x01 | (tamper.UserVerified ? 0x04 : 0) | (eligible ? 0x08 : 0) | (BackedUp ? 0x10 : 0));
        var authData = AuthenticatorData(rpId, flags, counter, includeCredential: false);
        var clientData = ClientData("webauthn.get", challenge, tamper.Origin ?? origin);
        var signature = key.SignData([.. authData, .. SHA256.HashData(clientData)], HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);
        if (tamper.BreakSignature)
        {
            signature[^1] ^= 0xFF;
        }

        return new JsonObject
        {
            ["id"] = Base64Url.EncodeToString(CredentialId),
            ["rawId"] = Base64Url.EncodeToString(CredentialId),
            ["type"] = "public-key",
            ["response"] = new JsonObject
            {
                ["authenticatorData"] = Base64Url.EncodeToString(authData),
                ["clientDataJSON"] = Base64Url.EncodeToString(clientData),
                ["signature"] = Base64Url.EncodeToString(signature),
                ["userHandle"] = (tamper.UserHandle ?? userHandle) is { } handle ? Base64Url.EncodeToString(handle) : null,
            },
            ["clientExtensionResults"] = new JsonObject(),
        }.ToJsonString();
    }

    private static byte[] ClientData(string type, string challenge, string origin) =>
        JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["type"] = type,
            ["challenge"] = challenge,
            ["origin"] = origin,
            ["crossOrigin"] = false,
        });

    private byte[] AuthenticatorData(string rpId, byte flags, uint counter, bool includeCredential)
    {
        var data = new List<byte>();
        data.AddRange(SHA256.HashData(Encoding.UTF8.GetBytes(rpId)));
        data.Add(flags);
        data.AddRange([(byte)(counter >> 24), (byte)(counter >> 16), (byte)(counter >> 8), (byte)counter]);
        if (includeCredential)
        {
            data.AddRange(AaGuid.ToByteArray(bigEndian: true));
            data.AddRange([(byte)(CredentialId.Length >> 8), (byte)CredentialId.Length]);
            data.AddRange(CredentialId);
            data.AddRange(CoseKey());
        }

        return [.. data];
    }

    // COSE_Key（EC2・ES256・P-256）。CBOR を手で組む（試験の道具に、CBOR のライブラリを足さない）
    private byte[] CoseKey()
    {
        var parameters = key.ExportParameters(false);
        var cose = new List<byte> { 0xA5, 0x01, 0x02, 0x03, 0x26, 0x20, 0x01 };
        cose.AddRange([0x21, 0x58, 0x20]);
        cose.AddRange(parameters.Q.X!);
        cose.AddRange([0x22, 0x58, 0x20]);
        cose.AddRange(parameters.Q.Y!);
        return [.. cose];
    }

    private static byte[] AttestationObject(byte[] authData)
    {
        var cbor = new List<byte> { 0xA3 };
        cbor.AddRange(Text("fmt"));
        cbor.AddRange(Text("none"));
        cbor.AddRange(Text("attStmt"));
        cbor.Add(0xA0);
        cbor.AddRange(Text("authData"));
        cbor.AddRange([0x59, (byte)(authData.Length >> 8), (byte)authData.Length]);
        cbor.AddRange(authData);
        return [.. cbor];
    }

    private static byte[] Text(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return [(byte)(0x60 | bytes.Length), .. bytes];
    }
}
