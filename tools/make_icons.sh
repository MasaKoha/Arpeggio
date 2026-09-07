#!/bin/bash
# 使い方: make_icons.sh <arpeggio.svg> <出力ディレクトリ> [小サイズ用 svg]
# 16 / 32 px は小サイズ用 SVG（矩形波なし）から作る。指定が無ければ通常 SVG を使う
# SVG から PNG（16〜1024）・macOS .icns・Windows .ico を作る
set -eu
SVG="$1"; OUT="$2"; SMALL="${3:-$1}"; mkdir -p "$OUT/arpeggio.iconset"
for size in 64 128 256 512 1024; do
  rsvg-convert -w $size -h $size "$SVG" -o "$OUT/arpeggio-$size.png"
done
for size in 16 32; do
  rsvg-convert -w $size -h $size "$SMALL" -o "$OUT/arpeggio-$size.png"
done
cp "$OUT/arpeggio-16.png"   "$OUT/arpeggio.iconset/icon_16x16.png"
cp "$OUT/arpeggio-32.png"   "$OUT/arpeggio.iconset/icon_16x16@2x.png"
cp "$OUT/arpeggio-32.png"   "$OUT/arpeggio.iconset/icon_32x32.png"
cp "$OUT/arpeggio-64.png"   "$OUT/arpeggio.iconset/icon_32x32@2x.png"
cp "$OUT/arpeggio-128.png"  "$OUT/arpeggio.iconset/icon_128x128.png"
cp "$OUT/arpeggio-256.png"  "$OUT/arpeggio.iconset/icon_128x128@2x.png"
cp "$OUT/arpeggio-256.png"  "$OUT/arpeggio.iconset/icon_256x256.png"
cp "$OUT/arpeggio-512.png"  "$OUT/arpeggio.iconset/icon_256x256@2x.png"
cp "$OUT/arpeggio-512.png"  "$OUT/arpeggio.iconset/icon_512x512.png"
cp "$OUT/arpeggio-1024.png" "$OUT/arpeggio.iconset/icon_512x512@2x.png"
iconutil -c icns "$OUT/arpeggio.iconset" -o "$OUT/arpeggio.icns"
python3 - "$OUT" <<'PY'
import sys; from PIL import Image
out=sys.argv[1]
large=Image.open(f"{out}/arpeggio-1024.png").resize((256,256),Image.LANCZOS)
small=[Image.open(f"{out}/arpeggio-16.png"),Image.open(f"{out}/arpeggio-32.png")]
# PIL の ICO は先頭画像より大きいサイズを捨てるため、先頭を最大サイズにして小サイズを append で差し替える
large.save(f"{out}/arpeggio.ico", format="ICO", sizes=[(16,16),(32,32),(48,48),(64,64),(128,128),(256,256)], append_images=small)
PY
rm -rf "$OUT/arpeggio.iconset"
ls -la "$OUT" | awk '{print $5, $9}'
