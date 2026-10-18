#!/usr/bin/env bash
# NOTICE の依存一覧が、実際の依存と一致しているかを検査する。
#
# **デュアルライセンス（AGPL-3.0-or-later OR 商用）の前提を機械で守るための検査。**
# 依存が寛容ライセンス（MIT / BSD 系 / Apache-2.0）だけであることは人が確かめるしかないが、
# 「NOTICE に載っていない依存がある」「版数がずれている」は機械で見つけられる。
#
# 検査するもの
#   - Directory.Packages.props の PackageVersion（Include と Version）
#     **Core は Central Package Management のため、版は .csproj ではなくここにある。**
# NOTICE の該当行が「名称 バージョン / ライセンス / 入手元」の形であることを前提に、
# 「名称 バージョン /」を含む行があるかを見る。
#
# **推移的な依存までは見ない。** NOTICE は直接採用したものを記す文書であり、
# 推移的な依存は上流の NOTICE が受け持つ。脆弱性の方は推移的な依存も見ている
# （scripts/nuget-vulnerable-check.sh）。
set -euo pipefail
cd "$(dirname "$0")/.."

NOTICE_FILE="NOTICE"
PACKAGES_PROPS="Directory.Packages.props"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

fail=0

# 「名称<TAB>版」で書き出す
grep -hoE '<PackageVersion[^>]*Include="[^"]+"[^>]*Version="[^"]+"' "$PACKAGES_PROPS" \
    | sed -E 's/.*Include="([^"]+)".*Version="([^"]+)".*/\1\t\2/' \
    | sort -u > "$work/nuget.tsv"

echo "== NuGet の直接依存が NOTICE に載っているか =="
if [ ! -s "$work/nuget.tsv" ]; then
    echo "NG: 依存を 1 つも拾えなかった。抽出のしかたが実物と合っていない" >&2
    exit 1
fi

while IFS=$'\t' read -r name version; do
    [ -n "$name" ] && [ -n "$version" ] || continue
    if grep -qF "${name} ${version} /" "$NOTICE_FILE"; then
        printf '  OK   %s %s\n' "$name" "$version"
    else
        printf '  NG   %s %s が NOTICE に見当たらない\n' "$name" "$version" >&2
        fail=1
    fi
done < "$work/nuget.tsv"

echo
if [ "$fail" -ne 0 ]; then
    echo "NG: NOTICE と実際の依存が食い違っている" >&2
    echo "    NOTICE へ「名称 バージョン / ライセンス / 入手元」の行を足すか、版数を直すこと。" >&2
    echo "    **寛容ライセンス（MIT / BSD 系 / Apache-2.0）以外は採用しないこと**（LICENSING.md）。" >&2
else
    echo "OK: NOTICE の依存一覧は実際の依存と一致している"
fi

exit "$fail"
