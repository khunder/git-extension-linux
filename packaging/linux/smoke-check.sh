#!/usr/bin/env bash
# Post-install check for the .deb/.rpm/.pkg.tar.zst packages: the installed files exist and every native library
# resolves (no "not found" in ldd). The app has no command line flag to run headless, so this does not start it.
#
#   packaging/linux/smoke-check.sh [install-dir]      (default /opt/git-extensions)
set -euo pipefail

dir="${1:-/opt/git-extensions}"
fail=0
ldconfig_bin="$(command -v ldconfig || echo /sbin/ldconfig)"

for f in "$dir/GitExtensions.Avalonia" "$dir/GitExtensions.AskPass" "$dir/GitExtensions.Avalonia.dll" "$dir/libSkiaSharp.so" "$dir/libHarfBuzzSharp.so" \
         /usr/bin/git-extensions /usr/share/applications/git-extensions.desktop \
         /usr/share/icons/hicolor/256x256/apps/git-extensions.png /usr/share/icons/hicolor/scalable/apps/git-extensions.svg; do
  if [[ ! -e "$f" ]]; then echo "MISSING: $f"; fail=1; fi
done
[[ -x "$dir/GitExtensions.Avalonia" ]] || { echo "not executable: $dir/GitExtensions.Avalonia"; fail=1; }
[[ "$(readlink -f /usr/bin/git-extensions)" == "$(readlink -f "$dir/GitExtensions.Avalonia")" ]] || { echo "/usr/bin/git-extensions does not point to the apphost"; fail=1; }
command -v git >/dev/null || { echo "git is not installed"; fail=1; }

# libX11 and friends are loaded with dlopen by Avalonia, so check they are present too.
# (The listing is captured first: with pipefail, `ldconfig -p | grep -q` can fail with SIGPIPE when grep exits early.)
system_libs="$("$ldconfig_bin" -p)"
for lib in libX11.so.6 libXcursor.so.1 libXi.so.6 libXrandr.so.2 libICE.so.6 libSM.so.6 libfontconfig.so.1; do
  if ! grep -q "$lib" <<<"$system_libs"; then echo "MISSING system library: $lib"; fail=1; fi
done

for bin in "$dir/GitExtensions.Avalonia" "$dir/GitExtensions.AskPass" "$dir"/libSkiaSharp.so "$dir"/libHarfBuzzSharp.so "$dir"/libcoreclr.so "$dir"/libclrjit.so "$dir"/libSystem.Native.so "$dir"/libSystem.Security.Cryptography.Native.OpenSsl.so; do
  [[ -e "$bin" ]] || continue
  out="$(ldd "$bin" 2>&1 || true)"
  if grep -q 'not found' <<<"$out"; then
    echo "UNRESOLVED in $bin:"; grep 'not found' <<<"$out"; fail=1
  fi
done

if [[ "$fail" != 0 ]]; then echo "smoke check FAILED"; exit 1; fi
echo "smoke check passed"
