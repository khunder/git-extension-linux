# Shared helpers for the packaging scripts (sourced, not executed).

PKG_NAME=git-extensions
PKG_URL=https://github.com/khunder/git-extensions-linux
PKG_LICENSE_SPDX=GPL-3.0-or-later
PKG_SUMMARY="Graphical Git client (Git Extensions for Linux)"
PKG_DESCRIPTION="Git Extensions is a graphical user interface for Git: browse history, stage changes, commit, manage branches, remotes and stashes. This package bundles the .NET runtime, so nothing else needs to be installed besides Git."

packaging_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$packaging_dir/../.." && pwd)"

# require_env VAR... : fail with a clear message when a variable is empty.
require_env() {
  local v
  for v in "$@"; do
    if [[ -z "${!v:-}" ]]; then
      echo "error: $v is required" >&2
      exit 2
    fi
  done
}

# check_publish_dir DIR : the published tree must contain the app and the askpass helper.
check_publish_dir() {
  local d="$1" f
  for f in GitExtensions.Avalonia GitExtensions.Avalonia.dll GitExtensions.AskPass libSkiaSharp.so; do
    if [[ ! -e "$d/$f" ]]; then
      echo "error: $d does not look like a published tree (missing $f)" >&2
      exit 2
    fi
  done
}

# rid_for_arch ARCH : x64|amd64|x86_64 -> linux-x64 ; arm64|aarch64 -> linux-arm64
rid_for_arch() {
  case "$1" in
    x64|amd64|x86_64) echo linux-x64 ;;
    arm64|aarch64) echo linux-arm64 ;;
    *) echo "error: unsupported architecture '$1'" >&2; return 1 ;;
  esac
}

# stage_root PUBLISH_DIR DEST : lays the application out as it is installed by the .deb and .rpm packages:
#   /opt/git-extensions/...                          the published tree
#   /usr/bin/git-extensions                          symlink to the apphost
#   /usr/share/applications/git-extensions.desktop
#   /usr/share/icons/hicolor/<size>x<size>/apps/git-extensions.png and scalable/apps/git-extensions.svg
#   /usr/share/doc/git-extensions/LICENSE.md
stage_root() {
  local pub="$1" dest="$2" size
  rm -rf "$dest"
  mkdir -p "$dest/opt/$PKG_NAME" "$dest/usr/bin" "$dest/usr/share/applications" "$dest/usr/share/doc/$PKG_NAME"
  cp -a "$pub/." "$dest/opt/$PKG_NAME/"
  chmod 755 "$dest/opt/$PKG_NAME/GitExtensions.Avalonia" "$dest/opt/$PKG_NAME/GitExtensions.AskPass"
  find "$dest/opt/$PKG_NAME" -type d -exec chmod 755 {} +
  find "$dest/opt/$PKG_NAME" -type f ! -perm /111 -exec chmod 644 {} +
  find "$dest/opt/$PKG_NAME" -type f -perm /111 -exec chmod 755 {} +
  ln -s "/opt/$PKG_NAME/GitExtensions.Avalonia" "$dest/usr/bin/$PKG_NAME"
  sed 's|^Exec=.*|Exec=git-extensions %f|' "$packaging_dir/git-extensions.desktop" > "$dest/usr/share/applications/$PKG_NAME.desktop"
  chmod 644 "$dest/usr/share/applications/$PKG_NAME.desktop"
  for size in 16 32 48 64 128 256 512; do
    install -Dm644 "$packaging_dir/icons/git-extensions-$size.png" "$dest/usr/share/icons/hicolor/${size}x${size}/apps/$PKG_NAME.png"
  done
  install -Dm644 "$packaging_dir/git-extensions.svg" "$dest/usr/share/icons/hicolor/scalable/apps/$PKG_NAME.svg"
  install -Dm644 "$repo_root/LICENSE.md" "$dest/usr/share/doc/$PKG_NAME/LICENSE.md"
}
