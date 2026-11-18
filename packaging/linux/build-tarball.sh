#!/usr/bin/env bash
# Builds the self-contained Linux tarball (no .NET runtime needed on the target machine).
#
#   VERSION=0.7.1 ARCH=x64 PUBLISH_DIR=<published tree> packaging/linux/build-tarball.sh
#
# Inputs (environment):
#   VERSION      package version (default 0.7.1)
#   ARCH         x64 (default) or arm64
#   PUBLISH_DIR  output of publish.sh; when empty the app is published first
#   OUT_DIR      output directory (default artifacts/dist)
#
# Output: $OUT_DIR/git-extensions-linux-$VERSION-$ARCH.tar.gz
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

VERSION="${VERSION:-0.7.1}"
ARCH="${ARCH:-x64}"
OUT_DIR="${OUT_DIR:-$repo_root/artifacts/dist}"
RID="$(rid_for_arch "$ARCH")"
PUBLISH_DIR="${PUBLISH_DIR:-}"

if [[ -z "$PUBLISH_DIR" ]]; then
  PUBLISH_DIR="$repo_root/artifacts/publish/$RID"
  "$packaging_dir/publish.sh" "$RID" "$PUBLISH_DIR"
fi
check_publish_dir "$PUBLISH_DIR"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
stage="$work/git-extensions"
mkdir -p "$stage" "$OUT_DIR"
cp -a "$PUBLISH_DIR" "$stage/app"
cp "$packaging_dir/git-extensions.desktop" "$packaging_dir/git-extensions.svg" "$packaging_dir/install.sh" "$repo_root/LICENSE.md" "$stage/"
cp -r "$packaging_dir/icons" "$stage/icons"
chmod +x "$stage/install.sh" "$stage/app/GitExtensions.Avalonia" "$stage/app/GitExtensions.AskPass"

tarball="$OUT_DIR/git-extensions-linux-$VERSION-$ARCH.tar.gz"
tar --owner=0 --group=0 --numeric-owner -C "$work" -czf "$tarball" git-extensions
echo "Created $tarball ($(du -h "$tarball" | cut -f1))"
