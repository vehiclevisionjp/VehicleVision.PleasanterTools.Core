# Claude向けリポジトリ指示

このリポジトリで作業する場合は、共通のリポジトリ指示に従ってください。

@.github/copilot-instructions.md

パスに紐づく詳細な規約は `.github/instructions/*.instructions.md` に分かれています。
**そのパスのファイルを触る前に、該当するものを読んでください。**
Claude Code は `applyTo` を自動で解釈しないため、自分で開く必要があります。

- [ドキュメント](.github/instructions/ドキュメント.instructions.md) — `_documents/**`

これらの詳細ルールは上記Copilot指示および各 instructions ファイルを単一の参照元とし、
このファイルへ複製しないでください。

## このリポジトリ固有の注意

- **ここはアプリではなく、各アプリが参照する共通部品です。** 1 行の変更が
  AtomFeed / CalendarFeed / IdentityProvider / Questionnaire などの全アプリへ効きます。
  変更の前に [`_documents/コア化の方針.md`](_documents/コア化の方針.md) の「Core に置くもの・置かないもの」を読むこと
- **アプリ名やアプリ固有の値（環境変数の接頭辞・ファイル名・ロック名など）を書かないこと。**
  引数で受けます
- 公開 API を変えたら、**参照している各アプリが追随できているか**を確認し、Issue に残すこと

## 共有メモリリポジトリ

このリポジトリと、各アプリを Core へ集約する作業で得た知見は、組織内部（internal）の
共有メモリリポジトリで共有します。作業ツリーはハーネスのメモリディレクトリそのものです。

```text
~/.claude/projects/d--repos-VehicleVision-PleasanterTools-Core/memory/
```

- **別途 clone を作らないこと。** 2 つあると必ず片方が古くなる
- セッション開始時に `git -C <memory> pull --rebase` で他の人の追記を取り込むこと
- メモリを追加・更新したら `MEMORY.md` の索引も更新し、コミットして push すること
  （本体リポジトリと違い、push まで指示を待たなくてよい）
- **このリポジトリは Public。** 共有メモリの場所・URL・中身をここへ書き写さないこと

なお共有メモリは正式な規約の置き場所ではありません。このファイルと
`.github/copilot-instructions.md` が常に優先され、食い違ったらメモリ側を直します。

### 本リポジトリの規約を変えたとき

**共有メモリに古くなった記述が無いか必ず確認すること。** 規約変更はメモリを黙って矛盾させます。
