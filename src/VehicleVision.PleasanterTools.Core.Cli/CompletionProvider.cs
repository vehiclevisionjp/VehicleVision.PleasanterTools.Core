namespace VehicleVision.PleasanterTools.Core.Cli;

/// <summary>
/// 動的な値（<see cref="OptionValueCompletion.Dynamic"/>）の候補を返す（仕様書 5.6）。
/// 例外・取り消し・時間超過のときは、候補を出さずに戻る（エラーも出さない）。
/// 秘密（接続文字列など）を候補に含めないこと。
/// </summary>
/// <param name="optionName">補完するオプションの名前（接頭辞なし。例: <c>table</c>）。</param>
/// <param name="currentWord">入力中の語（先頭から入力済みの部分。空のこともある）。</param>
/// <param name="cancellationToken">時間超過・Ctrl+C の合図。</param>
public delegate Task<IReadOnlyList<string>> CompletionProvider(
    string optionName, string currentWord, CancellationToken cancellationToken);
