#!/usr/bin/env bash
# Builds .deb and .rpm packages with fpm from a published (framework-dependent-free) linux build.
# Usage: build-packages.sh <version> <publish-dir> <arch: x64|arm64> <output-dir>
set -euo pipefail

version="$1"
publish_dir="$2"
arch="$3"
output_dir="$4"

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
staging="$(mktemp -d)"
trap 'rm -rf "$staging"' EXIT

case "$arch" in
  x64) deb_arch=amd64; rpm_arch=x86_64 ;;
  arm64) deb_arch=arm64; rpm_arch=aarch64 ;;
  *) echo "Unknown architecture: $arch" >&2; exit 1 ;;
esac

install -d "$staging/usr/lib/mkoptic" "$staging/usr/bin" \
  "$staging/usr/share/applications" "$staging/usr/share/pixmaps" \
  "$staging/usr/share/doc/mkoptic"

cp -r "$publish_dir"/. "$staging/usr/lib/mkoptic/"
chmod 755 "$staging/usr/lib/mkoptic/MkOptic"
ln -s ../lib/mkoptic/MkOptic "$staging/usr/bin/mkoptic"
install -m 644 "$root/packaging/linux/mkoptic.desktop" "$staging/usr/share/applications/mkoptic.desktop"
install -m 644 "$root/src/MkOptic/Assets/mkoptic.png" "$staging/usr/share/pixmaps/mkoptic.png"
install -m 644 "$root/LICENSE" "$staging/usr/share/doc/mkoptic/copyright"

mkdir -p "$output_dir"
common=(
  -s dir -n mkoptic -v "$version" -C "$staging"
  --description "Markdown editor and viewer with live preview"
  --url "https://github.com/ambyte/MkOptic"
  --maintainer "Sergey Gulyaev"
  --license MIT
  --after-install "$root/packaging/linux/postinst.sh"
  --after-remove "$root/packaging/linux/postinst.sh"
)

fpm "${common[@]}" -t deb -a "$deb_arch" \
  -d libfontconfig1 -d libice6 -d libsm6 \
  -p "$output_dir/MkOptic-${version}-linux-${deb_arch}.deb" usr
fpm "${common[@]}" -t rpm -a "$rpm_arch" \
  -d fontconfig -d libICE -d libSM \
  -p "$output_dir/MkOptic-${version}-linux-${rpm_arch}.rpm" usr
