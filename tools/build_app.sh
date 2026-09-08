#!/bin/bash
# ネイティブ依存の配置を維持し、配布先の .NET インストールを不要にする。
set -eu
set -o pipefail

usage() {
  cat <<'USAGE'
使い方: bash tools/build_app.sh <osx-arm64|osx-x64|win-x64|win-arm64> [バージョン] [ビルド番号]
例: bash tools/build_app.sh osx-arm64 1.0.0 1
バージョンは既定 1.0.0、ビルド番号は既定 1。出力先は artifacts/。
macOS バンドルの生成には macOS が必要。Windows ZIP は macOS / Linux / Git Bash で生成できる。
USAGE
}

fail() {
  printf '%s\n' "$*" >&2
  exit 1
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "必要なコマンドがありません: $1"
}

if [ "${1:-}" = "--help" ] || [ "${1:-}" = "-h" ]; then
  usage
  exit 0
fi
if [ "$#" -lt 1 ] || [ "$#" -gt 3 ]; then
  usage >&2
  exit 1
fi

RUNTIME="$1"
VERSION="${2:-1.0.0}"
BUILD_NUMBER="${3:-1}"
case "$RUNTIME" in
  osx-arm64|osx-x64|win-x64|win-arm64) ;;
  *) fail "未対応のランタイムです: $RUNTIME" ;;
esac
# plist への展開と .NET のバージョン指定を、同じ数値形式に制限する。
[[ "$VERSION" =~ ^(0|[1-9][0-9]{0,3})\.(0|[1-9][0-9]{0,3})\.(0|[1-9][0-9]{0,3})$ ]] \
  || fail "バージョンは 0〜9999 の整数 3 個（例: 1.0.0）で指定してください。"
[[ "$BUILD_NUMBER" =~ ^[1-9][0-9]{0,3}$ ]] \
  || fail "ビルド番号は 1〜9999 で指定してください。"

SCRIPT_DIRECTORY="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIRECTORY="$(cd "$SCRIPT_DIRECTORY/.." && pwd)"
ARTIFACTS="$PROJECT_DIRECTORY/artifacts"
ARCHIVE_NAME="Arpeggio-$VERSION-$BUILD_NUMBER-$RUNTIME.zip"
DOWNLOAD_RETRIES=3
require_command dotnet
case "$RUNTIME" in
  osx-*)
    [ "$(uname -s)" = "Darwin" ] || fail "macOS バンドルは macOS 上で生成してください。"
    require_command plutil
    require_command codesign
    require_command ditto
    ;;
  win-*)
    require_command zip
    require_command curl
    ;;
esac

[ ! -L "$ARTIFACTS" ] || fail "artifacts/ にシンボリックリンクは使用できません。"
mkdir -p "$ARTIFACTS"
STAGING="$(mktemp -d "$ARTIFACTS/.app-$RUNTIME.XXXXXX")"
trap 'rm -rf "$STAGING"' EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

dotnet publish "$PROJECT_DIRECTORY/src/Arpeggio.Daw/Arpeggio.Daw.csproj" \
  --configuration Release --runtime "$RUNTIME" --self-contained true \
  --output "$STAGING/publish" \
  -p:UseAppHost=true -p:PublishSingleFile=false -p:PublishTrimmed=false -p:PublishAot=false \
  -p:DebugType=None -p:DebugSymbols=false \
  -p:Version="$VERSION" -p:AssemblyVersion="$VERSION.0" -p:FileVersion="$VERSION.$BUILD_NUMBER"

mkdir -p "$STAGING/package"
case "$RUNTIME" in
  osx-*)
    BUNDLE="$STAGING/package/Arpeggio.app"
    mkdir -p "$BUNDLE/Contents/Resources"
    mv "$STAGING/publish" "$BUNDLE/Contents/MacOS"
    [ -f "$BUNDLE/Contents/MacOS/arpeggio-daw" ] || fail "apphost が publish されていません。"
    [ -f "$BUNDLE/Contents/MacOS/libSDL3.dylib" ] || fail "SDL3 のネイティブ依存がありません。"
    chmod +x "$BUNDLE/Contents/MacOS/arpeggio-daw"
    cp "$PROJECT_DIRECTORY/assets/icon/arpeggio.icns" "$BUNDLE/Contents/Resources/arpeggio.icns"
    sed -e "s/@VERSION@/$VERSION/g" -e "s/@BUILD_NUMBER@/$BUILD_NUMBER/g" \
      "$SCRIPT_DIRECTORY/macos/Info.plist" > "$BUNDLE/Contents/Info.plist"
    plutil -lint "$BUNDLE/Contents/Info.plist"
    # Apple Silicon でのローカル実行用。Developer ID 署名・公証による配布元の証明は行わない。
    # codesign は Contents/MacOS 配下の .dll も入れ子のコードとして扱うため、--deep で一括署名する
    # （個別署名＋バンドル署名だと "In subcomponent: ....dll" で失敗する）。
    codesign --force --deep --sign - "$BUNDLE"
    codesign --verify --strict "$BUNDLE"
    ditto -c -k --sequesterRsrc --keepParent "$BUNDLE" "$STAGING/$ARCHIVE_NAME"
    ;;
  win-*)
    mv "$STAGING/publish" "$STAGING/package/Arpeggio"
    [ -f "$STAGING/package/Arpeggio/arpeggio-daw.exe" ] || fail "Windows apphost が publish されていません。"
    [ -f "$STAGING/package/Arpeggio/SDL3.dll" ] || fail "SDL3 のネイティブ依存がありません。"
    # SDL3.dll が必要とする VC++ ランタイムを、未導入の PC でも同じ ZIP から導入できるようにする。
    REDISTRIBUTABLE_NAME="vc_redist.${RUNTIME#win-}.exe"
    mkdir -p "$STAGING/package/Arpeggio/Prerequisites"
    curl --fail --location --proto '=https' --proto-redir '=https' --retry "$DOWNLOAD_RETRIES" \
      "https://aka.ms/vc14/$REDISTRIBUTABLE_NAME" \
      --output "$STAGING/package/Arpeggio/Prerequisites/$REDISTRIBUTABLE_NAME"
    cp "$SCRIPT_DIRECTORY/windows/README.txt" "$STAGING/package/Arpeggio/README-Windows.txt"
    (cd "$STAGING/package" && zip -q -r "$STAGING/$ARCHIVE_NAME" Arpeggio)
    ;;
esac

# publish・署名・圧縮の成功までは前回の配布物を保持する。
rm -rf "$ARTIFACTS/$RUNTIME"
mv "$STAGING/package" "$ARTIFACTS/$RUNTIME"
mv -f "$STAGING/$ARCHIVE_NAME" "$ARTIFACTS/$ARCHIVE_NAME"
printf '配布フォルダ: %s\n配布 ZIP: %s\n' "$ARTIFACTS/$RUNTIME" "$ARTIFACTS/$ARCHIVE_NAME"
