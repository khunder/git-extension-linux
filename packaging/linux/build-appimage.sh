#!/usr/bin/env bash
# Builds a portable AppImage from the published, self-contained tree (the .NET runtime is bundled).
#
#   VERSION=0.2.0 ARCH=x64 PUBLISH_DIR=<published tree> packaging/linux/build-appimage.sh
#
# Inputs (environment):
#   VERSION      package version (default 0.2.0)
#   ARCH         x64 (default) or arm64
#   PUBLISH_DIR  output of publish.sh; when empty the app is published first
#   OUT_DIR      output directory (default artifacts/dist)
#   TOOLS_DIR    where appimagetool and the runtime are cached (default artifacts/appimage-tools)
#
# Output: $OUT_DIR/git-extensions-$VERSION-<x86_64|aarch64>.AppImage
#
# The AppImage bundles the application only. Like the distribution packages it uses the system's X11, fontconfig and
# OpenSSL 3 libraries, and it needs `git` on the PATH.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

APPIMAGETOOL_VERSION=1.9.1
RUNTIME_TAG=20251108

VERSION="${VERSION:-0.2.0}"
ARCH="${ARCH:-x64}"
OUT_DIR="${OUT_DIR:-$repo_root/artifacts/dist}"
TOOLS_DIR="${TOOLS_DIR:-$repo_root/artifacts/appimage-tools}"
RID="$(rid_for_arch "$ARCH")"
PUBLISH_DIR="${PUBLISH_DIR:-}"

case "$RID" in
  linux-x64) APPIMAGE_ARCH=x86_64 ;;
  linux-arm64) APPIMAGE_ARCH=aarch64 ;;
esac

if [[ -z "$PUBLISH_DIR" ]]; then
  PUBLISH_DIR="$repo_root/artifacts/publish/$RID"
  "$packaging_dir/publish.sh" "$RID" "$PUBLISH_DIR"
fi
check_publish_dir "$PUBLISH_DIR"

# The tool itself always runs on the build machine; only the runtime embedded in the image follows the target.
host_arch="$(uname -m)"
case "$host_arch" in
  x86_64|aarch64) ;;
  *) echo "error: unsupported build machine architecture $host_arch" >&2; exit 2 ;;
esac

mkdir -p "$TOOLS_DIR" "$OUT_DIR"
tool="$TOOLS_DIR/appimagetool-$APPIMAGETOOL_VERSION-$host_arch.AppImage"
runtime="$TOOLS_DIR/runtime-$RUNTIME_TAG-$APPIMAGE_ARCH"
if [[ ! -x "$tool" ]]; then
  curl -fsSL -o "$tool" "https://github.com/AppImage/appimagetool/releases/download/$APPIMAGETOOL_VERSION/appimagetool-$host_arch.AppImage"
  chmod +x "$tool"
fi
if [[ ! -s "$runtime" ]]; then
  curl -fsSL -o "$runtime" "https://github.com/AppImage/type2-runtime/releases/download/$RUNTIME_TAG/runtime-$APPIMAGE_ARCH"
fi

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
appdir="$work/AppDir"
app="$appdir/usr/lib/$PKG_NAME"
mkdir -p "$app" "$appdir/usr/bin" "$appdir/usr/share/applications" "$appdir/usr/share/metainfo"
cp -a "$PUBLISH_DIR/." "$app/"
chmod 755 "$app/GitExtensions.Avalonia" "$app/GitExtensions.AskPass"
ln -s "../lib/$PKG_NAME/GitExtensions.Avalonia" "$appdir/usr/bin/$PKG_NAME"

# AppRun starts the application from inside the image; the askpass helper stays next to the apphost, where it is found.
cat > "$appdir/AppRun" <<'EOF'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/lib/git-extensions/GitExtensions.Avalonia" "$@"
EOF
chmod 755 "$appdir/AppRun"

sed 's|^Exec=.*|Exec=git-extensions %f|' "$packaging_dir/git-extensions.desktop" > "$appdir/$PKG_NAME.desktop"
cp "$appdir/$PKG_NAME.desktop" "$appdir/usr/share/applications/$PKG_NAME.desktop"
cp "$packaging_dir/icons/git-extensions-256.png" "$appdir/$PKG_NAME.png"
ln -s "$PKG_NAME.png" "$appdir/.DirIcon"
for size in 16 32 48 64 128 256 512; do
  install -Dm644 "$packaging_dir/icons/git-extensions-$size.png" "$appdir/usr/share/icons/hicolor/${size}x${size}/apps/$PKG_NAME.png"
done
install -Dm644 "$packaging_dir/git-extensions.svg" "$appdir/usr/share/icons/hicolor/scalable/apps/$PKG_NAME.svg"
install -Dm644 "$repo_root/LICENSE.md" "$appdir/usr/share/doc/$PKG_NAME/LICENSE.md"

cat > "$appdir/usr/share/metainfo/$PKG_NAME.appdata.xml" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<component type="desktop-application">
  <id>git-extensions</id>
  <metadata_license>CC0-1.0</metadata_license>
  <project_license>$PKG_LICENSE_SPDX</project_license>
  <name>Git Extensions Next</name>
  <summary>$PKG_SUMMARY</summary>
  <description>
    <p>$PKG_DESCRIPTION</p>
  </description>
  <launchable type="desktop-id">$PKG_NAME.desktop</launchable>
  <url type="homepage">$PKG_URL</url>
  <provides>
    <binary>$PKG_NAME</binary>
  </provides>
  <releases>
    <release version="$VERSION"/>
  </releases>
</component>
EOF

out="$OUT_DIR/git-extensions-$VERSION-$APPIMAGE_ARCH.AppImage"
rm -f "$out"
# --appimage-extract-and-run: build machines (CI, containers) usually have no FUSE.
ARCH="$APPIMAGE_ARCH" "$tool" --appimage-extract-and-run --no-appstream --runtime-file "$runtime" "$appdir" "$out"
chmod 755 "$out"
echo "Created $out ($(du -h "$out" | cut -f1))"
