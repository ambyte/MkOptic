#!/usr/bin/env bash
# Builds MkOptic.app from a published osx build and wraps it into a .dmg (macOS only).
# Usage: build-dmg.sh <version> <publish-dir> <arch: x64|arm64> <output-dir>
set -euo pipefail

version="$1"
publish_dir="$2"
arch="$3"
output_dir="$4"

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

app="$work/MkOptic.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources" "$output_dir"
cp -R "$publish_dir"/. "$app/Contents/MacOS/"
chmod +x "$app/Contents/MacOS/MkOptic"
sed "s/@VERSION@/$version/g" "$root/packaging/macos/Info.plist" > "$app/Contents/Info.plist"

# Build the .icns icon from the 256x256 PNG.
iconset="$work/mkoptic.iconset"
mkdir "$iconset"
for size in 16 32 64 128 256; do
  sips -z "$size" "$size" "$root/src/MkOptic/Assets/mkoptic.png" --out "$iconset/icon_${size}x${size}.png" >/dev/null
done
cp "$iconset/icon_32x32.png" "$iconset/icon_16x16@2x.png"
cp "$iconset/icon_64x64.png" "$iconset/icon_32x32@2x.png"
cp "$iconset/icon_256x256.png" "$iconset/icon_128x128@2x.png"
rm "$iconset/icon_64x64.png"
iconutil -c icns "$iconset" -o "$app/Contents/Resources/mkoptic.icns"

# Ad-hoc signature: required for arm64 binaries to run, it is not a Developer ID signature.
codesign --force --deep --sign - "$app"

dmg_root="$work/dmg"
mkdir "$dmg_root"
cp -R "$app" "$dmg_root/"
ln -s /Applications "$dmg_root/Applications"
hdiutil create -volname "MkOptic" -srcfolder "$dmg_root" -ov -format UDZO \
  "$output_dir/MkOptic-${version}-macos-${arch}.dmg"
