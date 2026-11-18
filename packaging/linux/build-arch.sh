#!/usr/bin/env bash
# Builds an Arch Linux package with makepkg from the prebuilt tarball (see PKGBUILD). Must run as a NON-root user
# (makepkg refuses to run as root) on an Arch system or inside an archlinux container.
#
#   VERSION=0.7.0 ARCH=x86_64 TARBALL=artifacts/dist/git-extensions-linux-0.7.0-x64.tar.gz packaging/linux/build-arch.sh
#
# Inputs: VERSION (default 0.7.0), ARCH (x86_64 default | aarch64), TARBALL (required), OUT_DIR (default artifacts/dist).
# Output: $OUT_DIR/git-extensions-$VERSION-1-$ARCH.pkg.tar.zst
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

VERSION="${VERSION:-0.7.0}"
ARCH="${ARCH:-x86_64}"
OUT_DIR="${OUT_DIR:-$repo_root/artifacts/dist}"
require_env TARBALL
[[ -f "$TARBALL" ]] || { echo "error: $TARBALL not found" >&2; exit 2; }
[[ "$(id -u)" != 0 ]] || { echo "error: makepkg must not run as root" >&2; exit 2; }
command -v makepkg >/dev/null || { echo "error: makepkg not found" >&2; exit 2; }

case "$ARCH" in
  x86_64) tag=x64 ;;
  aarch64) tag=arm64 ;;
  *) echo "error: unsupported architecture '$ARCH'" >&2; exit 2 ;;
esac

[[ "$ARCH" == "$(uname -m)" ]] || { echo "error: makepkg builds for the host architecture only ($(uname -m)), not $ARCH" >&2; exit 2; }

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
cp "$packaging_dir/PKGBUILD" "$work/"
cp "$TARBALL" "$work/git-extensions-linux-$VERSION-$tag.tar.gz"
mkdir -p "$OUT_DIR"

(cd "$work" && GE_VERSION="$VERSION" PKGDEST="$work/out" makepkg -f --nodeps --noconfirm --skipinteg)
built="$(find "$work/out" -name '*.pkg.tar.*' ! -name '*.sig' | head -n1)"
[[ -n "$built" ]] || { echo "error: makepkg produced no package" >&2; exit 1; }
out="$OUT_DIR/git-extensions-${VERSION//-/_}-1-$ARCH.pkg.tar.zst"
cp "$built" "$out"
echo "Created $out ($(du -h "$out" | cut -f1))"
