#!/usr/bin/env bash
# Builds an .rpm from a published tree with rpmbuild. The same spec serves Fedora and openSUSE.
#
#   VERSION=0.1.0 ARCH=x86_64 DISTRO=fedora PUBLISH_DIR=<published tree> packaging/linux/build-rpm.sh
#
# Inputs: VERSION (default 0.1.0), ARCH (x86_64 default | aarch64), DISTRO (fedora | opensuse; only used in the output
# file name, the spec itself picks the package names from %suse_version), PUBLISH_DIR (required), OUT_DIR (default artifacts/dist).
# Output: $OUT_DIR/git-extensions-$VERSION-1.$DISTRO.$ARCH.rpm
# Needs: rpm-build. A cross build (aarch64 on an x86_64 host) works because nothing is compiled.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

VERSION="${VERSION:-0.1.0}"
ARCH="${ARCH:-x86_64}"
DISTRO="${DISTRO:-linux}"
OUT_DIR="${OUT_DIR:-$repo_root/artifacts/dist}"
require_env PUBLISH_DIR
rid_for_arch "$ARCH" >/dev/null
check_publish_dir "$PUBLISH_DIR"
command -v rpmbuild >/dev/null || { echo "error: rpmbuild not found (install rpm-build)" >&2; exit 2; }

# rpm versions cannot contain '-': 0.2.0-rc1 -> 0.2.0~rc1
RPM_VERSION="${VERSION//-/\~}"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
stage_root "$PUBLISH_DIR" "$work/stage"
mkdir -p "$work/top"/{BUILD,RPMS,SRPMS,SPECS,SOURCES} "$OUT_DIR"

rpmbuild -bb --target "$ARCH" \
  --define "_topdir $work/top" \
  --define "ge_version $RPM_VERSION" \
  --define "ge_release 1" \
  --define "ge_stage $work/stage" \
  "$packaging_dir/git-extensions.spec"

built="$(find "$work/top/RPMS" -name '*.rpm' | head -n1)"
[[ -n "$built" ]] || { echo "error: rpmbuild produced no package" >&2; exit 1; }
out="$OUT_DIR/$PKG_NAME-$VERSION-1.$DISTRO.$ARCH.rpm"
cp "$built" "$out"
echo "Created $out ($(du -h "$out" | cut -f1))"
rpm -qpl "$out" | head -n 20 || true
rpm -qp --requires "$out" || true
