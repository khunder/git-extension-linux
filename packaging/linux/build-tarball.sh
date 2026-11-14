#!/usr/bin/env bash
# Builds a self-contained Linux tarball of the Avalonia UI (no .NET runtime needed on the target machine).
#
#   packaging/linux/build-tarball.sh            # linux-x64
#   RID=linux-arm64 packaging/linux/build-tarball.sh
#
# Output: artifacts/publish/git-extensions-avalonia-<rid>.tar.gz
set -euo pipefail

RID="${RID:-linux-x64}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
stage="$root/artifacts/publish/stage-$RID/git-extensions"
tarball="$root/artifacts/publish/git-extensions-avalonia-$RID.tar.gz"

rm -rf "$root/artifacts/publish/stage-$RID"
mkdir -p "$stage"

# The Avalonia project publishes the askpass helper next to the app (see CopyAskPassToPublishDir).
dotnet publish "$root/src/app/GitExtensions.Avalonia/GitExtensions.Avalonia.csproj" \
  -c Release -r "$RID" --self-contained true \
  -p:DebugType=None -p:DebugSymbols=false \
  -o "$stage/app"

cp "$root/packaging/linux/git-extensions.desktop" "$root/packaging/linux/git-extensions.svg" "$stage/"
cp "$root/packaging/linux/install.sh" "$stage/install.sh"
chmod +x "$stage/install.sh"

tar -C "$root/artifacts/publish/stage-$RID" -czf "$tarball" git-extensions
echo "Created $tarball ($(du -h "$tarball" | cut -f1))"
