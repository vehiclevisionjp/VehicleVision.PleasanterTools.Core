#!/usr/bin/env bash
# dotnet pack の成果物（*.nupkg）が、公開してよい形かを検査する。公開はしない。
# 使い方: bash ./scripts/verify-packages.sh <nupkg のあるディレクトリ> <期待する版>
#
# 確かめること（1 つでも外れたら非ゼロで終わる）
#   1. パッケージの集合が EXPECTED と一致する（足りない・余計なものがあれば失敗。シンボルパッケージも同じ）
#   2. すべて同じ版である（nuspec の version が引数と一致）
#   3. メタデータ（説明・作者・プロジェクト URL・リポジトリ・ライセンス・README）が入っている
#   4. 同じリポジトリのパッケージへの依存が、期待する組み合わせで、同じ版を指す
#   5. lib 配下に DLL が入っている
#
# パッケージを足す・外すときは、EXPECTED と EXPECTED_INTERNAL_DEPS を更新する
# （_documents/リリース手順書.md「パッケージの一覧」）。
set -euo pipefail

if [ "$#" -ne 2 ]; then
    echo "使い方: $0 <nupkg のあるディレクトリ> <期待する版>" >&2
    exit 2
fi
DIR=$1
VERSION=$2
PREFIX=VehicleVision.PleasanterTools.Core

# 期待するパッケージの集合（ID の接尾辞。接頭辞は PREFIX）
EXPECTED=(Mail Data Web Web.Testing Cli Contracts Cli.Testing)

# 同じリポジトリのパッケージへの依存（接尾辞。依存先をカンマで並べる。無ければ空）。
# Core.Data は Core.Cli を参照しない（_documents/コア化の方針.md）
declare -A EXPECTED_INTERNAL_DEPS=(
    [Mail]=""
    [Data]=""
    [Web]=""
    [Web.Testing]=""
    [Contracts]=""
    [Cli]="Contracts"
    [Cli.Testing]="Cli,Contracts"
)

fail=0
error() {
    echo "::error::$1"
    fail=1
}

# --- 1. 集合 ---
check_set() {
    local ext=$1 expected_sorted actual_sorted
    expected_sorted=$(for s in "${EXPECTED[@]}"; do echo "$PREFIX.$s.$VERSION.$ext"; done | sort)
    actual_sorted=$(cd "$DIR" && ls -1 ./*."$ext" 2>/dev/null | sed 's|^\./||' | sort || true)
    if [ "$expected_sorted" != "$actual_sorted" ]; then
        error "*.$ext の一覧が期待と違う。足りない・余計なものがある（期待: ${#EXPECTED[@]} 個）"
        echo "--- 期待"
        echo "$expected_sorted"
        echo "--- 実際"
        echo "$actual_sorted"
    fi
}
check_set nupkg
check_set snupkg

# --- 2〜5. 各パッケージ ---
for s in "${EXPECTED[@]}"; do
    id="$PREFIX.$s"
    file="$DIR/$id.$VERSION.nupkg"
    if [ ! -f "$file" ]; then
        continue # 集合の検査で報告済み
    fi
    nuspec=$(unzip -p "$file" '*.nuspec' | tr -d '\r')
    entries=$(unzip -Z1 "$file")

    tag() { printf '%s' "$nuspec" | sed -nE "s|.*<$1( [^>]*)?>([^<]*)</$1>.*|\2|p" | head -n 1; }

    [ "$(tag id)" = "$id" ] || error "$id: nuspec の id が違う: $(tag id)"
    [ "$(tag version)" = "$VERSION" ] || error "$id: 版が $VERSION ではない: $(tag version)"
    for t in authors description projectUrl license readme; do
        [ -n "$(tag $t)" ] || error "$id: メタデータ $t が空"
    done
    printf '%s' "$nuspec" | grep -q '<repository ' || error "$id: repository が無い"
    for f in "$(tag license)" "$(tag readme)"; do
        printf '%s\n' "$entries" | grep -qx "$f" || error "$id: $f が梱包されていない"
    done
    printf '%s\n' "$entries" | grep -Eq '^lib/[^/]+/[^/]+\.dll$' || error "$id: lib 配下に DLL が無い"

    # 同じリポジトリのパッケージへの依存（id=...version=... の組）
    actual_deps=$(printf '%s' "$nuspec" | grep -o "<dependency id=\"$PREFIX\.[^\"]*\" version=\"[^\"]*\"" \
        | sed -E "s|<dependency id=\"$PREFIX\.([^\"]*)\" version=\"([^\"]*)\"|\1 \2|" | sort || true)
    expected_deps=$(printf '%s' "${EXPECTED_INTERNAL_DEPS[$s]}" | tr ',' '\n' | { grep -v '^$' || true; } \
        | while read -r d; do echo "$d $VERSION"; done | sort)
    if [ "$actual_deps" != "$expected_deps" ]; then
        error "$id: 同じリポジトリのパッケージへの依存が期待と違う（同じ版を指すこと）"
        echo "--- 期待"
        echo "$expected_deps"
        echo "--- 実際"
        echo "$actual_deps"
    fi
done

if [ "$fail" -ne 0 ]; then
    exit 1
fi

echo "パッケージの検査に成功: ${#EXPECTED[@]} 個、すべて $VERSION"
for s in "${EXPECTED[@]}"; do
    echo "  $PREFIX.$s.$VERSION"
done
