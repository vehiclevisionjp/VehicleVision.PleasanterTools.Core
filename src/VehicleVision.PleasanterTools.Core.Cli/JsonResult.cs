using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using VehicleVision.PleasanterTools.Core.Contracts;

namespace VehicleVision.PleasanterTools.Core.Cli;

/// <summary><c>--json</c> の枠（仕様書 3 章）を、ASCII の 1 行の JSON にする。</summary>
public static class JsonResult
{
    // 既定のエンコーダーの挙動に頼らず、印字可能な ASCII（0x20〜0x7E）だけをそのまま出し、
    // それ以外（非 ASCII・制御文字・DEL）は \uXXXX（サロゲートペアは 2 つ）にする
    private static readonly JavaScriptEncoder Encoder =
        JavaScriptEncoder.Create(new TextEncoderSettings(UnicodeRange.Create(' ', '~')));

    /// <summary>
    /// 枠と固有の項目を、ASCII の 1 行の JSON にする。項目の順は
    /// <c>command</c>・<c>ok</c>・<c>exitCode</c>・<c>errors</c>・<c>warnings</c>、そのあとに固有の項目（枠と同じ階層）。
    /// </summary>
    /// <param name="command">コマンド名。</param>
    /// <param name="ok">終了コードが 0 のとき真。<paramref name="exitCode"/> と食い違えば <see cref="ArgumentException"/>。</param>
    /// <param name="exitCode">終了コード。</param>
    /// <param name="errors">失敗・問題の一覧。</param>
    /// <param name="warnings">警告の一覧。</param>
    /// <param name="writeFields">固有の項目を書く。名前は camelCase で、枠の 5 項目の名前は使わない。</param>
    /// <param name="redact">秘密を伏せる関数。<c>message</c> と <c>path</c> に適用する。</param>
    public static string Serialize(
        string command,
        bool ok,
        int exitCode,
        IReadOnlyList<CliIssue> errors,
        IReadOnlyList<CliIssue> warnings,
        Action<Utf8JsonWriter>? writeFields = null,
        Func<string, string>? redact = null)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(warnings);
        if (ok != (exitCode == ExitCodeValues.Success))
        {
            throw new ArgumentException("ok must be true only when the exit code is 0.", nameof(ok));
        }

        return Write(writer =>
        {
            writer.WriteString(JsonKeys.Command, command);
            writer.WriteBoolean(JsonKeys.Ok, ok);
            writer.WriteNumber(JsonKeys.ExitCode, exitCode);
            WriteIssues(writer, JsonKeys.Errors, errors, redact);
            WriteIssues(writer, JsonKeys.Warnings, warnings, redact);
            writeFields?.Invoke(writer);
        });
    }

    /// <summary><c>--version --json</c> の枠（<c>command</c>・<c>ok</c>・<c>version</c>。決定 U15）。</summary>
    /// <param name="version">バージョン。</param>
    public static string SerializeVersion(string version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return Write(writer =>
        {
            writer.WriteString(JsonKeys.Command, "version");
            writer.WriteBoolean(JsonKeys.Ok, true);
            writer.WriteString(JsonKeys.Version, version);
        });
    }

    private static string Write(Action<Utf8JsonWriter> body)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = Encoder, Indented = false }))
        {
            writer.WriteStartObject();
            body(writer);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteIssues(Utf8JsonWriter writer, string name, IReadOnlyList<CliIssue> issues, Func<string, string>? redact)
    {
        writer.WriteStartArray(name);
        foreach (var issue in issues)
        {
            writer.WriteStartObject();
            writer.WriteString(JsonKeys.Code, issue.Code);
            writer.WriteString(JsonKeys.Message, redact is null ? issue.Message : redact(issue.Message));
            if (issue.Path is not null)
            {
                writer.WriteString(JsonKeys.Path, redact is null ? issue.Path : redact(issue.Path));
            }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }
}
