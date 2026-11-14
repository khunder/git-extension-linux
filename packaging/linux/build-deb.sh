#!/usr/bin/env bash
# Builds a .deb (Debian, Ubuntu and derivatives) from a published tree.
#
#   VERSION=0.1.0 ARCH=amd64 PUBLISH_DIR=<published tree> packaging/linux/build-deb.sh
#
# Inputs: VERSION (default 0.1.0), ARCH (amd64 default | arm64), PUBLISH_DIR (required), OUT_DIR (default artifacts/dist).
# Output: $OUT_DIR/git-extensions_${VERSION}_${ARCH}.deb
# Uses dpkg-deb when available, otherwise assembles the archive with ar/tar (the result is identical in structure).
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

VERSION="${VERSION:-0.1.0}"
ARCH="${ARCH:-amd64}"
OUT_DIR="${OUT_DIR:-$repo_root/artifacts/dist}"
require_env PUBLISH_DIR
rid_for_arch "$ARCH" >/dev/null
check_publish_dir "$PUBLISH_DIR"

# Native dependencies of the bundled runtime: libSkiaSharp (fontconfig/freetype/png/expat/zlib), the X11 client
# libraries Avalonia loads at run time, OpenSSL for HTTPS, and the C++ runtime. libssl3t64 is the Ubuntu 24.04/Debian 13 name.
DEPENDS="git, libc6, libgcc-s1, libstdc++6, zlib1g, libx11-6, libxcursor1, libxi6, libxrandr2, libice6, libsm6, libfontconfig1, libfreetype6, libpng16-16 | libpng16-16t64, libssl3t64 | libssl3"
RECOMMENDS="openssh-client"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
root="$work/root"
stage_root "$PUBLISH_DIR" "$root"

# Debian wants the copyright file under that exact name.
cat > "$root/usr/share/doc/$PKG_NAME/copyright" <<EOF
Format: https://www.debian.org/doc/packaging-manuals/copyright-format/1.0/
Upstream-Name: Git Extensions
Source: $PKG_URL

Files: *
Copyright: 2008-2026 Git Extensions Team and contributors
License: GPL-3+
 This program is free software: you can redistribute it and/or modify it under the terms of the GNU General
 Public License as published by the Free Software Foundation, either version 3 of the License, or (at your
 option) any later version. The full text is in /usr/share/common-licenses/GPL-3 and in LICENSE.md next to this file.
EOF
chmod 644 "$root/usr/share/doc/$PKG_NAME/copyright"

installed_size="$(du -sk "$root" | cut -f1)"
ctl="$work/control"
mkdir -p "$ctl"
cat > "$ctl/control" <<EOF
Package: $PKG_NAME
Version: $VERSION
Section: vcs
Priority: optional
Architecture: $ARCH
Maintainer: Git Extensions for Linux <noreply@github.com>
Installed-Size: $installed_size
Depends: $DEPENDS
Recommends: $RECOMMENDS
Homepage: $PKG_URL
Description: $PKG_SUMMARY
 $PKG_DESCRIPTION
EOF
cat > "$ctl/postinst" <<'EOF'
#!/bin/sh
set -e
if [ "$1" = configure ]; then
  command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database -q /usr/share/applications || true
  command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor || true
fi
exit 0
EOF
cp "$ctl/postinst" "$ctl/postrm"
sed -i 's/\[ "\$1" = configure \]/[ "$1" = remove ] || [ "$1" = purge ]/' "$ctl/postrm"
chmod 755 "$ctl/postinst" "$ctl/postrm"
(cd "$root" && find opt usr -type f -exec md5sum {} +) > "$ctl/md5sums"

mkdir -p "$OUT_DIR"
deb="$OUT_DIR/${PKG_NAME}_${VERSION}_${ARCH}.deb"
rm -f "$deb"
if command -v dpkg-deb >/dev/null 2>&1; then
  mkdir -p "$root/DEBIAN"
  cp "$ctl"/* "$root/DEBIAN/"
  dpkg-deb --root-owner-group -Zxz --build "$root" "$deb"
else
  echo "dpkg-deb not found, assembling the archive with ar/tar"
  tar --owner=0 --group=0 --numeric-owner -C "$ctl" -czf "$work/control.tar.gz" .
  tar --owner=0 --group=0 --numeric-owner -C "$root" -cJf "$work/data.tar.xz" .
  echo "2.0" > "$work/debian-binary"
  (cd "$work" && ar rc "$deb" debian-binary control.tar.gz data.tar.xz)
fi
echo "Created $deb ($(du -h "$deb" | cut -f1))"
