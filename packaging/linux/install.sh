#!/usr/bin/env bash
# User-level installer for the Git Extensions Avalonia UI. Run it from the extracted tarball.
#
#   ./install.sh                 # installs under ~/.local
#   PREFIX=/opt/git-ext ./install.sh   # custom prefix (needs permission to write there)
#   ./install.sh --uninstall
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
prefix="${PREFIX:-$HOME/.local}"
app_dir="$prefix/opt/git-extensions"
bin_dir="$prefix/bin"
desktop_dir="$prefix/share/applications"
icon_dir="$prefix/share/icons/hicolor/scalable/apps"

if [[ "${1:-}" == "--uninstall" ]]; then
  rm -rf "$app_dir" "$bin_dir/git-extensions" "$desktop_dir/git-extensions.desktop" "$icon_dir/git-extensions.svg"
  echo "Removed Git Extensions from $prefix"
  exit 0
fi

mkdir -p "$app_dir" "$bin_dir" "$desktop_dir" "$icon_dir"
rm -rf "$app_dir"
mkdir -p "$app_dir"
cp -a "$here/app/." "$app_dir/"
chmod +x "$app_dir/GitExtensions.Avalonia" "$app_dir/GitExtensions.AskPass"

ln -sf "$app_dir/GitExtensions.Avalonia" "$bin_dir/git-extensions"
cp "$here/git-extensions.svg" "$icon_dir/git-extensions.svg"

# Absolute Exec path so the launcher works even when ~/.local/bin is not on the desktop session's PATH.
sed "s|^Exec=.*|Exec=$app_dir/GitExtensions.Avalonia %f|" "$here/git-extensions.desktop" > "$desktop_dir/git-extensions.desktop"

if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database "$desktop_dir" >/dev/null 2>&1 || true
fi

echo "Installed to $app_dir"
echo "Run: git-extensions [path-to-repository]   (make sure $bin_dir is on your PATH)"
